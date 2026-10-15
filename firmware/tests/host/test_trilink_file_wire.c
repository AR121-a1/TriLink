#include "trilink_file_wire.h"

#include <limits.h>
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

static void make_resource_id(uint8_t resource_id[TRILINK_RESOURCE_ID_SIZE])
{
    for (size_t index = 0U; index < TRILINK_RESOURCE_ID_SIZE; ++index) {
        resource_id[index] = (uint8_t)(0xA0U + index);
    }
}

static void test_constants(void)
{
    CHECK(TRILINK_FILE_OPEN_DATA_SIZE == 44U);
    CHECK(TRILINK_FILE_CHUNK_HEADER_SIZE == 10U);
    CHECK(TRILINK_FILE_CHUNK_DATA_MAX == 158U);
    CHECK(TRILINK_FILE_ACK_DATA_SIZE == 12U);
    CHECK(TRILINK_FILE_CLOSE_DATA_SIZE == 9U);
    CHECK(TRILINK_FILE_CANCEL_DATA_SIZE == 5U);
    CHECK(TRILINK_FILE_WINDOW_MAX == 32U);
    CHECK(TRILINK_FILE_CHUNK_HEADER_SIZE + TRILINK_FILE_CHUNK_DATA_MAX
          == TRILINK_SERVICE_DATA_MAX);
}

static void test_open_round_trip(void)
{
    uint8_t resource_id[TRILINK_RESOURCE_ID_SIZE];
    make_resource_id(resource_id);
    const trilink_file_open_view_t input = {
        .transfer_id = 0x78563412U,
        .resource_id = resource_id,
        .transfer_size = 0x44332211U,
        .chunk_size = TRILINK_FILE_CHUNK_DATA_MAX,
        .window_size = 4U,
        .flags = 0U,
    };
    uint8_t encoded[TRILINK_PAYLOAD_MAX] = {0};
    size_t encoded_len = 999U;
    CHECK(trilink_file_open_encode(&input,
                                   encoded,
                                   sizeof(encoded),
                                   &encoded_len));
    CHECK(encoded_len == TRILINK_SERVICE_HEADER_SIZE
                         + TRILINK_FILE_OPEN_DATA_SIZE);
    CHECK(encoded[0] == TRILINK_SERVICE_ID_FILE);
    CHECK(encoded[1] == TRILINK_FILE_OP_OPEN);
    CHECK(encoded[2] == TRILINK_FILE_OPEN_DATA_SIZE && encoded[3] == 0U);
    CHECK(encoded[4] == 0x12U && encoded[7] == 0x78U);
    CHECK(memcmp(&encoded[8], resource_id, sizeof(resource_id)) == 0);
    CHECK(encoded[40] == 0x11U && encoded[43] == 0x44U);
    CHECK(encoded[44] == (TRILINK_FILE_CHUNK_DATA_MAX & 0xFFU));
    CHECK(encoded[45] == 0U);
    CHECK(encoded[46] == 4U && encoded[47] == 0U);

    trilink_file_open_view_t output;
    CHECK(trilink_file_open_decode_view(encoded, encoded_len, &output));
    CHECK(output.transfer_id == input.transfer_id);
    CHECK(output.resource_id == &encoded[8]);
    CHECK(memcmp(output.resource_id, resource_id, sizeof(resource_id)) == 0);
    CHECK(output.transfer_size == input.transfer_size);
    CHECK(output.chunk_size == input.chunk_size);
    CHECK(output.window_size == input.window_size);
    CHECK(output.flags == 0U);

    trilink_file_open_view_t empty_resource = input;
    empty_resource.transfer_size = 0U;
    empty_resource.chunk_size = 1U;
    empty_resource.window_size = TRILINK_FILE_WINDOW_MAX;
    CHECK(trilink_file_open_encode(&empty_resource,
                                   encoded,
                                   sizeof(encoded),
                                   &encoded_len));
    CHECK(trilink_file_open_decode_view(encoded, encoded_len, &output));
    CHECK(output.transfer_size == 0U && output.chunk_size == 1U);
    CHECK(output.window_size == TRILINK_FILE_WINDOW_MAX);
}

static void test_open_rejections(void)
{
    uint8_t resource_id[TRILINK_RESOURCE_ID_SIZE];
    make_resource_id(resource_id);
    trilink_file_open_view_t input = {
        .transfer_id = 1U,
        .resource_id = resource_id,
        .transfer_size = 100U,
        .chunk_size = 100U,
        .window_size = 4U,
        .flags = 0U,
    };
    uint8_t encoded[TRILINK_PAYLOAD_MAX] = {0};
    size_t encoded_len = 123U;
    CHECK(!trilink_file_open_encode(NULL,
                                    encoded,
                                    sizeof(encoded),
                                    &encoded_len));
    CHECK(encoded_len == 0U);
    input.transfer_id = 0U;
    CHECK(!trilink_file_open_encode(&input,
                                    encoded,
                                    sizeof(encoded),
                                    &encoded_len));
    input.transfer_id = 1U;
    input.resource_id = NULL;
    CHECK(!trilink_file_open_encode(&input,
                                    encoded,
                                    sizeof(encoded),
                                    &encoded_len));
    input.resource_id = resource_id;
    input.chunk_size = 0U;
    CHECK(!trilink_file_open_encode(&input,
                                    encoded,
                                    sizeof(encoded),
                                    &encoded_len));
    input.chunk_size = TRILINK_FILE_CHUNK_DATA_MAX + 1U;
    CHECK(!trilink_file_open_encode(&input,
                                    encoded,
                                    sizeof(encoded),
                                    &encoded_len));
    input.chunk_size = 100U;
    input.window_size = 0U;
    CHECK(!trilink_file_open_encode(&input,
                                    encoded,
                                    sizeof(encoded),
                                    &encoded_len));
    input.window_size = TRILINK_FILE_WINDOW_MAX + 1U;
    CHECK(!trilink_file_open_encode(&input,
                                    encoded,
                                    sizeof(encoded),
                                    &encoded_len));
    input.window_size = 4U;
    input.flags = 1U;
    CHECK(!trilink_file_open_encode(&input,
                                    encoded,
                                    sizeof(encoded),
                                    &encoded_len));
    input.flags = 0U;
    CHECK(!trilink_file_open_encode(&input,
                                    encoded,
                                    TRILINK_SERVICE_HEADER_SIZE,
                                    &encoded_len));
    CHECK(!trilink_file_open_encode(&input,
                                    NULL,
                                    sizeof(encoded),
                                    &encoded_len));
    CHECK(!trilink_file_open_encode(&input,
                                    encoded,
                                    sizeof(encoded),
                                    NULL));

    CHECK(trilink_file_open_encode(&input,
                                   encoded,
                                   sizeof(encoded),
                                   &encoded_len));
    trilink_file_open_view_t output;
    CHECK(!trilink_file_open_decode_view(NULL, encoded_len, &output));
    CHECK(output.resource_id == NULL && output.transfer_id == 0U);
    CHECK(!trilink_file_open_decode_view(encoded, encoded_len, NULL));
    encoded[0] = TRILINK_SERVICE_ID_CONTENT;
    CHECK(!trilink_file_open_decode_view(encoded, encoded_len, &output));
    encoded[0] = TRILINK_SERVICE_ID_FILE;
    encoded[1] = TRILINK_FILE_OP_ACK;
    CHECK(!trilink_file_open_decode_view(encoded, encoded_len, &output));
    encoded[1] = TRILINK_FILE_OP_OPEN;
    encoded[4] = 0U;
    encoded[5] = 0U;
    encoded[6] = 0U;
    encoded[7] = 0U;
    CHECK(!trilink_file_open_decode_view(encoded, encoded_len, &output));
    CHECK(output.resource_id == NULL && output.transfer_id == 0U);
}

static void test_chunk_round_trip_and_limits(void)
{
    static const uint8_t bytes[] = {0x00U, 0x7FU, 0x80U, 0xFFU};
    const trilink_file_chunk_view_t input = {
        .transfer_id = 0x44332211U,
        .offset = 0x10203040U,
        .data_len = (uint16_t)sizeof(bytes),
        .data = bytes,
    };
    uint8_t encoded[TRILINK_PAYLOAD_MAX] = {0};
    size_t encoded_len = 0U;
    CHECK(trilink_file_chunk_encode(&input,
                                    encoded,
                                    sizeof(encoded),
                                    &encoded_len));
    CHECK(encoded_len == TRILINK_SERVICE_HEADER_SIZE
                         + TRILINK_FILE_CHUNK_HEADER_SIZE + sizeof(bytes));
    CHECK(encoded[0] == TRILINK_SERVICE_ID_FILE);
    CHECK(encoded[1] == TRILINK_FILE_OP_CHUNK);
    CHECK(encoded[4] == 0x11U && encoded[7] == 0x44U);
    CHECK(encoded[8] == 0x40U && encoded[11] == 0x10U);
    CHECK(encoded[12] == sizeof(bytes) && encoded[13] == 0U);
    CHECK(memcmp(&encoded[14], bytes, sizeof(bytes)) == 0);

    trilink_file_chunk_view_t output;
    CHECK(trilink_file_chunk_decode_view(encoded, encoded_len, &output));
    CHECK(output.transfer_id == input.transfer_id);
    CHECK(output.offset == input.offset);
    CHECK(output.data_len == sizeof(bytes));
    CHECK(output.data == &encoded[TRILINK_SERVICE_HEADER_SIZE
                                  + TRILINK_FILE_CHUNK_HEADER_SIZE]);
    CHECK(memcmp(output.data, bytes, sizeof(bytes)) == 0);

    uint8_t maximum[TRILINK_FILE_CHUNK_DATA_MAX];
    for (size_t index = 0U; index < sizeof(maximum); ++index) {
        maximum[index] = (uint8_t)index;
    }
    trilink_file_chunk_view_t max_input = input;
    max_input.offset = 0U;
    max_input.data_len = (uint16_t)sizeof(maximum);
    max_input.data = maximum;
    CHECK(trilink_file_chunk_encode(&max_input,
                                    encoded,
                                    sizeof(encoded),
                                    &encoded_len));
    CHECK(encoded_len == TRILINK_PAYLOAD_MAX);
    CHECK(trilink_file_chunk_decode_view(encoded, encoded_len, &output));
    CHECK(output.data_len == TRILINK_FILE_CHUNK_DATA_MAX);
    CHECK(memcmp(output.data, maximum, sizeof(maximum)) == 0);
}

static void test_chunk_rejections(void)
{
    static const uint8_t byte = 0x42U;
    trilink_file_chunk_view_t input = {
        .transfer_id = 1U,
        .offset = 0U,
        .data_len = 1U,
        .data = &byte,
    };
    uint8_t encoded[TRILINK_PAYLOAD_MAX] = {0};
    size_t encoded_len = 123U;
    CHECK(!trilink_file_chunk_encode(NULL,
                                     encoded,
                                     sizeof(encoded),
                                     &encoded_len));
    CHECK(encoded_len == 0U);
    input.transfer_id = 0U;
    CHECK(!trilink_file_chunk_encode(&input,
                                     encoded,
                                     sizeof(encoded),
                                     &encoded_len));
    input.transfer_id = 1U;
    input.data_len = 0U;
    CHECK(!trilink_file_chunk_encode(&input,
                                     encoded,
                                     sizeof(encoded),
                                     &encoded_len));
    input.data_len = TRILINK_FILE_CHUNK_DATA_MAX + 1U;
    CHECK(!trilink_file_chunk_encode(&input,
                                     encoded,
                                     sizeof(encoded),
                                     &encoded_len));
    input.data_len = 1U;
    input.data = NULL;
    CHECK(!trilink_file_chunk_encode(&input,
                                     encoded,
                                     sizeof(encoded),
                                     &encoded_len));
    input.data = &byte;
    input.offset = UINT32_MAX;
    CHECK(!trilink_file_chunk_encode(&input,
                                     encoded,
                                     sizeof(encoded),
                                     &encoded_len));
    input.offset = 0U;
    CHECK(!trilink_file_chunk_encode(&input,
                                     encoded,
                                     TRILINK_SERVICE_HEADER_SIZE,
                                     &encoded_len));

    CHECK(trilink_file_chunk_encode(&input,
                                    encoded,
                                    sizeof(encoded),
                                    &encoded_len));
    trilink_file_chunk_view_t output;
    CHECK(!trilink_file_chunk_decode_view(NULL, encoded_len, &output));
    CHECK(output.data == NULL && output.transfer_id == 0U);
    CHECK(!trilink_file_chunk_decode_view(encoded, encoded_len, NULL));
    encoded[12] = 0U;
    CHECK(!trilink_file_chunk_decode_view(encoded, encoded_len, &output));
    encoded[12] = 2U;
    CHECK(!trilink_file_chunk_decode_view(encoded, encoded_len, &output));
    encoded[12] = 1U;
    encoded[4] = 0U;
    encoded[5] = 0U;
    encoded[6] = 0U;
    encoded[7] = 0U;
    CHECK(!trilink_file_chunk_decode_view(encoded, encoded_len, &output));
}

static void test_ack(void)
{
    const trilink_file_ack_t input = {
        .transfer_id = 0x78563412U,
        .base_chunk = 0x44332211U,
        .received_bitmap = 0xA5A5F00FU,
    };
    uint8_t encoded[TRILINK_PAYLOAD_MAX] = {0};
    size_t encoded_len = 0U;
    CHECK(trilink_file_ack_encode(&input,
                                  encoded,
                                  sizeof(encoded),
                                  &encoded_len));
    CHECK(encoded_len == TRILINK_SERVICE_HEADER_SIZE
                         + TRILINK_FILE_ACK_DATA_SIZE);
    CHECK(encoded[1] == TRILINK_FILE_OP_ACK);
    CHECK(encoded[4] == 0x12U && encoded[7] == 0x78U);
    CHECK(encoded[8] == 0x11U && encoded[11] == 0x44U);
    CHECK(encoded[12] == 0x0FU && encoded[15] == 0xA5U);

    trilink_file_ack_t output;
    CHECK(trilink_file_ack_decode(encoded, encoded_len, &output));
    CHECK(output.transfer_id == input.transfer_id);
    CHECK(output.base_chunk == input.base_chunk);
    CHECK(output.received_bitmap == input.received_bitmap);

    trilink_file_ack_t zero_bitmap = input;
    zero_bitmap.received_bitmap = 0U;
    CHECK(trilink_file_ack_encode(&zero_bitmap,
                                  encoded,
                                  sizeof(encoded),
                                  &encoded_len));
    CHECK(trilink_file_ack_decode(encoded, encoded_len, &output));
    CHECK(output.received_bitmap == 0U);

    trilink_file_ack_t invalid = input;
    invalid.transfer_id = 0U;
    CHECK(!trilink_file_ack_encode(&invalid,
                                   encoded,
                                   sizeof(encoded),
                                   &encoded_len));
    CHECK(!trilink_file_ack_encode(NULL,
                                   encoded,
                                   sizeof(encoded),
                                   &encoded_len));
    CHECK(!trilink_file_ack_encode(&input,
                                   encoded,
                                   TRILINK_SERVICE_HEADER_SIZE,
                                   &encoded_len));
    CHECK(trilink_file_ack_encode(&input,
                                  encoded,
                                  sizeof(encoded),
                                  &encoded_len));
    CHECK(!trilink_file_ack_decode(NULL, encoded_len, &output));
    CHECK(!trilink_file_ack_decode(encoded, encoded_len, NULL));
    encoded[2] -= 1U;
    CHECK(!trilink_file_ack_decode(encoded, encoded_len - 1U, &output));
    encoded[2] += 1U;
    memset(&encoded[4], 0, 4U);
    CHECK(!trilink_file_ack_decode(encoded, encoded_len, &output));
    CHECK(output.transfer_id == 0U && output.received_bitmap == 0U);
}

static void test_close_and_cancel(void)
{
    const trilink_file_close_t close_input = {
        .transfer_id = 7U,
        .received_size = 0x78563412U,
        .status = TRILINK_FILE_CLOSE_HASH_MISMATCH,
    };
    uint8_t encoded[TRILINK_PAYLOAD_MAX] = {0};
    size_t encoded_len = 0U;
    CHECK(trilink_file_close_encode(&close_input,
                                    encoded,
                                    sizeof(encoded),
                                    &encoded_len));
    CHECK(encoded_len == TRILINK_SERVICE_HEADER_SIZE
                         + TRILINK_FILE_CLOSE_DATA_SIZE);
    CHECK(encoded[1] == TRILINK_FILE_OP_CLOSE);
    CHECK(encoded[8] == 0x12U && encoded[11] == 0x78U);
    CHECK(encoded[12] == TRILINK_FILE_CLOSE_HASH_MISMATCH);
    trilink_file_close_t close_output;
    CHECK(trilink_file_close_decode(encoded, encoded_len, &close_output));
    CHECK(close_output.transfer_id == close_input.transfer_id);
    CHECK(close_output.received_size == close_input.received_size);
    CHECK(close_output.status == close_input.status);

    trilink_file_close_t close_invalid = close_input;
    close_invalid.transfer_id = 0U;
    CHECK(!trilink_file_close_encode(&close_invalid,
                                     encoded,
                                     sizeof(encoded),
                                     &encoded_len));
    close_invalid = close_input;
    close_invalid.status = TRILINK_FILE_CLOSE_IO_ERROR + 1U;
    CHECK(!trilink_file_close_encode(&close_invalid,
                                     encoded,
                                     sizeof(encoded),
                                     &encoded_len));
    CHECK(trilink_file_close_encode(&close_input,
                                    encoded,
                                    sizeof(encoded),
                                    &encoded_len));
    encoded[12] = 0xFFU;
    CHECK(!trilink_file_close_decode(encoded, encoded_len, &close_output));
    CHECK(close_output.transfer_id == 0U);

    const trilink_file_cancel_t cancel_input = {
        .transfer_id = 9U,
        .reason = TRILINK_FILE_CANCEL_TIMEOUT,
    };
    CHECK(trilink_file_cancel_encode(&cancel_input,
                                     encoded,
                                     sizeof(encoded),
                                     &encoded_len));
    CHECK(encoded_len == TRILINK_SERVICE_HEADER_SIZE
                         + TRILINK_FILE_CANCEL_DATA_SIZE);
    CHECK(encoded[1] == TRILINK_FILE_OP_CANCEL);
    CHECK(encoded[8] == TRILINK_FILE_CANCEL_TIMEOUT);
    trilink_file_cancel_t cancel_output;
    CHECK(trilink_file_cancel_decode(encoded, encoded_len, &cancel_output));
    CHECK(cancel_output.transfer_id == cancel_input.transfer_id);
    CHECK(cancel_output.reason == cancel_input.reason);

    trilink_file_cancel_t cancel_invalid = cancel_input;
    cancel_invalid.transfer_id = 0U;
    CHECK(!trilink_file_cancel_encode(&cancel_invalid,
                                      encoded,
                                      sizeof(encoded),
                                      &encoded_len));
    cancel_invalid = cancel_input;
    cancel_invalid.reason = 0U;
    CHECK(!trilink_file_cancel_encode(&cancel_invalid,
                                      encoded,
                                      sizeof(encoded),
                                      &encoded_len));
    cancel_invalid.reason = TRILINK_FILE_CANCEL_RESOURCE_CHANGED + 1U;
    CHECK(!trilink_file_cancel_encode(&cancel_invalid,
                                      encoded,
                                      sizeof(encoded),
                                      &encoded_len));
    CHECK(trilink_file_cancel_encode(&cancel_input,
                                     encoded,
                                     sizeof(encoded),
                                     &encoded_len));
    encoded[8] = 0U;
    CHECK(!trilink_file_cancel_decode(encoded, encoded_len, &cancel_output));
    CHECK(cancel_output.transfer_id == 0U && cancel_output.reason == 0U);
}

static void test_app_data_composition(void)
{
    static const uint8_t bytes[] = {1U, 2U, 3U, 4U, 5U};
    const trilink_file_chunk_view_t chunk = {
        .transfer_id = 3U,
        .offset = 158U,
        .data_len = (uint16_t)sizeof(bytes),
        .data = bytes,
    };
    uint8_t payload[TRILINK_PAYLOAD_MAX] = {0};
    size_t payload_len = 0U;
    CHECK(trilink_file_chunk_encode(&chunk,
                                    payload,
                                    sizeof(payload),
                                    &payload_len));
    const trilink_frame_view_t frame = {
        .type = TRILINK_MSG_APP_DATA,
        .flags = 0U,
        .payload_len = (uint16_t)payload_len,
        .group_id = 0x31474C54UL,
        .boot_id = 5U,
        .term = 1U,
        .sequence = 88U,
        .payload = payload,
    };
    uint8_t wire[TRILINK_FRAME_MAX_SIZE] = {0};
    size_t wire_len = 0U;
    CHECK(trilink_frame_encode_view(&frame, wire, sizeof(wire), &wire_len));
    trilink_frame_view_t decoded_frame;
    CHECK(trilink_frame_decode_view(wire, wire_len, &decoded_frame)
          == TRILINK_DECODE_OK);
    trilink_file_chunk_view_t decoded_chunk;
    CHECK(trilink_file_chunk_decode_view(decoded_frame.payload,
                                         decoded_frame.payload_len,
                                         &decoded_chunk));
    CHECK(decoded_chunk.transfer_id == chunk.transfer_id);
    CHECK(decoded_chunk.offset == chunk.offset);
    CHECK(decoded_chunk.data
          == &wire[TRILINK_FRAME_HEADER_SIZE
                   + TRILINK_SERVICE_HEADER_SIZE
                   + TRILINK_FILE_CHUNK_HEADER_SIZE]);
    CHECK(memcmp(decoded_chunk.data, bytes, sizeof(bytes)) == 0);
}

int main(void)
{
    test_constants();
    test_open_round_trip();
    test_open_rejections();
    test_chunk_round_trip_and_limits();
    test_chunk_rejections();
    test_ack();
    test_close_and_cancel();
    test_app_data_composition();

    printf("trilink_file_wire host tests: checks=%u failures=%u\n",
           s_checks,
           s_failures);
    return (s_failures == 0U) ? 0 : 1;
}
