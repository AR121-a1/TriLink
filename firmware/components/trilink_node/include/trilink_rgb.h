#pragma once
#include <stdbool.h>
#include <stdint.h>
/* No pin use before explicit local opt-in. Fixed verified HW-678A GPIO48 only. */
bool trilink_rgb_init(void);
bool trilink_rgb_enable(bool enabled);
bool trilink_rgb_enabled(void);
/* Driver faults latch off for this boot; neither enabling nor retrying clears it. */
bool trilink_rgb_faulted(void);
bool trilink_rgb_begin(uint8_t red, uint8_t green, uint8_t blue);
/* 0 pending/no result; 1 driver completed; 2 failed. Consumes terminal result. */
unsigned trilink_rgb_result(void);
