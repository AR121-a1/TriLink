#pragma once

#include <stdbool.h>
#include <stdint.h>

#include "esp_err.h"
#include "trilink_protocol.h"
#include "trilink_control.h"

#ifdef __cplusplus
extern "C" {
#endif

#define TRILINK_NODE_RADIO_CHANNEL 6U
#define TRILINK_NODE_GROUP_ID      0x31474C54UL

typedef struct {
    bool started;
    bool cluster_complete;
    bool local_is_leader;
    uint8_t local_mac[TRILINK_MAC_LEN];
    uint8_t leader_mac[TRILINK_MAC_LEN];
    uint32_t boot_id;
    uint32_t next_sequence;
    uint32_t active_peers;
    uint32_t rx_valid;
    uint32_t rx_invalid;
    uint32_t rx_wrong_group;
    uint32_t rx_deferred;
    uint32_t rx_transport_dropped;
    uint32_t tx_frame_failures;
} trilink_node_status_t;

/*
 * Starts fixed-channel discovery and bounded six-member automatic routing.
 * The node task is the sole owner of the peer table and leader view.
 */
esp_err_t trilink_node_start(void);

esp_err_t trilink_node_get_status(trilink_node_status_t *status);

/* Copies a published snapshot; never lends the node-owned peer table. */
esp_err_t trilink_node_get_discovery(trilink_discovery_snapshot_t *snapshot);

#ifdef __cplusplus
}
#endif
