idf_component_register(
    SRCS "trilink_web.c" "trilink_web_policy.c"
    INCLUDE_DIRS "include"
    REQUIRES esp_http_server esp_timer esp_hw_support freertos lwip trilink_protocol
    EMBED_TXTFILES "www/index.html" "www/app.js" "www/app.css"
)
target_compile_options(${COMPONENT_LIB} PRIVATE -Wall -Wextra -Werror -fstack-usage)
