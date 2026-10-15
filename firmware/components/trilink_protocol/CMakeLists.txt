idf_component_register(
    SRCS "trilink_protocol.c"
         "trilink_service.c"
         "trilink_content.c"
         "trilink_file_wire.c"
         "trilink_control.c"
         "trilink_route.c"
         "trilink_route_control.c"
         "trilink_room.c"
    INCLUDE_DIRS "include"
)

target_compile_options(${COMPONENT_LIB} PRIVATE
    -Wall -Wextra -Werror -fstack-usage
)
