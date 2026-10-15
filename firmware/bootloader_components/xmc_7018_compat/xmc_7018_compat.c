/*
 * Provenance: exact narrow workaround used by the successfully booted
 * esp32s3_usb_espnow_bridge boards.  It accepts only the observed
 * XM25QH128AHIG JEDEC ID and preserves every other bootloader error.
 */
#include "esp_attr.h"
#include "esp_err.h"
#include "esp_log.h"
#include "esp_rom_spiflash.h"

#define XMC_XM25QH128AHIG_JEDEC_ID 0x207018U

extern esp_err_t __real_bootloader_flash_xmc_startup(void);

esp_err_t IRAM_ATTR __wrap_bootloader_flash_xmc_startup(void)
{
    const esp_err_t error = __real_bootloader_flash_xmc_startup();
    if (error == ESP_OK) {
        return ESP_OK;
    }

    if (g_rom_flashchip.device_id == XMC_XM25QH128AHIG_JEDEC_ID) {
        ESP_EARLY_LOGW("xmc_7018",
                       "accepting verified XMC JEDEC ID 20 70 18");
        return ESP_OK;
    }

    return error;
}
