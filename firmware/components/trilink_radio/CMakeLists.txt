idf_component_register(
    SRCS "trilink_radio.c"
    INCLUDE_DIRS "include"
    REQUIRES esp_event esp_netif esp_wifi freertos log nvs_flash trilink_protocol
)

target_compile_options(${COMPONENT_LIB} PRIVATE
    -Wall -Wextra -Werror -fstack-usage
)
