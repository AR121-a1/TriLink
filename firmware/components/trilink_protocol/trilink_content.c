#include "trilink_content.h"

#include <limits.h>
#include <string.h>

_Static_assert(TRILINK_CONTENT_SUMMARY_TEXT_MAX == 128U,
               "summary text budget drifted");
_Static_assert(TRILINK_CONTENT_ERROR_DETAIL_MAX == 163U,
               "error detail budget drifted");

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

static uint32_t read_u32_le(const uint8_t *source)
{
    return (uint32_t)source[0]
           | ((uint32_t)source[1] << 8U)
           | ((uint32_t)source[2] << 16U)
           | ((uint32_t)source[3] << 24U);
}

static bool write_service_header(uint8_t opcode,
                                 uint16_t data_len,
                                 uint8_t *output,
                                 size_t output_capacity,
                                 size_t *output_len)
{
    if (output_len != NULL) {
        *output_len = 0U;
    }
    if ((output == NULL) || (output_len == NULL)
        || (data_len > TRILINK_SERVICE_DATA_MAX)) {
        return false;
    }

    const size_t encoded_len = TRILINK_SERVICE_HEADER_SIZE + (size_t)data_len;
    if (output_capacity < encoded_len) {
        return false;
    }

    output[0] = TRILINK_SERVICE_ID_CONTENT;
    output[1] = opcode;
    write_u16_le(&output[2], data_len);
    *output_len = encoded_len;
    return true;
}

static bool decode_service(const uint8_t *input,
                           size_t input_len,
                           uint8_t opcode,
                           trilink_service_view_t *service)
{
    return trilink_service_decode_view(input, input_len, service)
           && (service->service_id == TRILINK_SERVICE_ID_CONTENT)
           && (service->opcode == opcode);
}

static bool continuation(uint8_t value)
{
    return (value & 0xC0U) == 0x80U;
}

bool trilink_content_utf8_is_valid(const uint8_t *data, size_t len)
{
    if ((data == NULL) && (len != 0U)) {
        return false;
    }

    size_t index = 0U;
    while (index < len) {
        const uint8_t first = data[index];
        if (first == 0U) {
            return false;
        }
        if (first <= 0x7FU) {
            ++index;
            continue;
        }
        if ((first >= 0xC2U) && (first <= 0xDFU)) {
            if (((len - index) < 2U) || !continuation(data[index + 1U])) {
                return false;
            }
            index += 2U;
            continue;
        }
        if (first == 0xE0U) {
            if (((len - index) < 3U)
                || (data[index + 1U] < 0xA0U)
                || (data[index + 1U] > 0xBFU)
                || !continuation(data[index + 2U])) {
                return false;
            }
            index += 3U;
            continue;
        }
        if (((first >= 0xE1U) && (first <= 0xECU))
            || ((first >= 0xEEU) && (first <= 0xEFU))) {
            if (((len - index) < 3U)
                || !continuation(data[index + 1U])
                || !continuation(data[index + 2U])) {
                return false;
            }
            index += 3U;
            continue;
        }
        if (first == 0xEDU) {
            if (((len - index) < 3U)
                || (data[index + 1U] < 0x80U)
                || (data[index + 1U] > 0x9FU)
                || !continuation(data[index + 2U])) {
                return false;
            }
            index += 3U;
            continue;
        }
        if (first == 0xF0U) {
            if (((len - index) < 4U)
                || (data[index + 1U] < 0x90U)
                || (data[index + 1U] > 0xBFU)
                || !continuation(data[index + 2U])
                || !continuation(data[index + 3U])) {
                return false;
            }
            index += 4U;
            continue;
        }
        if ((first >= 0xF1U) && (first <= 0xF3U)) {
            if (((len - index) < 4U)
                || !continuation(data[index + 1U])
                || !continuation(data[index + 2U])
                || !continuation(data[index + 3U])) {
                return false;
            }
            index += 4U;
            continue;
        }
        if (first == 0xF4U) {
            if (((len - index) < 4U)
                || (data[index + 1U] < 0x80U)
                || (data[index + 1U] > 0x8FU)
                || !continuation(data[index + 2U])
                || !continuation(data[index + 3U])) {
                return false;
            }
            index += 4U;
            continue;
        }
        return false;
    }
    return true;
}

bool trilink_content_resource_id_is_valid(
    const uint8_t resource_id[TRILINK_RESOURCE_ID_SIZE])
{
    if (resource_id == NULL) {
        return false;
    }
    uint8_t aggregate = 0U;
    for (size_t index = 0U; index < TRILINK_RESOURCE_ID_SIZE; ++index) {
        aggregate |= resource_id[index];
    }
    return aggregate != 0U;
}

bool trilink_content_text_encode(const uint8_t *text,
                                 uint16_t text_len,
                                 uint8_t *output,
                                 size_t output_capacity,
                                 size_t *output_len)
{
    if (output_len != NULL) {
        *output_len = 0U;
    }
    if ((text_len == 0U) || (text_len > TRILINK_SERVICE_DATA_MAX)
        || !trilink_content_utf8_is_valid(text, text_len)
        || !write_service_header(TRILINK_CONTENT_OP_TEXT,
                                 text_len,
                                 output,
                                 output_capacity,
                                 output_len)) {
        return false;
    }
    memcpy(&output[TRILINK_SERVICE_HEADER_SIZE], text, text_len);
    return true;
}

bool trilink_content_text_decode_view(const uint8_t *input,
                                      size_t input_len,
                                      trilink_content_text_view_t *message)
{
    if (message != NULL) {
        memset(message, 0, sizeof(*message));
    }
    if (message == NULL) {
        return false;
    }

    trilink_service_view_t service;
    if (!decode_service(input, input_len, TRILINK_CONTENT_OP_TEXT, &service)
        || (service.data_len == 0U)
        || !trilink_content_utf8_is_valid(service.data, service.data_len)) {
        return false;
    }
    message->text_len = service.data_len;
    message->text = service.data;
    return true;
}

bool trilink_content_summary_encode(
    const trilink_content_summary_view_t *message,
    uint8_t *output,
    size_t output_capacity,
    size_t *output_len)
{
    if (output_len != NULL) {
        *output_len = 0U;
    }
    if ((message == NULL)
        || !trilink_content_resource_id_is_valid(message->resource_id)
        || ((message->flags & ~TRILINK_CONTENT_SUMMARY_FLAG_KNOWN_MASK) != 0U)
        || (message->name_len == 0U)
        || (((size_t)message->name_len + (size_t)message->summary_len)
            > TRILINK_CONTENT_SUMMARY_TEXT_MAX)
        || !trilink_content_utf8_is_valid(message->name, message->name_len)
        || !trilink_content_utf8_is_valid(message->summary,
                                           message->summary_len)) {
        return false;
    }

    const uint16_t data_len = (uint16_t)(TRILINK_CONTENT_SUMMARY_HEADER_SIZE
                              + (size_t)message->name_len
                              + (size_t)message->summary_len);
    if (!write_service_header(TRILINK_CONTENT_OP_SUMMARY,
                              data_len,
                              output,
                              output_capacity,
                              output_len)) {
        return false;
    }

    uint8_t *data = &output[TRILINK_SERVICE_HEADER_SIZE];
    memcpy(data, message->resource_id, TRILINK_RESOURCE_ID_SIZE);
    write_u32_le(&data[32], message->total_size);
    data[36] = message->media_type;
    data[37] = message->flags;
    data[38] = message->name_len;
    data[39] = message->summary_len;
    memcpy(&data[TRILINK_CONTENT_SUMMARY_HEADER_SIZE],
           message->name,
           message->name_len);
    if (message->summary_len != 0U) {
        memcpy(&data[TRILINK_CONTENT_SUMMARY_HEADER_SIZE + message->name_len],
               message->summary,
               message->summary_len);
    }
    return true;
}

bool trilink_content_summary_decode_view(
    const uint8_t *input,
    size_t input_len,
    trilink_content_summary_view_t *message)
{
    if (message != NULL) {
        memset(message, 0, sizeof(*message));
    }
    if (message == NULL) {
        return false;
    }

    trilink_service_view_t service;
    if (!decode_service(input, input_len, TRILINK_CONTENT_OP_SUMMARY, &service)
        || (service.data_len < TRILINK_CONTENT_SUMMARY_HEADER_SIZE)) {
        return false;
    }

    const uint8_t *data = service.data;
    const uint8_t name_len = data[38];
    const uint8_t summary_len = data[39];
    if (!trilink_content_resource_id_is_valid(data)
        || ((data[37] & ~TRILINK_CONTENT_SUMMARY_FLAG_KNOWN_MASK) != 0U)
        || (name_len == 0U)
        || ((size_t)name_len + (size_t)summary_len
            > TRILINK_CONTENT_SUMMARY_TEXT_MAX)
        || (service.data_len != (TRILINK_CONTENT_SUMMARY_HEADER_SIZE
                                 + (size_t)name_len
                                 + (size_t)summary_len))) {
        return false;
    }

    const uint8_t *name = &data[TRILINK_CONTENT_SUMMARY_HEADER_SIZE];
    const uint8_t *summary = &name[name_len];
    if (!trilink_content_utf8_is_valid(name, name_len)
        || !trilink_content_utf8_is_valid(summary, summary_len)) {
        return false;
    }

    message->resource_id = data;
    message->total_size = read_u32_le(&data[32]);
    message->media_type = data[36];
    message->flags = data[37];
    message->name_len = name_len;
    message->summary_len = summary_len;
    message->name = name;
    message->summary = (summary_len == 0U) ? NULL : summary;
    return true;
}

bool trilink_content_get_range_encode(
    const trilink_content_get_range_view_t *message,
    uint8_t *output,
    size_t output_capacity,
    size_t *output_len)
{
    if (output_len != NULL) {
        *output_len = 0U;
    }
    if ((message == NULL)
        || !trilink_content_resource_id_is_valid(message->resource_id)
        || (message->length == 0U)
        || (message->offset > (UINT32_MAX - message->length))
        || !write_service_header(TRILINK_CONTENT_OP_GET_RANGE,
                                 TRILINK_CONTENT_GET_RANGE_DATA_SIZE,
                                 output,
                                 output_capacity,
                                 output_len)) {
        return false;
    }

    uint8_t *data = &output[TRILINK_SERVICE_HEADER_SIZE];
    memcpy(data, message->resource_id, TRILINK_RESOURCE_ID_SIZE);
    write_u32_le(&data[32], message->offset);
    write_u32_le(&data[36], message->length);
    return true;
}

bool trilink_content_get_range_decode_view(
    const uint8_t *input,
    size_t input_len,
    trilink_content_get_range_view_t *message)
{
    if (message != NULL) {
        memset(message, 0, sizeof(*message));
    }
    if (message == NULL) {
        return false;
    }

    trilink_service_view_t service;
    if (!decode_service(input,
                        input_len,
                        TRILINK_CONTENT_OP_GET_RANGE,
                        &service)
        || (service.data_len != TRILINK_CONTENT_GET_RANGE_DATA_SIZE)
        || !trilink_content_resource_id_is_valid(service.data)) {
        return false;
    }
    const uint32_t offset = read_u32_le(&service.data[32]);
    const uint32_t length = read_u32_le(&service.data[36]);
    if ((length == 0U) || (offset > (UINT32_MAX - length))) {
        return false;
    }
    message->resource_id = service.data;
    message->offset = offset;
    message->length = length;
    return true;
}

static bool content_error_code_is_valid(uint8_t code)
{
    return (code >= TRILINK_CONTENT_ERROR_NOT_FOUND)
           && (code <= TRILINK_CONTENT_ERROR_IO);
}

bool trilink_content_error_encode(
    const trilink_content_error_view_t *message,
    uint8_t *output,
    size_t output_capacity,
    size_t *output_len)
{
    if (output_len != NULL) {
        *output_len = 0U;
    }
    if ((message == NULL) || (message->request_sequence == 0U)
        || !content_error_code_is_valid(message->code)
        || (message->detail_len > TRILINK_CONTENT_ERROR_DETAIL_MAX)
        || !trilink_content_utf8_is_valid(message->detail,
                                           message->detail_len)) {
        return false;
    }
    const uint16_t data_len = (uint16_t)(TRILINK_CONTENT_ERROR_HEADER_SIZE
                                         + message->detail_len);
    if (!write_service_header(TRILINK_CONTENT_OP_ERROR,
                              data_len,
                              output,
                              output_capacity,
                              output_len)) {
        return false;
    }

    uint8_t *data = &output[TRILINK_SERVICE_HEADER_SIZE];
    write_u32_le(data, message->request_sequence);
    data[4] = message->code;
    if (message->detail_len != 0U) {
        memcpy(&data[TRILINK_CONTENT_ERROR_HEADER_SIZE],
               message->detail,
               message->detail_len);
    }
    return true;
}

bool trilink_content_error_decode_view(
    const uint8_t *input,
    size_t input_len,
    trilink_content_error_view_t *message)
{
    if (message != NULL) {
        memset(message, 0, sizeof(*message));
    }
    if (message == NULL) {
        return false;
    }

    trilink_service_view_t service;
    if (!decode_service(input, input_len, TRILINK_CONTENT_OP_ERROR, &service)
        || (service.data_len < TRILINK_CONTENT_ERROR_HEADER_SIZE)) {
        return false;
    }
    const uint32_t request_sequence = read_u32_le(service.data);
    const uint8_t code = service.data[4];
    const size_t detail_len = (size_t)service.data_len
                              - TRILINK_CONTENT_ERROR_HEADER_SIZE;
    const uint8_t *detail = &service.data[TRILINK_CONTENT_ERROR_HEADER_SIZE];
    if ((request_sequence == 0U) || !content_error_code_is_valid(code)
        || (detail_len > TRILINK_CONTENT_ERROR_DETAIL_MAX)
        || !trilink_content_utf8_is_valid(detail, detail_len)) {
        return false;
    }

    message->request_sequence = request_sequence;
    message->code = code;
    message->detail_len = (uint8_t)detail_len;
    message->detail = (detail_len == 0U) ? NULL : detail;
    return true;
}
