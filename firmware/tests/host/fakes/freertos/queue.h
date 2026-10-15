#pragma once
#include <stddef.h>
#include <string.h>
#include "FreeRTOS.h"
/* Deterministic single-thread adapter for the real bridge owner logic.
 * This does not model RTOS scheduling or prove task concurrency. */
typedef struct {
    size_t capacity, size, head, count;
    uint8_t *storage;
} StaticQueue_t;
typedef StaticQueue_t *QueueHandle_t;
static inline QueueHandle_t xQueueCreateStatic(size_t n, size_t size, uint8_t *storage, StaticQueue_t *q)
{
    *q = (StaticQueue_t){n, size, 0, 0, storage};
    return q;
}
static inline int xQueueSend(QueueHandle_t q, const void *item, TickType_t ticks)
{
    (void)ticks;
    if (q == NULL || q->count == q->capacity) return pdFALSE;
    memcpy(q->storage + ((q->head+q->count) % q->capacity)*q->size, item, q->size);
    ++q->count;
    return pdTRUE;
}
static inline int xQueueReceive(QueueHandle_t q, void *item, TickType_t ticks)
{
    (void)ticks;
    if (q == NULL || q->count == 0) return pdFALSE;
    memcpy(item, q->storage + q->head*q->size, q->size);
    q->head = (q->head+1U) % q->capacity;
    --q->count;
    return pdTRUE;
}
static inline size_t uxQueueSpacesAvailable(QueueHandle_t q)
{
    return q->capacity - q->count;
}
