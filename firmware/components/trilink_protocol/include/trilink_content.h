#pragma once

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#include "trilink_service.h"

#ifdef __cplusplus
extern "C" {
#endif

/*
 * Compact, text-first resource discovery carried by TRILINK_MSG_APP_DATA.
 * The 32-byte resource identifier is the SHA-256 digest of the exact original
 * bytes stored on the publishing PC.  Strings are length-delimited UTF-8 and
 * are never NUL-terminated on the wire.
 */
#define TRILINK_RESOURCE_ID_SIZE                32U
#define TRILINK_CONTENT_SUMMARY_HEADER_SIZE     40U
#define TRILINK_CONTENT_SUMMARY_TEXT_MAX        \
    (TRILINK_SERVICE_DATA_MAX - TRILINK_CONTENT_SUMMARY_HEADER_SIZE)
#define TRILINK_CONTENT_GET_RANGE_DATA_SIZE     40U
#define TRILINK_CONTENT_ERROR_HEADER_SIZE        5U
#define TRILINK_CONTENT_ERROR_DETAIL_MAX         \
    (TRILINK_SERVICE_DATA_MAX - TRILINK_CONTENT_ERROR_HEADER_SIZE)

#define TRILINK_CONTENT_SUMMARY_FLAG_FETCHABLE  0x01U
#define TRILINK_CONTENT_SUMMARY_FLAG_KNOWN_MASK \
    TRILINK_CONTENT_SUMMARY_FLAG_FETCHABLE

typedef enum {
    TRILINK_CONTENT_OP_TEXT = 1,
    TRILINK_CONTENT_OP_SUMMARY = 2,
    TRILINK_CONTENT_OP_GET_RANGE = 3,
    TRILINK_CONTENT_OP_ERROR = 4,
} trilink_content_opcode_t;

/* Advisory media classes; the SHA-256 identifier remains authoritative. */
typedef enum {
    TRILINK_CONTENT_MEDIA_BINARY = 0,
    TRILINK_CONTENT_MEDIA_TEXT_UTF8 = 1,
    TRILINK_CONTENT_MEDIA_JSON_UTF8 = 2,
    TRILINK_CONTENT_MEDIA_IMAGE = 3,
    TRILINK_CONTENT_MEDIA_GAME_DATA = 4,
} trilink_content_media_type_t;

typedef enum {
    TRILINK_CONTENT_ERROR_NOT_FOUND = 1,
    TRILINK_CONTENT_ERROR_BAD_RANGE = 2,
    TRILINK_CONTENT_ERROR_DENIED = 3,
    TRILINK_CONTENT_ERROR_BUSY = 4,
    TRILINK_CONTENT_ERROR_BAD_REQUEST = 5,
    TRILINK_CONTENT_ERROR_IO = 6,
} trilink_content_error_code_t;

typedef struct {
    uint16_t text_len;
    const uint8_t *text;
} trilink_content_text_view_t;

typedef struct {
    const uint8_t *resource_id;
    uint32_t total_size;
    uint8_t media_type;
    uint8_t flags;
    uint8_t name_len;
    uint8_t summary_len;
    const uint8_t *name;
    const uint8_t *summary;
} trilink_content_summary_view_t;

typedef struct {
    const uint8_t *resource_id;
    uint32_t offset;
    uint32_t length;
} trilink_content_get_range_view_t;

typedef struct {
    uint32_t request_sequence;
    uint8_t code;
    uint8_t detail_len;
    const uint8_t *detail;
} trilink_content_error_view_t;

/* Valid UTF-8 without embedded U+0000; an empty view is valid. */
bool trilink_content_utf8_is_valid(const uint8_t *data, size_t len);

bool trilink_content_resource_id_is_valid(
    const uint8_t resource_id[TRILINK_RESOURCE_ID_SIZE]);

bool trilink_content_text_encode(const uint8_t *text,
                                 uint16_t text_len,
                                 uint8_t *output,
                                 size_t output_capacity,
                                 size_t *output_len);

/* text points into input and has the same lifetime as input. */
bool trilink_content_text_decode_view(const uint8_t *input,
                                      size_t input_len,
                                      trilink_content_text_view_t *message);

bool trilink_content_summary_encode(
    const trilink_content_summary_view_t *message,
    uint8_t *output,
    size_t output_capacity,
    size_t *output_len);

/* resource_id/name/summary point into input. */
bool trilink_content_summary_decode_view(
    const uint8_t *input,
    size_t input_len,
    trilink_content_summary_view_t *message);

bool trilink_content_get_range_encode(
    const trilink_content_get_range_view_t *message,
    uint8_t *output,
    size_t output_capacity,
    size_t *output_len);

/* resource_id points into input. */
bool trilink_content_get_range_decode_view(
    const uint8_t *input,
    size_t input_len,
    trilink_content_get_range_view_t *message);

bool trilink_content_error_encode(
    const trilink_content_error_view_t *message,
    uint8_t *output,
    size_t output_capacity,
    size_t *output_len);

/* detail points into input. */
bool trilink_content_error_decode_view(
    const uint8_t *input,
    size_t input_len,
    trilink_content_error_view_t *message);

#ifdef __cplusplus
}
#endif
