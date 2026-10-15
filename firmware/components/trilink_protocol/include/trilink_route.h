#pragma once
#include "trilink_service.h"

/* Six-member bounded link-state transport. Single-owner; callbacks copy
 * borrowed bytes and never block. ACK means destination inbox acceptance,
 * NOT PC consumption, Room consensus or application execution. */
#define TRILINK_ROUTE_MAX_MEMBERS TRILINK_ROOM_MAX_MEMBERS
#define TRILINK_ROUTE_FORMAT 2U
#define TRILINK_ROUTE_HEADER_SIZE 36U
#define TRILINK_ROUTE_DATA_MAX (TRILINK_SERVICE_DATA_MAX - TRILINK_ROUTE_HEADER_SIZE)
#define TRILINK_ROUTE_RETRY_MS 1200U
#define TRILINK_ROUTE_ATTEMPTS 4U
#define TRILINK_ROUTE_DEDUP_MS 30000U
#define TRILINK_ROUTE_TRANSIT_MS 10000U
#define TRILINK_ROUTE_CACHE_SIZE 32U
#define TRILINK_ROUTE_TOPOLOGY_MS 1000U
#define TRILINK_ROUTE_TOPOLOGY_TTL_MS 3500U
#define TRILINK_ROUTE_DATA 1U
#define TRILINK_ROUTE_ACK 2U
#define TRILINK_ROUTE_TOPOLOGY 3U

typedef struct {
    uint32_t room, epoch, boot, message;
    uint8_t source[6], destination[6];
    uint8_t remaining, kind, attempt, visited;
    uint16_t length;
    const uint8_t *data;
} trilink_route_view_t;

typedef struct {
    uint32_t room, epoch;
    uint8_t count, members[TRILINK_ROUTE_MAX_MEMBERS][6];
} trilink_route_config_t;

typedef enum {
    TRILINK_ROUTE_ACCEPTED = 1,
    TRILINK_ROUTE_TIMEOUT,
} trilink_route_result_t;

typedef struct {
    void *context;
    bool (*send)(void *context, const uint8_t next[6],
                 const uint8_t *payload, size_t length, bool acknowledgement);
    bool (*receive)(void *context, const trilink_route_view_t *message);
    void (*complete)(void *context, uint32_t message, trilink_route_result_t result);
} trilink_route_io_t;

typedef struct {
    uint8_t source[6], destination[6], kind, attempts_seen;
    uint32_t boot, message, digest;
    uint16_t length;
    uint64_t expires;
    bool valid;
} trilink_route_seen_t;

typedef struct {
    uint32_t boot, sequence, retired_boot[2];
    uint64_t expires;
    uint8_t neighbors, best_remaining;
    bool relay, valid;
} trilink_route_link_t;

typedef struct {
    trilink_route_config_t config;
    trilink_route_io_t io;
    uint8_t local[6], position, neighbors;
    uint32_t boot, next_message, topology_sequence, membership_digest;
    bool configured, relay_enabled, pending, topology_dirty;
    uint8_t destination[6], body[TRILINK_ROUTE_DATA_MAX];
    uint16_t length;
    uint32_t pending_message;
    unsigned attempts;
    uint64_t retry_at, topology_at;
    trilink_route_seen_t seen[TRILINK_ROUTE_CACHE_SIZE];
    trilink_route_seen_t transit[TRILINK_ROUTE_CACHE_SIZE];
    trilink_route_link_t links[TRILINK_ROUTE_MAX_MEMBERS];
    uint32_t forwarded, delivered, duplicates, rejected, queue_full, timeouts;
    uint32_t topology_received, topology_forwarded, transit_duplicates, no_route;
} trilink_route_t;

bool trilink_route_mac_valid(const uint8_t mac[6]);
bool trilink_route_encode(const trilink_route_view_t *message,
                          uint8_t *output, size_t capacity, size_t *length);
bool trilink_route_decode(const uint8_t *payload, size_t length,
                          trilink_route_view_t *message);
bool trilink_route_init(trilink_route_t *node, const uint8_t local[6],
                        uint32_t boot, const trilink_route_io_t *io);
/* Canonicalizes the unordered roster. Same config is idempotent. Same-room
 * newer epoch may replace membership only with no local send in flight.
 * This is explicit transport provisioning, NOT a Room authorization service. */
bool trilink_route_configure(trilink_route_t *node, const trilink_route_config_t *config);
void trilink_route_neighbors(trilink_route_t *node, const uint8_t *macs, size_t count);
void trilink_route_relay(trilink_route_t *node, bool enabled);
bool trilink_route_lookup(const trilink_route_t *node, const uint8_t destination[6],
                          uint64_t now, uint8_t next[6], uint8_t *hops);
bool trilink_route_submit(trilink_route_t *node, const uint8_t destination[6],
                          const uint8_t *data, size_t length, uint64_t now,
                          uint32_t *message);
void trilink_route_tick(trilink_route_t *node, uint64_t now);
void trilink_route_input(trilink_route_t *node, const uint8_t previous[6],
                         const uint8_t *payload, size_t length, uint64_t now);
