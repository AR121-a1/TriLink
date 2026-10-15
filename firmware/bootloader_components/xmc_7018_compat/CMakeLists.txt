idf_component_register(
    SRCS "xmc_7018_compat.c"
    REQUIRES bootloader_support spi_flash
    WHOLE_ARCHIVE
)

target_link_libraries(${COMPONENT_LIB} INTERFACE
    "-Wl,--wrap=bootloader_flash_xmc_startup"
)
