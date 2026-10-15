#pragma once
#include "esp_err.h"

/* TinyUSB CDC + NCM composite endpoint. Logs remain on UART0.
 * Call from app_main before trilink_node_start(). No Serial/JTAG driver and
 * no second USB PHY owner. CDC never depends on NCM host driver or DTR. */
esp_err_t trilink_usb_start(void);
