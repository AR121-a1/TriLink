idf_component_register(
    SRCS "trilink_node.c" "trilink_bridge.c" "trilink_app.c" "trilink_rgb.c"
    INCLUDE_DIRS "include"
    REQUIRES esp_system esp_timer freertos log trilink_cluster trilink_protocol trilink_radio led_strip
)

target_compile_options(${COMPONENT_LIB} PRIVATE
    -Wall -Wextra -Werror -fstack-usage
)
