#include "wakenic_board.h"

#include <inttypes.h>

#include "esp_flash.h"
#include "esp_log.h"
#include "esp_mac.h"
#include "esp_psram.h"
#include "esp_system.h"
#include "sdkconfig.h"

#if !defined(CONFIG_IDF_TARGET_ESP32S3)
#error "WakeNIC-P2P skeleton supports only ESP32-S3"
#endif

#if !defined(CONFIG_ESPTOOLPY_FLASHSIZE_16MB)
#error "HW-678A N16R8 baseline requires 16 MB flash configuration"
#endif

#if !defined(CONFIG_ESPTOOLPY_FLASHMODE_DIO)
#error "The locally verified HW-678A baseline uses DIO flash mode"
#endif

#if !defined(CONFIG_SPIRAM) || !defined(CONFIG_SPIRAM_MODE_OCT) \
    || !defined(CONFIG_SPIRAM_SPEED_80M)
#error "HW-678A N16R8 baseline requires 80 MHz Octal PSRAM"
#endif

static const char *TAG = "wakenic_board";

void wakenic_board_log_identity(void)
{
    uint8_t station_mac[6] = {0};
    const esp_err_t mac_result = esp_read_mac(station_mac, ESP_MAC_WIFI_STA);

    ESP_LOGI(TAG,
             "board=%s module=%s flash=%uMiB psram=%uMiB reset_reason=%d",
             WAKENIC_BOARD_NAME,
             WAKENIC_MODULE_NAME,
             WAKENIC_FLASH_SIZE_MIB,
             WAKENIC_PSRAM_SIZE_MIB,
             (int)esp_reset_reason());
    ESP_LOGI(TAG,
             "debug=CH343/UART0 native_usb=reserved wifi_mac_status=%s",
             esp_err_to_name(mac_result));
    if (mac_result == ESP_OK) {
        ESP_LOGI(TAG, "wifi_sta_mac=" MACSTR, MAC2STR(station_mac));
    }
}

esp_err_t wakenic_board_validate(void)
{
    uint32_t flash_size = 0U;
    const esp_err_t flash_result =
        esp_flash_get_physical_size(NULL, &flash_size);
    if (flash_result != ESP_OK) {
        return flash_result;
    }

    const size_t psram_size = esp_psram_get_size();
    const uint32_t expected_flash =
        WAKENIC_FLASH_SIZE_MIB * 1024U * 1024U;
    const size_t expected_psram =
        (size_t)WAKENIC_PSRAM_SIZE_MIB * 1024U * 1024U;

    ESP_LOGI(TAG,
             "detected_flash=%" PRIu32 " detected_psram=%u",
             flash_size,
             (unsigned)psram_size);

    if ((flash_size != expected_flash) || (psram_size != expected_psram)) {
        ESP_LOGE(TAG,
                 "capacity mismatch expected_flash=%" PRIu32
                 " expected_psram=%u",
                 expected_flash,
                 (unsigned)expected_psram);
        return ESP_ERR_INVALID_STATE;
    }

    return ESP_OK;
}
