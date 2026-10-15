#pragma once
#include "trilink_route.h"

/* Volatile replicated membership, not a consensus or authentication protocol.
 * One node-task owner; no allocation. Membership changes freeze on missing ACKs. */
#define TRILINK_ROOM_WIRE_SIZE 132U
#define TRILINK_ROOM_SLOTS 6U
#define TRILINK_ROOM_SERVICE 5U
#define TRILINK_ROOM_OPCODE 2U
#define TRILINK_ROOM_LIVE_MS 3500U
enum { ROOM_SNAPSHOT=1, ROOM_ACK, ROOM_JOIN, ROOM_INVITE, ROOM_LEAVE, ROOM_DENY,
       ROOM_OFFER, ROOM_CONFIRM,
       ROOM_CREATE=16, ROOM_APPROVE, ROOM_REJECT, ROOM_KICK, ROOM_RETRY };
typedef struct { uint8_t mac[6]; uint32_t boot; } trilink_room_member_t;
typedef struct {
    uint8_t kind, ttl, count, formed;
    uint8_t source[6], authority[6], target[6], name[24];
    uint32_t boot, operation, room, incarnation, revision, term;
    trilink_room_member_t members[6];
    uint8_t status;
} trilink_room_packet_t;
typedef struct { trilink_room_packet_t packet; uint64_t expires; bool valid; } trilink_room_slot_t;
typedef struct {
    uint8_t mac[6], ttl;
    uint32_t boot, operation, digest;
    uint64_t expires;
} trilink_room_seen_t;
typedef struct {
    void *context;
    bool (*send)(void *, const uint8_t *, size_t);
    void (*changed)(void *);
} trilink_room_io_t;
typedef struct {
    uint8_t local[6]; uint32_t boot, sequence;
    bool active, waiting, join_confirmed;
    /* Local admission baseline, never encoded in the 132-byte wire packet. */
    uint32_t join_revision_floor;
    trilink_room_packet_t state, request;
    trilink_room_slot_t nearby[6], requests[6], invitations[6];
    trilink_room_seen_t seen[32];
    uint64_t acknowledgements[6], next_advert, next_request, retired_until, request_until;
    unsigned attempts;
    uint32_t rejected, queue_full;
    trilink_room_io_t io;
} trilink_room_t;
bool trilink_room_encode(const trilink_room_packet_t *, uint8_t out[TRILINK_ROOM_WIRE_SIZE]);
bool trilink_room_decode(const uint8_t *, size_t, trilink_room_packet_t *);
bool trilink_room_init(trilink_room_t *, const uint8_t mac[6], uint32_t boot, const trilink_room_io_t *);
bool trilink_room_is_leader(const trilink_room_t *);
bool trilink_room_retiring(const trilink_room_t *, uint64_t now);
bool trilink_room_synchronized(const trilink_room_t *, uint64_t now);
int trilink_room_member(const trilink_room_packet_t *, const uint8_t mac[6], uint32_t boot);
bool trilink_room_command(trilink_room_t *, const trilink_room_packet_t *, uint64_t now);
void trilink_room_input(trilink_room_t *, const uint8_t *, size_t, uint64_t now);
void trilink_room_tick(trilink_room_t *, uint64_t now);
