#pragma once

#include <stddef.h>
#include <stdint.h>

typedef unsigned TickType_t;
typedef int BaseType_t;
typedef unsigned UBaseType_t;
typedef int portMUX_TYPE;

#define pdTRUE 1
#define pdFALSE 0
#define pdPASS 1
#define portMAX_DELAY UINT32_MAX
#define portMUX_INITIALIZER_UNLOCKED 0
#define pdMS_TO_TICKS(ms) ((TickType_t)(ms))
#define portENTER_CRITICAL(lock) ((void)(lock))
#define portEXIT_CRITICAL(lock) ((void)(lock))

/* Single-item deterministic queue; not a concurrent FreeRTOS model. */
typedef struct {
    uint8_t *storage;
    size_t item_size;
    unsigned count;
} StaticQueue_t;
typedef StaticQueue_t *QueueHandle_t;
