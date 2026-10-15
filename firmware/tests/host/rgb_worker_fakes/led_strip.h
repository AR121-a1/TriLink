#pragma once
#include <stdint.h>
#include "esp_err.h"

typedef struct rgb_worker_fake_strip *led_strip_handle_t;
typedef struct {
    int strip_gpio_num;
    uint32_t max_leds;
    unsigned led_model;
    unsigned color_component_format;
} led_strip_config_t;
typedef struct {
    uint32_t resolution_hz;
} led_strip_rmt_config_t;

#define LED_MODEL_WS2812 1U
#define LED_STRIP_COLOR_COMPONENT_FMT_GRB 1U

esp_err_t led_strip_new_rmt_device(const led_strip_config_t *config,
                                   const led_strip_rmt_config_t *rmt,
                                   led_strip_handle_t *strip);
esp_err_t led_strip_set_pixel(led_strip_handle_t strip, uint32_t index,
                             uint32_t red, uint32_t green, uint32_t blue);
esp_err_t led_strip_refresh(led_strip_handle_t strip);
esp_err_t led_strip_clear(led_strip_handle_t strip);
esp_err_t led_strip_del(led_strip_handle_t strip);
