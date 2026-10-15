#pragma once
#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>
#include "esp_err.h"

/* HTTP never calls the node bridge. The sole USB transaction worker drains
 * this one-slot mailbox when idle and MUST complete every taken ticket once,
 * including local rejection/timeout. No automatic retries or persistence. */
#define TRILINK_WEB_LINE_CAPACITY 384U
#define TRILINK_WEB_REPLY_CAPACITY 2048U
esp_err_t trilink_web_start(void);
bool trilink_web_take(char *line, size_t capacity, uint32_t *ticket);
bool trilink_web_can_submit(uint32_t ticket);
void trilink_web_complete(uint32_t ticket, const char *reply, size_t length);
