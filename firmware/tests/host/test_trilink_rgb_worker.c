/* Runs the real RGB public API and worker with deterministic fake queue/task/LED.
 * The task is captured, not started concurrently. Empty queue idle is escaped
 * with setjmp after three receives; each case resets module state as a fresh
 * boot. This is fault-path coverage, not an RTOS/ISR scheduling stress test.
 *
 * TRILINK_RGB_SOURCE may select another source file for behavior comparison.
 * For a source without the fault latch, also set TRILINK_RGB_HAS_FAULT_LATCH=0.
 * All functional failure expectations stay the same in either compilation.
 */
#include <setjmp.h>
#include <stdbool.h>
#include <stdio.h>
#include <string.h>
#include "freertos/FreeRTOS.h"
#include "freertos/queue.h"
#include "freertos/task.h"
#include "led_strip.h"

#ifndef TRILINK_RGB_SOURCE
#define TRILINK_RGB_SOURCE "../../components/trilink_node/trilink_rgb.c"
#endif
#ifndef TRILINK_RGB_HAS_FAULT_LATCH
#define TRILINK_RGB_HAS_FAULT_LATCH 1
#endif
#include TRILINK_RGB_SOURCE

static unsigned checks, failures, cases;
#define CHECK(condition) do { \
    ++checks; \
    if (!(condition)) { \
        ++failures; \
        fprintf(stderr, "line %d: %s\n", __LINE__, #condition); \
    } \
} while (0)

typedef enum {
    FAIL_NONE, FAIL_CREATE, FAIL_SET, FAIL_REFRESH, FAIL_CLEAR
} fail_stage_t;
struct rgb_worker_fake_strip {
    bool live;
    uint8_t color[3];
};
static struct {
    TaskFunction_t task;
    void *task_argument;
    fail_stage_t fail_stage;
    unsigned fail_call;
    bool delete_fails, send_fails, running;
    unsigned queue_creates, task_creates, sends, receives, idle_receives;
    unsigned creates, sets, refreshes, clears, deletes, allocations;
    unsigned delays, delay_ms, logs;
    struct rgb_worker_fake_strip strip;
    jmp_buf idle;
} f;

const char *esp_err_to_name(esp_err_t error)
{
    return error == ESP_OK ? "ESP_OK" : "injected_error";
}
void rgb_worker_fake_log(const char *tag, const char *format, ...)
{
    CHECK(strcmp(tag, "trilink_rgb") == 0);
    CHECK(format != NULL);
    ++f.logs;
}
QueueHandle_t xQueueCreateStatic(UBaseType_t length, UBaseType_t item_size,
                                uint8_t *storage, StaticQueue_t *queue)
{
    ++f.queue_creates;
    CHECK(length == 1 && item_size == 3);
    CHECK(storage != NULL && queue != NULL);
    queue->storage = storage;
    queue->item_size = item_size;
    queue->count = 0;
    return queue;
}
BaseType_t xTaskCreate(TaskFunction_t task, const char *name, uint32_t stack_depth,
                      void *argument, UBaseType_t priority, TaskHandle_t *handle)
{
    ++f.task_creates;
    CHECK(task != NULL && strcmp(name, "trilink_rgb") == 0);
    CHECK(stack_depth == 3072 && priority == 2 && handle == NULL);
    f.task = task;
    f.task_argument = argument;
    return pdPASS;
}
BaseType_t xQueueSend(QueueHandle_t queue, const void *item, TickType_t wait)
{
    ++f.sends;
    CHECK(queue != NULL && item != NULL && wait == 0);
    if (f.send_fails || queue->count != 0) return pdFALSE;
    memcpy(queue->storage, item, queue->item_size);
    queue->count = 1;
    return pdTRUE;
}
BaseType_t xQueueReceive(QueueHandle_t queue, void *item, TickType_t wait)
{
    ++f.receives;
    CHECK(queue != NULL && item != NULL && wait == portMAX_DELAY && f.running);
    if (queue->count == 0) {
        if (++f.idle_receives == 3) longjmp(f.idle, 1);
        return pdFALSE;
    }
    memcpy(item, queue->storage, queue->item_size);
    queue->count = 0;
    return pdTRUE;
}
void vTaskDelay(TickType_t delay)
{
    CHECK(delay == 80);
    ++f.delays;
    f.delay_ms += delay;
}
UBaseType_t uxTaskGetStackHighWaterMark(TaskHandle_t handle)
{
    CHECK(handle == NULL);
    return 1024;
}
static bool fails_at(fail_stage_t stage, unsigned call)
{
    return f.fail_stage == stage && f.fail_call == call;
}
static void valid_strip(led_strip_handle_t strip)
{
    CHECK(strip == &f.strip && strip->live);
}
esp_err_t led_strip_new_rmt_device(const led_strip_config_t *config,
                                   const led_strip_rmt_config_t *rmt,
                                   led_strip_handle_t *strip)
{
    ++f.creates;
    CHECK(config->strip_gpio_num == 48 && config->max_leds == 1);
    CHECK(config->led_model == LED_MODEL_WS2812);
    CHECK(config->color_component_format == LED_STRIP_COLOR_COMPONENT_FMT_GRB);
    CHECK(rmt->resolution_hz == 10000000U && strip != NULL);
    if (fails_at(FAIL_CREATE, f.creates)) {
        *strip = NULL;
        return ESP_ERR_NO_MEM;
    }
    CHECK(!f.strip.live);
    f.strip.live = true;
    ++f.allocations;
    *strip = &f.strip;
    return ESP_OK;
}
esp_err_t led_strip_set_pixel(led_strip_handle_t strip, uint32_t index,
                             uint32_t red, uint32_t green, uint32_t blue)
{
    valid_strip(strip);
    ++f.sets;
    CHECK(index == 0 && red == 4 && green == 8 && blue == 12);
    if (fails_at(FAIL_SET, f.sets)) return ESP_FAIL;
    strip->color[0] = (uint8_t)red;
    strip->color[1] = (uint8_t)green;
    strip->color[2] = (uint8_t)blue;
    return ESP_OK;
}
esp_err_t led_strip_refresh(led_strip_handle_t strip)
{
    valid_strip(strip);
    ++f.refreshes;
    CHECK(strip->color[0] == 4 && strip->color[1] == 8 && strip->color[2] == 12);
    return fails_at(FAIL_REFRESH, f.refreshes) ? ESP_ERR_TIMEOUT : ESP_OK;
}
esp_err_t led_strip_clear(led_strip_handle_t strip)
{
    valid_strip(strip);
    ++f.clears;
    if (fails_at(FAIL_CLEAR, f.clears)) return ESP_FAIL;
    memset(strip->color, 0, sizeof(strip->color));
    return ESP_OK;
}
esp_err_t led_strip_del(led_strip_handle_t strip)
{
    valid_strip(strip);
    ++f.deletes;
    if (f.delete_fails) return ESP_ERR_INVALID_STATE;
    strip->live = false;
    return ESP_OK;
}
static void fresh_boot(void)
{
    ++cases;
    memset(&f, 0, sizeof(f));
    /* Reset only as a fresh-boot fixture, never through the product API. */
    s_jobs = NULL;
    memset(&s_queue, 0, sizeof(s_queue));
    memset(s_storage, 0, sizeof(s_storage));
    s_enabled = false;
    s_busy = false;
    s_result = 0;
#if TRILINK_RGB_HAS_FAULT_LATCH
    s_faulted = false;
#endif
    CHECK(trilink_rgb_init());
    CHECK(f.queue_creates == 1 && f.task_creates == 1 && f.task != NULL);
    CHECK(f.creates == 0 && f.allocations == 0 && f.sets == 0 && f.refreshes == 0);
    CHECK(!trilink_rgb_enabled() && trilink_rgb_result() == 0);
#if TRILINK_RGB_HAS_FAULT_LATCH
    CHECK(!trilink_rgb_faulted());
#endif
}
static void run_to_idle(void)
{
    f.idle_receives = 0;
    f.running = true;
    if (setjmp(f.idle) == 0) {
        f.task(f.task_argument);
        CHECK(false); /* Real worker is an infinite task; normal return is invalid. */
    }
    f.running = false;
    CHECK(f.idle_receives == 3);
}
static void begin_job(void)
{
    CHECK(trilink_rgb_enable(true));
    CHECK(trilink_rgb_enabled());
    CHECK(trilink_rgb_begin(4, 8, 12));
    CHECK(trilink_rgb_result() == 0);
    CHECK(!trilink_rgb_begin(4, 8, 12));
    CHECK(!trilink_rgb_enable(false));
}
static void default_disabled_and_success(void)
{
    fresh_boot();
    CHECK(!trilink_rgb_begin(4, 8, 12));
    run_to_idle();
    CHECK(f.creates == 0 && f.sets == 0 && f.refreshes == 0 && f.clears == 0);
    CHECK(f.logs == 0 && f.sends == 0);
    CHECK(trilink_rgb_enable(true));
    CHECK(!trilink_rgb_begin(33, 8, 12));
    CHECK(!trilink_rgb_begin(4, 33, 12));
    CHECK(!trilink_rgb_begin(4, 8, 33));
    CHECK(f.sends == 0 && f.creates == 0);
    begin_job();
    run_to_idle();
    CHECK(f.creates == 1 && f.allocations == 1 && f.strip.live);
    CHECK(f.sets == 2 && f.refreshes == 2 && f.clears == 2 && f.deletes == 0);
    CHECK(f.delays == 3 && f.delay_ms == 240 && f.logs == 1);
    CHECK(f.strip.color[0] == 0 && f.strip.color[1] == 0 && f.strip.color[2] == 0);
    CHECK(trilink_rgb_enabled());
    CHECK(!trilink_rgb_begin(4, 8, 12)); /* Completed result still occupies busy. */
    CHECK(trilink_rgb_result() == 1 && trilink_rgb_result() == 0);
    CHECK(trilink_rgb_enable(false) && !trilink_rgb_enabled());
    CHECK(!trilink_rgb_begin(4, 8, 12));
    CHECK(trilink_rgb_enable(true) && trilink_rgb_begin(4, 8, 12));
    CHECK(trilink_rgb_result() == 0); /* Result consumption releases busy. */
}
static void queue_failure_is_retryable(void)
{
    fresh_boot();
    CHECK(trilink_rgb_enable(true));
    f.send_fails = true;
    CHECK(!trilink_rgb_begin(4, 8, 12));
    CHECK(trilink_rgb_result() == 0 && trilink_rgb_enabled());
    CHECK(trilink_rgb_enable(false) && trilink_rgb_enable(true));
#if TRILINK_RGB_HAS_FAULT_LATCH
    CHECK(!trilink_rgb_faulted());
#endif
    f.send_fails = false;
    CHECK(trilink_rgb_begin(4, 8, 12));
    run_to_idle();
    CHECK(trilink_rgb_result() == 1 && f.creates == 1 && f.deletes == 0);
}
static void driver_failure(fail_stage_t stage, unsigned call, bool delete_fails,
                           unsigned sets, unsigned refreshes, unsigned clears,
                           unsigned delays)
{
    fresh_boot();
    f.fail_stage = stage;
    f.fail_call = call;
    f.delete_fails = delete_fails;
    begin_job();
    run_to_idle();
    CHECK(f.creates == 1);
    CHECK(f.sets == sets && f.refreshes == refreshes && f.clears == clears);
    CHECK(f.delays == delays && f.delay_ms == delays * 80 && f.logs == 1);
    CHECK(f.deletes == (stage == FAIL_CREATE ? 0U : 1U));
    CHECK(f.allocations == (stage == FAIL_CREATE ? 0U : 1U));
    CHECK(f.strip.live == (stage != FAIL_CREATE && delete_fails));
    CHECK(!trilink_rgb_enabled());
#if TRILINK_RGB_HAS_FAULT_LATCH
    CHECK(trilink_rgb_faulted());
#endif
    CHECK(!trilink_rgb_enable(true));
    CHECK(!trilink_rgb_begin(4, 8, 12));
    CHECK(trilink_rgb_result() == 2 && trilink_rgb_result() == 0);
    CHECK(trilink_rgb_enable(false)); /* Consuming failure releases busy. */
    CHECK(!trilink_rgb_enable(true) && !trilink_rgb_enabled());
    CHECK(!trilink_rgb_begin(4, 8, 12));
    unsigned old_creates = f.creates, old_sets = f.sets, old_refreshes = f.refreshes;
    unsigned old_clears = f.clears, old_deletes = f.deletes, old_sends = f.sends;
    run_to_idle();
    CHECK(f.creates == old_creates && f.sets == old_sets && f.refreshes == old_refreshes);
    CHECK(f.clears == old_clears && f.deletes == old_deletes && f.sends == old_sends);
    CHECK(trilink_rgb_result() == 0);
}
int main(void)
{
    default_disabled_and_success();
    queue_failure_is_retryable();
    driver_failure(FAIL_CREATE, 1, false, 0, 0, 0, 0);
    driver_failure(FAIL_SET, 1, false, 1, 0, 0, 0);
    driver_failure(FAIL_REFRESH, 1, false, 1, 1, 0, 0);
    driver_failure(FAIL_CLEAR, 1, false, 1, 1, 1, 1);
    driver_failure(FAIL_SET, 2, false, 2, 1, 1, 2);
    driver_failure(FAIL_REFRESH, 2, false, 2, 2, 1, 2);
    driver_failure(FAIL_CLEAR, 2, false, 2, 2, 2, 3);
    driver_failure(FAIL_REFRESH, 1, true, 1, 1, 0, 0);
    driver_failure(FAIL_CLEAR, 1, true, 1, 1, 1, 1);
    printf("trilink_rgb_worker tests: checks=%u failures=%u cases=%u (real worker; single-thread fake RTOS/LED)\n",
           checks, failures, cases);
    return failures ? 1 : 0;
}
