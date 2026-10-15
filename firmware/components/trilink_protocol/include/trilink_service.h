#pragma once

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#include "trilink_protocol.h"

#ifdef __cplusplus
extern "C" {
#endif

/*
 * TRILINK_MSG_APP_DATA payload envelope.  It multiplexes lightweight game,
 * file and later application services without changing the base wire frame.
 * Service 0 is reserved; 1..31 are project services and 32..255 are available
 * to later application modules.
 */
#define TRILINK_SERVICE_HEADER_SIZE   4U
#define TRILINK_SERVICE_DATA_MAX      (TRILINK_PAYLOAD_MAX \
                                       - TRILINK_SERVICE_HEADER_SIZE)

#define TRILINK_SERVICE_ID_GAME        1U
#define TRILINK_SERVICE_ID_FILE        2U
#define TRILINK_SERVICE_ID_DIAGNOSTIC  3U
#define TRILINK_SERVICE_ID_CONTENT     4U
#define TRILINK_SERVICE_ID_ROUTE       9U
#define TRILINK_SERVICE_ID_USER_MIN    32U

typedef struct {
    uint8_t service_id;
    uint8_t opcode;
    uint16_t data_len;
    const uint8_t *data;
} trilink_service_view_t;

bool trilink_service_id_is_valid(uint8_t service_id);

bool trilink_service_encode(const trilink_service_view_t *message,
                            uint8_t *output,
                            size_t output_capacity,
                            size_t *output_len);

/* data points into input and has the same lifetime as input. */
bool trilink_service_decode_view(const uint8_t *input,
                                 size_t input_len,
                                 trilink_service_view_t *message);

#ifdef __cplusplus
}
#endif
