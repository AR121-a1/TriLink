#pragma once
#include "trilink_route.h"

#define TRILINK_ROUTE_LINE_SIZE 384U
#define TRILINK_ROUTE_NONCE_SIZE 33U
typedef enum {
    TRILINK_ROUTE_CMD_LEGACY = 0, TRILINK_ROUTE_CMD_BAD,
    TRILINK_ROUTE_CMD_INFO, TRILINK_ROUTE_CMD_CONFIG,
    TRILINK_ROUTE_CMD_SEND, TRILINK_ROUTE_CMD_POLL, TRILINK_ROUTE_CMD_RELAY,
    TRILINK_ROUTE_CMD_CONSUME,
    TRILINK_ROUTE_CMD_TABLE,
    TRILINK_ROUTE_CMD_ROOM, TRILINK_ROUTE_CMD_ROOM_GET,
    TRILINK_ROUTE_CMD_RGB_ENABLE, TRILINK_ROUTE_CMD_RGB, TRILINK_ROUTE_CMD_RGB_RESULT,
    TRILINK_ROUTE_CMD_CAPS,
} trilink_route_command_kind_t;
typedef struct {
    trilink_route_command_kind_t kind;
    char nonce[TRILINK_ROUTE_NONCE_SIZE];
    trilink_route_config_t config;
    uint8_t destination[6], data[TRILINK_ROUTE_DATA_MAX];
    size_t length;
    uint32_t boot, message;
    bool relay_enabled;
} trilink_route_command_t;
typedef struct {
    char line[TRILINK_ROUTE_LINE_SIZE];
    size_t length;
    bool discard, carriage_return;
} trilink_route_parser_t;

bool trilink_route_control_feed(trilink_route_parser_t *parser, uint8_t byte,
                                trilink_route_command_t *command);
bool trilink_route_hex_decode(const char *text, uint8_t *output, size_t length);
void trilink_route_hex_encode(const uint8_t *data, size_t length, char *text);
