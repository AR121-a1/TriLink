idf_component_register(
    SRCS "wakenic_ports.c"
    INCLUDE_DIRS "include"
    REQUIRES log wakenic_core
)

target_compile_options(${COMPONENT_LIB} PRIVATE
    -Wall -Wextra -Werror -fstack-usage
)
