#include "trilink_protocol.h"

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

static trilink_frame_t make_frame(void)
{
    trilink_frame_t frame = {
        .type = TRILINK_MSG_APP_DATA,
        .flags = TRILINK_FLAG_ACK_REQUIRED,
        .payload_len = 3U,
        .group_id = 0x78563412U,
        .boot_id = 0xA4A3A2A1U,
        .term = 0xB4B3B2B1U,
        .sequence = 0xC4C3C2C1U,
        .payload = {0x00U, 0x7FU, 0xFFU},
    };
    return frame;
}

static size_t encode_frame(const trilink_frame_t *frame,
                           uint8_t encoded[TRILINK_FRAME_MAX_SIZE])
{
    size_t encoded_len = 999U;
    CHECK(trilink_frame_encode(frame,
                               encoded,
                               TRILINK_FRAME_MAX_SIZE,
                               &encoded_len));
    return encoded_len;
}

static void test_constants_and_crc(void)
{
    static const uint8_t known[] = "123456789";
    CHECK(TRILINK_PAYLOAD_MAX == 172U);
    CHECK(TRILINK_FRAME_HEADER_SIZE + TRILINK_PAYLOAD_MAX
          + TRILINK_FRAME_CRC_SIZE == TRILINK_FRAME_MAX_SIZE);
    CHECK(trilink_crc32(known, sizeof(known) - 1U) == 0xCBF43926U);
    CHECK(trilink_crc32(NULL, 0U) == 0U);
    CHECK(trilink_crc32(NULL, 1U) == 0U);

    const uint8_t valid_types[] = {
        TRILINK_MSG_HELLO,
        TRILINK_MSG_HEARTBEAT,
        TRILINK_MSG_LEADER_ANNOUNCE,
        TRILINK_MSG_GAME_INPUT,
        TRILINK_MSG_GAME_STATE,
        TRILINK_MSG_ACK,
        TRILINK_MSG_APP_DATA,
    };
    for (size_t index = 0;
         index < (sizeof(valid_types) / sizeof(valid_types[0]));
         ++index) {
        CHECK(trilink_message_type_is_valid(valid_types[index]));
    }
    CHECK(!trilink_message_type_is_valid(0U));
    CHECK(!trilink_message_type_is_valid(4U));
    CHECK(!trilink_message_type_is_valid(255U));
}

static void test_frame_round_trip_and_endianness(void)
{
    const trilink_frame_t input = make_frame();
    uint8_t encoded[TRILINK_FRAME_MAX_SIZE] = {0};
    const size_t encoded_len = encode_frame(&input, encoded);
    CHECK(encoded_len == TRILINK_FRAME_HEADER_SIZE + input.payload_len
                         + TRILINK_FRAME_CRC_SIZE);
    CHECK(encoded[0] == 0x54U);
    CHECK(encoded[1] == 0x4CU);
    CHECK(encoded[2] == TRILINK_FRAME_VERSION);
    CHECK(encoded[3] == TRILINK_FRAME_HEADER_SIZE);
    CHECK(encoded[4] == TRILINK_MSG_APP_DATA);
    CHECK(encoded[5] == TRILINK_FLAG_ACK_REQUIRED);
    CHECK(encoded[6] == 3U);
    CHECK(encoded[7] == 0U);
    CHECK(encoded[8] == 0x12U && encoded[11] == 0x78U);
    CHECK(encoded[12] == 0xA1U && encoded[15] == 0xA4U);
    CHECK(encoded[16] == 0xB1U && encoded[19] == 0xB4U);
    CHECK(encoded[20] == 0xC1U && encoded[23] == 0xC4U);

    trilink_frame_t output;
    CHECK(trilink_frame_decode(encoded, encoded_len, &output)
          == TRILINK_DECODE_OK);
    CHECK(output.type == input.type);
    CHECK(output.flags == input.flags);
    CHECK(output.payload_len == input.payload_len);
    CHECK(output.group_id == input.group_id);
    CHECK(output.boot_id == input.boot_id);
    CHECK(output.term == input.term);
    CHECK(output.sequence == input.sequence);
    CHECK(memcmp(output.payload, input.payload, input.payload_len) == 0);
}

static void test_non_owning_frame_view(void)
{
    const trilink_frame_t input = make_frame();
    uint8_t owned_encoded[TRILINK_FRAME_MAX_SIZE] = {0};
    const size_t owned_len = encode_frame(&input, owned_encoded);

    const trilink_frame_view_t input_view = {
        .type = input.type,
        .flags = input.flags,
        .payload_len = input.payload_len,
        .group_id = input.group_id,
        .boot_id = input.boot_id,
        .term = input.term,
        .sequence = input.sequence,
        .payload = input.payload,
    };
    uint8_t view_encoded[TRILINK_FRAME_MAX_SIZE] = {0};
    size_t view_len = 999U;
    CHECK(trilink_frame_encode_view(&input_view,
                                    view_encoded,
                                    sizeof(view_encoded),
                                    &view_len));
    CHECK(view_len == owned_len);
    CHECK(memcmp(view_encoded, owned_encoded, owned_len) == 0);

    trilink_frame_view_t output_view;
    CHECK(trilink_frame_decode_view(view_encoded, view_len, &output_view)
          == TRILINK_DECODE_OK);
    CHECK(output_view.type == input.type);
    CHECK(output_view.payload_len == input.payload_len);
    CHECK(output_view.group_id == input.group_id);
    CHECK(output_view.boot_id == input.boot_id);
    CHECK(output_view.term == input.term);
    CHECK(output_view.sequence == input.sequence);
    CHECK(output_view.payload
          == &view_encoded[TRILINK_FRAME_HEADER_SIZE]);
    CHECK(memcmp(output_view.payload, input.payload, input.payload_len) == 0);

    trilink_frame_view_t invalid = input_view;
    invalid.payload = NULL;
    view_len = 999U;
    CHECK(!trilink_frame_encode_view(&invalid,
                                     view_encoded,
                                     sizeof(view_encoded),
                                     &view_len));
    CHECK(view_len == 0U);

    invalid = input_view;
    invalid.payload_len = 0U;
    invalid.payload = NULL;
    CHECK(trilink_frame_encode_view(&invalid,
                                    view_encoded,
                                    sizeof(view_encoded),
                                    &view_len));
    CHECK(trilink_frame_decode_view(view_encoded, view_len, &output_view)
          == TRILINK_DECODE_OK);
    CHECK(output_view.payload_len == 0U);
    CHECK(output_view.payload == NULL);

    view_encoded[view_len - 1U] ^= 0x01U;
    CHECK(trilink_frame_decode_view(view_encoded, view_len, &output_view)
          == TRILINK_DECODE_BAD_CRC);
    CHECK(output_view.type == 0U);
    CHECK(output_view.payload == NULL);
}

static void test_maximum_frame_and_encode_rejections(void)
{
    trilink_frame_t frame = make_frame();
    frame.payload_len = TRILINK_PAYLOAD_MAX;
    memset(frame.payload, 0x5AU, sizeof(frame.payload));
    uint8_t encoded[TRILINK_FRAME_MAX_SIZE] = {0};
    size_t encoded_len = encode_frame(&frame, encoded);
    CHECK(encoded_len == TRILINK_FRAME_MAX_SIZE);

    trilink_frame_t output;
    CHECK(trilink_frame_decode(encoded, encoded_len, &output)
          == TRILINK_DECODE_OK);
    CHECK(output.payload_len == TRILINK_PAYLOAD_MAX);
    CHECK(memcmp(output.payload, frame.payload, TRILINK_PAYLOAD_MAX) == 0);

    size_t stale_len = 123U;
    CHECK(!trilink_frame_encode(NULL,
                                encoded,
                                sizeof(encoded),
                                &stale_len));
    CHECK(stale_len == 0U);
    CHECK(!trilink_frame_encode(&frame, NULL, sizeof(encoded), &stale_len));
    CHECK(!trilink_frame_encode(&frame, encoded, sizeof(encoded), NULL));
    CHECK(!trilink_frame_encode(&frame,
                                encoded,
                                sizeof(encoded) - 1U,
                                &stale_len));
    CHECK(stale_len == 0U);

    frame.type = 0U;
    CHECK(!trilink_frame_encode(&frame,
                                encoded,
                                sizeof(encoded),
                                &stale_len));
    frame = make_frame();
    frame.flags = 0x80U;
    CHECK(!trilink_frame_encode(&frame,
                                encoded,
                                sizeof(encoded),
                                &stale_len));
    frame = make_frame();
    frame.payload_len = TRILINK_PAYLOAD_MAX + 1U;
    CHECK(!trilink_frame_encode(&frame,
                                encoded,
                                sizeof(encoded),
                                &stale_len));
}

static void test_decode_rejections(void)
{
    trilink_frame_t frame = make_frame();
    uint8_t encoded[TRILINK_FRAME_MAX_SIZE + 1U] = {0};
    const size_t encoded_len = encode_frame(&frame, encoded);
    trilink_frame_t output;

    CHECK(trilink_frame_decode(NULL, 0U, &output)
          == TRILINK_DECODE_INVALID_ARGUMENT);
    CHECK(trilink_frame_decode(encoded, encoded_len, NULL)
          == TRILINK_DECODE_INVALID_ARGUMENT);
    CHECK(trilink_frame_decode(encoded,
                               TRILINK_FRAME_HEADER_SIZE
                                   + TRILINK_FRAME_CRC_SIZE - 1U,
                               &output) == TRILINK_DECODE_TOO_SHORT);

    encoded[0] ^= 0x01U;
    CHECK(trilink_frame_decode(encoded, encoded_len, &output)
          == TRILINK_DECODE_BAD_MAGIC);
    encoded[0] ^= 0x01U;
    encoded[2] += 1U;
    CHECK(trilink_frame_decode(encoded, encoded_len, &output)
          == TRILINK_DECODE_BAD_VERSION);
    encoded[2] -= 1U;
    encoded[3] -= 1U;
    CHECK(trilink_frame_decode(encoded, encoded_len, &output)
          == TRILINK_DECODE_BAD_HEADER_LENGTH);
    encoded[3] += 1U;
    encoded[4] = 0U;
    CHECK(trilink_frame_decode(encoded, encoded_len, &output)
          == TRILINK_DECODE_BAD_TYPE);
    encoded[4] = frame.type;
    encoded[5] = 0x80U;
    CHECK(trilink_frame_decode(encoded, encoded_len, &output)
          == TRILINK_DECODE_BAD_FLAGS);
    encoded[5] = frame.flags;
    encoded[6] = 0xFFU;
    encoded[7] = 0xFFU;
    CHECK(trilink_frame_decode(encoded, encoded_len, &output)
          == TRILINK_DECODE_BAD_PAYLOAD_LENGTH);

}

static void test_length_and_crc_rejections(void)
{
    const trilink_frame_t frame = make_frame();
    uint8_t encoded[TRILINK_FRAME_MAX_SIZE + 1U] = {0};
    const size_t encoded_len = encode_frame(&frame, encoded);
    trilink_frame_t output;

    CHECK(trilink_frame_decode(encoded, encoded_len - 1U, &output)
          == TRILINK_DECODE_BAD_TOTAL_LENGTH);
    CHECK(trilink_frame_decode(encoded, encoded_len + 1U, &output)
          == TRILINK_DECODE_BAD_TOTAL_LENGTH);
    encoded[TRILINK_FRAME_HEADER_SIZE + 1U] ^= 0x01U;
    CHECK(trilink_frame_decode(encoded, encoded_len, &output)
          == TRILINK_DECODE_BAD_CRC);
    CHECK(output.type == 0U);
    CHECK(output.payload_len == 0U);
}

static void test_hello_codec(void)
{
    const trilink_hello_t hello = {
        .capabilities = 0x78563412U,
        .hello_interval_ms = 500U,
        .max_nodes = TRILINK_CLUSTER_NODE_COUNT,
    };
    uint8_t encoded[TRILINK_HELLO_PAYLOAD_SIZE] = {0};
    CHECK(trilink_hello_encode(&hello, encoded, sizeof(encoded)));
    CHECK(encoded[0] == 0x12U && encoded[3] == 0x78U);
    CHECK(encoded[4] == 0xF4U && encoded[5] == 0x01U);
    CHECK(encoded[6] == TRILINK_CLUSTER_NODE_COUNT);
    CHECK(encoded[7] == 0U);

    trilink_hello_t output;
    CHECK(trilink_hello_decode(encoded, sizeof(encoded), &output));
    CHECK(output.capabilities == hello.capabilities);
    CHECK(output.hello_interval_ms == hello.hello_interval_ms);
    CHECK(output.max_nodes == hello.max_nodes);

    CHECK(!trilink_hello_encode(NULL, encoded, sizeof(encoded)));
    CHECK(!trilink_hello_encode(&hello, NULL, sizeof(encoded)));
    CHECK(!trilink_hello_encode(&hello, encoded, sizeof(encoded) - 1U));
    trilink_hello_t invalid = hello;
    invalid.hello_interval_ms = 0U;
    CHECK(!trilink_hello_encode(&invalid, encoded, sizeof(encoded)));
    invalid = hello;
    invalid.max_nodes = 4U;
    CHECK(!trilink_hello_encode(&invalid, encoded, sizeof(encoded)));

    CHECK(!trilink_hello_decode(NULL, sizeof(encoded), &output));
    CHECK(!trilink_hello_decode(encoded, sizeof(encoded), NULL));
    CHECK(!trilink_hello_decode(encoded, sizeof(encoded) - 1U, &output));
    encoded[7] = 1U;
    CHECK(!trilink_hello_decode(encoded, sizeof(encoded), &output));
    CHECK(output.capabilities == 0U);
    encoded[7] = 0U;
    encoded[6] = 4U;
    CHECK(!trilink_hello_decode(encoded, sizeof(encoded), &output));
    trilink_hello_t six = hello;
    six.max_nodes = TRILINK_ROOM_MAX_MEMBERS;
    six.capabilities |= TRILINK_CAP_AUTO_ROUTE;
    CHECK(trilink_hello_encode(&six, encoded, sizeof(encoded)));
    CHECK(trilink_hello_decode(encoded, sizeof(encoded), &output));
    CHECK(output.max_nodes == 6U);
    CHECK((output.capabilities & TRILINK_CAP_AUTO_ROUTE) != 0U);
}

static void test_result_names(void)
{
    CHECK(strcmp(trilink_decode_result_name(TRILINK_DECODE_OK), "OK") == 0);
    CHECK(strcmp(trilink_decode_result_name(TRILINK_DECODE_BAD_CRC),
                 "BAD_CRC") == 0);
    CHECK(strcmp(trilink_decode_result_name((trilink_decode_result_t)99),
                 "UNKNOWN") == 0);
}

int main(void)
{
    test_constants_and_crc();
    test_frame_round_trip_and_endianness();
    test_non_owning_frame_view();
    test_maximum_frame_and_encode_rejections();
    test_decode_rejections();
    test_length_and_crc_rejections();
    test_hello_codec();
    test_result_names();

    printf("trilink_protocol host tests: checks=%u failures=%u\n",
           s_checks,
           s_failures);
    return (s_failures == 0U) ? 0 : 1;
}
