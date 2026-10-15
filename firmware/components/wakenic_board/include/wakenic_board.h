#pragma once

#include "esp_err.h"

#ifdef __cplusplus
extern "C" {
#endif

#define WAKENIC_BOARD_NAME       "HW-678A"
#define WAKENIC_MODULE_NAME      "ESP32-S3-WROOM-1-N16R8"
#define WAKENIC_FLASH_SIZE_MIB   16U
#define WAKENIC_PSRAM_SIZE_MIB   8U

void wakenic_board_log_identity(void);

esp_err_t wakenic_board_validate(void);

#ifdef __cplusplus
}
#endif

