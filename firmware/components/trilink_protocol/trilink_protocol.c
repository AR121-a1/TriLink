#include "trilink_protocol.h"

#include <string.h>

enum {
    MAGIC_OFFSET = 0,
    VERSION_OFFSET = 2,
    HEADER_LENGTH_OFFSET = 3,
    TYPE_OFFSET = 4,
    FLAGS_OFFSET = 5,
    PAYLOAD_LENGTH_OFFSET = 6,
    GROUP_ID_OFFSET = 8,
    BOOT_ID_OFFSET = 12,
    TERM_OFFSET = 16,
    SEQUENCE_OFFSET = 20,
};

static void write_u16_le(uint8_t *destination, uint16_t value)
{
    destination[0] = (uint8_t)(value & 0xFFU);
    destination[1] = (uint8_t)((value >> 8U) & 0xFFU);
}

static void write_u32_le(uint8_t *destination, uint32_t value)
{
    destination[0] = (uint8_t)(value & 0xFFU);
    destination[1] = (uint8_t)((value >> 8U) & 0xFFU);
    destination[2] = (uint8_t)((value >> 16U) & 0xFFU);
    destination[3] = (uint8_t)((value >> 24U) & 0xFFU);
}

static uint16_t read_u16_le(const uint8_t *source)
{
    return (uint16_t)source[0] | ((uint16_t)source[1] << 8U);
}

static uint32_t read_u32_le(const uint8_t *source)
{
    return (uint32_t)source[0]
           | ((uint32_t)source[1] << 8U)
           | ((uint32_t)source[2] << 16U)
           | ((uint32_t)source[3] << 24U);
}

uint32_t trilink_crc32(const uint8_t *data, size_t len)
{
    if ((data == NULL) && (len != 0U)) {
        return 0U;
    }

    uint32_t crc = 0xFFFFFFFFU;
    for (size_t index = 0; index < len; ++index) {
        crc ^= data[index];
        for (unsigned bit = 0; bit < 8U; ++bit) {
            const uint32_t mask = (uint32_t)(-(int32_t)(crc & 1U));
            crc = (crc >> 1U) ^ (0xEDB88320U & mask);
        }
    }
    return ~crc;
}

bool trilink_message_type_is_valid(uint8_t type)
{
    switch (type) {
    case TRILINK_MSG_HELLO:
    case TRILINK_MSG_HEARTBEAT:
    case TRILINK_MSG_LEADER_ANNOUNCE:
    case TRILINK_MSG_GAME_INPUT:
    case TRILINK_MSG_GAME_STATE:
    case TRILINK_MSG_ACK:
    case TRILINK_MSG_APP_DATA:
        return true;
    default:
        return false;
    }
}

bool trilink_frame_encode(const trilink_frame_t *frame,
                          uint8_t *output,
                          size_t output_capacity,
                          size_t *output_len)
{
    if (output_len != NULL) {
        *output_len = 0U;
    }
    if (frame == NULL) {
        return false;
    }

    const trilink_frame_view_t view = {
        .type = frame->type,
        .flags = frame->flags,
        .payload_len = frame->payload_len,
        .group_id = frame->group_id,
        .boot_id = frame->boot_id,
        .term = frame->term,
        .sequence = frame->sequence,
        .payload = (frame->payload_len == 0U) ? NULL : frame->payload,
    };
    return trilink_frame_encode_view(&view,
                                     output,
                                     output_capacity,
                                     output_len);
}

bool trilink_frame_encode_view(const trilink_frame_view_t *frame,
                               uint8_t *output,
                               size_t output_capacity,
                               size_t *output_len)
{
    if (output_len != NULL) {
        *output_len = 0U;
    }
    if ((frame == NULL) || (output == NULL) || (output_len == NULL)
        || ((frame->payload == NULL) && (frame->payload_len != 0U))) {
        return false;
    }
    if (!trilink_message_type_is_valid(frame->type)
        || ((frame->flags & (uint8_t)(~TRILINK_FLAG_KNOWN_MASK)) != 0U)
        || (frame->payload_len > TRILINK_PAYLOAD_MAX)) {
        return false;
    }

    const size_t encoded_len = TRILINK_FRAME_HEADER_SIZE
                               + (size_t)frame->payload_len
                               + TRILINK_FRAME_CRC_SIZE;
    if (output_capacity < encoded_len) {
        return false;
    }

    write_u16_le(&output[MAGIC_OFFSET], TRILINK_FRAME_MAGIC);
    output[VERSION_OFFSET] = TRILINK_FRAME_VERSION;
    output[HEADER_LENGTH_OFFSET] = TRILINK_FRAME_HEADER_SIZE;
    output[TYPE_OFFSET] = frame->type;
    output[FLAGS_OFFSET] = frame->flags;
    write_u16_le(&output[PAYLOAD_LENGTH_OFFSET], frame->payload_len);
    write_u32_le(&output[GROUP_ID_OFFSET], frame->group_id);
    write_u32_le(&output[BOOT_ID_OFFSET], frame->boot_id);
    write_u32_le(&output[TERM_OFFSET], frame->term);
    write_u32_le(&output[SEQUENCE_OFFSET], frame->sequence);

    if (frame->payload_len != 0U) {
        memcpy(&output[TRILINK_FRAME_HEADER_SIZE],
               frame->payload,
               frame->payload_len);
    }

    const uint32_t crc = trilink_crc32(
        output,
        encoded_len - TRILINK_FRAME_CRC_SIZE);
    write_u32_le(&output[encoded_len - TRILINK_FRAME_CRC_SIZE], crc);
    *output_len = encoded_len;
    return true;
}

trilink_decode_result_t trilink_frame_decode(const uint8_t *input,
                                              size_t input_len,
                                              trilink_frame_t *frame)
{
    if (frame != NULL) {
        memset(frame, 0, sizeof(*frame));
    }
    if (frame == NULL) {
        return TRILINK_DECODE_INVALID_ARGUMENT;
    }

    trilink_frame_view_t view;
    const trilink_decode_result_t result = trilink_frame_decode_view(input,
                                                                     input_len,
                                                                     &view);
    if (result != TRILINK_DECODE_OK) {
        return result;
    }

    frame->type = view.type;
    frame->flags = view.flags;
    frame->payload_len = view.payload_len;
    frame->group_id = view.group_id;
    frame->boot_id = view.boot_id;
    frame->term = view.term;
    frame->sequence = view.sequence;
    if (view.payload_len != 0U) {
        memcpy(frame->payload, view.payload, view.payload_len);
    }
    return TRILINK_DECODE_OK;
}

trilink_decode_result_t trilink_frame_decode_view(
    const uint8_t *input,
    size_t input_len,
    trilink_frame_view_t *frame)
{
    if (frame != NULL) {
        memset(frame, 0, sizeof(*frame));
    }
    if ((input == NULL) || (frame == NULL)) {
        return TRILINK_DECODE_INVALID_ARGUMENT;
    }
    if (input_len < (TRILINK_FRAME_HEADER_SIZE + TRILINK_FRAME_CRC_SIZE)) {
        return TRILINK_DECODE_TOO_SHORT;
    }
    if (read_u16_le(&input[MAGIC_OFFSET]) != TRILINK_FRAME_MAGIC) {
        return TRILINK_DECODE_BAD_MAGIC;
    }
    if (input[VERSION_OFFSET] != TRILINK_FRAME_VERSION) {
        return TRILINK_DECODE_BAD_VERSION;
    }
    if (input[HEADER_LENGTH_OFFSET] != TRILINK_FRAME_HEADER_SIZE) {
        return TRILINK_DECODE_BAD_HEADER_LENGTH;
    }
    if (!trilink_message_type_is_valid(input[TYPE_OFFSET])) {
        return TRILINK_DECODE_BAD_TYPE;
    }
    if ((input[FLAGS_OFFSET] & (uint8_t)(~TRILINK_FLAG_KNOWN_MASK)) != 0U) {
        return TRILINK_DECODE_BAD_FLAGS;
    }

    const uint16_t payload_len = read_u16_le(&input[PAYLOAD_LENGTH_OFFSET]);
    if (payload_len > TRILINK_PAYLOAD_MAX) {
        return TRILINK_DECODE_BAD_PAYLOAD_LENGTH;
    }
    const size_t expected_len = TRILINK_FRAME_HEADER_SIZE
                                + (size_t)payload_len
                                + TRILINK_FRAME_CRC_SIZE;
    if (input_len != expected_len) {
        return TRILINK_DECODE_BAD_TOTAL_LENGTH;
    }

    const uint32_t expected_crc = read_u32_le(
        &input[expected_len - TRILINK_FRAME_CRC_SIZE]);
    const uint32_t actual_crc = trilink_crc32(
        input,
        expected_len - TRILINK_FRAME_CRC_SIZE);
    if (expected_crc != actual_crc) {
        return TRILINK_DECODE_BAD_CRC;
    }

    frame->type = input[TYPE_OFFSET];
    frame->flags = input[FLAGS_OFFSET];
    frame->payload_len = payload_len;
    frame->group_id = read_u32_le(&input[GROUP_ID_OFFSET]);
    frame->boot_id = read_u32_le(&input[BOOT_ID_OFFSET]);
    frame->term = read_u32_le(&input[TERM_OFFSET]);
    frame->sequence = read_u32_le(&input[SEQUENCE_OFFSET]);
    frame->payload = (payload_len == 0U)
                         ? NULL
                         : &input[TRILINK_FRAME_HEADER_SIZE];
    return TRILINK_DECODE_OK;
}

bool trilink_hello_encode(const trilink_hello_t *hello,
                          uint8_t *output,
                          size_t output_capacity)
{
    if ((hello == NULL) || (output == NULL)
        || (output_capacity < TRILINK_HELLO_PAYLOAD_SIZE)
        || (hello->hello_interval_ms == 0U)
        || (hello->max_nodes != TRILINK_CLUSTER_NODE_COUNT && hello->max_nodes != TRILINK_ROOM_MAX_MEMBERS)) {
        return false;
    }

    write_u32_le(&output[0], hello->capabilities);
    write_u16_le(&output[4], hello->hello_interval_ms);
    output[6] = hello->max_nodes;
    output[7] = 0U;
    return true;
}

bool trilink_hello_decode(const uint8_t *input,
                          size_t input_len,
                          trilink_hello_t *hello)
{
    if (hello != NULL) {
        memset(hello, 0, sizeof(*hello));
    }
    if ((input == NULL) || (hello == NULL)
        || (input_len != TRILINK_HELLO_PAYLOAD_SIZE)
        || (input[7] != 0U)) {
        return false;
    }

    hello->capabilities = read_u32_le(&input[0]);
    hello->hello_interval_ms = read_u16_le(&input[4]);
    hello->max_nodes = input[6];
    return (hello->hello_interval_ms != 0U)
           && (hello->max_nodes == TRILINK_CLUSTER_NODE_COUNT || hello->max_nodes == TRILINK_ROOM_MAX_MEMBERS);
}

const char *trilink_decode_result_name(trilink_decode_result_t result)
{
    switch (result) {
    case TRILINK_DECODE_OK:
        return "OK";
    case TRILINK_DECODE_INVALID_ARGUMENT:
        return "INVALID_ARGUMENT";
    case TRILINK_DECODE_TOO_SHORT:
        return "TOO_SHORT";
    case TRILINK_DECODE_BAD_MAGIC:
        return "BAD_MAGIC";
    case TRILINK_DECODE_BAD_VERSION:
        return "BAD_VERSION";
    case TRILINK_DECODE_BAD_HEADER_LENGTH:
        return "BAD_HEADER_LENGTH";
    case TRILINK_DECODE_BAD_TYPE:
        return "BAD_TYPE";
    case TRILINK_DECODE_BAD_FLAGS:
        return "BAD_FLAGS";
    case TRILINK_DECODE_BAD_PAYLOAD_LENGTH:
        return "BAD_PAYLOAD_LENGTH";
    case TRILINK_DECODE_BAD_TOTAL_LENGTH:
        return "BAD_TOTAL_LENGTH";
    case TRILINK_DECODE_BAD_CRC:
        return "BAD_CRC";
    default:
        return "UNKNOWN";
    }
}
