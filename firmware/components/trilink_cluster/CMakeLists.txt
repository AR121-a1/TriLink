idf_component_register(
    SRCS "trilink_cluster.c"
    INCLUDE_DIRS "include"
    REQUIRES trilink_protocol
)

target_compile_options(${COMPONENT_LIB} PRIVATE
    -Wall -Wextra -Werror -fstack-usage
)
