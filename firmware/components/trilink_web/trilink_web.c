#include "trilink_web.h"
#include "trilink_web_policy.h"
#include <stdio.h>
#include <string.h>
#include <errno.h>
#include <inttypes.h>
#include "esp_http_server.h"
#include "esp_random.h"
#include "esp_timer.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "lwip/sockets.h"

static httpd_handle_t s_server;
static portMUX_TYPE s_lock = portMUX_INITIALIZER_UNLOCKED;
static trilink_web_mailbox_t s_box;
static bool s_ready;
static char s_token[33];
/* One synchronous HTTP server task owns these buffers. Never on its stack. */
static char s_line[TRILINK_WEB_LINE_MAX], s_response[TRILINK_WEB_REPLY_MAX];
extern const unsigned char page_start[] asm("_binary_index_html_start");
extern const unsigned char page_end[] asm("_binary_index_html_end");
extern const unsigned char script_start[] asm("_binary_app_js_start");
extern const unsigned char script_end[] asm("_binary_app_js_end");
extern const unsigned char style_start[] asm("_binary_app_css_start");
extern const unsigned char style_end[] asm("_binary_app_css_end");

static uint64_t now_ms(void) { return (uint64_t)(esp_timer_get_time() / 1000LL); }

bool trilink_web_take(char *line, size_t capacity, uint32_t *ticket)
{
    const uint64_t now = now_ms();
    portENTER_CRITICAL(&s_lock);
    bool result = s_ready && trilink_web_dequeue(&s_box, now, line, capacity, ticket);
    portEXIT_CRITICAL(&s_lock);
    return result;
}

void trilink_web_complete(uint32_t ticket, const char *reply, size_t length)
{
    portENTER_CRITICAL(&s_lock);
    if (s_ready) trilink_web_finish(&s_box, ticket, reply, length);
    portEXIT_CRITICAL(&s_lock);
}

bool trilink_web_can_submit(uint32_t ticket)
{
    const uint64_t now = now_ms();
    portENTER_CRITICAL(&s_lock);
    bool valid = s_ready && s_box.state == WEB_RUNNING && s_box.ticket == ticket && now < s_box.deadline;
    portEXIT_CRITICAL(&s_lock);
    return valid;
}

static bool header(httpd_req_t *req, const char *name, char *out, size_t capacity, bool required)
{
    const size_t length = httpd_req_get_hdr_value_len(req, name);
    out[0] = 0;
    if (!length) return !required;
    return length < capacity && httpd_req_get_hdr_value_str(req, name, out, capacity) == ESP_OK;
}

static void response_headers(httpd_req_t *req)
{
    (void)httpd_resp_set_hdr(req, "Cache-Control", "no-store");
    (void)httpd_resp_set_hdr(req, "Connection", "close");
    (void)httpd_resp_set_hdr(req, "X-Content-Type-Options", "nosniff");
    (void)httpd_resp_set_hdr(req, "X-Frame-Options", "DENY");
    (void)httpd_resp_set_hdr(req, "Referrer-Policy", "no-referrer");
    (void)httpd_resp_set_hdr(req, "Cross-Origin-Resource-Policy", "same-origin");
    (void)httpd_resp_set_hdr(req, "Content-Security-Policy",
        "default-src 'none'; script-src 'self'; style-src 'self'; connect-src 'self'; img-src 'none'; frame-ancestors 'none'; base-uri 'none'; form-action 'none'");
}

static esp_err_t error(httpd_req_t *req, const char *status, const char *message)
{
    response_headers(req);
    (void)httpd_resp_set_status(req, status);
    (void)httpd_resp_set_type(req, "text/plain; charset=utf-8");
    return httpd_resp_send(req, message, HTTPD_RESP_USE_STRLEN);
}

static bool local_request(httpd_req_t *req)
{
    struct sockaddr_in local = {0}, peer = {0};
    socklen_t local_length = sizeof(local), peer_length = sizeof(peer);
    const int socket = httpd_req_to_sockfd(req);
    char host[48], origin[64], site[24];
    return getsockname(socket, (struct sockaddr *)&local, &local_length) == 0
        && getpeername(socket, (struct sockaddr *)&peer, &peer_length) == 0
        && local.sin_family == AF_INET && peer.sin_family == AF_INET
        && trilink_web_local_ipv4(ntohl(local.sin_addr.s_addr), ntohl(peer.sin_addr.s_addr))
        && header(req, "Host", host, sizeof(host), true) && trilink_web_allowed_host(host)
        && header(req, "Origin", origin, sizeof(origin), false) && trilink_web_allowed_origin(origin)
        && header(req, "Sec-Fetch-Site", site, sizeof(site), false)
        && (!site[0] || !strcmp(site, "same-origin") || !strcmp(site, "none"))
        && strchr(req->uri, '?') == NULL;
}

static esp_err_t asset(httpd_req_t *req)
{
    if (!local_request(req)) return error(req, "403 Forbidden", "LOCAL_ORIGIN_REQUIRED\n");
    const unsigned char *start = page_start, *end = page_end;
    const char *type = "text/html; charset=utf-8";
    if (!strcmp(req->uri, "/app.js")) { start = script_start; end = script_end; type = "application/javascript; charset=utf-8"; }
    else if (!strcmp(req->uri, "/app.css")) { start = style_start; end = style_end; type = "text/css; charset=utf-8"; }
    response_headers(req); (void)httpd_resp_set_type(req, type);
    return httpd_resp_send(req, (const char *)start, (ssize_t)(end - start - 1));
}

static esp_err_t session(httpd_req_t *req)
{
    if (!local_request(req)) return error(req, "403 Forbidden", "LOCAL_ORIGIN_REQUIRED\n");
    response_headers(req); (void)httpd_resp_set_type(req, "application/json");
    char json[192];
    const int length = snprintf(json, sizeof(json),
        "{\"protocol\":1,\"token\":\"%s\",\"firmware\":\"0.16.0\",\"endpoint\":\"/api/v1/exchange\",\"uptime_ms\":%" PRIu64 "}", s_token, now_ms());
    return httpd_resp_send(req, json, length);
}

static bool connection_alive(httpd_req_t *req)
{
    char byte;
    const int count = recv(httpd_req_to_sockfd(req), &byte, 1, MSG_PEEK | MSG_DONTWAIT);
    return count > 0 || (count < 0 && (errno == EAGAIN || errno == EWOULDBLOCK || errno == EINTR));
}

static esp_err_t exchange(httpd_req_t *req)
{
    if (!local_request(req)) return error(req, "403 Forbidden", "LOCAL_ORIGIN_REQUIRED\n");
    char token[40], type[48], confirm[16], expires[24];
    if (!header(req, "X-TriLink-Token", token, sizeof(token), true)
        || !trilink_web_token_equal(s_token, token)) return error(req, "403 Forbidden", "SESSION_REQUIRED\n");
    if (!header(req, "Content-Type", type, sizeof(type), true)
        || (strcmp(type, "text/plain") && strcmp(type, "text/plain; charset=utf-8")))
        return error(req, "415 Unsupported Media Type", "TEXT_PLAIN_REQUIRED\n");
    if (!req->content_len || req->content_len >= sizeof(s_line))
        return error(req, "413 Payload Too Large", "LINE_LIMIT_383_BYTES\n");
    size_t received = 0U;
    const uint64_t read_deadline = now_ms() + 1200U;
    while (received < req->content_len) {
        if (now_ms() >= read_deadline) return error(req, "408 Request Timeout", "BODY_TIMEOUT_NOT_SUBMITTED\n");
        const int count = httpd_req_recv(req, s_line + received, req->content_len - received);
        if (count <= 0) return error(req, "408 Request Timeout", "BODY_INCOMPLETE_NOT_SUBMITTED\n");
        received += (size_t)count;
    }
    s_line[received] = 0;
    bool mutation = false;
    if (!trilink_web_validate_line(s_line, received, &mutation))
        return error(req, "400 Bad Request", "INVALID_OR_UNSUPPORTED_COMMAND\n");
    if (mutation && (!header(req, "X-TriLink-Confirm", confirm, sizeof(confirm), true)
        || strcmp(confirm, "execute"))) return error(req, "409 Conflict", "EXPLICIT_CONFIRM_REQUIRED_NOT_SUBMITTED\n");
    uint32_t ticket = 0;
    uint64_t now = now_ms();
    uint64_t deadline = now + TRILINK_WEB_WAIT_MS;
    if (mutation && (!header(req, "X-TriLink-Deadline", expires, sizeof(expires), true)
        || !trilink_web_deadline(expires, now, &deadline)))
        return error(req, "409 Conflict", "INTENT_EXPIRED_OR_MISSING_NOT_SUBMITTED\n");
    if (!connection_alive(req)) return ESP_FAIL;
    portENTER_CRITICAL(&s_lock);
    bool queued = trilink_web_enqueue(&s_box, s_line, received, now, &ticket);
    if (queued && deadline < s_box.deadline) s_box.deadline = deadline;
    portEXIT_CRITICAL(&s_lock);
    if (!queued) return error(req, "503 Service Unavailable", "BUSY_NOT_SUBMITTED\n");
    size_t length = 0;
    for (;;) {
        if (!connection_alive(req)) {
            portENTER_CRITICAL(&s_lock); trilink_web_abandon(&s_box, ticket); portEXIT_CRITICAL(&s_lock);
            return ESP_FAIL;
        }
        now = now_ms();
        portENTER_CRITICAL(&s_lock);
        const int result = trilink_web_collect(&s_box, ticket, now, s_response, sizeof(s_response), &length);
        portEXIT_CRITICAL(&s_lock);
        if (result > 0) break;
        if (result < 0) return error(req, "504 Gateway Timeout", "RESULT_UNKNOWN_DO_NOT_REPLAY_REFRESH_STATUS\n");
        vTaskDelay(pdMS_TO_TICKS(10U));
    }
    response_headers(req); (void)httpd_resp_set_type(req, "text/plain; charset=utf-8");
    return httpd_resp_send(req, s_response, (ssize_t)length);
}

esp_err_t trilink_web_start(void)
{
    if (s_server) return ESP_ERR_INVALID_STATE;
    uint8_t random[16]; static const char hex[] = "0123456789abcdef";
    esp_fill_random(random, sizeof(random));
    for (unsigned i = 0; i < sizeof(random); ++i) { s_token[2U*i] = hex[random[i] >> 4U]; s_token[2U*i+1U] = hex[random[i] & 15U]; }
    s_token[32] = 0;
    httpd_config_t config = HTTPD_DEFAULT_CONFIG();
    config.stack_size = 4096U; config.max_open_sockets = 3U;
    config.max_uri_handlers = 5U; config.max_resp_headers = 8U;
    config.recv_wait_timeout = 1U; config.send_wait_timeout = 1U;
    config.lru_purge_enable = true;
    esp_err_t result = httpd_start(&s_server, &config);
    if (result != ESP_OK) { s_server = NULL; return result; }
    const httpd_uri_t routes[] = {
        {.uri="/", .method=HTTP_GET, .handler=asset},
        {.uri="/app.js", .method=HTTP_GET, .handler=asset},
        {.uri="/app.css", .method=HTTP_GET, .handler=asset},
        {.uri="/api/v1/session", .method=HTTP_GET, .handler=session},
        {.uri="/api/v1/exchange", .method=HTTP_POST, .handler=exchange},
    };
    for (unsigned i = 0; i < sizeof(routes)/sizeof(routes[0]); ++i) {
        result = httpd_register_uri_handler(s_server, &routes[i]);
        if (result != ESP_OK) { (void)httpd_stop(s_server); s_server = NULL; return result; }
    }
    portENTER_CRITICAL(&s_lock); s_ready = true; portEXIT_CRITICAL(&s_lock);
    return ESP_OK;
}
