#include "trilink_web_policy.h"
#include "trilink_control.h"
#include "trilink_route_control.h"
#include <string.h>

_Static_assert(sizeof(trilink_web_mailbox_t) <= 2496U, "web mailbox RAM exceeds bound");

bool trilink_web_allowed_host(const char *host)
{
    return host && (!strcmp(host, "192.168.240.1") || !strcmp(host, "192.168.240.1:80"));
}

bool trilink_web_allowed_origin(const char *origin)
{
    return !origin || !origin[0] || !strcmp(origin, "http://192.168.240.1")
        || !strcmp(origin, "http://192.168.240.1:80");
}

bool trilink_web_local_ipv4(uint32_t local, uint32_t peer)
{
    /* Host byte order, exact USB destination and unicast /24 peer only. */
    return local == 0xc0a8f001U && (peer == 0xc0a8f002U || peer == 0xc0a8f003U);
}

bool trilink_web_deadline(const char *text, uint64_t now, uint64_t *deadline)
{
    if (!text || !text[0] || !deadline) return false;
    uint64_t value = 0;
    for (size_t i = 0; text[i]; ++i) {
        if (i >= 20U || text[i] < '0' || text[i] > '9'
            || value > (UINT64_MAX - (unsigned)(text[i]-'0')) / 10U) return false;
        value = value * 10U + (unsigned)(text[i]-'0');
    }
    if (value <= now || value - now > 5000U) return false;
    *deadline = value;
    return true;
}

bool trilink_web_token_equal(const char *expected, const char *provided)
{
    if (!expected || !provided || strlen(expected) != 32U || strlen(provided) != 32U) return false;
    unsigned difference = 0U;
    for (unsigned i = 0; i < 32U; ++i) difference |= (unsigned char)expected[i] ^ (unsigned char)provided[i];
    return difference == 0U;
}

bool trilink_web_validate_line(const char *line, size_t length, bool *mutation)
{
    if (mutation) *mutation = false;
    if (!line || !mutation || length < 2U || length >= TRILINK_WEB_LINE_MAX || line[length-1U] != '\n') return false;
    /* No CR, NUL, tabs, extra lines, high bytes, or ambiguous whitespace. */
    for (size_t i = 0; i + 1U < length; ++i) {
        unsigned char c = (unsigned char)line[i];
        if (c < 32U || c > 126U || (c == ' ' && (!i || line[i-1U] == ' '))) return false;
    }
    if (line[length-2U] == ' ') return false;
    if (!strncmp(line, "TRILINK/1 ", 10U)) {
        trilink_control_parser_t parser = {0};
        trilink_control_command_t command = {0};
        for (size_t i = 0; i < length; ++i) (void)trilink_control_feed(&parser, (uint8_t)line[i], &command);
        return command.kind == TRILINK_CONTROL_HELLO || command.kind == TRILINK_CONTROL_SEARCH;
    }
    trilink_route_parser_t parser = {0};
    trilink_route_command_t command = {0};
    for (size_t i = 0; i < length; ++i) (void)trilink_route_control_feed(&parser, (uint8_t)line[i], &command);
    switch (command.kind) {
    case TRILINK_ROUTE_CMD_CAPS:
    case TRILINK_ROUTE_CMD_ROOM_GET:
    case TRILINK_ROUTE_CMD_RGB_RESULT: return true;
    case TRILINK_ROUTE_CMD_ROOM:
    case TRILINK_ROUTE_CMD_RGB:
    case TRILINK_ROUTE_CMD_RGB_ENABLE: *mutation = true; return true;
    default: return false;
    }
}

bool trilink_web_enqueue(trilink_web_mailbox_t *box, const char *line,
                          size_t length, uint64_t now, uint32_t *ticket)
{
    bool mutation;
    if (!box || !ticket || box->state != WEB_IDLE || box->sequence == UINT32_MAX
        || now > UINT64_MAX - TRILINK_WEB_WAIT_MS || !trilink_web_validate_line(line, length, &mutation)) return false;
    box->ticket = ++box->sequence;
    box->deadline = now + TRILINK_WEB_WAIT_MS;
    memcpy(box->line, line, length); box->line[length] = 0;
    box->reply_length = 0; box->state = WEB_QUEUED;
    *ticket = box->ticket;
    return true;
}

bool trilink_web_dequeue(trilink_web_mailbox_t *box, uint64_t now,
                          char *line, size_t capacity, uint32_t *ticket)
{
    if (!box || !line || !ticket || box->state != WEB_QUEUED) return false;
    if (now >= box->deadline) { box->state = WEB_IDLE; return false; }
    size_t length = strlen(box->line);
    if (capacity <= length) return false;
    memcpy(line, box->line, length + 1U); *ticket = box->ticket;
    box->state = WEB_RUNNING;
    return true;
}

void trilink_web_finish(trilink_web_mailbox_t *box, uint32_t ticket,
                        const char *reply, size_t length)
{
    if (!box || ticket != box->ticket) return;
    if (box->state == WEB_ABANDONED) { box->state = WEB_IDLE; return; }
    if (box->state != WEB_RUNNING) return;
    if (!reply || !length || length >= sizeof(box->reply)) {
        static const char error[] = "TRILINK/3 ERROR - TRANSPORT_RESULT_UNKNOWN\n";
        memcpy(box->reply, error, sizeof(error)); box->reply_length = sizeof(error)-1U;
    } else {
        memcpy(box->reply, reply, length); box->reply[length] = 0; box->reply_length = length;
    }
    box->state = WEB_DONE;
}

void trilink_web_abandon(trilink_web_mailbox_t *box, uint32_t ticket)
{
    if (!box || ticket != box->ticket) return;
    if (box->state == WEB_RUNNING) box->state = WEB_ABANDONED;
    else if (box->state == WEB_QUEUED || box->state == WEB_DONE) box->state = WEB_IDLE;
}

int trilink_web_collect(trilink_web_mailbox_t *box, uint32_t ticket,
                        uint64_t now, char *reply, size_t capacity, size_t *length)
{
    if (length) *length = 0;
    if (!box || !reply || !length || ticket != box->ticket) return -1;
    if (box->state == WEB_DONE) {
        if (capacity <= box->reply_length) { box->state = WEB_IDLE; return -1; }
        memcpy(reply, box->reply, box->reply_length+1U); *length = box->reply_length;
        box->state = WEB_IDLE; return 1;
    }
    if (box->state == WEB_IDLE || box->state == WEB_ABANDONED) return -1;
    if (now >= box->deadline) { trilink_web_abandon(box, ticket); return -1; }
    return 0;
}
