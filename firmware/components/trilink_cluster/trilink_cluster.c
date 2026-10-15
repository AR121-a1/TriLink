#include "trilink_cluster.h"

#include <string.h>

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

static int mac_compare(const uint8_t left[TRILINK_MAC_LEN],
                       const uint8_t right[TRILINK_MAC_LEN])
{
    return memcmp(left, right, TRILINK_MAC_LEN);
}

static bool sequence_is_newer(uint32_t candidate, uint32_t reference)
{
    return ((int32_t)(candidate - reference)) > 0;
}

static void recompute_leader(trilink_cluster_t *cluster)
{
    memcpy(cluster->leader_mac, cluster->local_mac, TRILINK_MAC_LEN);
    for (size_t index = 0; index < TRILINK_CLUSTER_MAX_PEERS; ++index) {
        if (cluster->peers[index].active
            && (mac_compare(cluster->peers[index].mac,
                            cluster->leader_mac) < 0)) {
            memcpy(cluster->leader_mac,
                   cluster->peers[index].mac,
                   TRILINK_MAC_LEN);
        }
    }
}

static trilink_peer_t *find_peer(trilink_cluster_t *cluster,
                                 const uint8_t mac[TRILINK_MAC_LEN])
{
    for (size_t index = 0; index < TRILINK_CLUSTER_MAX_PEERS; ++index) {
        if (cluster->peers[index].active
            && (mac_compare(cluster->peers[index].mac, mac) == 0)) {
            return &cluster->peers[index];
        }
    }
    return NULL;
}

static trilink_peer_t *find_free_peer(trilink_cluster_t *cluster)
{
    for (size_t index = 0; index < TRILINK_CLUSTER_MAX_PEERS; ++index) {
        if (!cluster->peers[index].active) {
            return &cluster->peers[index];
        }
    }
    return NULL;
}

static bool observation_is_valid(
    const trilink_cluster_t *cluster,
    const uint8_t source_mac[TRILINK_MAC_LEN])
{
    return (cluster != NULL) && cluster->initialized
           && mac_is_valid_unicast(source_mac);
}

bool trilink_cluster_init(trilink_cluster_t *cluster,
                          const uint8_t local_mac[TRILINK_MAC_LEN],
                          uint32_t local_boot_id)
{
    if ((cluster == NULL) || !mac_is_valid_unicast(local_mac)) {
        return false;
    }

    memset(cluster, 0, sizeof(*cluster));
    memcpy(cluster->local_mac, local_mac, TRILINK_MAC_LEN);
    memcpy(cluster->leader_mac, local_mac, TRILINK_MAC_LEN);
    cluster->local_boot_id = local_boot_id;
    cluster->initialized = true;
    return true;
}

trilink_peer_result_t trilink_cluster_observe_hello(
    trilink_cluster_t *cluster,
    const uint8_t source_mac[TRILINK_MAC_LEN],
    uint32_t boot_id,
    uint32_t capabilities,
    uint32_t sequence,
    int8_t rssi,
    uint64_t now_ms)
{
    if (!observation_is_valid(cluster, source_mac)) {
        return TRILINK_PEER_INVALID;
    }
    if (mac_compare(cluster->local_mac, source_mac) == 0) {
        return TRILINK_PEER_SELF;
    }

    trilink_peer_t *peer = find_peer(cluster, source_mac);
    if (peer == NULL) {
        peer = find_free_peer(cluster);
        if (peer == NULL) {
            return TRILINK_PEER_TABLE_FULL;
        }

        memset(peer, 0, sizeof(*peer));
        peer->active = true;
        memcpy(peer->mac, source_mac, TRILINK_MAC_LEN);
        peer->boot_id = boot_id;
        peer->capabilities = capabilities;
        peer->last_sequence = sequence;
        peer->last_seen_ms = now_ms;
        peer->last_rssi = rssi;
        recompute_leader(cluster);
        return TRILINK_PEER_ADDED;
    }

    if (peer->boot_id != boot_id) {
        peer->boot_id = boot_id;
        peer->capabilities = capabilities;
        peer->last_sequence = sequence;
        peer->last_seen_ms = now_ms;
        peer->last_rssi = rssi;
        return TRILINK_PEER_RESTARTED;
    }
    if (sequence == peer->last_sequence) {
        peer->last_seen_ms = now_ms;
        peer->last_rssi = rssi;
        return TRILINK_PEER_REFRESHED;
    }
    if (!sequence_is_newer(sequence, peer->last_sequence)) {
        return TRILINK_PEER_STALE;
    }

    peer->last_sequence = sequence;
    peer->capabilities = capabilities;
    peer->last_seen_ms = now_ms;
    peer->last_rssi = rssi;
    return TRILINK_PEER_UPDATED;
}

trilink_peer_result_t trilink_cluster_observe_heartbeat(
    trilink_cluster_t *cluster,
    const uint8_t source_mac[TRILINK_MAC_LEN],
    uint32_t boot_id,
    uint32_t sequence,
    int8_t rssi,
    uint64_t now_ms)
{
    if (!observation_is_valid(cluster, source_mac)) {
        return TRILINK_PEER_INVALID;
    }
    if (mac_compare(cluster->local_mac, source_mac) == 0) {
        return TRILINK_PEER_SELF;
    }

    trilink_peer_t *peer = find_peer(cluster, source_mac);
    if ((peer == NULL) || (peer->boot_id != boot_id)) {
        return TRILINK_PEER_UNKNOWN;
    }

    if (sequence == peer->last_sequence) {
        peer->last_seen_ms = now_ms;
        peer->last_rssi = rssi;
        return TRILINK_PEER_REFRESHED;
    }
    if (!sequence_is_newer(sequence, peer->last_sequence)) {
        return TRILINK_PEER_STALE;
    }
    peer->last_sequence = sequence;
    peer->last_seen_ms = now_ms;
    peer->last_rssi = rssi;
    return TRILINK_PEER_UPDATED;
}

size_t trilink_cluster_expire(
    trilink_cluster_t *cluster,
    uint64_t now_ms,
    uint64_t timeout_ms,
    uint8_t expired_macs[][TRILINK_MAC_LEN],
    size_t expired_capacity)
{
    if ((cluster == NULL) || !cluster->initialized || (timeout_ms == 0U)
        || ((expired_macs == NULL) && (expired_capacity != 0U))) {
        return 0U;
    }

    size_t expired_count = 0U;
    for (size_t index = 0; index < TRILINK_CLUSTER_MAX_PEERS; ++index) {
        trilink_peer_t *peer = &cluster->peers[index];
        if (!peer->active || (now_ms < peer->last_seen_ms)
            || ((now_ms - peer->last_seen_ms) < timeout_ms)) {
            continue;
        }

        if ((expired_macs != NULL) && (expired_count < expired_capacity)) {
            memcpy(expired_macs[expired_count], peer->mac, TRILINK_MAC_LEN);
        }
        ++expired_count;
        memset(peer, 0, sizeof(*peer));
    }

    if (expired_count != 0U) {
        recompute_leader(cluster);
    }
    return expired_count;
}

size_t trilink_cluster_peer_count(const trilink_cluster_t *cluster)
{
    if ((cluster == NULL) || !cluster->initialized) {
        return 0U;
    }

    size_t count = 0U;
    for (size_t index = 0; index < TRILINK_CLUSTER_MAX_PEERS; ++index) {
        if (cluster->peers[index].active) {
            ++count;
        }
    }
    return count;
}

bool trilink_cluster_is_complete(const trilink_cluster_t *cluster)
{
    /* Legacy diagnostic means enough nodes for a three-player application,
     * not membership in a Room and not a full discovery table. */
    return trilink_cluster_peer_count(cluster) >= TRILINK_CLUSTER_NODE_COUNT - 1U;
}

bool trilink_cluster_is_local_leader(const trilink_cluster_t *cluster)
{
    return (cluster != NULL) && cluster->initialized
           && (mac_compare(cluster->local_mac, cluster->leader_mac) == 0);
}

bool trilink_cluster_get_leader(
    const trilink_cluster_t *cluster,
    uint8_t leader_mac[TRILINK_MAC_LEN])
{
    if ((cluster == NULL) || !cluster->initialized || (leader_mac == NULL)) {
        return false;
    }
    memcpy(leader_mac, cluster->leader_mac, TRILINK_MAC_LEN);
    return true;
}

const trilink_peer_t *trilink_cluster_peer_at(
    const trilink_cluster_t *cluster,
    size_t active_index)
{
    if ((cluster == NULL) || !cluster->initialized) {
        return NULL;
    }

    size_t seen = 0U;
    for (size_t index = 0; index < TRILINK_CLUSTER_MAX_PEERS; ++index) {
        if (!cluster->peers[index].active) {
            continue;
        }
        if (seen == active_index) {
            return &cluster->peers[index];
        }
        ++seen;
    }
    return NULL;
}

const char *trilink_peer_result_name(trilink_peer_result_t result)
{
    switch (result) {
    case TRILINK_PEER_INVALID:
        return "INVALID";
    case TRILINK_PEER_SELF:
        return "SELF";
    case TRILINK_PEER_ADDED:
        return "ADDED";
    case TRILINK_PEER_UPDATED:
        return "UPDATED";
    case TRILINK_PEER_REFRESHED:
        return "REFRESHED";
    case TRILINK_PEER_STALE:
        return "STALE";
    case TRILINK_PEER_RESTARTED:
        return "RESTARTED";
    case TRILINK_PEER_UNKNOWN:
        return "UNKNOWN";
    case TRILINK_PEER_TABLE_FULL:
        return "TABLE_FULL";
    default:
        return "INVALID_RESULT";
    }
}
