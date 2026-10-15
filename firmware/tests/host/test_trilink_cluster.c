#include "trilink_cluster.h"

#include <stdio.h>
#include <string.h>

static unsigned s_checks;
static unsigned s_failures;

#define CHECK(expression)                                                     \
    do {                                                                      \
        ++s_checks;                                                           \
        if (!(expression)) {                                                  \
            ++s_failures;                                                     \
            fprintf(stderr, "FAIL %s:%d: %s\n", __FILE__, __LINE__,          \
                    #expression);                                             \
        }                                                                     \
    } while (0)

static const uint8_t MAC_A[TRILINK_MAC_LEN] = {
    0x10U, 0x00U, 0x00U, 0x00U, 0x00U, 0x01U,
};
static const uint8_t MAC_B[TRILINK_MAC_LEN] = {
    0x20U, 0x00U, 0x00U, 0x00U, 0x00U, 0x02U,
};
static const uint8_t MAC_C[TRILINK_MAC_LEN] = {
    0x30U, 0x00U, 0x00U, 0x00U, 0x00U, 0x03U,
};
static const uint8_t MAC_D[TRILINK_MAC_LEN] = {
    0x40U, 0x00U, 0x00U, 0x00U, 0x00U, 0x04U,
};

static trilink_peer_result_t observe(trilink_cluster_t *cluster,
                                     const uint8_t mac[TRILINK_MAC_LEN],
                                     uint32_t boot_id,
                                     uint32_t sequence,
                                     uint64_t now_ms)
{
    return trilink_cluster_observe_hello(cluster,
                                         mac,
                                         boot_id,
                                         0x55AAU,
                                         sequence,
                                         -42,
                                         now_ms);
}

static void check_leader(const trilink_cluster_t *cluster,
                         const uint8_t expected[TRILINK_MAC_LEN])
{
    uint8_t actual[TRILINK_MAC_LEN] = {0};
    CHECK(trilink_cluster_get_leader(cluster, actual));
    CHECK(memcmp(actual, expected, TRILINK_MAC_LEN) == 0);
}

static void test_initialization_and_invalid_inputs(void)
{
    trilink_cluster_t cluster;
    CHECK(trilink_cluster_init(&cluster, MAC_B, 0x1234U));
    CHECK(cluster.initialized);
    CHECK(cluster.local_boot_id == 0x1234U);
    CHECK(trilink_cluster_peer_count(&cluster) == 0U);
    CHECK(!trilink_cluster_is_complete(&cluster));
    CHECK(trilink_cluster_is_local_leader(&cluster));
    check_leader(&cluster, MAC_B);
    CHECK(trilink_cluster_peer_at(&cluster, 0U) == NULL);

    const uint8_t zero[TRILINK_MAC_LEN] = {0};
    const uint8_t multicast[TRILINK_MAC_LEN] = {
        0x01U, 0U, 0U, 0U, 0U, 1U,
    };
    CHECK(!trilink_cluster_init(NULL, MAC_A, 1U));
    CHECK(!trilink_cluster_init(&cluster, NULL, 1U));
    CHECK(!trilink_cluster_init(&cluster, zero, 1U));
    CHECK(!trilink_cluster_init(&cluster, multicast, 1U));

    memset(&cluster, 0, sizeof(cluster));
    CHECK(observe(&cluster, MAC_A, 1U, 1U, 1U)
          == TRILINK_PEER_INVALID);
    CHECK(trilink_cluster_peer_count(NULL) == 0U);
    CHECK(!trilink_cluster_is_complete(NULL));
    CHECK(!trilink_cluster_is_local_leader(NULL));
    CHECK(!trilink_cluster_get_leader(NULL, (uint8_t *)MAC_A));
    CHECK(!trilink_cluster_get_leader(&cluster, NULL));
    CHECK(trilink_cluster_peer_at(NULL, 0U) == NULL);
}

static void test_all_six_node_arrival_views(void)
{
    const uint8_t *nodes[] = {MAC_A, MAC_B, MAC_C};
    unsigned scenarios = 0U;

    for (size_t local = 0; local < 3U; ++local) {
        size_t remotes[2];
        size_t next = 0U;
        for (size_t candidate = 0; candidate < 3U; ++candidate) {
            if (candidate != local) {
                remotes[next++] = candidate;
            }
        }

        for (size_t reverse = 0; reverse < 2U; ++reverse) {
            trilink_cluster_t cluster;
            CHECK(trilink_cluster_init(&cluster,
                                       nodes[local],
                                       (uint32_t)(100U + local)));
            const size_t first = remotes[reverse ? 1U : 0U];
            const size_t second = remotes[reverse ? 0U : 1U];
            CHECK(observe(&cluster,
                          nodes[first],
                          (uint32_t)(200U + first),
                          1U,
                          10U) == TRILINK_PEER_ADDED);
            CHECK(observe(&cluster,
                          nodes[second],
                          (uint32_t)(200U + second),
                          1U,
                          20U) == TRILINK_PEER_ADDED);
            CHECK(trilink_cluster_peer_count(&cluster) == 2U);
            CHECK(trilink_cluster_is_complete(&cluster));
            check_leader(&cluster, MAC_A);
            CHECK(trilink_cluster_is_local_leader(&cluster) == (local == 0U));
            ++scenarios;
        }
    }
    CHECK(scenarios == 6U);
}

static void test_refresh_update_restart_and_heartbeat(void)
{
    trilink_cluster_t cluster;
    CHECK(trilink_cluster_init(&cluster, MAC_B, 1U));
    CHECK(observe(&cluster, MAC_C, 20U, 10U, 100U)
          == TRILINK_PEER_ADDED);
    const trilink_peer_t *peer = trilink_cluster_peer_at(&cluster, 0U);
    CHECK(peer != NULL);
    CHECK(peer->last_sequence == 10U);
    CHECK(peer->last_seen_ms == 100U);

    CHECK(observe(&cluster, MAC_C, 20U, 10U, 110U)
          == TRILINK_PEER_REFRESHED);
    peer = trilink_cluster_peer_at(&cluster, 0U);
    CHECK(peer->last_sequence == 10U);
    CHECK(peer->last_seen_ms == 110U);
    CHECK(observe(&cluster, MAC_C, 20U, 9U, 120U)
          == TRILINK_PEER_STALE);
    peer = trilink_cluster_peer_at(&cluster, 0U);
    CHECK(peer->last_seen_ms == 110U);
    CHECK(observe(&cluster, MAC_C, 20U, 11U, 130U)
          == TRILINK_PEER_UPDATED);
    peer = trilink_cluster_peer_at(&cluster, 0U);
    CHECK(peer->last_sequence == 11U);

    CHECK(trilink_cluster_observe_heartbeat(&cluster,
                                            MAC_A,
                                            10U,
                                            1U,
                                            -50,
                                            140U)
          == TRILINK_PEER_UNKNOWN);
    CHECK(trilink_cluster_observe_heartbeat(&cluster,
                                            MAC_C,
                                            99U,
                                            12U,
                                            -51,
                                            150U)
          == TRILINK_PEER_UNKNOWN);
    CHECK(trilink_cluster_observe_heartbeat(&cluster,
                                            MAC_C,
                                            20U,
                                            12U,
                                            -52,
                                            160U)
          == TRILINK_PEER_UPDATED);
    peer = trilink_cluster_peer_at(&cluster, 0U);
    CHECK(peer->last_sequence == 12U);
    CHECK(peer->last_rssi == -52);
    CHECK(peer->last_seen_ms == 160U);
    CHECK(trilink_cluster_observe_heartbeat(&cluster,
                                            MAC_C,
                                            20U,
                                            12U,
                                            -53,
                                            170U)
          == TRILINK_PEER_REFRESHED);

    CHECK(observe(&cluster, MAC_C, 21U, 1U, 180U)
          == TRILINK_PEER_RESTARTED);
    peer = trilink_cluster_peer_at(&cluster, 0U);
    CHECK(peer->boot_id == 21U);
    CHECK(peer->last_sequence == 1U);
}

static void test_table_limit_self_and_invalid_mac(void)
{
    trilink_cluster_t cluster;
    CHECK(trilink_cluster_init(&cluster, MAC_A, 1U));
    CHECK(observe(&cluster, MAC_A, 1U, 1U, 10U) == TRILINK_PEER_SELF);
    CHECK(trilink_cluster_peer_count(&cluster) == 0U);
    CHECK(observe(&cluster, MAC_B, 2U, 1U, 20U) == TRILINK_PEER_ADDED);
    CHECK(observe(&cluster, MAC_C, 3U, 1U, 30U) == TRILINK_PEER_ADDED);
    for (uint8_t i = 0; i < 4U; ++i) {
        const uint8_t mac[6] = {0x50U, 0U, 0U, 0U, 0U, i};
        CHECK(observe(&cluster, mac, 5U, 1U, 30U) == TRILINK_PEER_ADDED);
    }
    CHECK(observe(&cluster, MAC_D, 4U, 1U, 40U)
          == TRILINK_PEER_TABLE_FULL);
    CHECK(trilink_cluster_peer_count(&cluster) == 6U);
    CHECK(trilink_cluster_is_complete(&cluster));
    CHECK(observe(&cluster, MAC_B, 2U, 2U, 40U) == TRILINK_PEER_UPDATED);
    CHECK(trilink_cluster_expire(&cluster, 2030U, 2000U, NULL, 0U) == 5U);
    CHECK(observe(&cluster, MAC_D, 4U, 1U, 2040U) == TRILINK_PEER_ADDED);

    const uint8_t multicast[TRILINK_MAC_LEN] = {
        0xFFU, 0xFFU, 0xFFU, 0xFFU, 0xFFU, 0xFFU,
    };
    CHECK(observe(&cluster, multicast, 5U, 1U, 50U)
          == TRILINK_PEER_INVALID);
    CHECK(observe(&cluster, NULL, 5U, 1U, 50U)
          == TRILINK_PEER_INVALID);
}

static void test_expiry_releases_slots_and_reelects(void)
{
    trilink_cluster_t cluster;
    CHECK(trilink_cluster_init(&cluster, MAC_C, 1U));
    CHECK(observe(&cluster, MAC_A, 2U, 1U, 100U) == TRILINK_PEER_ADDED);
    CHECK(observe(&cluster, MAC_B, 3U, 1U, 200U) == TRILINK_PEER_ADDED);
    check_leader(&cluster, MAC_A);

    uint8_t expired[TRILINK_CLUSTER_MAX_PEERS][TRILINK_MAC_LEN] = {{0}};
    CHECK(trilink_cluster_expire(&cluster,
                                 199U,
                                 100U,
                                 expired,
                                 TRILINK_CLUSTER_MAX_PEERS) == 0U);
    CHECK(trilink_cluster_expire(&cluster,
                                 200U,
                                 100U,
                                 expired,
                                 TRILINK_CLUSTER_MAX_PEERS) == 1U);
    CHECK(memcmp(expired[0], MAC_A, TRILINK_MAC_LEN) == 0);
    CHECK(trilink_cluster_peer_count(&cluster) == 1U);
    check_leader(&cluster, MAC_B);

    CHECK(observe(&cluster, MAC_D, 4U, 1U, 210U) == TRILINK_PEER_ADDED);
    CHECK(trilink_cluster_peer_count(&cluster) == 2U);
    CHECK(trilink_cluster_expire(&cluster,
                                 400U,
                                 100U,
                                 NULL,
                                 0U) == 2U);
    CHECK(trilink_cluster_peer_count(&cluster) == 0U);
    check_leader(&cluster, MAC_C);
    CHECK(trilink_cluster_is_local_leader(&cluster));

    CHECK(trilink_cluster_expire(NULL, 1U, 1U, NULL, 0U) == 0U);
    CHECK(trilink_cluster_expire(&cluster, 1U, 0U, NULL, 0U) == 0U);
    CHECK(trilink_cluster_expire(&cluster, 1U, 1U, NULL, 1U) == 0U);
}

static void test_sequence_wrap(void)
{
    trilink_cluster_t cluster;
    CHECK(trilink_cluster_init(&cluster, MAC_A, 1U));
    CHECK(observe(&cluster, MAC_B, 2U, 0xFFFFFFFEU, 1U)
          == TRILINK_PEER_ADDED);
    CHECK(observe(&cluster, MAC_B, 2U, 1U, 2U)
          == TRILINK_PEER_UPDATED);
    const trilink_peer_t *peer = trilink_cluster_peer_at(&cluster, 0U);
    CHECK(peer != NULL);
    CHECK(peer->last_sequence == 1U);
    CHECK(observe(&cluster, MAC_B, 2U, 0xFFFFFF00U, 3U)
          == TRILINK_PEER_STALE);
    CHECK(peer->last_sequence == 1U);
}

static void test_result_names(void)
{
    CHECK(strcmp(trilink_peer_result_name(TRILINK_PEER_ADDED), "ADDED")
          == 0);
    CHECK(strcmp(trilink_peer_result_name(TRILINK_PEER_TABLE_FULL),
                 "TABLE_FULL") == 0);
    CHECK(strcmp(trilink_peer_result_name(TRILINK_PEER_STALE), "STALE") == 0);
    CHECK(strcmp(trilink_peer_result_name((trilink_peer_result_t)99),
                 "INVALID_RESULT") == 0);
}

int main(void)
{
    test_initialization_and_invalid_inputs();
    test_all_six_node_arrival_views();
    test_refresh_update_restart_and_heartbeat();
    test_table_limit_self_and_invalid_mac();
    test_expiry_releases_slots_and_reelects();
    test_sequence_wrap();
    test_result_names();

    printf("trilink_cluster host tests: checks=%u failures=%u\n",
           s_checks,
           s_failures);
    return (s_failures == 0U) ? 0 : 1;
}
