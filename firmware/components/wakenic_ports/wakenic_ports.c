#include "wakenic_ports.h"

#include "esp_log.h"
#include "wakenic_core.h"

static const char *TAG = "wakenic_ports";

esp_err_t wakenic_ports_register_usb_stub(void)
{
    ESP_LOGI(TAG,
             "usb backend=stub: native USB remains unconfigured and reserved");
    return wakenic_core_post(WAKENIC_EVENT_USB_PORT_REGISTERED, 0U, 0);
}
