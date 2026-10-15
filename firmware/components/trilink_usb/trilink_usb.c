#include "trilink_usb.h"

#include <inttypes.h>
#include <stdio.h>
#include <string.h>
#include "esp_log.h"
#include "esp_mac.h"
#include "esp_timer.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "tinyusb.h"
#include "tinyusb_default_config.h"
#include "tinyusb_cdc_acm.h"
#include "trilink_control.h"
#include "trilink_node.h"
#include "trilink_bridge.h"
#include "trilink_web.h"
#include "trilink_usb_mux.h"
#include "trilink_usb_network.h"

#define USB_TASK_STACK 3072U
#define USB_COMMANDS_PER_SECOND 20U
#define USB_PARTIAL_LINE_TIMEOUT_MS 500U
#define USB_DESCRIPTOR_LENGTH (TUD_CONFIG_DESC_LEN + TUD_CDC_DESC_LEN + TUD_CDC_NCM_DESC_LEN)

/* Single owner for parsers, bridge producer/reply consumer and CDC transmit.
 * HTTP handlers never invoke bridge or node state-changing operations. */
static const char *TAG = "trilink_usb";
static TaskHandle_t s_task;
static trilink_control_parser_t s_parser, s_http_parser;
static trilink_route_parser_t s_route_parser, s_http_route_parser;
static trilink_route_command_t s_route_command, s_http_route_command;
static trilink_usb_mux_t s_mux;
static char s_reply[TRILINK_CONTROL_REPLY_SIZE];
static char s_http_line[384];
static uint8_t s_chunk[64];
static uint32_t s_commands, s_invalid, s_rate_dropped, s_tx_failed, s_late_replies;
static uint64_t s_rate_start;
static unsigned s_rate_count;
static portMUX_TYPE s_event_lock = portMUX_INITIALIZER_UNLOCKED;
static uint32_t s_usb_epoch;
static bool s_usb_mounted;
static char s_serial[13];
static char s_host_mac_string[13];
static const char *s_strings[] = {
    (const char[]){0x09, 0x04}, "TriLink development", "TriLink USB Node",
    s_serial, "TriLink CDC", "TriLink NCM", s_host_mac_string
};

/* VID is Espressif's development VID, NOT a production VID allocation.
 * Product/interface strings and MI numbers form the PC discovery contract.
 * CDC: MI00/01, NCM: MI02/03, stable serial from physical Wi-Fi MAC. */
static const tusb_desc_device_t s_device_descriptor = {
    .bLength = sizeof(tusb_desc_device_t), .bDescriptorType = TUSB_DESC_DEVICE,
    .bcdUSB = 0x0200, .bDeviceClass = TUSB_CLASS_MISC,
    .bDeviceSubClass = MISC_SUBCLASS_COMMON, .bDeviceProtocol = MISC_PROTOCOL_IAD,
    .bMaxPacketSize0 = 64, .idVendor = 0x303A, .idProduct = 0x4011,
    .bcdDevice = 0x0160, .iManufacturer = 1, .iProduct = 2,
    .iSerialNumber = 3, .bNumConfigurations = 1,
};
static const uint8_t s_configuration[] = {
    /* Bus powered, 500mA envelope including S3 radio, no remote wake claim. */
    TUD_CONFIG_DESCRIPTOR(1, 4, 0, USB_DESCRIPTOR_LENGTH, 0, 500),
    TUD_CDC_DESCRIPTOR(0, 4, 0x81, 8, 0x02, 0x82, 64),
    TUD_CDC_NCM_DESCRIPTOR(2, 5, 6, 0x83, 64, 0x04, 0x84, 64, 1514),
};
_Static_assert(CFG_TUD_CDC == 1 && CFG_TUD_NCM == 1, "TriLink requires one CDC and one NCM");
_Static_assert(sizeof(s_configuration) == USB_DESCRIPTOR_LENGTH, "USB descriptor size mismatch");

static uint64_t now_ms(void) { return (uint64_t)(esp_timer_get_time() / 1000LL); }

static uint32_t usb_epoch(bool *mounted)
{
    portENTER_CRITICAL(&s_event_lock);
    uint32_t epoch = s_usb_epoch;
    *mounted = s_usb_mounted;
    portEXIT_CRITICAL(&s_event_lock);
    return epoch;
}

static void usb_event(tinyusb_event_t *event, void *argument)
{
    (void)argument;
    if (event->id != TINYUSB_EVENT_ATTACHED && event->id != TINYUSB_EVENT_DETACHED) return;
    portENTER_CRITICAL(&s_event_lock);
    s_usb_mounted = event->id == TINYUSB_EVENT_ATTACHED;
    ++s_usb_epoch;
    portEXIT_CRITICAL(&s_event_lock);
}

static void send_reply(trilink_usb_owner_t owner, uint32_t ticket, uint32_t epoch,
                       const char *reply, size_t length)
{
    if (owner == TRILINK_USB_OWNER_WEB) {
        trilink_web_complete(ticket, reply, length);
        return;
    }
    bool mounted = false;
    if (epoch != usb_epoch(&mounted) || !mounted) { ++s_tx_failed; return; }
    /* Use mounted, never tud_cdc_connected(): legacy host opens DTR/RTS=false.
     * One bounded queue + flush, executed only by this worker, never USB ISR. */
    const size_t queued = tinyusb_cdcacm_write_queue(TINYUSB_CDC_ACM_0,
        (const uint8_t *)reply, length);
    const esp_err_t error = tinyusb_cdcacm_write_flush(TINYUSB_CDC_ACM_0, pdMS_TO_TICKS(100U));
    if (queued != length || error != ESP_OK) ++s_tx_failed;
}

static void send_error_version(trilink_usb_owner_t owner, uint32_t ticket, uint32_t epoch,
                               unsigned version, const char *nonce, const char *error)
{
    const int size = snprintf(s_reply, sizeof(s_reply), "\nTRILINK/%u ERROR %s %s\n",
                              version, nonce && *nonce ? nonce : "-", error);
    if (size > 0 && (size_t)size < sizeof(s_reply))
        send_reply(owner, ticket, epoch, s_reply, (size_t)size);
}

static void send_error(trilink_usb_owner_t owner, uint32_t ticket, uint32_t epoch,
                       const char *nonce, const char *error)
{
    send_error_version(owner, ticket, epoch, 3U, nonce, error);
}

static void command_complete(trilink_usb_owner_t owner, uint32_t ticket, uint32_t epoch,
                              const trilink_control_command_t *legacy,
                              const trilink_route_command_t *route, uint64_t now)
{
    if (now < s_rate_start || now - s_rate_start >= 1000U) {
        s_rate_start = now;
        s_rate_count = 0;
    }
    if (s_rate_count >= USB_COMMANDS_PER_SECOND) {
        ++s_rate_dropped;
        if (owner == TRILINK_USB_OWNER_WEB) {
            const bool v1 = route->kind == TRILINK_ROUTE_CMD_LEGACY;
            send_error_version(owner, ticket, epoch, v1 ? 1U : 3U,
                               v1 ? legacy->nonce : route->nonce, "RATE_LIMIT_NOT_SUBMITTED");
        }
        return; /* CDC preserves the existing no-error-flood policy. */
    }
    ++s_rate_count;
    ++s_commands;
    if (route->kind != TRILINK_ROUTE_CMD_LEGACY) {
        if (s_mux.owner != TRILINK_USB_OWNER_NONE) {
            send_error(owner, ticket, epoch, route->nonce, "BUSY_OR_NOT_READY");
            return;
        }
        if (!trilink_usb_mux_begin(&s_mux, owner, ticket, epoch,
                                  route->nonce[0] ? route->nonce : "-", now)) {
            send_error(owner, ticket, epoch, route->nonce, "BAD_REQUEST");
            return;
        }
        if (owner == TRILINK_USB_OWNER_WEB && !trilink_web_can_submit(ticket)) {
            trilink_usb_mux_release(&s_mux);
            send_error(owner, ticket, epoch, route->nonce, "EXPIRED_NOT_SUBMITTED");
            return;
        }
        if (!trilink_bridge_submit(route)) {
            trilink_usb_mux_release(&s_mux);
            send_error(owner, ticket, epoch, route->nonce, "BUSY_OR_NOT_READY");
        }
        return;
    }
    if (legacy->kind == TRILINK_CONTROL_INVALID) ++s_invalid;
    trilink_discovery_snapshot_t snapshot = {0};
    (void)trilink_node_get_discovery(&snapshot);
    size_t length = 0;
    if (trilink_control_reply(legacy, &snapshot, now, s_reply, sizeof(s_reply), &length)) {
        send_reply(owner, ticket, epoch, s_reply, length);
    } else {
        ++s_tx_failed;
        send_error_version(owner, ticket, epoch, 1U, legacy->nonce, "INTERNAL");
    }
}

static void drain_bridge(uint64_t now)
{
    if (trilink_bridge_reply(s_reply)) {
        if (trilink_usb_mux_matches(&s_mux, s_reply)) {
            if (!s_mux.expired) send_reply(s_mux.owner, s_mux.ticket, s_mux.epoch,
                                          s_reply, strlen(s_reply));
            else ++s_late_replies;
            trilink_usb_mux_release(&s_mux);
        } else ++s_late_replies;
    }
    if (trilink_usb_mux_timeout(&s_mux, now)) {
        send_error(s_mux.owner, s_mux.ticket, s_mux.epoch, s_mux.nonce, "RESULT_UNKNOWN");
        /* Keep mux owner quarantined until its late reply, never replay. */
    }
}

static void process_web(uint32_t epoch, uint64_t now)
{
    uint32_t ticket = 0;
    if (s_mux.owner != TRILINK_USB_OWNER_NONE
        || !trilink_web_take(s_http_line, sizeof(s_http_line), &ticket)) return;
    memset(&s_http_parser, 0, sizeof(s_http_parser));
    memset(&s_http_route_parser, 0, sizeof(s_http_route_parser));
    memset(&s_http_route_command, 0, sizeof(s_http_route_command));
    bool completed = false;
    for (size_t i = 0; i < sizeof(s_http_line) && s_http_line[i]; ++i) {
        trilink_control_command_t command;
        (void)trilink_route_control_feed(&s_http_route_parser, (uint8_t)s_http_line[i], &s_http_route_command);
        if (trilink_control_feed(&s_http_parser, (uint8_t)s_http_line[i], &command)) {
            command_complete(TRILINK_USB_OWNER_WEB, ticket, epoch, &command, &s_http_route_command, now);
            completed = true;
            break;
        }
    }
    if (!completed) send_error(TRILINK_USB_OWNER_WEB, ticket, epoch, "-", "BAD_REQUEST");
}

static void usb_task(void *argument)
{
    (void)argument;
    uint64_t last_byte = now_ms(), last_log = last_byte;
    s_rate_start = last_byte;
    uint32_t last_epoch = 0;
    bool web_turn = true;
    for (;;) {
        bool mounted = false;
        const uint32_t epoch = usb_epoch(&mounted);
        uint64_t now = now_ms();
        trilink_usb_network_poll(mounted);
        if (epoch != last_epoch || now - last_byte >= USB_PARTIAL_LINE_TIMEOUT_MS) {
            memset(&s_parser, 0, sizeof(s_parser));
            memset(&s_route_parser, 0, sizeof(s_route_parser));
            last_epoch = epoch;
        }
        drain_bridge(now);
        /* Alternate turns, giving an already waiting HTTP request a chance
         * ahead of a busy CDC producer. Both ultimately share the one mux. */
        if (web_turn) process_web(epoch, now);
        size_t count = 0;
        (void)tinyusb_cdcacm_read(TINYUSB_CDC_ACM_0, s_chunk, sizeof(s_chunk), &count);
        for (size_t i = 0; i < count; ++i) {
            trilink_control_command_t command;
            last_byte = now;
            (void)trilink_route_control_feed(&s_route_parser, s_chunk[i], &s_route_command);
            if (!trilink_control_feed(&s_parser, s_chunk[i], &command)) continue;
            now = now_ms();
            command_complete(TRILINK_USB_OWNER_CDC, 0, epoch, &command, &s_route_command, now);
        }
        if (!web_turn) process_web(epoch, now);
        web_turn = !web_turn;
        drain_bridge(now_ms());
        if (now - last_log >= 5000U) {
            ESP_LOGI(TAG, "commands=%" PRIu32 " invalid=%" PRIu32 " rate_drop=%" PRIu32
                " tx_fail=%" PRIu32 " stale=%" PRIu32 " owner=%u stack_hwm=%u",
                s_commands, s_invalid, s_rate_dropped, s_tx_failed, s_late_replies,
                (unsigned)s_mux.owner, (unsigned)uxTaskGetStackHighWaterMark(NULL));
            last_log = now;
        }
        vTaskDelay(pdMS_TO_TICKS(10U));
    }
}

esp_err_t trilink_usb_start(void)
{
    if (s_task != NULL) return ESP_ERR_INVALID_STATE;
    uint8_t mac[6];
    esp_err_t result = esp_read_mac(mac, ESP_MAC_WIFI_STA);
    if (result != ESP_OK) return result;
    (void)snprintf(s_serial, sizeof(s_serial), "%02X%02X%02X%02X%02X%02X",
                   mac[0], mac[1], mac[2], mac[3], mac[4], mac[5]);
    (void)snprintf(s_host_mac_string, sizeof(s_host_mac_string), "%02X%02X%02X%02X%02X%02X",
        (mac[0] | 2U) & 0xFEU, mac[1], mac[2], mac[3], mac[4], mac[5] ^ 0x40U);
    const esp_err_t network_result = trilink_usb_network_prepare(mac);
    if (network_result != ESP_OK)
        ESP_LOGE(TAG, "NCM prepare failed: %s; keeping CDC control", esp_err_to_name(network_result));
    tinyusb_config_t config = TINYUSB_DEFAULT_CONFIG(usb_event);
    config.descriptor.device = &s_device_descriptor;
    config.descriptor.full_speed_config = s_configuration;
    config.descriptor.string = s_strings;
    config.descriptor.string_count = sizeof(s_strings) / sizeof(s_strings[0]);
    result = tinyusb_driver_install(&config);
    if (result != ESP_OK) return result;
    /* TinyUSB clears RX/TX FIFOs in cdcd_reset on each USB bus reset.
     * Pin this explicitly: reconnect must not preserve old request bytes. */
    const tud_cdc_configure_fifo_t fifo = {.rx_persistent = false, .tx_persistent = false};
    (void)tud_cdc_configure_fifo(&fifo);
    const tinyusb_config_cdcacm_t cdc = {.cdc_port = TINYUSB_CDC_ACM_0};
    result = tinyusb_cdcacm_init(&cdc);
    if (result != ESP_OK) { (void)tinyusb_driver_uninstall(); return result; }
    if (network_result == ESP_OK) {
        result = trilink_usb_network_bind();
        if (result != ESP_OK) ESP_LOGE(TAG, "NCM/web initialization: %s", esp_err_to_name(result));
    }
    if (xTaskCreate(usb_task, "trilink_usb", USB_TASK_STACK, NULL, 3U, &s_task) != pdPASS) {
        (void)tinyusb_driver_uninstall();
        (void)tinyusb_cdcacm_deinit(TINYUSB_CDC_ACM_0);
        s_task = NULL;
        return ESP_ERR_NO_MEM;
    }
    ESP_LOGI(TAG, "TinyUSB CDC+NCM ready; CDC DTR-independent, max_commands=%u/s", USB_COMMANDS_PER_SECOND);
    return ESP_OK;
}
