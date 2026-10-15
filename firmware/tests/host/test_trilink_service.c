#include "trilink_service.h"

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

static void test_constants_and_ids(void)
{
    CHECK(TRILINK_SERVICE_HEADER_SIZE == 4U);
    CHECK(TRILINK_SERVICE_DATA_MAX == 168U);
    CHECK(TRILINK_SERVICE_HEADER_SIZE + TRILINK_SERVICE_DATA_MAX
          == TRILINK_PAYLOAD_MAX);
    CHECK(!trilink_service_id_is_valid(0U));
    CHECK(trilink_service_id_is_valid(TRILINK_SERVICE_ID_GAME));
    CHECK(trilink_service_id_is_valid(TRILINK_SERVICE_ID_FILE));
    CHECK(trilink_service_id_is_valid(TRILINK_SERVICE_ID_DIAGNOSTIC));
    CHECK(trilink_service_id_is_valid(TRILINK_SERVICE_ID_CONTENT));
    CHECK(trilink_service_id_is_valid(TRILINK_SERVICE_ID_USER_MIN));
    CHECK(trilink_service_id_is_valid(255U));
}

static void test_round_trip(void)
{
    static const uint8_t data[] = {0x00U, 0x7FU, 0xFFU};
    const trilink_service_view_t input = {
        .service_id = TRILINK_SERVICE_ID_FILE,
        .opcode = 0xA5U,
        .data_len = sizeof(data),
        .data = data,
    };
    uint8_t encoded[TRILINK_PAYLOAD_MAX] = {0};
    size_t encoded_len = 999U;
    CHECK(trilink_service_encode(&input,
                                 encoded,
                                 sizeof(encoded),
                                 &encoded_len));
    CHECK(encoded_len == TRILINK_SERVICE_HEADER_SIZE + sizeof(data));
    CHECK(encoded[0] == TRILINK_SERVICE_ID_FILE);
    CHECK(encoded[1] == 0xA5U);
    CHECK(encoded[2] == sizeof(data));
    CHECK(encoded[3] == 0U);
    CHECK(memcmp(&encoded[TRILINK_SERVICE_HEADER_SIZE], data, sizeof(data))
          == 0);

    trilink_service_view_t output;
    CHECK(trilink_service_decode_view(encoded, encoded_len, &output));
    CHECK(output.service_id == input.service_id);
    CHECK(output.opcode == input.opcode);
    CHECK(output.data_len == input.data_len);
    CHECK(output.data == &encoded[TRILINK_SERVICE_HEADER_SIZE]);
    CHECK(memcmp(output.data, data, sizeof(data)) == 0);
}

static void test_empty_and_maximum(void)
{
    trilink_service_view_t message = {
        .service_id = TRILINK_SERVICE_ID_GAME,
        .opcode = 0U,
        .data_len = 0U,
        .data = NULL,
    };
    uint8_t encoded[TRILINK_PAYLOAD_MAX] = {0};
    size_t encoded_len = 0U;
    CHECK(trilink_service_encode(&message,
                                 encoded,
                                 sizeof(encoded),
                                 &encoded_len));
    CHECK(encoded_len == TRILINK_SERVICE_HEADER_SIZE);
    trilink_service_view_t output;
    CHECK(trilink_service_decode_view(encoded, encoded_len, &output));
    CHECK(output.data_len == 0U);
    CHECK(output.data == NULL);

    uint8_t data[TRILINK_SERVICE_DATA_MAX];
    for (size_t index = 0; index < sizeof(data); ++index) {
        data[index] = (uint8_t)index;
    }
    message.service_id = 255U;
    message.opcode = 0xFFU;
    message.data_len = sizeof(data);
    message.data = data;
    CHECK(trilink_service_encode(&message,
                                 encoded,
                                 sizeof(encoded),
                                 &encoded_len));
    CHECK(encoded_len == TRILINK_PAYLOAD_MAX);
    CHECK(trilink_service_decode_view(encoded, encoded_len, &output));
    CHECK(output.data_len == TRILINK_SERVICE_DATA_MAX);
    CHECK(memcmp(output.data, data, sizeof(data)) == 0);
}

static void test_encode_rejections(void)
{
    static const uint8_t byte = 0x42U;
    trilink_service_view_t message = {
        .service_id = TRILINK_SERVICE_ID_FILE,
        .opcode = 1U,
        .data_len = 1U,
        .data = &byte,
    };
    uint8_t encoded[TRILINK_PAYLOAD_MAX] = {0};
    size_t encoded_len = 123U;

    CHECK(!trilink_service_encode(NULL,
                                  encoded,
                                  sizeof(encoded),
                                  &encoded_len));
    CHECK(encoded_len == 0U);
    encoded_len = 123U;
    CHECK(!trilink_service_encode(&message,
                                  NULL,
                                  sizeof(encoded),
                                  &encoded_len));
    CHECK(encoded_len == 0U);
    CHECK(!trilink_service_encode(&message,
                                  encoded,
                                  sizeof(encoded),
                                  NULL));
    CHECK(!trilink_service_encode(&message,
                                  encoded,
                                  TRILINK_SERVICE_HEADER_SIZE,
                                  &encoded_len));
    CHECK(encoded_len == 0U);

    message.service_id = 0U;
    CHECK(!trilink_service_encode(&message,
                                  encoded,
                                  sizeof(encoded),
                                  &encoded_len));
    message.service_id = TRILINK_SERVICE_ID_FILE;
    message.data = NULL;
    CHECK(!trilink_service_encode(&message,
                                  encoded,
                                  sizeof(encoded),
                                  &encoded_len));
    message.data = &byte;
    message.data_len = TRILINK_SERVICE_DATA_MAX + 1U;
    CHECK(!trilink_service_encode(&message,
                                  encoded,
                                  sizeof(encoded),
                                  &encoded_len));
}

static void test_decode_rejections(void)
{
    uint8_t encoded[TRILINK_PAYLOAD_MAX + 1U] = {
        TRILINK_SERVICE_ID_FILE, 1U, 1U, 0U, 0x42U,
    };
    trilink_service_view_t output = {
        .service_id = 99U,
        .data = encoded,
    };

    CHECK(!trilink_service_decode_view(NULL, sizeof(encoded), &output));
    CHECK(output.service_id == 0U && output.data == NULL);
    CHECK(!trilink_service_decode_view(encoded, sizeof(encoded), NULL));
    CHECK(!trilink_service_decode_view(encoded,
                                       TRILINK_SERVICE_HEADER_SIZE - 1U,
                                       &output));
    encoded[0] = 0U;
    CHECK(!trilink_service_decode_view(encoded, 5U, &output));
    encoded[0] = TRILINK_SERVICE_ID_FILE;
    CHECK(!trilink_service_decode_view(encoded, 4U, &output));
    CHECK(!trilink_service_decode_view(encoded, 6U, &output));
    encoded[2] = (uint8_t)(TRILINK_SERVICE_DATA_MAX + 1U);
    encoded[3] = 0U;
    CHECK(!trilink_service_decode_view(encoded, 4U, &output));
    CHECK(output.service_id == 0U && output.data == NULL);
}

static void test_app_data_composition(void)
{
    static const uint8_t file_chunk[] = {1U, 2U, 3U, 4U, 5U};
    const trilink_service_view_t service = {
        .service_id = TRILINK_SERVICE_ID_FILE,
        .opcode = 2U,
        .data_len = sizeof(file_chunk),
        .data = file_chunk,
    };
    uint8_t payload[TRILINK_PAYLOAD_MAX] = {0};
    size_t payload_len = 0U;
    CHECK(trilink_service_encode(&service,
                                 payload,
                                 sizeof(payload),
                                 &payload_len));

    const trilink_frame_view_t frame = {
        .type = TRILINK_MSG_APP_DATA,
        .flags = TRILINK_FLAG_ACK_REQUIRED,
        .payload_len = (uint16_t)payload_len,
        .group_id = 0x31474C54UL,
        .boot_id = 7U,
        .term = 1U,
        .sequence = 99U,
        .payload = payload,
    };
    uint8_t wire[TRILINK_FRAME_MAX_SIZE] = {0};
    size_t wire_len = 0U;
    CHECK(trilink_frame_encode_view(&frame,
                                    wire,
                                    sizeof(wire),
                                    &wire_len));

    trilink_frame_view_t decoded_frame;
    CHECK(trilink_frame_decode_view(wire, wire_len, &decoded_frame)
          == TRILINK_DECODE_OK);
    CHECK(decoded_frame.type == TRILINK_MSG_APP_DATA);
    CHECK(decoded_frame.payload
          == &wire[TRILINK_FRAME_HEADER_SIZE]);

    trilink_service_view_t decoded_service;
    CHECK(trilink_service_decode_view(decoded_frame.payload,
                                      decoded_frame.payload_len,
                                      &decoded_service));
    CHECK(decoded_service.service_id == TRILINK_SERVICE_ID_FILE);
    CHECK(decoded_service.opcode == service.opcode);
    CHECK(decoded_service.data_len == sizeof(file_chunk));
    CHECK(decoded_service.data
          == &wire[TRILINK_FRAME_HEADER_SIZE
                   + TRILINK_SERVICE_HEADER_SIZE]);
    CHECK(memcmp(decoded_service.data,
                 file_chunk,
                 sizeof(file_chunk)) == 0);
}

int main(void)
{
    test_constants_and_ids();
    test_round_trip();
    test_empty_and_maximum();
    test_encode_rejections();
    test_decode_rejections();
    test_app_data_composition();

    printf("trilink_service host tests: checks=%u failures=%u\n",
           s_checks,
           s_failures);
    return (s_failures == 0U) ? 0 : 1;
}
