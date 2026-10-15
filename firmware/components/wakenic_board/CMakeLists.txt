idf_component_register(
    SRCS "wakenic_board.c"
    INCLUDE_DIRS "include"
    REQUIRES esp_psram esp_system log spi_flash
)

target_compile_options(${COMPONENT_LIB} PRIVATE
    -Wall -Wextra -Werror -fstack-usage
)
