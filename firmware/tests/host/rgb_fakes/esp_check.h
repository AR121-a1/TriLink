#pragma once

#include <stddef.h>
#include <stdbool.h>
#include "esp_err.h"

#ifndef __containerof
#define __containerof(ptr, type, member) \
    ((type *)((char *)(ptr) - offsetof(type, member)))
#endif
#ifndef BIT
#define BIT(index) (1U << (index))
#endif

#define ESP_RETURN_ON_FALSE(condition, code, tag, ...) do { \
    (void)(tag); \
    if (!(condition)) return (code); \
} while (0)
#define ESP_RETURN_ON_ERROR(expression, tag, ...) do { \
    esp_err_t fake_check_error = (expression); \
    (void)(tag); \
    if (fake_check_error != ESP_OK) return fake_check_error; \
} while (0)
#define ESP_GOTO_ON_FALSE(condition, code, label, tag, ...) do { \
    (void)(tag); \
    if (!(condition)) { ret = (code); goto label; } \
} while (0)
#define ESP_GOTO_ON_ERROR(expression, label, tag, ...) do { \
    esp_err_t fake_check_error = (expression); \
    (void)(tag); \
    if (fake_check_error != ESP_OK) { ret = fake_check_error; goto label; } \
} while (0)

