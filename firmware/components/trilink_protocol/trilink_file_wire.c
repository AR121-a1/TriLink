#include "trilink_file_wire.h"

#include <limits.h>
#include <string.h>

_Static_assert(TRILINK_FILE_CHUNK_DATA_MAX == 158U,
               "file chunk budget drifted");

static void write_u16_le(uint8_t *destination, uint16_t value)
{
    destination[0] = (uint8_t)(value & 0xFFU);
    destination[1] = (uint8_t)((value >> 8U) & 0xFFU);
}

static uint16_t read_u16_le(const uint8_t *source)
{
    return (uint16_t)source[0] | ((uint16_t)source[1] << 8U);
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
    output[0] = TRILINK_SERVICE_ID_FILE;
    output[1] = opcode;
    write_u16_le(&output[2], data_len);
    *output_len = encoded_len;
    return true;
}

static bool decode_service(const uint8_t *input,
                           size_t input_len,
                           uint8_t opcode,
                           uint16_t expected_data_len,
                           trilink_service_view_t *service)
{
    return trilink_service_decode_view(input, input_len, service)
           && (service->service_id == TRILINK_SERVICE_ID_FILE)
           && (service->opcode == opcode)
           && (service->data_len == expected_data_len);
}

static bool transfer_id_is_valid(uint32_t transfer_id)
{
    return transfer_id != 0U;
}

bool trilink_file_open_encode(const trilink_file_open_view_t *message,
                              uint8_t *output,
                              size_t output_capacity,
                              size_t *output_len)
{
    if (output_len != NULL) {
        *output_len = 0U;
    }
    if ((message == NULL) || !transfer_id_is_valid(message->transfer_id)
        || !trilink_content_resource_id_is_valid(message->resource_id)
        || (message->chunk_size == 0U)
        || (message->chunk_size > TRILINK_FILE_CHUNK_DATA_MAX)
        || (message->window_size == 0U)
        || (message->window_size > TRILINK_FILE_WINDOW_MAX)
        || ((message->flags & ~TRILINK_FILE_OPEN_FLAG_KNOWN_MASK) != 0U)
        || !write_service_header(TRILINK_FILE_OP_OPEN,
                                 TRILINK_FILE_OPEN_DATA_SIZE,
                                 output,
                                 output_capacity,
                                 output_len)) {
        return false;
    }

    uint8_t *data = &output[TRILINK_SERVICE_HEADER_SIZE];
    write_u32_le(data, message->transfer_id);
    memcpy(&data[4], message->resource_id, TRILINK_RESOURCE_ID_SIZE);
    write_u32_le(&data[36], message->transfer_size);
    write_u16_le(&data[40], message->chunk_size);
    data[42] = message->window_size;
    data[43] = message->flags;
    return true;
}

bool trilink_file_open_decode_view(const uint8_t *input,
                                   size_t input_len,
                                   trilink_file_open_view_t *message)
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
                        TRILINK_FILE_OP_OPEN,
                        TRILINK_FILE_OPEN_DATA_SIZE,
                        &service)) {
        return false;
    }
    const uint32_t transfer_id = read_u32_le(service.data);
    const uint16_t chunk_size = read_u16_le(&service.data[40]);
    const uint8_t window_size = service.data[42];
    const uint8_t flags = service.data[43];
    if (!transfer_id_is_valid(transfer_id)
        || !trilink_content_resource_id_is_valid(&service.data[4])
        || (chunk_size == 0U) || (chunk_size > TRILINK_FILE_CHUNK_DATA_MAX)
        || (window_size == 0U) || (window_size > TRILINK_FILE_WINDOW_MAX)
        || ((flags & ~TRILINK_FILE_OPEN_FLAG_KNOWN_MASK) != 0U)) {
        return false;
    }

    message->transfer_id = transfer_id;
    message->resource_id = &service.data[4];
    message->transfer_size = read_u32_le(&service.data[36]);
    message->chunk_size = chunk_size;
    message->window_size = window_size;
    message->flags = flags;
    return true;
}

bool trilink_file_chunk_encode(const trilink_file_chunk_view_t *message,
                               uint8_t *output,
                               size_t output_capacity,
                               size_t *output_len)
{
    if (output_len != NULL) {
        *output_len = 0U;
    }
    if ((message == NULL) || !transfer_id_is_valid(message->transfer_id)
        || (message->data_len == 0U)
        || (message->data_len > TRILINK_FILE_CHUNK_DATA_MAX)
        || (message->data == NULL)
        || (message->offset > (UINT32_MAX - message->data_len))) {
        return false;
    }
    const uint16_t service_data_len = (uint16_t)(
        TRILINK_FILE_CHUNK_HEADER_SIZE + message->data_len);
    if (!write_service_header(TRILINK_FILE_OP_CHUNK,
                              service_data_len,
                              output,
                              output_capacity,
                              output_len)) {
        return false;
    }

    uint8_t *data = &output[TRILINK_SERVICE_HEADER_SIZE];
    write_u32_le(data, message->transfer_id);
    write_u32_le(&data[4], message->offset);
    write_u16_le(&data[8], message->data_len);
    memcpy(&data[TRILINK_FILE_CHUNK_HEADER_SIZE],
           message->data,
           message->data_len);
    return true;
}

bool trilink_file_chunk_decode_view(const uint8_t *input,
                                    size_t input_len,
                                    trilink_file_chunk_view_t *message)
{
    if (message != NULL) {
        memset(message, 0, sizeof(*message));
    }
    if (message == NULL) {
        return false;
    }

    trilink_service_view_t service;
    if (!trilink_service_decode_view(input, input_len, &service)
        || (service.service_id != TRILINK_SERVICE_ID_FILE)
        || (service.opcode != TRILINK_FILE_OP_CHUNK)
        || (service.data_len <= TRILINK_FILE_CHUNK_HEADER_SIZE)) {
        return false;
    }
    const uint32_t transfer_id = read_u32_le(service.data);
    const uint32_t offset = read_u32_le(&service.data[4]);
    const uint16_t data_len = read_u16_le(&service.data[8]);
    if (!transfer_id_is_valid(transfer_id)
        || (data_len == 0U) || (data_len > TRILINK_FILE_CHUNK_DATA_MAX)
        || (service.data_len != (TRILINK_FILE_CHUNK_HEADER_SIZE
                                 + (size_t)data_len))
        || (offset > (UINT32_MAX - data_len))) {
        return false;
    }

    message->transfer_id = transfer_id;
    message->offset = offset;
    message->data_len = data_len;
    message->data = &service.data[TRILINK_FILE_CHUNK_HEADER_SIZE];
    return true;
}

bool trilink_file_ack_encode(const trilink_file_ack_t *message,
                             uint8_t *output,
                             size_t output_capacity,
                             size_t *output_len)
{
    if (output_len != NULL) {
        *output_len = 0U;
    }
    if ((message == NULL) || !transfer_id_is_valid(message->transfer_id)
        || !write_service_header(TRILINK_FILE_OP_ACK,
                                 TRILINK_FILE_ACK_DATA_SIZE,
                                 output,
                                 output_capacity,
                                 output_len)) {
        return false;
    }

    uint8_t *data = &output[TRILINK_SERVICE_HEADER_SIZE];
    write_u32_le(data, message->transfer_id);
    write_u32_le(&data[4], message->base_chunk);
    write_u32_le(&data[8], message->received_bitmap);
    return true;
}

bool trilink_file_ack_decode(const uint8_t *input,
                             size_t input_len,
                             trilink_file_ack_t *message)
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
                        TRILINK_FILE_OP_ACK,
                        TRILINK_FILE_ACK_DATA_SIZE,
                        &service)) {
        return false;
    }
    const uint32_t transfer_id = read_u32_le(service.data);
    if (!transfer_id_is_valid(transfer_id)) {
        return false;
    }
    message->transfer_id = transfer_id;
    message->base_chunk = read_u32_le(&service.data[4]);
    message->received_bitmap = read_u32_le(&service.data[8]);
    return true;
}

static bool close_status_is_valid(uint8_t status)
{
    return status <= TRILINK_FILE_CLOSE_IO_ERROR;
}

bool trilink_file_close_encode(const trilink_file_close_t *message,
                               uint8_t *output,
                               size_t output_capacity,
                               size_t *output_len)
{
    if (output_len != NULL) {
        *output_len = 0U;
    }
    if ((message == NULL) || !transfer_id_is_valid(message->transfer_id)
        || !close_status_is_valid(message->status)
        || !write_service_header(TRILINK_FILE_OP_CLOSE,
                                 TRILINK_FILE_CLOSE_DATA_SIZE,
                                 output,
                                 output_capacity,
                                 output_len)) {
        return false;
    }
    uint8_t *data = &output[TRILINK_SERVICE_HEADER_SIZE];
    write_u32_le(data, message->transfer_id);
    write_u32_le(&data[4], message->received_size);
    data[8] = message->status;
    return true;
}

bool trilink_file_close_decode(const uint8_t *input,
                               size_t input_len,
                               trilink_file_close_t *message)
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
                        TRILINK_FILE_OP_CLOSE,
                        TRILINK_FILE_CLOSE_DATA_SIZE,
                        &service)) {
        return false;
    }
    const uint32_t transfer_id = read_u32_le(service.data);
    const uint8_t status = service.data[8];
    if (!transfer_id_is_valid(transfer_id) || !close_status_is_valid(status)) {
        return false;
    }
    message->transfer_id = transfer_id;
    message->received_size = read_u32_le(&service.data[4]);
    message->status = status;
    return true;
}

static bool cancel_reason_is_valid(uint8_t reason)
{
    return (reason >= TRILINK_FILE_CANCEL_USER)
           && (reason <= TRILINK_FILE_CANCEL_RESOURCE_CHANGED);
}

bool trilink_file_cancel_encode(const trilink_file_cancel_t *message,
                                uint8_t *output,
                                size_t output_capacity,
                                size_t *output_len)
{
    if (output_len != NULL) {
        *output_len = 0U;
    }
    if ((message == NULL) || !transfer_id_is_valid(message->transfer_id)
        || !cancel_reason_is_valid(message->reason)
        || !write_service_header(TRILINK_FILE_OP_CANCEL,
                                 TRILINK_FILE_CANCEL_DATA_SIZE,
                                 output,
                                 output_capacity,
                                 output_len)) {
        return false;
    }
    uint8_t *data = &output[TRILINK_SERVICE_HEADER_SIZE];
    write_u32_le(data, message->transfer_id);
    data[4] = message->reason;
    return true;
}

bool trilink_file_cancel_decode(const uint8_t *input,
                                size_t input_len,
                                trilink_file_cancel_t *message)
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
                        TRILINK_FILE_OP_CANCEL,
                        TRILINK_FILE_CANCEL_DATA_SIZE,
                        &service)) {
        return false;
    }
    const uint32_t transfer_id = read_u32_le(service.data);
    const uint8_t reason = service.data[4];
    if (!transfer_id_is_valid(transfer_id)
        || !cancel_reason_is_valid(reason)) {
        return false;
    }
    message->transfer_id = transfer_id;
    message->reason = reason;
    return true;
}
