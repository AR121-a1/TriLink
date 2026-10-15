#pragma once

#include "driver/rmt_types.h"
#include "driver/rmt_encoder.h"
#include "esp_err.h"

typedef struct {
    rmt_clock_source_t clk_src;
    int gpio_num;
    size_t mem_block_symbols;
    uint32_t resolution_hz;
    size_t trans_queue_depth;
    struct {
        uint32_t with_dma : 1;
        uint32_t invert_out : 1;
    } flags;
} rmt_tx_channel_config_t;

typedef struct {
    int loop_count;
    struct {
        uint32_t eot_level : 1;
        uint32_t queue_nonblocking : 1;
    } flags;
} rmt_transmit_config_t;

esp_err_t rmt_new_tx_channel(const rmt_tx_channel_config_t *config,
                             rmt_channel_handle_t *channel);
esp_err_t rmt_enable(rmt_channel_handle_t channel);
esp_err_t rmt_transmit(rmt_channel_handle_t channel, rmt_encoder_handle_t encoder,
                       const void *payload, size_t payload_size,
                       const rmt_transmit_config_t *config);
esp_err_t rmt_tx_wait_all_done(rmt_channel_handle_t channel, int timeout_ms);
esp_err_t rmt_disable(rmt_channel_handle_t channel);
esp_err_t rmt_del_channel(rmt_channel_handle_t channel);

