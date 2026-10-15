/*
 * Compile the actual vendored RMT driver with isolated ESP-IDF API fakes.
 * No scheduler, USB port, GPIO, or RMT peripheral is touched by this test.
 * TRILINK_RGB_RMT_SOURCE can select a captured driver for regression checks.
 */
#include <stdbool.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>
#include "driver/rmt_tx.h"
#include "led_strip.h"
#include "led_strip_interface.h"
#include "led_strip_rmt_encoder.h"

struct fake_rmt_channel {
    bool enabled;
    bool in_flight;
    bool deleted;
};
struct fake_rmt_encoder { bool deleted; };

static struct {
    struct fake_rmt_channel channel;
    struct fake_rmt_encoder encoder;
    esp_err_t enable_result;
    esp_err_t transmit_result;
    esp_err_t wait_result;
    esp_err_t disable_result;
    esp_err_t channel_delete_result;
    esp_err_t encoder_delete_result;
    rmt_transmit_config_t transmit_config;
    const uint8_t *payload;
    size_t payload_size;
    int wait_timeout_ms;
    unsigned enables, transmits, waits, disables;
    unsigned channel_deletes, encoder_deletes, allocations, frees;
    unsigned unsafe_frees;
    void *allocation;
    char calls[128];
    size_t call_count;
} fake;
static unsigned checks, failures;

#define CHECK(condition) do { \
    ++checks; \
    if (!(condition)) { \
        ++failures; \
        fprintf(stderr, "FAIL %s:%d: %s\n", __func__, __LINE__, #condition); \
    } \
} while (0)

static void call(char name)
{
    if (fake.call_count + 1 < sizeof(fake.calls)) {
        fake.calls[fake.call_count++] = name;
        fake.calls[fake.call_count] = '\0';
    }
}

static void *tracked_calloc(size_t count, size_t size)
{
    ++fake.allocations;
    fake.allocation = calloc(count, size);
    return fake.allocation;
}

static void tracked_free(void *pointer)
{
    ++fake.frees;
    if (fake.channel.enabled || fake.channel.in_flight) ++fake.unsafe_frees;
    if (pointer == fake.allocation) fake.allocation = NULL;
    free(pointer);
}

#define calloc tracked_calloc
#define free tracked_free
#ifndef TRILINK_RGB_RMT_SOURCE
#define TRILINK_RGB_RMT_SOURCE "../../components/led_strip/src/led_strip_rmt_dev.c"
#endif
#include TRILINK_RGB_RMT_SOURCE
#undef free
#undef calloc

esp_err_t rmt_new_tx_channel(const rmt_tx_channel_config_t *config,
                             rmt_channel_handle_t *channel)
{
    CHECK(config->gpio_num == 48);
    *channel = &fake.channel;
    return ESP_OK;
}

esp_err_t rmt_new_led_strip_encoder(const led_strip_encoder_config_t *config,
                                   rmt_encoder_handle_t *encoder)
{
    CHECK(config->led_model == LED_MODEL_WS2812);
    *encoder = &fake.encoder;
    return ESP_OK;
}

esp_err_t rmt_enable(rmt_channel_handle_t channel)
{
    call('E');
    ++fake.enables;
    CHECK(channel == &fake.channel && !channel->deleted);
    if (channel->enabled) return ESP_ERR_INVALID_STATE;
    if (fake.enable_result == ESP_OK) channel->enabled = true;
    return fake.enable_result;
}

esp_err_t rmt_transmit(rmt_channel_handle_t channel, rmt_encoder_handle_t encoder,
                       const void *payload, size_t payload_size,
                       const rmt_transmit_config_t *config)
{
    call('T');
    ++fake.transmits;
    CHECK(channel == &fake.channel && channel->enabled);
    CHECK(encoder == &fake.encoder && !encoder->deleted);
    fake.transmit_config = *config;
    fake.payload = payload;
    fake.payload_size = payload_size;
    if (fake.transmit_result == ESP_OK) channel->in_flight = true;
    return fake.transmit_result;
}

esp_err_t rmt_tx_wait_all_done(rmt_channel_handle_t channel, int timeout_ms)
{
    call('W');
    ++fake.waits;
    CHECK(channel == &fake.channel && channel->enabled);
    fake.wait_timeout_ms = timeout_ms;
    /* Return immediately even for -1: the caller's bound itself is asserted. */
    if (fake.wait_result == ESP_OK) channel->in_flight = false;
    return fake.wait_result;
}

esp_err_t rmt_disable(rmt_channel_handle_t channel)
{
    call('D');
    ++fake.disables;
    CHECK(channel == &fake.channel && !channel->deleted);
    if (!channel->enabled) return ESP_ERR_INVALID_STATE;
    if (fake.disable_result == ESP_OK) {
        channel->enabled = false;
        channel->in_flight = false;
    }
    return fake.disable_result;
}

esp_err_t rmt_del_channel(rmt_channel_handle_t channel)
{
    call('C');
    ++fake.channel_deletes;
    CHECK(channel == &fake.channel && !channel->deleted);
    if (channel->enabled || channel->in_flight) return ESP_ERR_INVALID_STATE;
    if (fake.channel_delete_result == ESP_OK) channel->deleted = true;
    return fake.channel_delete_result;
}

esp_err_t rmt_del_encoder(rmt_encoder_handle_t encoder)
{
    call('X');
    ++fake.encoder_deletes;
    CHECK(encoder == &fake.encoder && !encoder->deleted);
    if (fake.encoder_delete_result == ESP_OK) encoder->deleted = true;
    return fake.encoder_delete_result;
}

static led_strip_handle_t new_strip(void)
{
    /* A failed regression must not leak allocations across test cases. */
    free(fake.allocation);
    memset(&fake, 0, sizeof(fake));
    fake.wait_timeout_ms = -999;
    led_strip_config_t config = {
        .strip_gpio_num = 48,
        .max_leds = 1,
        .led_model = LED_MODEL_WS2812,
        .color_component_format = LED_STRIP_COLOR_COMPONENT_FMT_GRBW,
        .flags = { .invert_out = 0 },
    };
    led_strip_rmt_config_t rmt_config = { .resolution_hz = 10000000 };
    led_strip_handle_t strip = NULL;
    CHECK(led_strip_new_rmt_device(&config, &rmt_config, &strip) == ESP_OK);
    CHECK(strip != NULL);
    return strip;
}

static void check_fault_latched(led_strip_handle_t strip)
{
    unsigned old_enables = fake.enables;
    unsigned old_transmits = fake.transmits;
    unsigned old_disables = fake.disables;
    CHECK(strip->refresh(strip) == ESP_ERR_INVALID_STATE);
    CHECK(fake.enables == old_enables);
    CHECK(fake.transmits == old_transmits);
    CHECK(fake.disables == old_disables);
}

static void healthy_refresh(void)
{
    led_strip_handle_t strip = new_strip();
    CHECK(strip->set_pixel_rgbw(strip, 0, 3, 5, 7, 11) == ESP_OK);
    CHECK(strip->refresh(strip) == ESP_OK);
    CHECK(strcmp(fake.calls, "ETWD") == 0);
    CHECK(fake.transmit_config.flags.queue_nonblocking == 1);
    CHECK(fake.transmit_config.loop_count == 0);
    CHECK(fake.wait_timeout_ms == 100);
    CHECK(fake.payload_size == 4);
    CHECK(fake.payload[0] == 5 && fake.payload[1] == 3 &&
          fake.payload[2] == 7 && fake.payload[3] == 11);
    CHECK(!fake.channel.enabled && !fake.channel.in_flight);
    CHECK(strip->set_pixel(strip, 0, 13, 17, 19) == ESP_OK);
    CHECK(strip->refresh(strip) == ESP_OK);
    CHECK(fake.enables == 2 && fake.transmits == 2 && fake.waits == 2 && fake.disables == 2);
    CHECK(strip->del(strip) == ESP_OK);
    CHECK(fake.channel_deletes == 1 && fake.encoder_deletes == 1 && fake.frees == 1);
    CHECK(fake.unsafe_frees == 0);
}

static void transmit_failure(void)
{
    led_strip_handle_t strip = new_strip();
    fake.transmit_result = ESP_FAIL;
    CHECK(strip->refresh(strip) == ESP_FAIL);
    CHECK(strcmp(fake.calls, "ETD") == 0);
    CHECK(fake.disables == 1 && fake.waits == 0);
    CHECK(!fake.channel.enabled);
    fake.transmit_result = ESP_OK;
    check_fault_latched(strip);
    CHECK(strip->del(strip) == ESP_OK);
    CHECK(fake.unsafe_frees == 0);
}

static void enable_failure(void)
{
    led_strip_handle_t strip = new_strip();
    fake.enable_result = ESP_FAIL;
    CHECK(strip->refresh(strip) == ESP_FAIL);
    CHECK(strcmp(fake.calls, "E") == 0);
    CHECK(fake.enables == 1 && fake.transmits == 0 && fake.waits == 0 && fake.disables == 0);
    CHECK(!fake.channel.enabled && !fake.channel.in_flight);
    fake.enable_result = ESP_OK;
    check_fault_latched(strip);
    CHECK(strip->del(strip) == ESP_OK);
    CHECK(fake.channel_deletes == 1 && fake.encoder_deletes == 1 && fake.frees == 1);
    CHECK(fake.unsafe_frees == 0);
}

static void wait_failure(esp_err_t error)
{
    led_strip_handle_t strip = new_strip();
    fake.wait_result = error;
    CHECK(strip->refresh(strip) == error);
    CHECK(strcmp(fake.calls, "ETWD") == 0);
    CHECK(fake.wait_timeout_ms == 100);
    CHECK(fake.disables == 1);
    CHECK(!fake.channel.enabled && !fake.channel.in_flight);
    fake.wait_result = ESP_OK;
    check_fault_latched(strip);
    CHECK(strip->del(strip) == ESP_OK);
    CHECK(fake.unsafe_frees == 0);
}

static void stop_failure_preserves_payload_and_retry_delete(void)
{
    led_strip_handle_t strip = new_strip();
    CHECK(strip->set_pixel_rgbw(strip, 0, 23, 29, 31, 37) == ESP_OK);
    fake.wait_result = ESP_ERR_TIMEOUT;
    fake.disable_result = ESP_FAIL;
    CHECK(strip->refresh(strip) == ESP_ERR_TIMEOUT);
    CHECK(fake.channel.enabled && fake.channel.in_flight);
    uint8_t held_payload[4];
    memcpy(held_payload, fake.payload, sizeof(held_payload));
    unsigned old_transmits = fake.transmits;
    CHECK(strip->set_pixel(strip, 0, 41, 43, 47) == ESP_ERR_INVALID_STATE);
    CHECK(memcmp(held_payload, fake.payload, sizeof(held_payload)) == 0);
    CHECK(strip->set_pixel_rgbw(strip, 0, 53, 59, 61, 67) == ESP_ERR_INVALID_STATE);
    CHECK(memcmp(held_payload, fake.payload, sizeof(held_payload)) == 0);
    CHECK(strip->clear(strip) == ESP_ERR_INVALID_STATE);
    CHECK(memcmp(held_payload, fake.payload, sizeof(held_payload)) == 0);
    CHECK(fake.transmits == old_transmits);
    check_fault_latched(strip);

    fake.call_count = 0;
    fake.calls[0] = '\0';
    CHECK(strip->del(strip) != ESP_OK);
    CHECK(strcmp(fake.calls, "D") == 0);
    CHECK(fake.channel_deletes == 0 && fake.encoder_deletes == 0 && fake.frees == 0);
    CHECK(fake.channel.enabled && fake.channel.in_flight);
    CHECK(memcmp(held_payload, fake.payload, sizeof(held_payload)) == 0);

    fake.disable_result = ESP_OK;
    fake.call_count = 0;
    fake.calls[0] = '\0';
    CHECK(strip->del(strip) == ESP_OK);
    CHECK(strcmp(fake.calls, "DCX") == 0);
    CHECK(!fake.channel.enabled && !fake.channel.in_flight);
    CHECK(fake.channel_deletes == 1 && fake.encoder_deletes == 1 && fake.frees == 1);
    CHECK(fake.unsafe_frees == 0);
}

static void disable_failure_is_fault(void)
{
    led_strip_handle_t strip = new_strip();
    fake.disable_result = ESP_FAIL;
    CHECK(strip->refresh(strip) == ESP_FAIL);
    CHECK(fake.wait_timeout_ms == 100);
    CHECK(fake.channel.enabled);
    check_fault_latched(strip);
    fake.disable_result = ESP_OK;
    CHECK(strip->del(strip) == ESP_OK);
    CHECK(fake.unsafe_frees == 0);
}

static void reused_handle_timeout(void)
{
    led_strip_handle_t strip = new_strip();
    CHECK(strip->set_pixel(strip, 0, 3, 5, 7) == ESP_OK);
    CHECK(strip->refresh(strip) == ESP_OK);
    CHECK(!fake.channel.enabled && !fake.channel.in_flight);
    CHECK(strip->set_pixel(strip, 0, 11, 13, 17) == ESP_OK);
    fake.wait_result = ESP_ERR_TIMEOUT;
    CHECK(strip->refresh(strip) == ESP_ERR_TIMEOUT);
    CHECK(strcmp(fake.calls, "ETWDETWD") == 0);
    CHECK(fake.wait_timeout_ms == 100);
    CHECK(fake.enables == 2 && fake.transmits == 2 && fake.waits == 2 && fake.disables == 2);
    CHECK(!fake.channel.enabled && !fake.channel.in_flight);
    fake.wait_result = ESP_OK;
    check_fault_latched(strip);
    CHECK(strip->del(strip) == ESP_OK);
    CHECK(fake.unsafe_frees == 0);
}

static void channel_delete_retry(void)
{
    led_strip_handle_t strip = new_strip();
    fake.channel_delete_result = ESP_FAIL;
    CHECK(strip->del(strip) == ESP_FAIL);
    CHECK(fake.encoder_deletes == 0 && fake.frees == 0);
    fake.channel_delete_result = ESP_OK;
    CHECK(strip->del(strip) == ESP_OK);
    CHECK(fake.channel_deletes == 2 && fake.encoder_deletes == 1 && fake.frees == 1);
    CHECK(fake.unsafe_frees == 0);
}

static void encoder_delete_retry(void)
{
    led_strip_handle_t strip = new_strip();
    fake.encoder_delete_result = ESP_FAIL;
    CHECK(strip->del(strip) == ESP_FAIL);
    CHECK(fake.channel_deletes == 1 && fake.encoder_deletes == 1 && fake.frees == 0);
    fake.encoder_delete_result = ESP_OK;
    CHECK(strip->del(strip) == ESP_OK);
    CHECK(fake.channel_deletes == 1 && fake.encoder_deletes == 2 && fake.frees == 1);
    CHECK(fake.unsafe_frees == 0);
}

int main(void)
{
    healthy_refresh();
    enable_failure();
    transmit_failure();
    wait_failure(ESP_ERR_TIMEOUT);
    wait_failure(ESP_FAIL);
    stop_failure_preserves_payload_and_retry_delete();
    disable_failure_is_fault();
    reused_handle_timeout();
    channel_delete_retry();
    encoder_delete_retry();
    free(fake.allocation);
    printf("%s: RMT driver fault paths: %u checks, %u failures\n",
           failures == 0 ? "PASS" : "FAIL", checks, failures);
    return failures == 0 ? 0 : 1;
}
