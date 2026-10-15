#pragma once

#include "trilink_protocol.h"

/* USB text control is separate from the unchanged 200-byte radio format. */
#define TRILINK_CONTROL_NONCE_MAX 32U
#define TRILINK_CONTROL_LINE_SIZE 64U
#define TRILINK_CONTROL_REPLY_SIZE 512U
#define TRILINK_DISCOVERY_PEERS TRILINK_DISCOVERY_MAX_PEERS
#define TRILINK_DISCOVERY_TTL_MS 2000U

typedef enum {
    TRILINK_CONTROL_NONE = 0,
    TRILINK_CONTROL_HELLO,
    TRILINK_CONTROL_SEARCH,
    TRILINK_CONTROL_INVALID,
} trilink_control_kind_t;

typedef struct {
    trilink_control_kind_t kind;
    char nonce[TRILINK_CONTROL_NONCE_MAX + 1U];
} trilink_control_command_t;

typedef struct {
    char line[TRILINK_CONTROL_LINE_SIZE];
    size_t length;
    bool discard;
    bool carriage_return;
} trilink_control_parser_t;

typedef struct {
    uint8_t mac[TRILINK_MAC_LEN];
    int8_t rssi;
    uint64_t last_seen_ms;
} trilink_discovery_peer_t;

typedef struct {
    bool ready;
    uint8_t local_mac[TRILINK_MAC_LEN];
    uint32_t capabilities;
    size_t peer_count;
    trilink_discovery_peer_t peers[TRILINK_DISCOVERY_PEERS];
} trilink_discovery_snapshot_t;

/* Single-owner parser; zero initialize/reset on disconnect or partial-line timeout.
 * Returns true only on a completed nonempty line (including rejected lines).
 * An overflow discards the ENTIRE line, never executes its truncated prefix. */
bool trilink_control_feed(trilink_control_parser_t *parser, uint8_t byte,
                          trilink_control_command_t *command);

/* Builds a complete transaction, including END for an empty SEARCH.
 * Names are deterministic TriLink-<12 hex MAC>, Base64 UTF-8 on USB.
 * Room fields remain '-' until a real room service is implemented.
 * No allocation, no radio operations, no partial output on capacity failure. */
bool trilink_control_reply(const trilink_control_command_t *command,
                           const trilink_discovery_snapshot_t *snapshot,
                           uint64_t now_ms, char *output, size_t capacity,
                           size_t *output_len);
