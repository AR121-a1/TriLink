idf_component_register(
    SRCS "wakenic_core.c" "wakenic_state.c"
    INCLUDE_DIRS "include"
    REQUIRES esp_timer freertos heap log
)

target_compile_options(${COMPONENT_LIB} PRIVATE
    -Wall -Wextra -Werror -fstack-usage
)
