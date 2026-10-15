#pragma once

#include <stddef.h>
#include <stdint.h>

#include "esp_err.h"
#include "trilink_protocol.h"

#ifdef __cplusplus
extern "C" {
#endif

#define TRILINK_RADIO_RX_POOL_DEPTH 16U

typedef struct {
    uint8_t source_mac[TRILINK_MAC_LEN];
    uint16_t data_len;
    int8_t rssi;
    uint8_t data[TRILINK_FRAME_MAX_SIZE];
} trilink_radio_rx_frame_t;

typedef struct {
    uint32_t tx_success;
    uint32_t tx_failed;
    uint32_t tx_api_errors;
    uint32_t tx_timeouts;
    uint32_t rx_queued;
    uint32_t rx_dropped;
    uint32_t rx_acquired;
    uint32_t rx_released;
    uint32_t rx_ownership_errors;
    uint32_t rx_ready;
    uint32_t rx_in_use;
    uint32_t peers_added;
    uint32_t peers_removed;
} trilink_radio_stats_t;

esp_err_t trilink_radio_init(uint8_t channel);

esp_err_t trilink_radio_deinit(void);

/*
 * Single-consumer, task-context receive API.  A successful acquire borrows a
 * fixed-pool slot; the caller must release the exact pointer after parsing.
 * No payload copy occurs between acquire and release.
 */
esp_err_t trilink_radio_rx_acquire(
    const trilink_radio_rx_frame_t **frame,
    uint32_t timeout_ms);

esp_err_t trilink_radio_rx_release(
    const trilink_radio_rx_frame_t *frame);

esp_err_t trilink_radio_add_peer(
    const uint8_t peer_mac[TRILINK_MAC_LEN]);

esp_err_t trilink_radio_remove_peer(
    const uint8_t peer_mac[TRILINK_MAC_LEN]);

/* Task-context API. Calls are serialized and wait for ESP-NOW completion. */
esp_err_t trilink_radio_send(const uint8_t destination_mac[TRILINK_MAC_LEN],
                             const uint8_t *data,
                             size_t data_len);

esp_err_t trilink_radio_send_broadcast(const uint8_t *data, size_t data_len);

esp_err_t trilink_radio_get_local_mac(uint8_t mac[TRILINK_MAC_LEN]);

void trilink_radio_get_stats(trilink_radio_stats_t *stats);

#ifdef __cplusplus
}
#endif
