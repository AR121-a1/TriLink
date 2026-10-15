idf_component_register(
    SRCS "trilink_usb.c" "trilink_usb_network.c" "trilink_usb_mux.c"
    INCLUDE_DIRS "include"
    REQUIRES esp_timer esp_netif esp_event esp_hw_support lwip freertos log trilink_node trilink_protocol trilink_web esp_tinyusb
)
target_compile_options(${COMPONENT_LIB} PRIVATE -Wall -Wextra -Werror -fstack-usage)
