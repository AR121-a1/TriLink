#pragma once

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

/*
 * TriLink v1 is deliberately smaller than ESP-NOW's transport limit.  The
 * fixed 200-byte ceiling keeps every callback copy and queue item bounded.
 * Multi-byte fields are encoded explicitly as little-endian values; this C
 * structure is never sent with memcpy.
 */
#define TRILINK_MAC_LEN             6U
#define TRILINK_FRAME_MAGIC         0x4C54U
#define TRILINK_FRAME_VERSION       1U
#define TRILINK_FRAME_HEADER_SIZE   24U
#define TRILINK_FRAME_CRC_SIZE      4U
#define TRILINK_FRAME_MAX_SIZE      200U
#define TRILINK_PAYLOAD_MAX         (TRILINK_FRAME_MAX_SIZE \
                                     - TRILINK_FRAME_HEADER_SIZE \
                                     - TRILINK_FRAME_CRC_SIZE)
#define TRILINK_HELLO_PAYLOAD_SIZE  8U
#define TRILINK_CLUSTER_NODE_COUNT  3U
/* Historical discovery-ready threshold above is not a Room size limit. */
#define TRILINK_DISCOVERY_MAX_PEERS 6U
#define TRILINK_ROOM_MAX_MEMBERS    6U

#define TRILINK_FLAG_ACK_REQUIRED   0x01U
#define TRILINK_FLAG_RETRANSMIT     0x02U
#define TRILINK_FLAG_KNOWN_MASK     (TRILINK_FLAG_ACK_REQUIRED \
                                     | TRILINK_FLAG_RETRANSMIT)

#define TRILINK_CAP_ESPNOW_UNICAST  (1UL << 0)
#define TRILINK_CAP_USB_PC_LINK     (1UL << 1)
#define TRILINK_CAP_GAME_RELAY      (1UL << 2)
#define TRILINK_CAP_HID_WAKE        (1UL << 3)
#define TRILINK_CAP_CHAIN_TRANSPORT (1UL << 4)
#define TRILINK_CAP_AUTO_ROUTE      (1UL << 5)
#define TRILINK_CAP_ROOM_RGB        (1UL << 6)
#define TRILINK_CAP_NODE_MODULES    (1UL << 7)

typedef enum {
    TRILINK_MSG_HELLO = 1,
    TRILINK_MSG_HEARTBEAT = 2,
    TRILINK_MSG_LEADER_ANNOUNCE = 3,
    TRILINK_MSG_GAME_INPUT = 16,
    TRILINK_MSG_GAME_STATE = 17,
    TRILINK_MSG_ACK = 18,
    TRILINK_MSG_APP_DATA = 32,
} trilink_message_type_t;

typedef struct {
    uint8_t type;
    uint8_t flags;
    uint16_t payload_len;
    uint32_t group_id;
    uint32_t boot_id;
    uint32_t term;
    uint32_t sequence;
    uint8_t payload[TRILINK_PAYLOAD_MAX];
} trilink_frame_t;

/*
 * Non-owning wire view.  payload points into the caller's encoded buffer and
 * is valid only while that buffer remains valid.  This is the preferred hot
 * path for radio and bulk-data consumers because it avoids a payload copy.
 */
typedef struct {
    uint8_t type;
    uint8_t flags;
    uint16_t payload_len;
    uint32_t group_id;
    uint32_t boot_id;
    uint32_t term;
    uint32_t sequence;
    const uint8_t *payload;
} trilink_frame_view_t;

typedef struct {
    uint32_t capabilities;
    uint16_t hello_interval_ms;
    uint8_t max_nodes;
} trilink_hello_t;

typedef enum {
    TRILINK_DECODE_OK = 0,
    TRILINK_DECODE_INVALID_ARGUMENT,
    TRILINK_DECODE_TOO_SHORT,
    TRILINK_DECODE_BAD_MAGIC,
    TRILINK_DECODE_BAD_VERSION,
    TRILINK_DECODE_BAD_HEADER_LENGTH,
    TRILINK_DECODE_BAD_TYPE,
    TRILINK_DECODE_BAD_FLAGS,
    TRILINK_DECODE_BAD_PAYLOAD_LENGTH,
    TRILINK_DECODE_BAD_TOTAL_LENGTH,
    TRILINK_DECODE_BAD_CRC,
} trilink_decode_result_t;

uint32_t trilink_crc32(const uint8_t *data, size_t len);

bool trilink_message_type_is_valid(uint8_t type);

bool trilink_frame_encode(const trilink_frame_t *frame,
                          uint8_t *output,
                          size_t output_capacity,
                          size_t *output_len);

bool trilink_frame_encode_view(const trilink_frame_view_t *frame,
                               uint8_t *output,
                               size_t output_capacity,
                               size_t *output_len);

trilink_decode_result_t trilink_frame_decode(const uint8_t *input,
                                              size_t input_len,
                                              trilink_frame_t *frame);

trilink_decode_result_t trilink_frame_decode_view(
    const uint8_t *input,
    size_t input_len,
    trilink_frame_view_t *frame);

bool trilink_hello_encode(const trilink_hello_t *hello,
                          uint8_t *output,
                          size_t output_capacity);

bool trilink_hello_decode(const uint8_t *input,
                          size_t input_len,
                          trilink_hello_t *hello);

const char *trilink_decode_result_name(trilink_decode_result_t result);

#ifdef __cplusplus
}
#endif
