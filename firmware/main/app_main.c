#include "esp_app_desc.h"
#include "esp_err.h"
#include "esp_log.h"
#include "esp_system.h"
#include "trilink_node.h"
#include "wakenic_board.h"
#include "wakenic_core.h"
#include "trilink_usb.h"

static const char *TAG = "wakenic_app";

void app_main(void)
{
    const esp_app_desc_t *app = esp_app_get_description();
    ESP_LOGI(TAG,
             "project=%s version=%s idf=%s target=esp32s3 phase=trilink-real-room-rgb",
             app->project_name,
             app->version,
             esp_get_idf_version());

    ESP_ERROR_CHECK(wakenic_core_init());
    ESP_ERROR_CHECK(wakenic_core_post(WAKENIC_EVENT_APP_BOOT, 0U, 0));

    wakenic_board_log_identity();
    const esp_err_t board_result = wakenic_board_validate();
    if (board_result != ESP_OK) {
        ESP_LOGE(TAG, "Board validation failed: %s",
                 esp_err_to_name(board_result));
        ESP_ERROR_CHECK(wakenic_core_post(WAKENIC_EVENT_FATAL,
                                          0U,
                                          (int32_t)board_result));
        wakenic_core_run();
        return;
    }

    ESP_ERROR_CHECK(wakenic_core_post(WAKENIC_EVENT_BOARD_SELF_TEST_OK,
                                      0U,
                                      0));

    const esp_err_t ports_result = trilink_usb_start();
    if (ports_result != ESP_OK) {
        ESP_LOGE(TAG, "USB control startup failed: %s",
                 esp_err_to_name(ports_result));
        ESP_ERROR_CHECK(wakenic_core_post(WAKENIC_EVENT_FATAL,
                                          0U,
                                          (int32_t)ports_result));
    } else {
        ESP_ERROR_CHECK(wakenic_core_post(WAKENIC_EVENT_USB_PORT_REGISTERED,
                                          0U, 0));
        ESP_LOGI(TAG, "Real USB control backend started");

        const esp_err_t node_result = trilink_node_start();
        if (node_result != ESP_OK) {
            ESP_LOGE(TAG,
                     "TriLink radio/node startup failed: %s",
                     esp_err_to_name(node_result));
            ESP_ERROR_CHECK(wakenic_core_post(WAKENIC_EVENT_FATAL,
                                              0U,
                                              (int32_t)node_result));
        } else {
            ESP_ERROR_CHECK(wakenic_core_post(
                WAKENIC_EVENT_WIFI_PORT_REGISTERED,
                0U,
                0));
            ESP_LOGI(TAG,
                     "real ESP-NOW port ready; discovery runs in node owner task");
        }
    }

    ESP_LOGI(TAG, "Starting the single-owner controller");
    wakenic_core_run();
}
