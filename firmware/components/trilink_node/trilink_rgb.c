/* RMT/WS2812 setup derived from HW-678A hw678a_status_led.c.
 * Independent bounded executor: local opt-in, low brightness, explicit result.
 * Completion proves driver calls, not optical feedback. */
#include "trilink_rgb.h"
#include "freertos/FreeRTOS.h"
#include "freertos/queue.h"
#include "freertos/task.h"
#include "led_strip.h"
#include "esp_log.h"

typedef struct { uint8_t r,g,b; } rgb_job_t;
static QueueHandle_t s_jobs;
static StaticQueue_t s_queue;
static uint8_t s_storage[sizeof(rgb_job_t)];
static portMUX_TYPE s_lock=portMUX_INITIALIZER_UNLOCKED;
static bool s_enabled,s_busy,s_faulted;
static unsigned s_result;
static void worker(void *unused)
{
    (void)unused;
    led_strip_handle_t strip=NULL;
    for(;;) {
        rgb_job_t job;
        if(xQueueReceive(s_jobs,&job,portMAX_DELAY)!=pdTRUE) continue;
        esp_err_t result=ESP_OK;
        if(!strip) {
            const led_strip_config_t config={.strip_gpio_num=48,.max_leds=1,
                .led_model=LED_MODEL_WS2812,.color_component_format=LED_STRIP_COLOR_COMPONENT_FMT_GRB};
            const led_strip_rmt_config_t rmt={.resolution_hz=10000000U};
            result=led_strip_new_rmt_device(&config,&rmt,&strip);
        }
        for(unsigned pulse=0;pulse<2 && result==ESP_OK;++pulse) {
            result=led_strip_set_pixel(strip,0,job.r,job.g,job.b);
            if(result==ESP_OK) result=led_strip_refresh(strip);
            if(result==ESP_OK) vTaskDelay(pdMS_TO_TICKS(80));
            if(result==ESP_OK && strip) result=led_strip_clear(strip);
            if(!pulse && result==ESP_OK) vTaskDelay(pdMS_TO_TICKS(80));
        }
        if(result!=ESP_OK && strip) {
            /* No second transmission after a fault; retain ownership if stop/delete fails. */
            if(led_strip_del(strip)==ESP_OK) strip=NULL;
        }
        portENTER_CRITICAL(&s_lock);
        if(result!=ESP_OK) {s_faulted=true;s_enabled=false;}
        s_result=result==ESP_OK?1U:2U;
        portEXIT_CRITICAL(&s_lock);
        ESP_LOGI("trilink_rgb","driver_result=%s stack_hwm=%u",esp_err_to_name(result),
                 (unsigned)uxTaskGetStackHighWaterMark(NULL));
    }
}
bool trilink_rgb_init(void)
{
    s_jobs=xQueueCreateStatic(1,sizeof(rgb_job_t),s_storage,&s_queue);
    return s_jobs && xTaskCreate(worker,"trilink_rgb",3072,NULL,2,NULL)==pdPASS;
}
bool trilink_rgb_enable(bool enabled)
{
    bool ok;
    portENTER_CRITICAL(&s_lock); ok=!s_busy && (!enabled || !s_faulted); if(ok) s_enabled=enabled; portEXIT_CRITICAL(&s_lock);
    return ok;
}
bool trilink_rgb_enabled(void)
{
    bool enabled; portENTER_CRITICAL(&s_lock); enabled=s_enabled; portEXIT_CRITICAL(&s_lock); return enabled;
}
bool trilink_rgb_faulted(void)
{
    bool faulted; portENTER_CRITICAL(&s_lock); faulted=s_faulted; portEXIT_CRITICAL(&s_lock); return faulted;
}
bool trilink_rgb_begin(uint8_t red,uint8_t green,uint8_t blue)
{
    if(!s_jobs || red>32 || green>32 || blue>32) return false;
    bool ok;
    portENTER_CRITICAL(&s_lock); ok=s_enabled && !s_busy && !s_faulted; if(ok) {s_busy=true;s_result=0;} portEXIT_CRITICAL(&s_lock);
    if(!ok) return false;
    rgb_job_t job={red,green,blue};
    if(xQueueSend(s_jobs,&job,0)==pdTRUE) return true;
    portENTER_CRITICAL(&s_lock); s_busy=false; portEXIT_CRITICAL(&s_lock); return false;
}
unsigned trilink_rgb_result(void)
{
    unsigned result;
    portENTER_CRITICAL(&s_lock); result=s_result;
    if(result) {s_result=0;s_busy=false;} portEXIT_CRITICAL(&s_lock);
    return result;
}
