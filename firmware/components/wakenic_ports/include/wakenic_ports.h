#pragma once

#include "esp_err.h"

#ifdef __cplusplus
extern "C" {
#endif

/* Native USB is still reserved. Wi-Fi is now owned by trilink_radio. */
esp_err_t wakenic_ports_register_usb_stub(void);

#ifdef __cplusplus
}
#endif
