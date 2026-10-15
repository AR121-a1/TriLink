idf_component_register(
    SRCS "app_main.c"
    INCLUDE_DIRS "."
    REQUIRES esp_app_format esp_system trilink_node trilink_usb wakenic_board wakenic_core
)

target_compile_options(${COMPONENT_LIB} PRIVATE
    -Wall -Wextra -Werror -fstack-usage
)
