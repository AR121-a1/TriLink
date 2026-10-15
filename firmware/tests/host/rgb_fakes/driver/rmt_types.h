#pragma once

#include <stddef.h>
#include <stdint.h>

/* Minimal ESP-IDF 5.5.5 boundary types; real LED strip headers are retained. */
typedef struct fake_rmt_channel *rmt_channel_handle_t;
typedef struct fake_rmt_encoder *rmt_encoder_handle_t;
typedef enum { RMT_CLK_SRC_DEFAULT = 1 } rmt_clock_source_t;

