#include "trilink_service.h"

#include <string.h>

static void write_u16_le(uint8_t *destination, uint16_t value)
{
    destination[0] = (uint8_t)(value & 0xFFU);
    destination[1] = (uint8_t)((value >> 8U) & 0xFFU);
}

static uint16_t read_u16_le(const uint8_t *source)
{
    return (uint16_t)source[0] | ((uint16_t)source[1] << 8U);
}

bool trilink_service_id_is_valid(uint8_t service_id)
{
    return service_id != 0U;
}

bool trilink_service_encode(const trilink_service_view_t *message,
                            uint8_t *output,
                            size_t output_capacity,
                            size_t *output_len)
{
    if (output_len != NULL) {
        *output_len = 0U;
    }
    if ((message == NULL) || (output == NULL) || (output_len == NULL)
        || !trilink_service_id_is_valid(message->service_id)
        || (message->data_len > TRILINK_SERVICE_DATA_MAX)
        || ((message->data == NULL) && (message->data_len != 0U))) {
        return false;
    }

    const size_t encoded_len = TRILINK_SERVICE_HEADER_SIZE
                               + (size_t)message->data_len;
    if (output_capacity < encoded_len) {
        return false;
    }

    output[0] = message->service_id;
    output[1] = message->opcode;
    write_u16_le(&output[2], message->data_len);
    if (message->data_len != 0U) {
        memcpy(&output[TRILINK_SERVICE_HEADER_SIZE],
               message->data,
               message->data_len);
    }
    *output_len = encoded_len;
    return true;
}

bool trilink_service_decode_view(const uint8_t *input,
                                 size_t input_len,
                                 trilink_service_view_t *message)
{
    if (message != NULL) {
        memset(message, 0, sizeof(*message));
    }
    if ((input == NULL) || (message == NULL)
        || (input_len < TRILINK_SERVICE_HEADER_SIZE)
        || !trilink_service_id_is_valid(input[0])) {
        return false;
    }

    const uint16_t data_len = read_u16_le(&input[2]);
    if ((data_len > TRILINK_SERVICE_DATA_MAX)
        || (input_len != (TRILINK_SERVICE_HEADER_SIZE + (size_t)data_len))) {
        return false;
    }

    message->service_id = input[0];
    message->opcode = input[1];
    message->data_len = data_len;
    message->data = (data_len == 0U)
                        ? NULL
                        : &input[TRILINK_SERVICE_HEADER_SIZE];
    return true;
}
