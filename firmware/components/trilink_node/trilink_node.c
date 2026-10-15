#include "trilink_node.h"

#include <inttypes.h>
#include <stddef.h>
#include <string.h>

#include "esp_log.h"
#include "esp_mac.h"
#include "esp_random.h"
#include "esp_timer.h"
#include "freertos/FreeRTOS.h"
#include "freertos/task.h"
#include "freertos/queue.h"
#include "trilink_bridge.h"
#include "trilink_cluster.h"
#include "trilink_radio.h"

#define TRILINK_NODE_TASK_STACK         4096U
#define TRILINK_NODE_TASK_PRIORITY      5U
#define TRILINK_NODE_HELLO_INTERVAL_MS  500U
#define TRILINK_NODE_HEARTBEAT_PERIOD_MS 1000U
#define TRILINK_NODE_PEER_TIMEOUT_MS    TRILINK_DISCOVERY_TTL_MS
#define TRILINK_NODE_DIAGNOSTIC_MS      5000U

typedef struct {
    TaskHandle_t task;
    trilink_cluster_t cluster;
    uint32_t boot_id;
    uint32_t next_sequence;
    uint32_t rx_valid;
    uint32_t rx_invalid;
    uint32_t rx_wrong_group;
    uint32_t rx_deferred;
    uint32_t tx_frame_failures;
    bool initialized;
    bool last_complete;
} trilink_node_context_t;

static const char *TAG = "trilink_node";
static trilink_node_context_t s_node;
static trilink_node_status_t s_status;
static trilink_discovery_snapshot_t s_discovery;
static portMUX_TYPE s_status_lock = portMUX_INITIALIZER_UNLOCKED;

/* Radio waits belong here, never in the node owner or USB task. Both queues
 * own their bytes; no borrowed RX-pool pointer survives process_rx(). */
#define TX_DEPTH 8U
#define ACK_DEPTH 4U
#define TX_LIFETIME_MS 600U
typedef struct {
    uint8_t destination[6], data[TRILINK_FRAME_MAX_SIZE];
    uint16_t length;
    uint64_t expires;
} tx_item_t;
static QueueHandle_t s_tx_queue, s_ack_queue;
static StaticQueue_t s_tx_control, s_ack_control;
static uint8_t s_tx_storage[TX_DEPTH * sizeof(tx_item_t)];
static uint8_t s_ack_storage[ACK_DEPTH * sizeof(tx_item_t)];
static TaskHandle_t s_tx_task;
static tx_item_t s_tx_item; /* TX worker only */
static uint32_t s_tx_expired;

static uint64_t monotonic_ms(void)
{
    return (uint64_t)(esp_timer_get_time() / 1000LL);
}

static esp_err_t enqueue_wire(const uint8_t destination[6], const uint8_t *data,
                              size_t length, bool acknowledgement)
{
    if (length > TRILINK_FRAME_MAX_SIZE) return ESP_ERR_INVALID_SIZE;
    tx_item_t item = {.length = (uint16_t)length, .expires = monotonic_ms() + TX_LIFETIME_MS};
    memcpy(item.destination, destination, 6U);
    memcpy(item.data, data, length);
    return xQueueSend(acknowledgement ? s_ack_queue : s_tx_queue, &item, 0) == pdTRUE
               ? ESP_OK : ESP_ERR_NO_MEM;
}

static void tx_worker(void *argument)
{
    (void)argument;
    unsigned control_burst = 0;
    for (;;) {
        bool have = false;
        if (control_burst < ACK_DEPTH && xQueueReceive(s_ack_queue, &s_tx_item, 0) == pdTRUE) {
            ++control_burst;
            have = true;
        } else if (xQueueReceive(s_tx_queue, &s_tx_item, pdMS_TO_TICKS(20U)) == pdTRUE) {
            control_burst = 0;
            have = true;
        } else control_burst = 0;
        if (!have) continue;
        if (monotonic_ms() >= s_tx_item.expires) { ++s_tx_expired; continue; }
        const bool broadcast = (s_tx_item.destination[0] & 1U) != 0U;
        esp_err_t result = broadcast ? ESP_OK : trilink_radio_add_peer(s_tx_item.destination);
        if (result == ESP_OK)
            result = trilink_radio_send(s_tx_item.destination, s_tx_item.data, s_tx_item.length);
        if (result != ESP_OK)
            ESP_LOGD(TAG, "TX worker result=%s expired=%" PRIu32 " stack_hwm=%u",
                     esp_err_to_name(result), s_tx_expired,
                     (unsigned)uxTaskGetStackHighWaterMark(NULL));
    }
}

static void update_status(void)
{
    trilink_radio_stats_t radio = {0};
    trilink_radio_get_stats(&radio);
    trilink_node_status_t next = {
        .started = s_node.initialized,
        .cluster_complete = trilink_cluster_is_complete(&s_node.cluster),
        .local_is_leader = trilink_cluster_is_local_leader(&s_node.cluster),
        .boot_id = s_node.boot_id,
        .next_sequence = s_node.next_sequence,
        .active_peers = (uint32_t)trilink_cluster_peer_count(&s_node.cluster),
        .rx_valid = s_node.rx_valid,
        .rx_invalid = s_node.rx_invalid,
        .rx_wrong_group = s_node.rx_wrong_group,
        .rx_deferred = s_node.rx_deferred,
        .rx_transport_dropped = radio.rx_dropped,
        .tx_frame_failures = s_node.tx_frame_failures,
    };
    memcpy(next.local_mac, s_node.cluster.local_mac, TRILINK_MAC_LEN);
    (void)trilink_cluster_get_leader(&s_node.cluster, next.leader_mac);

    trilink_discovery_snapshot_t discovery = {
        .ready = s_node.initialized,
        .capabilities = TRILINK_CAP_ESPNOW_UNICAST | TRILINK_CAP_USB_PC_LINK | TRILINK_CAP_AUTO_ROUTE | TRILINK_CAP_ROOM_RGB | TRILINK_CAP_NODE_MODULES,
        .peer_count = trilink_cluster_peer_count(&s_node.cluster),
    };
    memcpy(discovery.local_mac, s_node.cluster.local_mac, TRILINK_MAC_LEN);
    for (size_t i = 0; i < discovery.peer_count; ++i) {
        const trilink_peer_t *peer = trilink_cluster_peer_at(&s_node.cluster, i);
        memcpy(discovery.peers[i].mac, peer->mac, TRILINK_MAC_LEN);
        discovery.peers[i].rssi = peer->last_rssi;
        discovery.peers[i].last_seen_ms = peer->last_seen_ms;
    }

    portENTER_CRITICAL(&s_status_lock);
    s_status = next;
    s_discovery = discovery;
    portEXIT_CRITICAL(&s_status_lock);
}

static esp_err_t encode_local_frame(uint8_t type,
                                    const uint8_t *payload,
                                    uint16_t payload_len,
                                    uint8_t output[TRILINK_FRAME_MAX_SIZE],
                                    size_t *output_len)
{
    if ((output == NULL) || (output_len == NULL)
        || ((payload == NULL) && (payload_len != 0U))) {
        return ESP_ERR_INVALID_ARG;
    }

    const trilink_frame_view_t frame = {
        .type = type,
        .flags = 0U,
        .payload_len = payload_len,
        .group_id = TRILINK_NODE_GROUP_ID,
        .boot_id = s_node.boot_id,
        .term = 0U,
        .sequence = s_node.next_sequence++,
        .payload = payload,
    };
    return trilink_frame_encode_view(&frame,
                                     output,
                                     TRILINK_FRAME_MAX_SIZE,
                                     output_len)
               ? ESP_OK
               : ESP_ERR_INVALID_ARG;
}

esp_err_t trilink_node_queue_payload(const uint8_t destination[6],
                                    const uint8_t *payload, size_t length,
                                    bool acknowledgement)
{
    if (destination == NULL || payload == NULL || length > TRILINK_PAYLOAD_MAX)
        return ESP_ERR_INVALID_ARG;
    uint8_t encoded[TRILINK_FRAME_MAX_SIZE];
    size_t encoded_len = 0U;
    esp_err_t result = encode_local_frame(TRILINK_MSG_APP_DATA, payload,
                                          (uint16_t)length, encoded, &encoded_len);
    return result == ESP_OK ? enqueue_wire(destination, encoded, encoded_len, acknowledgement) : result;
}

static void record_send_result(esp_err_t result, const char *kind)
{
    if (result == ESP_OK) {
        return;
    }
    ++s_node.tx_frame_failures;
    ESP_LOGW(TAG, "%s send failed: %s", kind, esp_err_to_name(result));
}

static void send_hello(void)
{
    const trilink_hello_t hello = {
        /* app_main starts USB before the node; no game/room/file claims. */
        .capabilities = TRILINK_CAP_ESPNOW_UNICAST | TRILINK_CAP_USB_PC_LINK | TRILINK_CAP_AUTO_ROUTE | TRILINK_CAP_ROOM_RGB | TRILINK_CAP_NODE_MODULES,
        .hello_interval_ms = TRILINK_NODE_HELLO_INTERVAL_MS,
        .max_nodes = TRILINK_ROOM_MAX_MEMBERS,
    };
    uint8_t hello_payload[TRILINK_HELLO_PAYLOAD_SIZE];
    if (!trilink_hello_encode(&hello,
                              hello_payload,
                              sizeof(hello_payload))) {
        ++s_node.tx_frame_failures;
        return;
    }

    uint8_t encoded[TRILINK_FRAME_MAX_SIZE];
    size_t encoded_len = 0U;
    esp_err_t result = encode_local_frame(TRILINK_MSG_HELLO,
                                          hello_payload,
                                          sizeof(hello_payload),
                                          encoded,
                                          &encoded_len);
    if (result == ESP_OK) {
        static const uint8_t broadcast[6] = {255,255,255,255,255,255};
        result = enqueue_wire(broadcast, encoded, encoded_len, false);
    }
    record_send_result(result, "HELLO");
}

static void send_heartbeats(void)
{
    const size_t peer_count = trilink_cluster_peer_count(&s_node.cluster);
    for (size_t index = 0; index < peer_count; ++index) {
        const trilink_peer_t *peer = trilink_cluster_peer_at(&s_node.cluster,
                                                             index);
        if (peer == NULL) {
            continue;
        }

        uint8_t encoded[TRILINK_FRAME_MAX_SIZE];
        size_t encoded_len = 0U;
        esp_err_t result = encode_local_frame(TRILINK_MSG_HEARTBEAT,
                                              NULL,
                                              0U,
                                              encoded,
                                              &encoded_len);
        if (result == ESP_OK) {
            result = enqueue_wire(peer->mac, encoded, encoded_len, false);
        }
        record_send_result(result, "HEARTBEAT");
    }
}

static void log_leader_change(const uint8_t previous[TRILINK_MAC_LEN])
{
    uint8_t leader[TRILINK_MAC_LEN];
    if (!trilink_cluster_get_leader(&s_node.cluster, leader)
        || (memcmp(previous, leader, TRILINK_MAC_LEN) == 0)) {
        return;
    }

    ESP_LOGI(TAG,
             "leader=" MACSTR " local=%s peers=%u policy=lowest-mac",
             MAC2STR(leader),
             trilink_cluster_is_local_leader(&s_node.cluster) ? "yes" : "no",
             (unsigned)trilink_cluster_peer_count(&s_node.cluster));
}

static void handle_hello(const trilink_radio_rx_frame_t *event,
                         const trilink_frame_view_t *frame,
                         uint64_t now_ms)
{
    trilink_hello_t hello;
    if (!trilink_hello_decode(frame->payload, frame->payload_len, &hello)) {
        ++s_node.rx_invalid;
        return;
    }

    uint8_t previous_leader[TRILINK_MAC_LEN];
    (void)trilink_cluster_get_leader(&s_node.cluster, previous_leader);
    const trilink_peer_result_t result = trilink_cluster_observe_hello(
        &s_node.cluster,
        event->source_mac,
        frame->boot_id,
        hello.capabilities,
        frame->sequence,
        event->rssi,
        now_ms);

    if ((result == TRILINK_PEER_ADDED)
        || (result == TRILINK_PEER_RESTARTED)) {
        const esp_err_t peer_result = trilink_radio_add_peer(event->source_mac);
        ESP_LOGI(TAG,
                 "peer=" MACSTR " event=%s boot=%08" PRIx32
                 " rssi=%d unicast=%s peers=%u/6",
                 MAC2STR(event->source_mac),
                 trilink_peer_result_name(result),
                 frame->boot_id,
                 event->rssi,
                 esp_err_to_name(peer_result),
                 (unsigned)trilink_cluster_peer_count(&s_node.cluster));
    } else if (result == TRILINK_PEER_TABLE_FULL) {
        ESP_LOGW(TAG,
                 "peer table full; ignored source=" MACSTR,
                 MAC2STR(event->source_mac));
    } else if ((result == TRILINK_PEER_INVALID)
               || (result == TRILINK_PEER_SELF)) {
        ++s_node.rx_invalid;
    }
    log_leader_change(previous_leader);
}

static void handle_heartbeat(const trilink_radio_rx_frame_t *event,
                             const trilink_frame_view_t *frame,
                             uint64_t now_ms)
{
    if (frame->payload_len != 0U) {
        ++s_node.rx_invalid;
        return;
    }
    const trilink_peer_result_t result = trilink_cluster_observe_heartbeat(
        &s_node.cluster,
        event->source_mac,
        frame->boot_id,
        frame->sequence,
        event->rssi,
        now_ms);
    if ((result == TRILINK_PEER_INVALID) || (result == TRILINK_PEER_SELF)) {
        ++s_node.rx_invalid;
    }
}

static void process_rx(const trilink_radio_rx_frame_t *event, uint64_t now_ms)
{
    trilink_frame_view_t frame;
    const trilink_decode_result_t decode = trilink_frame_decode_view(
        event->data,
        event->data_len,
        &frame);
    if (decode != TRILINK_DECODE_OK) {
        ++s_node.rx_invalid;
        ESP_LOGD(TAG,
                 "drop source=" MACSTR " decode=%s len=%u",
                 MAC2STR(event->source_mac),
                 trilink_decode_result_name(decode),
                 event->data_len);
        return;
    }
    if (frame.group_id != TRILINK_NODE_GROUP_ID) {
        ++s_node.rx_wrong_group;
        return;
    }

    ++s_node.rx_valid;
    switch (frame.type) {
    case TRILINK_MSG_HELLO:
        handle_hello(event, &frame, now_ms);
        break;
    case TRILINK_MSG_HEARTBEAT:
        handle_heartbeat(event, &frame, now_ms);
        break;
    case TRILINK_MSG_APP_DATA:
        trilink_bridge_input(event->source_mac, frame.payload, frame.payload_len, now_ms);
        break;
    default:
        /*
         * Future APP_DATA service dispatch lands here and must finish before
         * the borrowed radio slot is released by trilink_node_task().
         */
        ++s_node.rx_deferred;
        break;
    }
}

static void expire_peers(uint64_t now_ms)
{
    uint8_t previous_leader[TRILINK_MAC_LEN];
    uint8_t expired[TRILINK_CLUSTER_MAX_PEERS][TRILINK_MAC_LEN];
    (void)trilink_cluster_get_leader(&s_node.cluster, previous_leader);
    const size_t count = trilink_cluster_expire(
        &s_node.cluster,
        now_ms,
        TRILINK_NODE_PEER_TIMEOUT_MS,
        expired,
        TRILINK_CLUSTER_MAX_PEERS);

    for (size_t index = 0;
         (index < count) && (index < TRILINK_CLUSTER_MAX_PEERS);
         ++index) {
        const esp_err_t result = trilink_radio_remove_peer(expired[index]);
        ESP_LOGW(TAG,
                 "peer expired=" MACSTR " unicast_remove=%s peers=%u/6",
                 MAC2STR(expired[index]),
                 esp_err_to_name(result),
                 (unsigned)trilink_cluster_peer_count(&s_node.cluster));
    }
    if (count != 0U) {
        log_leader_change(previous_leader);
    }
}

static void log_cluster_state(void)
{
    const bool complete = trilink_cluster_is_complete(&s_node.cluster);
    if (complete != s_node.last_complete) {
        ESP_LOGI(TAG,
                 "discovery=%s visible_nodes=%u application_target=%u",
                 complete ? "READY" : "DISCOVERY",
                 (unsigned)(trilink_cluster_peer_count(&s_node.cluster) + 1U),
                 TRILINK_CLUSTER_NODE_COUNT);
        s_node.last_complete = complete;
    }
}

static void update_route_neighbors(void)
{
    uint8_t macs[TRILINK_DISCOVERY_MAX_PEERS][TRILINK_MAC_LEN];
    size_t count = 0U;
    for (size_t i = 0; i < trilink_cluster_peer_count(&s_node.cluster); ++i) {
        const trilink_peer_t *peer = trilink_cluster_peer_at(&s_node.cluster, i);
        if (peer != NULL && (peer->capabilities & TRILINK_CAP_AUTO_ROUTE) != 0U) {
            memcpy(macs[count++], peer->mac, TRILINK_MAC_LEN);
        }
    }
    trilink_bridge_neighbors(&macs[0][0], count);
}

static void log_diagnostics(void)
{
    trilink_radio_stats_t radio = {0};
    trilink_radio_get_stats(&radio);
    uint8_t leader[TRILINK_MAC_LEN];
    (void)trilink_cluster_get_leader(&s_node.cluster, leader);

    ESP_LOGI(TAG,
             "diag peers=%u/6 discovery_lowest_mac=" MACSTR " local_lowest=%s"
             " node_rx_ok=%" PRIu32 " node_rx_bad=%" PRIu32
             " wrong_group=%" PRIu32 " deferred=%" PRIu32
             " radio_tx_ok=%" PRIu32 " radio_tx_fail=%" PRIu32
             " radio_tx_timeout=%" PRIu32
             " radio_rx_total=%" PRIu32 " radio_rx_drop=%" PRIu32
             " rx_acq=%" PRIu32 " rx_rel=%" PRIu32
             " rx_ready=%" PRIu32 " rx_in_use=%" PRIu32
             " rx_owner_err=%" PRIu32 " stack_hwm=%u",
             (unsigned)trilink_cluster_peer_count(&s_node.cluster),
             MAC2STR(leader),
             trilink_cluster_is_local_leader(&s_node.cluster) ? "yes" : "no",
             s_node.rx_valid,
             s_node.rx_invalid,
             s_node.rx_wrong_group,
             s_node.rx_deferred,
             radio.tx_success,
             radio.tx_failed + radio.tx_api_errors,
             radio.tx_timeouts,
             radio.rx_queued,
             radio.rx_dropped,
             radio.rx_acquired,
             radio.rx_released,
             radio.rx_ready,
             radio.rx_in_use,
             radio.rx_ownership_errors,
             (unsigned)uxTaskGetStackHighWaterMark(NULL));
    ESP_LOGI(TAG, "txdiag data_queued=%u ack_queued=%u tx_stack_hwm=%u",
             (unsigned)uxQueueMessagesWaiting(s_tx_queue),
             (unsigned)uxQueueMessagesWaiting(s_ack_queue),
             (unsigned)uxTaskGetStackHighWaterMark(s_tx_task));
}

static void trilink_node_task(void *argument)
{
    (void)argument;
    uint64_t last_hello_ms = 0U;
    uint64_t last_heartbeat_ms = 0U;
    uint64_t last_diagnostic_ms = monotonic_ms();

    send_hello();
    last_hello_ms = monotonic_ms();
    for (;;) {
        const uint64_t before_wait = monotonic_ms();
        const uint64_t hello_due = last_hello_ms
                                   + TRILINK_NODE_HELLO_INTERVAL_MS;
        uint32_t wait_ms = (before_wait >= hello_due)
                                     ? 0U
                                     : (uint32_t)(hello_due - before_wait);
        if (wait_ms > 20U) wait_ms = 20U;
        const trilink_radio_rx_frame_t *frame = NULL;
        const esp_err_t receive_result = trilink_radio_rx_acquire(&frame,
                                                                  wait_ms);
        if (receive_result == ESP_OK) {
            process_rx(frame, monotonic_ms());
            const esp_err_t release_result = trilink_radio_rx_release(frame);
            if (release_result != ESP_OK) {
                ESP_LOGE(TAG,
                         "RX slot release failed: %s",
                         esp_err_to_name(release_result));
            }
        } else if (receive_result != ESP_ERR_TIMEOUT) {
            ESP_LOGE(TAG,
                     "RX acquire failed: %s",
                     esp_err_to_name(receive_result));
        }

        uint64_t now_ms = monotonic_ms();
        expire_peers(now_ms);
        update_route_neighbors();
        trilink_bridge_process(now_ms);
        if ((now_ms - last_hello_ms) >= TRILINK_NODE_HELLO_INTERVAL_MS) {
            send_hello();
            last_hello_ms = monotonic_ms();
            now_ms = last_hello_ms;
        }
        if ((now_ms - last_heartbeat_ms)
            >= TRILINK_NODE_HEARTBEAT_PERIOD_MS) {
            send_heartbeats();
            last_heartbeat_ms = monotonic_ms();
            now_ms = last_heartbeat_ms;
        }

        log_cluster_state();
        update_status();
        if ((now_ms - last_diagnostic_ms) >= TRILINK_NODE_DIAGNOSTIC_MS) {
            log_diagnostics();
            last_diagnostic_ms = now_ms;
        }
    }
}

esp_err_t trilink_node_start(void)
{
    if (s_node.initialized) {
        return ESP_ERR_INVALID_STATE;
    }

    memset(&s_node, 0, sizeof(s_node));
    esp_err_t result = trilink_radio_init(TRILINK_NODE_RADIO_CHANNEL);
    if (result != ESP_OK) {
        return result;
    }

    uint8_t local_mac[TRILINK_MAC_LEN];
    result = trilink_radio_get_local_mac(local_mac);
    if (result != ESP_OK) {
        (void)trilink_radio_deinit();
        return result;
    }

    s_node.boot_id = esp_random();
    if (s_node.boot_id == 0U) {
        s_node.boot_id = 1U;
    }
    if (!trilink_cluster_init(&s_node.cluster, local_mac, s_node.boot_id)) {
        (void)trilink_radio_deinit();
        return ESP_ERR_INVALID_STATE;
    }

    s_tx_queue = xQueueCreateStatic(TX_DEPTH, sizeof(tx_item_t), s_tx_storage, &s_tx_control);
    s_ack_queue = xQueueCreateStatic(ACK_DEPTH, sizeof(tx_item_t), s_ack_storage, &s_ack_control);
    result = trilink_bridge_init(local_mac, s_node.boot_id);
    if (result != ESP_OK || s_tx_queue == NULL || s_ack_queue == NULL) {
        (void)trilink_radio_deinit();
        return result != ESP_OK ? result : ESP_ERR_NO_MEM;
    }
    if (xTaskCreate(tx_worker, "trilink_tx", 3072U, NULL, 4U, &s_tx_task) != pdPASS) {
        (void)trilink_radio_deinit();
        return ESP_ERR_NO_MEM;
    }

    s_node.initialized = true;
    update_status();
    if (xTaskCreate(trilink_node_task,
                    "trilink_node",
                    TRILINK_NODE_TASK_STACK,
                    NULL,
                    TRILINK_NODE_TASK_PRIORITY,
                    &s_node.task) != pdPASS) {
        s_node.initialized = false;
        vTaskDelete(s_tx_task);
        s_tx_task = NULL;
        (void)trilink_radio_deinit();
        update_status();
        return ESP_ERR_NO_MEM;
    }

    ESP_LOGI(TAG,
             "node started mac=" MACSTR " boot=%08" PRIx32
             " group=%08" PRIx32 " channel=%u peer_limit=%u",
             MAC2STR(local_mac),
             s_node.boot_id,
             (uint32_t)TRILINK_NODE_GROUP_ID,
             TRILINK_NODE_RADIO_CHANNEL,
             TRILINK_CLUSTER_MAX_PEERS);
    return ESP_OK;
}

esp_err_t trilink_node_get_status(trilink_node_status_t *status)
{
    if (status == NULL) {
        return ESP_ERR_INVALID_ARG;
    }
    portENTER_CRITICAL(&s_status_lock);
    *status = s_status;
    portEXIT_CRITICAL(&s_status_lock);
    return status->started ? ESP_OK : ESP_ERR_INVALID_STATE;
}

esp_err_t trilink_node_get_discovery(trilink_discovery_snapshot_t *snapshot)
{
    if (snapshot == NULL) {
        return ESP_ERR_INVALID_ARG;
    }
    portENTER_CRITICAL(&s_status_lock);
    *snapshot = s_discovery;
    portEXIT_CRITICAL(&s_status_lock);
    return snapshot->ready ? ESP_OK : ESP_ERR_INVALID_STATE;
}
