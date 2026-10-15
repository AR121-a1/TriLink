#pragma once
#include "FreeRTOS.h"

typedef void (*TaskFunction_t)(void *);
typedef void *TaskHandle_t;
BaseType_t xTaskCreate(TaskFunction_t task, const char *name, uint32_t stack_depth,
                      void *argument, UBaseType_t priority, TaskHandle_t *handle);
void vTaskDelay(TickType_t delay);
UBaseType_t uxTaskGetStackHighWaterMark(TaskHandle_t handle);
