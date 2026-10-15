#pragma once

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#include "trilink_content.h"

#ifdef __cplusplus
extern "C" {
#endif

/*
 * Stateless wire records for bounded, selective-ACK file transfer.  This
 * module owns no transfer table, task, queue, timer or payload buffer.
 */
#define TRILINK_FILE_OPEN_DATA_SIZE       44U
#define TRILINK_FILE_CHUNK_HEADER_SIZE    10U
#define TRILINK_FILE_CHUNK_DATA_MAX       \
    (TRILINK_SERVICE_DATA_MAX - TRILINK_FILE_CHUNK_HEADER_SIZE)
#define TRILINK_FILE_ACK_DATA_SIZE        12U
#define TRILINK_FILE_CLOSE_DATA_SIZE       9U
#define TRILINK_FILE_CANCEL_DATA_SIZE      5U
#define TRILINK_FILE_WINDOW_MAX           32U
#define TRILINK_FILE_OPEN_FLAG_KNOWN_MASK  0U

typedef enum {
    TRILINK_FILE_OP_OPEN = 1,
    TRILINK_FILE_OP_CHUNK = 2,
    TRILINK_FILE_OP_ACK = 3,
    TRILINK_FILE_OP_CLOSE = 4,
    TRILINK_FILE_OP_CANCEL = 5,
} trilink_file_opcode_t;

typedef enum {
    TRILINK_FILE_CLOSE_OK = 0,
    TRILINK_FILE_CLOSE_HASH_MISMATCH = 1,
    TRILINK_FILE_CLOSE_SIZE_MISMATCH = 2,
    TRILINK_FILE_CLOSE_IO_ERROR = 3,
} trilink_file_close_status_t;

typedef enum {
    TRILINK_FILE_CANCEL_USER = 1,
    TRILINK_FILE_CANCEL_TIMEOUT = 2,
    TRILINK_FILE_CANCEL_PROTOCOL = 3,
    TRILINK_FILE_CANCEL_RESOURCE_CHANGED = 4,
} trilink_file_cancel_reason_t;

typedef struct {
    uint32_t transfer_id;
    const uint8_t *resource_id;
    uint32_t transfer_size;
    uint16_t chunk_size;
    uint8_t window_size;
    uint8_t flags;
} trilink_file_open_view_t;

typedef struct {
    uint32_t transfer_id;
    uint32_t offset;
    uint16_t data_len;
    const uint8_t *data;
} trilink_file_chunk_view_t;

/* All chunk indexes below base_chunk are acknowledged; bitmap bit n covers
 * chunk (base_chunk + n), for n in 0..31. */
typedef struct {
    uint32_t transfer_id;
    uint32_t base_chunk;
    uint32_t received_bitmap;
} trilink_file_ack_t;

typedef struct {
    uint32_t transfer_id;
    uint32_t received_size;
    uint8_t status;
} trilink_file_close_t;

typedef struct {
    uint32_t transfer_id;
    uint8_t reason;
} trilink_file_cancel_t;

bool trilink_file_open_encode(const trilink_file_open_view_t *message,
                              uint8_t *output,
                              size_t output_capacity,
                              size_t *output_len);

/* resource_id points into input. */
bool trilink_file_open_decode_view(const uint8_t *input,
                                   size_t input_len,
                                   trilink_file_open_view_t *message);

bool trilink_file_chunk_encode(const trilink_file_chunk_view_t *message,
                               uint8_t *output,
                               size_t output_capacity,
                               size_t *output_len);

/* data points into input. */
bool trilink_file_chunk_decode_view(const uint8_t *input,
                                    size_t input_len,
                                    trilink_file_chunk_view_t *message);

bool trilink_file_ack_encode(const trilink_file_ack_t *message,
                             uint8_t *output,
                             size_t output_capacity,
                             size_t *output_len);

bool trilink_file_ack_decode(const uint8_t *input,
                             size_t input_len,
                             trilink_file_ack_t *message);

bool trilink_file_close_encode(const trilink_file_close_t *message,
                               uint8_t *output,
                               size_t output_capacity,
                               size_t *output_len);

bool trilink_file_close_decode(const uint8_t *input,
                               size_t input_len,
                               trilink_file_close_t *message);

bool trilink_file_cancel_encode(const trilink_file_cancel_t *message,
                                uint8_t *output,
                                size_t output_capacity,
                                size_t *output_len);

bool trilink_file_cancel_decode(const uint8_t *input,
                                size_t input_len,
                                trilink_file_cancel_t *message);

#ifdef __cplusplus
}
#endif
