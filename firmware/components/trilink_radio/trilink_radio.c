#include "trilink_radio.h"

#include <stdbool.h>
#include <string.h>

#include "esp_event.h"
#include "esp_log.h"
#include "esp_netif.h"
#include "esp_now.h"
#include "esp_wifi.h"
#include "freertos/FreeRTOS.h"
#include "freertos/queue.h"
#include "freertos/semphr.h"
#include "nvs_flash.h"

#define TRILINK_RADIO_SEND_TIMEOUT_MS 500U

typedef enum {
    RX_SLOT_FREE = 0,
    RX_SLOT_FILLING,
    RX_SLOT_READY,
    RX_SLOT_ACQUIRED,
} radio_rx_slot_state_t;

_Static_assert(sizeof(trilink_radio_rx_frame_t) <= 216U,
               "radio RX frame exceeds the fixed pool budget");
_Static_assert(TRILINK_RADIO_RX_POOL_DEPTH <= UINT8_MAX,
               "radio RX slot index no longer fits in one byte");

static const char *TAG = "trilink_radio";
static const uint8_t s_broadcast_mac[TRILINK_MAC_LEN] = {
    0xFFU, 0xFFU, 0xFFU, 0xFFU, 0xFFU, 0xFFU,
};

/*
 * Wi-Fi writes each packet directly into one fixed slot.  The two queues carry
 * only one-byte indices, so acquiring a frame never copies its payload.
 */
static trilink_radio_rx_frame_t s_rx_pool[TRILINK_RADIO_RX_POOL_DEPTH];
static uint8_t s_rx_slot_state[TRILINK_RADIO_RX_POOL_DEPTH];
static QueueHandle_t s_rx_free_queue;
static QueueHandle_t s_rx_ready_queue;
static StaticQueue_t s_rx_free_queue_control;
static StaticQueue_t s_rx_ready_queue_control;
static uint8_t s_rx_free_queue_storage[TRILINK_RADIO_RX_POOL_DEPTH];
static uint8_t s_rx_ready_queue_storage[TRILINK_RADIO_RX_POOL_DEPTH];

static SemaphoreHandle_t s_tx_mutex;
static SemaphoreHandle_t s_tx_done;
static esp_now_send_status_t s_last_send_status;
static bool s_tx_in_flight;
static trilink_radio_stats_t s_stats;
static portMUX_TYPE s_stats_lock = portMUX_INITIALIZER_UNLOCKED;
static portMUX_TYPE s_rx_state_lock = portMUX_INITIALIZER_UNLOCKED;
static portMUX_TYPE s_tx_state_lock = portMUX_INITIALIZER_UNLOCKED;
static bool s_wifi_initialized;
static bool s_wifi_started;
static bool s_espnow_initialized;
static bool s_initialized;
static uint8_t s_channel;

static bool mac_is_broadcast(const uint8_t mac[TRILINK_MAC_LEN])
{
    return (mac != NULL)
           && (memcmp(mac, s_broadcast_mac, TRILINK_MAC_LEN) == 0);
}

static bool mac_is_valid_unicast(const uint8_t mac[TRILINK_MAC_LEN])
{
    if ((mac == NULL) || ((mac[0] & 0x01U) != 0U)) {
        return false;
    }

    uint8_t aggregate = 0U;
    for (size_t index = 0; index < TRILINK_MAC_LEN; ++index) {
        aggregate |= mac[index];
    }
    return aggregate != 0U;
}

static void stats_increment(uint32_t *counter)
{
    portENTER_CRITICAL(&s_stats_lock);
    ++(*counter);
    portEXIT_CRITICAL(&s_stats_lock);
}

static bool slot_transition(uint8_t index,
                            radio_rx_slot_state_t expected,
                            radio_rx_slot_state_t next)
{
    if (index >= TRILINK_RADIO_RX_POOL_DEPTH) {
        return false;
    }

    bool changed = false;
    portENTER_CRITICAL(&s_rx_state_lock);
    if (s_rx_slot_state[index] == (uint8_t)expected) {
        s_rx_slot_state[index] = (uint8_t)next;
        changed = true;
    }
    portEXIT_CRITICAL(&s_rx_state_lock);
    return changed;
}

static uint32_t rx_in_use_count(void)
{
    uint32_t count = 0U;
    portENTER_CRITICAL(&s_rx_state_lock);
    for (size_t index = 0; index < TRILINK_RADIO_RX_POOL_DEPTH; ++index) {
        if (s_rx_slot_state[index] == (uint8_t)RX_SLOT_ACQUIRED) {
            ++count;
        }
    }
    portEXIT_CRITICAL(&s_rx_state_lock);
    return count;
}

static bool create_rx_pool(void)
{
    memset(s_rx_pool, 0, sizeof(s_rx_pool));
    memset(s_rx_slot_state, RX_SLOT_FREE, sizeof(s_rx_slot_state));

    s_rx_free_queue = xQueueCreateStatic(
        TRILINK_RADIO_RX_POOL_DEPTH,
        sizeof(uint8_t),
        s_rx_free_queue_storage,
        &s_rx_free_queue_control);
    s_rx_ready_queue = xQueueCreateStatic(
        TRILINK_RADIO_RX_POOL_DEPTH,
        sizeof(uint8_t),
        s_rx_ready_queue_storage,
        &s_rx_ready_queue_control);
    if ((s_rx_free_queue == NULL) || (s_rx_ready_queue == NULL)) {
        s_rx_free_queue = NULL;
        s_rx_ready_queue = NULL;
        return false;
    }

    for (uint8_t index = 0U;
         index < TRILINK_RADIO_RX_POOL_DEPTH;
         ++index) {
        if (xQueueSend(s_rx_free_queue, &index, 0) != pdTRUE) {
            s_rx_free_queue = NULL;
            s_rx_ready_queue = NULL;
            return false;
        }
    }
    return true;
}

static TickType_t timeout_to_ticks(uint32_t timeout_ms)
{
    if (timeout_ms == 0U) {
        return 0U;
    }

    const uint64_t ticks = (((uint64_t)timeout_ms
                             * (uint64_t)configTICK_RATE_HZ)
                            + 999U)
                           / 1000U;
    if (ticks >= (uint64_t)portMAX_DELAY) {
        return portMAX_DELAY - 1U;
    }
    return (TickType_t)ticks;
}

static void radio_send_cb(const esp_now_send_info_t *tx_info,
                          esp_now_send_status_t status)
{
    (void)tx_info;

    bool notify_waiter = false;
    portENTER_CRITICAL(&s_tx_state_lock);
    if (s_tx_in_flight) {
        s_last_send_status = status;
        s_tx_in_flight = false;
        notify_waiter = true;
    }
    portEXIT_CRITICAL(&s_tx_state_lock);

    if (!notify_waiter) {
        return;
    }
    if (status == ESP_NOW_SEND_SUCCESS) {
        stats_increment(&s_stats.tx_success);
    } else {
        stats_increment(&s_stats.tx_failed);
    }
    if (s_tx_done != NULL) {
        (void)xSemaphoreGive(s_tx_done);
    }
}

static void radio_receive_cb(const esp_now_recv_info_t *receive_info,
                             const uint8_t *data,
                             int data_len)
{
    if ((receive_info == NULL) || (receive_info->src_addr == NULL)
        || (data == NULL) || (data_len <= 0)
        || ((size_t)data_len > TRILINK_FRAME_MAX_SIZE)
        || (s_rx_free_queue == NULL) || (s_rx_ready_queue == NULL)) {
        stats_increment(&s_stats.rx_dropped);
        return;
    }

    uint8_t index = 0U;
    if (xQueueReceive(s_rx_free_queue, &index, 0) != pdTRUE) {
        stats_increment(&s_stats.rx_dropped);
        return;
    }
    if (!slot_transition(index, RX_SLOT_FREE, RX_SLOT_FILLING)) {
        stats_increment(&s_stats.rx_dropped);
        return;
    }

    trilink_radio_rx_frame_t *frame = &s_rx_pool[index];
    memcpy(frame->source_mac, receive_info->src_addr, TRILINK_MAC_LEN);
    frame->data_len = (uint16_t)data_len;
    frame->rssi = (receive_info->rx_ctrl == NULL)
                      ? 0
                      : receive_info->rx_ctrl->rssi;
    memcpy(frame->data, data, frame->data_len);

    if (!slot_transition(index, RX_SLOT_FILLING, RX_SLOT_READY)
        || (xQueueSend(s_rx_ready_queue, &index, 0) != pdTRUE)) {
        if (slot_transition(index, RX_SLOT_READY, RX_SLOT_FREE)) {
            (void)xQueueSend(s_rx_free_queue, &index, 0);
        }
        stats_increment(&s_stats.rx_dropped);
        return;
    }
    stats_increment(&s_stats.rx_queued);
}

static void release_rtos_resources(void)
{
    /* RX queues and storage are static; clearing handles makes them inert. */
    s_rx_free_queue = NULL;
    s_rx_ready_queue = NULL;
    memset(s_rx_slot_state, RX_SLOT_FREE, sizeof(s_rx_slot_state));

    if (s_tx_mutex != NULL) {
        vSemaphoreDelete(s_tx_mutex);
        s_tx_mutex = NULL;
    }
    if (s_tx_done != NULL) {
        vSemaphoreDelete(s_tx_done);
        s_tx_done = NULL;
    }
}

static void reset_runtime_state(void)
{
    s_wifi_initialized = false;
    s_wifi_started = false;
    s_espnow_initialized = false;
    s_initialized = false;
    s_channel = 0U;
    portENTER_CRITICAL(&s_tx_state_lock);
    s_tx_in_flight = false;
    portEXIT_CRITICAL(&s_tx_state_lock);
}

esp_err_t trilink_radio_init(uint8_t channel)
{
    if (s_initialized) {
        return ESP_ERR_INVALID_STATE;
    }
    if ((channel < 1U) || (channel > 13U)) {
        return ESP_ERR_INVALID_ARG;
    }

    esp_err_t error = nvs_flash_init();
    if (error != ESP_OK) {
        ESP_LOGE(TAG,
                 "NVS init failed: %s; NVS is not erased automatically",
                 esp_err_to_name(error));
        return error;
    }

    memset(&s_stats, 0, sizeof(s_stats));
    if (!create_rx_pool()) {
        release_rtos_resources();
        reset_runtime_state();
        return ESP_ERR_NO_MEM;
    }

    s_tx_mutex = xSemaphoreCreateMutex();
    s_tx_done = xSemaphoreCreateBinary();
    if ((s_tx_mutex == NULL) || (s_tx_done == NULL)) {
        release_rtos_resources();
        reset_runtime_state();
        return ESP_ERR_NO_MEM;
    }

    error = esp_netif_init();
    if (error != ESP_OK) {
        goto fail;
    }
    error = esp_event_loop_create_default();
    /* USB networking may already own the process-wide default loop. */
    if (error != ESP_OK && error != ESP_ERR_INVALID_STATE) {
        goto fail;
    }

    wifi_init_config_t wifi_config = WIFI_INIT_CONFIG_DEFAULT();
    error = esp_wifi_init(&wifi_config);
    if (error != ESP_OK) {
        goto fail;
    }
    s_wifi_initialized = true;

    if ((error = esp_wifi_set_storage(WIFI_STORAGE_RAM)) != ESP_OK
        || (error = esp_wifi_set_mode(WIFI_MODE_STA)) != ESP_OK
        || (error = esp_wifi_start()) != ESP_OK) {
        goto fail;
    }
    s_wifi_started = true;

    if ((error = esp_wifi_set_ps(WIFI_PS_NONE)) != ESP_OK
        || (error = esp_wifi_set_channel(channel,
                                         WIFI_SECOND_CHAN_NONE)) != ESP_OK
        || (error = esp_now_init()) != ESP_OK) {
        goto fail;
    }
    s_espnow_initialized = true;

    if ((error = esp_now_register_send_cb(radio_send_cb)) != ESP_OK
        || (error = esp_now_register_recv_cb(radio_receive_cb)) != ESP_OK) {
        goto fail;
    }

    esp_now_peer_info_t peer = {0};
    memcpy(peer.peer_addr, s_broadcast_mac, TRILINK_MAC_LEN);
    peer.channel = channel;
    peer.ifidx = WIFI_IF_STA;
    peer.encrypt = false;
    error = esp_now_add_peer(&peer);
    if (error != ESP_OK) {
        goto fail;
    }

    s_channel = channel;
    s_initialized = true;
    ESP_LOGI(TAG,
             "ESP-NOW ready channel=%u rx_pool=%u slot_bytes=%u frame_max=%u",
             channel,
             TRILINK_RADIO_RX_POOL_DEPTH,
             (unsigned)sizeof(trilink_radio_rx_frame_t),
             TRILINK_FRAME_MAX_SIZE);
    return ESP_OK;

fail:
    if (s_espnow_initialized) {
        (void)esp_now_unregister_recv_cb();
        (void)esp_now_unregister_send_cb();
        (void)esp_now_deinit();
    }
    if (s_wifi_started) {
        (void)esp_wifi_stop();
    }
    if (s_wifi_initialized) {
        (void)esp_wifi_deinit();
    }
    release_rtos_resources();
    reset_runtime_state();
    return error;
}

esp_err_t trilink_radio_deinit(void)
{
    if (!s_initialized) {
        return ESP_ERR_INVALID_STATE;
    }
    if (rx_in_use_count() != 0U) {
        return ESP_ERR_INVALID_STATE;
    }

    const TickType_t timeout = pdMS_TO_TICKS(TRILINK_RADIO_SEND_TIMEOUT_MS);
    if (xSemaphoreTake(s_tx_mutex, timeout) != pdTRUE) {
        return ESP_ERR_TIMEOUT;
    }

    esp_err_t first_error = ESP_OK;
    esp_err_t result = esp_now_unregister_recv_cb();
    if ((result != ESP_OK) && (first_error == ESP_OK)) {
        first_error = result;
    }
    result = esp_now_unregister_send_cb();
    if ((result != ESP_OK) && (first_error == ESP_OK)) {
        first_error = result;
    }
    result = esp_now_deinit();
    if ((result != ESP_OK) && (first_error == ESP_OK)) {
        first_error = result;
    }
    result = esp_wifi_stop();
    if ((result != ESP_OK) && (first_error == ESP_OK)) {
        first_error = result;
    }
    result = esp_wifi_deinit();
    if ((result != ESP_OK) && (first_error == ESP_OK)) {
        first_error = result;
    }

    release_rtos_resources();
    reset_runtime_state();
    return first_error;
}

esp_err_t trilink_radio_rx_acquire(
    const trilink_radio_rx_frame_t **frame,
    uint32_t timeout_ms)
{
    if (frame == NULL) {
        return ESP_ERR_INVALID_ARG;
    }
    *frame = NULL;
    if (!s_initialized || (s_rx_ready_queue == NULL)) {
        return ESP_ERR_INVALID_STATE;
    }

    uint8_t index = 0U;
    if (xQueueReceive(s_rx_ready_queue,
                      &index,
                      timeout_to_ticks(timeout_ms)) != pdTRUE) {
        return ESP_ERR_TIMEOUT;
    }
    if (!slot_transition(index, RX_SLOT_READY, RX_SLOT_ACQUIRED)) {
        stats_increment(&s_stats.rx_ownership_errors);
        return ESP_ERR_INVALID_STATE;
    }

    *frame = &s_rx_pool[index];
    stats_increment(&s_stats.rx_acquired);
    return ESP_OK;
}

esp_err_t trilink_radio_rx_release(
    const trilink_radio_rx_frame_t *frame)
{
    if (!s_initialized) {
        return ESP_ERR_INVALID_STATE;
    }
    if (frame == NULL) {
        stats_increment(&s_stats.rx_ownership_errors);
        return ESP_ERR_INVALID_ARG;
    }

    uint8_t index = 0U;
    bool found = false;
    for (; index < TRILINK_RADIO_RX_POOL_DEPTH; ++index) {
        if (frame == &s_rx_pool[index]) {
            found = true;
            break;
        }
    }
    if (!found) {
        stats_increment(&s_stats.rx_ownership_errors);
        return ESP_ERR_INVALID_ARG;
    }
    if (!slot_transition(index, RX_SLOT_ACQUIRED, RX_SLOT_FREE)) {
        stats_increment(&s_stats.rx_ownership_errors);
        return ESP_ERR_INVALID_STATE;
    }
    if ((s_rx_free_queue == NULL)
        || (xQueueSend(s_rx_free_queue, &index, 0) != pdTRUE)) {
        (void)slot_transition(index, RX_SLOT_FREE, RX_SLOT_ACQUIRED);
        stats_increment(&s_stats.rx_ownership_errors);
        return ESP_FAIL;
    }

    stats_increment(&s_stats.rx_released);
    return ESP_OK;
}

esp_err_t trilink_radio_add_peer(
    const uint8_t peer_mac[TRILINK_MAC_LEN])
{
    if (!s_initialized) {
        return ESP_ERR_INVALID_STATE;
    }
    if (!mac_is_valid_unicast(peer_mac)) {
        return ESP_ERR_INVALID_ARG;
    }
    if (esp_now_is_peer_exist(peer_mac)) {
        return ESP_OK;
    }

    esp_now_peer_info_t peer = {0};
    memcpy(peer.peer_addr, peer_mac, TRILINK_MAC_LEN);
    peer.channel = s_channel;
    peer.ifidx = WIFI_IF_STA;
    peer.encrypt = false;
    const esp_err_t error = esp_now_add_peer(&peer);
    if (error == ESP_OK) {
        stats_increment(&s_stats.peers_added);
    }
    return error;
}

esp_err_t trilink_radio_remove_peer(
    const uint8_t peer_mac[TRILINK_MAC_LEN])
{
    if (!s_initialized) {
        return ESP_ERR_INVALID_STATE;
    }
    if (!mac_is_valid_unicast(peer_mac)) {
        return ESP_ERR_INVALID_ARG;
    }
    if (!esp_now_is_peer_exist(peer_mac)) {
        return ESP_OK;
    }

    const esp_err_t error = esp_now_del_peer(peer_mac);
    if (error == ESP_OK) {
        stats_increment(&s_stats.peers_removed);
    }
    return error;
}

esp_err_t trilink_radio_send(const uint8_t destination_mac[TRILINK_MAC_LEN],
                             const uint8_t *data,
                             size_t data_len)
{
    if (!s_initialized) {
        return ESP_ERR_INVALID_STATE;
    }
    if ((!mac_is_broadcast(destination_mac)
         && !mac_is_valid_unicast(destination_mac))
        || (data == NULL) || (data_len == 0U)
        || (data_len > TRILINK_FRAME_MAX_SIZE)) {
        return ESP_ERR_INVALID_ARG;
    }
    if (!esp_now_is_peer_exist(destination_mac)) {
        return ESP_ERR_NOT_FOUND;
    }

    const TickType_t timeout = pdMS_TO_TICKS(TRILINK_RADIO_SEND_TIMEOUT_MS);
    if (xSemaphoreTake(s_tx_mutex, timeout) != pdTRUE) {
        stats_increment(&s_stats.tx_timeouts);
        return ESP_ERR_TIMEOUT;
    }

    bool previous_send_pending;
    portENTER_CRITICAL(&s_tx_state_lock);
    previous_send_pending = s_tx_in_flight;
    portEXIT_CRITICAL(&s_tx_state_lock);
    if (previous_send_pending
        && (xSemaphoreTake(s_tx_done, timeout) != pdTRUE)) {
        stats_increment(&s_stats.tx_timeouts);
        (void)xSemaphoreGive(s_tx_mutex);
        return ESP_ERR_TIMEOUT;
    }

    (void)xSemaphoreTake(s_tx_done, 0);
    portENTER_CRITICAL(&s_tx_state_lock);
    s_tx_in_flight = true;
    portEXIT_CRITICAL(&s_tx_state_lock);

    const esp_err_t error = esp_now_send(destination_mac, data, data_len);
    if (error != ESP_OK) {
        portENTER_CRITICAL(&s_tx_state_lock);
        s_tx_in_flight = false;
        portEXIT_CRITICAL(&s_tx_state_lock);
        stats_increment(&s_stats.tx_api_errors);
        (void)xSemaphoreGive(s_tx_mutex);
        return error;
    }

    if (xSemaphoreTake(s_tx_done, timeout) != pdTRUE) {
        stats_increment(&s_stats.tx_timeouts);
        (void)xSemaphoreGive(s_tx_mutex);
        return ESP_ERR_TIMEOUT;
    }

    const esp_err_t completion =
        (s_last_send_status == ESP_NOW_SEND_SUCCESS) ? ESP_OK : ESP_FAIL;
    (void)xSemaphoreGive(s_tx_mutex);
    return completion;
}

esp_err_t trilink_radio_send_broadcast(const uint8_t *data, size_t data_len)
{
    return trilink_radio_send(s_broadcast_mac, data, data_len);
}

esp_err_t trilink_radio_get_local_mac(uint8_t mac[TRILINK_MAC_LEN])
{
    if (mac == NULL) {
        return ESP_ERR_INVALID_ARG;
    }
    if (!s_initialized) {
        return ESP_ERR_INVALID_STATE;
    }
    return esp_wifi_get_mac(WIFI_IF_STA, mac);
}

void trilink_radio_get_stats(trilink_radio_stats_t *stats)
{
    if (stats == NULL) {
        return;
    }
    portENTER_CRITICAL(&s_stats_lock);
    *stats = s_stats;
    portEXIT_CRITICAL(&s_stats_lock);

    stats->rx_ready = (s_rx_ready_queue == NULL)
                          ? 0U
                          : (uint32_t)uxQueueMessagesWaiting(
                                s_rx_ready_queue);
    stats->rx_in_use = rx_in_use_count();
}
