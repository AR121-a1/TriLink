#pragma once

#include <stdbool.h>
#include <stdint.h>
#include "esp_err.h"

/* No station association, Wi-Fi bridge, default route, NAT or filesystem. */
esp_err_t trilink_usb_network_prepare(uint8_t device_mac[6]);
esp_err_t trilink_usb_network_bind(void);
void trilink_usb_network_poll(bool mounted);

