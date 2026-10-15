#pragma once

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#include "trilink_protocol.h"

#ifdef __cplusplus
extern "C" {
#endif

#define TRILINK_CLUSTER_MAX_PEERS TRILINK_DISCOVERY_MAX_PEERS

typedef struct {
    bool active;
    uint8_t mac[TRILINK_MAC_LEN];
    uint32_t boot_id;
    uint32_t capabilities;
    uint32_t last_sequence;
    uint64_t last_seen_ms;
    int8_t last_rssi;
} trilink_peer_t;

typedef struct {
    bool initialized;
    uint8_t local_mac[TRILINK_MAC_LEN];
    uint32_t local_boot_id;
    trilink_peer_t peers[TRILINK_CLUSTER_MAX_PEERS];
    uint8_t leader_mac[TRILINK_MAC_LEN];
} trilink_cluster_t;

typedef enum {
    TRILINK_PEER_INVALID = 0,
    TRILINK_PEER_SELF,
    TRILINK_PEER_ADDED,
    TRILINK_PEER_UPDATED,
    TRILINK_PEER_REFRESHED,
    TRILINK_PEER_STALE,
    TRILINK_PEER_RESTARTED,
    TRILINK_PEER_UNKNOWN,
    TRILINK_PEER_TABLE_FULL,
} trilink_peer_result_t;

bool trilink_cluster_init(trilink_cluster_t *cluster,
                          const uint8_t local_mac[TRILINK_MAC_LEN],
                          uint32_t local_boot_id);

trilink_peer_result_t trilink_cluster_observe_hello(
    trilink_cluster_t *cluster,
    const uint8_t source_mac[TRILINK_MAC_LEN],
    uint32_t boot_id,
    uint32_t capabilities,
    uint32_t sequence,
    int8_t rssi,
    uint64_t now_ms);

trilink_peer_result_t trilink_cluster_observe_heartbeat(
    trilink_cluster_t *cluster,
    const uint8_t source_mac[TRILINK_MAC_LEN],
    uint32_t boot_id,
    uint32_t sequence,
    int8_t rssi,
    uint64_t now_ms);

/*
 * Removes peers whose age is at least timeout_ms.  When expired_macs is not
 * NULL, up to expired_capacity MAC addresses are copied for radio-peer cleanup.
 * The return value is the total number expired, even when output is truncated.
 */
size_t trilink_cluster_expire(
    trilink_cluster_t *cluster,
    uint64_t now_ms,
    uint64_t timeout_ms,
    uint8_t expired_macs[][TRILINK_MAC_LEN],
    size_t expired_capacity);

size_t trilink_cluster_peer_count(const trilink_cluster_t *cluster);

bool trilink_cluster_is_complete(const trilink_cluster_t *cluster);

bool trilink_cluster_is_local_leader(const trilink_cluster_t *cluster);

bool trilink_cluster_get_leader(
    const trilink_cluster_t *cluster,
    uint8_t leader_mac[TRILINK_MAC_LEN]);

const trilink_peer_t *trilink_cluster_peer_at(
    const trilink_cluster_t *cluster,
    size_t active_index);

const char *trilink_peer_result_name(trilink_peer_result_t result);

#ifdef __cplusplus
}
#endif
