#include "wakenic_core.h"

#include <inttypes.h>
#include <stddef.h>

#include "esp_heap_caps.h"
#include "esp_log.h"
#include "esp_timer.h"
#include "freertos/FreeRTOS.h"
#include "freertos/queue.h"
#include "freertos/task.h"

#define WAKENIC_EVENT_QUEUE_DEPTH 16U
#define WAKENIC_DIAGNOSTIC_PERIOD_MS 5000U

typedef struct {
    QueueHandle_t queue;
    wakenic_state_model_t model;
    uint32_t event_count;
    uint32_t invalid_event_count;
    uint32_t duplicate_event_count;
    uint32_t dropped_event_count;
    portMUX_TYPE counters_lock;
} wakenic_core_context_t;

static const char *TAG = "wakenic_core";
static wakenic_core_context_t s_core = {
    .model = {
        .state = WAKENIC_STATE_BOOT,
    },
    .counters_lock = portMUX_INITIALIZER_UNLOCKED,
};

static void log_transition(wakenic_state_t previous,
                           const wakenic_event_t *event)
{
    if (previous == s_core.model.state) {
        return;
    }

    ESP_LOGI(TAG,
             "state %s -> %s event=%s correlation=%" PRIu32
             " status=%" PRId32,
             wakenic_state_name(previous),
             wakenic_state_name(s_core.model.state),
             wakenic_event_name(event->type),
             event->correlation_id,
             event->status);
}

static void handle_event(const wakenic_event_t *event)
{
    ++s_core.event_count;
    const wakenic_state_t previous = s_core.model.state;
    const wakenic_reduce_result_t result =
        wakenic_state_reduce(&s_core.model, event);

    log_transition(previous, event);

    switch (result) {
    case WAKENIC_REDUCE_ACCEPTED:
        return;

    case WAKENIC_REDUCE_DUPLICATE:
        ++s_core.duplicate_event_count;
        ESP_LOGD(TAG,
                 "duplicate event=%s state=%s correlation=%" PRIu32,
                 wakenic_event_name(event->type),
                 wakenic_state_name(s_core.model.state),
                 event->correlation_id);
        return;

    case WAKENIC_REDUCE_FAULTED:
        ESP_LOGE(TAG,
                 "fault event=%s status=%" PRId32 " correlation=%" PRIu32,
                 wakenic_event_name(event->type),
                 event->status,
                 event->correlation_id);
        return;

    case WAKENIC_REDUCE_INVALID:
        ++s_core.invalid_event_count;
        ESP_LOGW(TAG,
                 "ignored event=%s in state=%s correlation=%" PRIu32,
                 wakenic_event_name(event->type),
                 wakenic_state_name(s_core.model.state),
                 event->correlation_id);
        return;
    }
}

static void log_diagnostics(void)
{
    uint32_t dropped;
    portENTER_CRITICAL(&s_core.counters_lock);
    dropped = s_core.dropped_event_count;
    portEXIT_CRITICAL(&s_core.counters_lock);

    const uint32_t free_heap =
        (uint32_t)heap_caps_get_free_size(MALLOC_CAP_INTERNAL | MALLOC_CAP_8BIT);
    const uint32_t min_heap =
        (uint32_t)heap_caps_get_minimum_free_size(MALLOC_CAP_INTERNAL
                                                 | MALLOC_CAP_8BIT);
    const uint32_t largest_heap =
        (uint32_t)heap_caps_get_largest_free_block(MALLOC_CAP_INTERNAL
                                                  | MALLOC_CAP_8BIT);
    const uint32_t psram_free =
        (uint32_t)heap_caps_get_free_size(MALLOC_CAP_SPIRAM | MALLOC_CAP_8BIT);
    const uint32_t psram_min =
        (uint32_t)heap_caps_get_minimum_free_size(MALLOC_CAP_SPIRAM
                                                 | MALLOC_CAP_8BIT);
    const uint32_t psram_largest =
        (uint32_t)heap_caps_get_largest_free_block(MALLOC_CAP_SPIRAM
                                                  | MALLOC_CAP_8BIT);
    const UBaseType_t stack_hwm = uxTaskGetStackHighWaterMark(NULL);
    const UBaseType_t queued = uxQueueMessagesWaiting(s_core.queue);
    const int64_t uptime_ms = esp_timer_get_time() / 1000LL;

    ESP_LOGI(TAG,
             "diag uptime_ms=%" PRId64 " state=%s free_heap=%" PRIu32
             " min_heap=%" PRIu32 " largest_heap=%" PRIu32
             " psram_free=%" PRIu32 " psram_min=%" PRIu32
             " psram_largest=%" PRIu32 " stack_hwm=%u queue=%u"
             " events=%" PRIu32 " invalid=%" PRIu32
             " duplicate=%" PRIu32 " dropped=%" PRIu32
             " board=%s wifi_port=%s usb_port=%s",
             uptime_ms,
             wakenic_state_name(s_core.model.state),
             free_heap,
             min_heap,
             largest_heap,
             psram_free,
             psram_min,
             psram_largest,
             (unsigned)stack_hwm,
             (unsigned)queued,
             s_core.event_count,
             s_core.invalid_event_count,
             s_core.duplicate_event_count,
             dropped,
             s_core.model.board_ready ? "ready" : "not_ready",
             s_core.model.wifi_port_registered ? "espnow_ready"
                                                : "not_registered",
             s_core.model.usb_port_registered ? "stub_registered"
                                               : "not_registered");
}

esp_err_t wakenic_core_init(void)
{
    if (s_core.queue != NULL) {
        return ESP_ERR_INVALID_STATE;
    }

    wakenic_state_model_init(&s_core.model);
    s_core.queue = xQueueCreate(WAKENIC_EVENT_QUEUE_DEPTH,
                                sizeof(wakenic_event_t));
    if (s_core.queue == NULL) {
        return ESP_ERR_NO_MEM;
    }

    ESP_LOGI(TAG,
             "typed event queue ready depth=%u owner=app_main",
             WAKENIC_EVENT_QUEUE_DEPTH);
    return ESP_OK;
}

esp_err_t wakenic_core_post(wakenic_event_type_t type,
                            uint32_t correlation_id,
                            int32_t status)
{
    if ((s_core.queue == NULL)
        || ((unsigned)type >= WAKENIC_EVENT_COUNT)) {
        return ESP_ERR_INVALID_ARG;
    }

    const wakenic_event_t event = {
        .type = type,
        .correlation_id = correlation_id,
        .status = status,
    };

    const BaseType_t queued = (type == WAKENIC_EVENT_FATAL)
                                  ? xQueueSendToFront(s_core.queue, &event, 0)
                                  : xQueueSendToBack(s_core.queue, &event, 0);
    if (queued != pdTRUE) {
        portENTER_CRITICAL(&s_core.counters_lock);
        ++s_core.dropped_event_count;
        portEXIT_CRITICAL(&s_core.counters_lock);
        return ESP_ERR_TIMEOUT;
    }

    return ESP_OK;
}

void wakenic_core_run(void)
{
    if (s_core.queue == NULL) {
        ESP_LOGE(TAG, "core_run called before core_init");
        return;
    }

    wakenic_event_t event;
    const TickType_t diagnostic_period =
        pdMS_TO_TICKS(WAKENIC_DIAGNOSTIC_PERIOD_MS);
    TickType_t last_diagnostic = xTaskGetTickCount();

    for (;;) {
        const TickType_t now = xTaskGetTickCount();
        const TickType_t elapsed = now - last_diagnostic;
        const TickType_t wait_ticks = (elapsed >= diagnostic_period)
                                          ? 0
                                          : diagnostic_period - elapsed;

        if (xQueueReceive(s_core.queue, &event, wait_ticks) == pdTRUE) {
            handle_event(&event);
        }

        const TickType_t after_receive = xTaskGetTickCount();
        if ((after_receive - last_diagnostic) >= diagnostic_period) {
            log_diagnostics();
            last_diagnostic = after_receive;
        }
    }
}
