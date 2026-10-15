#include "trilink_content.h"

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
        resource_id[index] = (uint8_t)(index + 1U);
    }
}

static void test_constants_and_utf8(void)
{
    CHECK(TRILINK_SERVICE_ID_CONTENT == 4U);
    CHECK(TRILINK_RESOURCE_ID_SIZE == 32U);
    CHECK(TRILINK_CONTENT_SUMMARY_HEADER_SIZE == 40U);
    CHECK(TRILINK_CONTENT_SUMMARY_TEXT_MAX == 128U);
    CHECK(TRILINK_CONTENT_GET_RANGE_DATA_SIZE == 40U);
    CHECK(TRILINK_CONTENT_ERROR_DETAIL_MAX == 163U);

    static const uint8_t ascii[] = {'h', 'i', '\n'};
    static const uint8_t chinese[] = {0xE4U, 0xBDU, 0xA0U,
                                      0xE5U, 0xA5U, 0xBDU};
    static const uint8_t four_byte[] = {0xF0U, 0x9FU, 0x8CU, 0x90U};
    static const uint8_t embedded_nul[] = {'a', 0U, 'b'};
    static const uint8_t overlong_two[] = {0xC0U, 0x80U};
    static const uint8_t overlong_three[] = {0xE0U, 0x80U, 0x80U};
    static const uint8_t surrogate[] = {0xEDU, 0xA0U, 0x80U};
    static const uint8_t above_unicode[] = {0xF4U, 0x90U, 0x80U, 0x80U};
    static const uint8_t truncated[] = {0xE4U, 0xBDU};
    static const uint8_t stray_continuation[] = {0x80U};

    CHECK(trilink_content_utf8_is_valid(NULL, 0U));
    CHECK(!trilink_content_utf8_is_valid(NULL, 1U));
    CHECK(trilink_content_utf8_is_valid(ascii, sizeof(ascii)));
    CHECK(trilink_content_utf8_is_valid(chinese, sizeof(chinese)));
    CHECK(trilink_content_utf8_is_valid(four_byte, sizeof(four_byte)));
    CHECK(!trilink_content_utf8_is_valid(embedded_nul,
                                          sizeof(embedded_nul)));
    CHECK(!trilink_content_utf8_is_valid(overlong_two, sizeof(overlong_two)));
    CHECK(!trilink_content_utf8_is_valid(overlong_three,
                                          sizeof(overlong_three)));
    CHECK(!trilink_content_utf8_is_valid(surrogate, sizeof(surrogate)));
    CHECK(!trilink_content_utf8_is_valid(above_unicode,
                                          sizeof(above_unicode)));
    CHECK(!trilink_content_utf8_is_valid(truncated, sizeof(truncated)));
    CHECK(!trilink_content_utf8_is_valid(stray_continuation,
                                          sizeof(stray_continuation)));

    uint8_t resource_id[TRILINK_RESOURCE_ID_SIZE] = {0};
    CHECK(!trilink_content_resource_id_is_valid(NULL));
    CHECK(!trilink_content_resource_id_is_valid(resource_id));
    resource_id[31] = 1U;
    CHECK(trilink_content_resource_id_is_valid(resource_id));
}

static void test_text_round_trip_and_limits(void)
{
    static const uint8_t text[] = {'T', 'r', 'i', 'L', 'i', 'n', 'k', ' ',
                                   0xE4U, 0xBDU, 0xA0U, 0xE5U, 0xA5U, 0xBDU};
    uint8_t encoded[TRILINK_PAYLOAD_MAX] = {0};
    size_t encoded_len = 999U;
    CHECK(trilink_content_text_encode(text,
                                      (uint16_t)sizeof(text),
                                      encoded,
                                      sizeof(encoded),
                                      &encoded_len));
    CHECK(encoded_len == TRILINK_SERVICE_HEADER_SIZE + sizeof(text));
    CHECK(encoded[0] == TRILINK_SERVICE_ID_CONTENT);
    CHECK(encoded[1] == TRILINK_CONTENT_OP_TEXT);
    CHECK(encoded[2] == sizeof(text) && encoded[3] == 0U);
    CHECK(memcmp(&encoded[TRILINK_SERVICE_HEADER_SIZE], text, sizeof(text))
          == 0);

    trilink_content_text_view_t decoded;
    CHECK(trilink_content_text_decode_view(encoded, encoded_len, &decoded));
    CHECK(decoded.text_len == sizeof(text));
    CHECK(decoded.text == &encoded[TRILINK_SERVICE_HEADER_SIZE]);
    CHECK(memcmp(decoded.text, text, sizeof(text)) == 0);

    uint8_t maximum[TRILINK_SERVICE_DATA_MAX];
    memset(maximum, 'x', sizeof(maximum));
    CHECK(trilink_content_text_encode(maximum,
                                      (uint16_t)sizeof(maximum),
                                      encoded,
                                      sizeof(encoded),
                                      &encoded_len));
    CHECK(encoded_len == TRILINK_PAYLOAD_MAX);
    CHECK(trilink_content_text_decode_view(encoded, encoded_len, &decoded));
    CHECK(decoded.text_len == TRILINK_SERVICE_DATA_MAX);

    encoded_len = 123U;
    CHECK(!trilink_content_text_encode(NULL,
                                       1U,
                                       encoded,
                                       sizeof(encoded),
                                       &encoded_len));
    CHECK(encoded_len == 0U);
    CHECK(!trilink_content_text_encode(text,
                                       0U,
                                       encoded,
                                       sizeof(encoded),
                                       &encoded_len));
    CHECK(!trilink_content_text_encode(text,
                                       TRILINK_SERVICE_DATA_MAX + 1U,
                                       encoded,
                                       sizeof(encoded),
                                       &encoded_len));
    CHECK(!trilink_content_text_encode(text,
                                       (uint16_t)sizeof(text),
                                       NULL,
                                       sizeof(encoded),
                                       &encoded_len));
    CHECK(!trilink_content_text_encode(text,
                                       (uint16_t)sizeof(text),
                                       encoded,
                                       TRILINK_SERVICE_HEADER_SIZE,
                                       &encoded_len));
    CHECK(!trilink_content_text_encode(text,
                                       (uint16_t)sizeof(text),
                                       encoded,
                                       sizeof(encoded),
                                       NULL));

    static const uint8_t invalid[] = {0xC0U, 0x80U};
    CHECK(!trilink_content_text_encode(invalid,
                                       (uint16_t)sizeof(invalid),
                                       encoded,
                                       sizeof(encoded),
                                       &encoded_len));

    CHECK(trilink_content_text_encode(text,
                                      (uint16_t)sizeof(text),
                                      encoded,
                                      sizeof(encoded),
                                      &encoded_len));
    CHECK(!trilink_content_text_decode_view(NULL, encoded_len, &decoded));
    CHECK(decoded.text == NULL && decoded.text_len == 0U);
    CHECK(!trilink_content_text_decode_view(encoded, encoded_len, NULL));
    encoded[0] = TRILINK_SERVICE_ID_FILE;
    CHECK(!trilink_content_text_decode_view(encoded, encoded_len, &decoded));
    encoded[0] = TRILINK_SERVICE_ID_CONTENT;
    encoded[1] = TRILINK_CONTENT_OP_SUMMARY;
    CHECK(!trilink_content_text_decode_view(encoded, encoded_len, &decoded));
    encoded[1] = TRILINK_CONTENT_OP_TEXT;
    encoded[4] = 0U;
    CHECK(!trilink_content_text_decode_view(encoded, encoded_len, &decoded));
}

static void test_summary_round_trip_and_rejections(void)
{
    uint8_t resource_id[TRILINK_RESOURCE_ID_SIZE];
    make_resource_id(resource_id);
    static const uint8_t name[] = {'m', 'a', 'p', '0', '1', '.', 'j', 's', 'o', 'n'};
    static const uint8_t summary[] = {
        0xE4U, 0xB8U, 0x89U, 0xE4U, 0xBAU, 0xBAU, ' ', 'v', '4'
    };
    const trilink_content_summary_view_t input = {
        .resource_id = resource_id,
        .total_size = 0x78563412U,
        .media_type = TRILINK_CONTENT_MEDIA_JSON_UTF8,
        .flags = TRILINK_CONTENT_SUMMARY_FLAG_FETCHABLE,
        .name_len = (uint8_t)sizeof(name),
        .summary_len = (uint8_t)sizeof(summary),
        .name = name,
        .summary = summary,
    };
    uint8_t encoded[TRILINK_PAYLOAD_MAX] = {0};
    size_t encoded_len = 999U;
    CHECK(trilink_content_summary_encode(&input,
                                         encoded,
                                         sizeof(encoded),
                                         &encoded_len));
    CHECK(encoded_len == TRILINK_SERVICE_HEADER_SIZE
                         + TRILINK_CONTENT_SUMMARY_HEADER_SIZE
                         + sizeof(name) + sizeof(summary));
    CHECK(encoded[0] == TRILINK_SERVICE_ID_CONTENT);
    CHECK(encoded[1] == TRILINK_CONTENT_OP_SUMMARY);
    CHECK(memcmp(&encoded[4], resource_id, sizeof(resource_id)) == 0);
    CHECK(encoded[36] == 0x12U && encoded[39] == 0x78U);
    CHECK(encoded[40] == TRILINK_CONTENT_MEDIA_JSON_UTF8);
    CHECK(encoded[41] == TRILINK_CONTENT_SUMMARY_FLAG_FETCHABLE);
    CHECK(encoded[42] == sizeof(name));
    CHECK(encoded[43] == sizeof(summary));

    trilink_content_summary_view_t output;
    CHECK(trilink_content_summary_decode_view(encoded, encoded_len, &output));
    CHECK(output.resource_id == &encoded[TRILINK_SERVICE_HEADER_SIZE]);
    CHECK(output.total_size == input.total_size);
    CHECK(output.media_type == input.media_type);
    CHECK(output.flags == input.flags);
    CHECK(output.name_len == sizeof(name));
    CHECK(output.summary_len == sizeof(summary));
    CHECK(output.name == &encoded[TRILINK_SERVICE_HEADER_SIZE
                                  + TRILINK_CONTENT_SUMMARY_HEADER_SIZE]);
    CHECK(output.summary == &output.name[sizeof(name)]);
    CHECK(memcmp(output.name, name, sizeof(name)) == 0);
    CHECK(memcmp(output.summary, summary, sizeof(summary)) == 0);

    uint8_t long_text[TRILINK_CONTENT_SUMMARY_TEXT_MAX];
    memset(long_text, 's', sizeof(long_text));
    trilink_content_summary_view_t maximum = input;
    maximum.name = long_text;
    maximum.name_len = 1U;
    maximum.summary = &long_text[1];
    maximum.summary_len = TRILINK_CONTENT_SUMMARY_TEXT_MAX - 1U;
    CHECK(trilink_content_summary_encode(&maximum,
                                         encoded,
                                         sizeof(encoded),
                                         &encoded_len));
    CHECK(encoded_len == TRILINK_PAYLOAD_MAX);
    CHECK(trilink_content_summary_decode_view(encoded, encoded_len, &output));
    CHECK(output.name_len + output.summary_len
          == TRILINK_CONTENT_SUMMARY_TEXT_MAX);

    trilink_content_summary_view_t no_summary = input;
    no_summary.summary = NULL;
    no_summary.summary_len = 0U;
    CHECK(trilink_content_summary_encode(&no_summary,
                                         encoded,
                                         sizeof(encoded),
                                         &encoded_len));
    CHECK(trilink_content_summary_decode_view(encoded, encoded_len, &output));
    CHECK(output.summary == NULL && output.summary_len == 0U);

    trilink_content_summary_view_t invalid = input;
    encoded_len = 123U;
    CHECK(!trilink_content_summary_encode(NULL,
                                          encoded,
                                          sizeof(encoded),
                                          &encoded_len));
    CHECK(encoded_len == 0U);
    invalid.resource_id = NULL;
    CHECK(!trilink_content_summary_encode(&invalid,
                                          encoded,
                                          sizeof(encoded),
                                          &encoded_len));
    uint8_t zero_id[TRILINK_RESOURCE_ID_SIZE] = {0};
    invalid = input;
    invalid.resource_id = zero_id;
    CHECK(!trilink_content_summary_encode(&invalid,
                                          encoded,
                                          sizeof(encoded),
                                          &encoded_len));
    invalid = input;
    invalid.flags = 0x80U;
    CHECK(!trilink_content_summary_encode(&invalid,
                                          encoded,
                                          sizeof(encoded),
                                          &encoded_len));
    invalid = input;
    invalid.name_len = 0U;
    CHECK(!trilink_content_summary_encode(&invalid,
                                          encoded,
                                          sizeof(encoded),
                                          &encoded_len));
    invalid = input;
    invalid.name_len = 129U;
    invalid.summary_len = 0U;
    CHECK(!trilink_content_summary_encode(&invalid,
                                          encoded,
                                          sizeof(encoded),
                                          &encoded_len));
    invalid = input;
    invalid.name = NULL;
    CHECK(!trilink_content_summary_encode(&invalid,
                                          encoded,
                                          sizeof(encoded),
                                          &encoded_len));
    CHECK(!trilink_content_summary_encode(&input,
                                          encoded,
                                          TRILINK_SERVICE_HEADER_SIZE,
                                          &encoded_len));

    CHECK(trilink_content_summary_encode(&input,
                                         encoded,
                                         sizeof(encoded),
                                         &encoded_len));
    CHECK(!trilink_content_summary_decode_view(NULL, encoded_len, &output));
    CHECK(output.resource_id == NULL && output.name == NULL);
    CHECK(!trilink_content_summary_decode_view(encoded, encoded_len, NULL));
    encoded[41] = 0x80U;
    CHECK(!trilink_content_summary_decode_view(encoded, encoded_len, &output));
    encoded[41] = input.flags;
    encoded[42] = 0U;
    CHECK(!trilink_content_summary_decode_view(encoded, encoded_len, &output));
    encoded[42] = input.name_len;
    encoded[43] += 1U;
    CHECK(!trilink_content_summary_decode_view(encoded, encoded_len, &output));
    encoded[43] -= 1U;
    encoded[4] = 0U;
    for (size_t index = 5U; index < 36U; ++index) {
        encoded[index] = 0U;
    }
    CHECK(!trilink_content_summary_decode_view(encoded, encoded_len, &output));
}

static void test_get_range(void)
{
    uint8_t resource_id[TRILINK_RESOURCE_ID_SIZE];
    make_resource_id(resource_id);
    const trilink_content_get_range_view_t input = {
        .resource_id = resource_id,
        .offset = 0x12345670U,
        .length = 0x1020304U,
    };
    uint8_t encoded[TRILINK_PAYLOAD_MAX] = {0};
    size_t encoded_len = 0U;
    CHECK(trilink_content_get_range_encode(&input,
                                           encoded,
                                           sizeof(encoded),
                                           &encoded_len));
    CHECK(encoded_len == TRILINK_SERVICE_HEADER_SIZE
                         + TRILINK_CONTENT_GET_RANGE_DATA_SIZE);
    CHECK(encoded[1] == TRILINK_CONTENT_OP_GET_RANGE);
    CHECK(encoded[36] == 0x70U && encoded[39] == 0x12U);
    CHECK(encoded[40] == 0x04U && encoded[43] == 0x01U);

    trilink_content_get_range_view_t output;
    CHECK(trilink_content_get_range_decode_view(encoded,
                                                 encoded_len,
                                                 &output));
    CHECK(output.resource_id == &encoded[TRILINK_SERVICE_HEADER_SIZE]);
    CHECK(output.offset == input.offset);
    CHECK(output.length == input.length);

    trilink_content_get_range_view_t invalid = input;
    invalid.resource_id = NULL;
    CHECK(!trilink_content_get_range_encode(&invalid,
                                            encoded,
                                            sizeof(encoded),
                                            &encoded_len));
    invalid = input;
    invalid.length = 0U;
    CHECK(!trilink_content_get_range_encode(&invalid,
                                            encoded,
                                            sizeof(encoded),
                                            &encoded_len));
    invalid.offset = UINT32_MAX;
    invalid.length = 1U;
    CHECK(!trilink_content_get_range_encode(&invalid,
                                            encoded,
                                            sizeof(encoded),
                                            &encoded_len));
    CHECK(!trilink_content_get_range_encode(NULL,
                                            encoded,
                                            sizeof(encoded),
                                            &encoded_len));
    CHECK(!trilink_content_get_range_encode(&input,
                                            NULL,
                                            sizeof(encoded),
                                            &encoded_len));

    CHECK(trilink_content_get_range_encode(&input,
                                           encoded,
                                           sizeof(encoded),
                                           &encoded_len));
    CHECK(!trilink_content_get_range_decode_view(NULL,
                                                 encoded_len,
                                                 &output));
    CHECK(!trilink_content_get_range_decode_view(encoded,
                                                 encoded_len,
                                                 NULL));
    encoded[2] -= 1U;
    CHECK(!trilink_content_get_range_decode_view(encoded,
                                                 encoded_len - 1U,
                                                 &output));
    encoded[2] += 1U;
    memset(&encoded[4], 0, TRILINK_RESOURCE_ID_SIZE);
    CHECK(!trilink_content_get_range_decode_view(encoded,
                                                 encoded_len,
                                                 &output));
}

static void test_error_and_frame_composition(void)
{
    static const uint8_t detail[] = {'n', 'o', 't', ' ', 'f', 'o', 'u', 'n', 'd'};
    const trilink_content_error_view_t input = {
        .request_sequence = 0x78563412U,
        .code = TRILINK_CONTENT_ERROR_NOT_FOUND,
        .detail_len = (uint8_t)sizeof(detail),
        .detail = detail,
    };
    uint8_t payload[TRILINK_PAYLOAD_MAX] = {0};
    size_t payload_len = 0U;
    CHECK(trilink_content_error_encode(&input,
                                       payload,
                                       sizeof(payload),
                                       &payload_len));
    CHECK(payload[4] == 0x12U && payload[7] == 0x78U);
    CHECK(payload[8] == TRILINK_CONTENT_ERROR_NOT_FOUND);

    trilink_content_error_view_t output;
    CHECK(trilink_content_error_decode_view(payload, payload_len, &output));
    CHECK(output.request_sequence == input.request_sequence);
    CHECK(output.code == input.code);
    CHECK(output.detail_len == sizeof(detail));
    CHECK(output.detail == &payload[TRILINK_SERVICE_HEADER_SIZE
                                    + TRILINK_CONTENT_ERROR_HEADER_SIZE]);
    CHECK(memcmp(output.detail, detail, sizeof(detail)) == 0);

    trilink_content_error_view_t empty = input;
    empty.detail_len = 0U;
    empty.detail = NULL;
    CHECK(trilink_content_error_encode(&empty,
                                       payload,
                                       sizeof(payload),
                                       &payload_len));
    CHECK(trilink_content_error_decode_view(payload, payload_len, &output));
    CHECK(output.detail == NULL && output.detail_len == 0U);

    uint8_t maximum_detail[TRILINK_CONTENT_ERROR_DETAIL_MAX];
    memset(maximum_detail, 'e', sizeof(maximum_detail));
    trilink_content_error_view_t maximum = input;
    maximum.detail_len = (uint8_t)sizeof(maximum_detail);
    maximum.detail = maximum_detail;
    CHECK(trilink_content_error_encode(&maximum,
                                       payload,
                                       sizeof(payload),
                                       &payload_len));
    CHECK(payload_len == TRILINK_PAYLOAD_MAX);
    CHECK(trilink_content_error_decode_view(payload, payload_len, &output));
    CHECK(output.detail_len == TRILINK_CONTENT_ERROR_DETAIL_MAX);

    trilink_content_error_view_t invalid = input;
    invalid.request_sequence = 0U;
    CHECK(!trilink_content_error_encode(&invalid,
                                        payload,
                                        sizeof(payload),
                                        &payload_len));
    invalid = input;
    invalid.code = 0U;
    CHECK(!trilink_content_error_encode(&invalid,
                                        payload,
                                        sizeof(payload),
                                        &payload_len));
    invalid.code = 7U;
    CHECK(!trilink_content_error_encode(&invalid,
                                        payload,
                                        sizeof(payload),
                                        &payload_len));
    invalid = input;
    invalid.detail = NULL;
    CHECK(!trilink_content_error_encode(&invalid,
                                        payload,
                                        sizeof(payload),
                                        &payload_len));
    invalid = input;
    invalid.detail_len = TRILINK_CONTENT_ERROR_DETAIL_MAX + 1U;
    CHECK(!trilink_content_error_encode(&invalid,
                                        payload,
                                        sizeof(payload),
                                        &payload_len));

    CHECK(trilink_content_error_encode(&input,
                                       payload,
                                       sizeof(payload),
                                       &payload_len));
    payload[8] = 7U;
    CHECK(!trilink_content_error_decode_view(payload, payload_len, &output));
    CHECK(output.detail == NULL && output.request_sequence == 0U);
    payload[8] = input.code;

    const trilink_frame_view_t frame = {
        .type = TRILINK_MSG_APP_DATA,
        .flags = TRILINK_FLAG_ACK_REQUIRED,
        .payload_len = (uint16_t)payload_len,
        .group_id = 0x31474C54UL,
        .boot_id = 3U,
        .term = 1U,
        .sequence = 77U,
        .payload = payload,
    };
    uint8_t wire[TRILINK_FRAME_MAX_SIZE] = {0};
    size_t wire_len = 0U;
    CHECK(trilink_frame_encode_view(&frame, wire, sizeof(wire), &wire_len));
    trilink_frame_view_t decoded_frame;
    CHECK(trilink_frame_decode_view(wire, wire_len, &decoded_frame)
          == TRILINK_DECODE_OK);
    CHECK(trilink_content_error_decode_view(decoded_frame.payload,
                                            decoded_frame.payload_len,
                                            &output));
    CHECK(output.detail
          == &wire[TRILINK_FRAME_HEADER_SIZE
                   + TRILINK_SERVICE_HEADER_SIZE
                   + TRILINK_CONTENT_ERROR_HEADER_SIZE]);
}

int main(void)
{
    test_constants_and_utf8();
    test_text_round_trip_and_limits();
    test_summary_round_trip_and_rejections();
    test_get_range();
    test_error_and_frame_composition();

    printf("trilink_content host tests: checks=%u failures=%u\n",
           s_checks,
           s_failures);
    return (s_failures == 0U) ? 0 : 1;
}
