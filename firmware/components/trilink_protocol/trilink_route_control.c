#include "trilink_route_control.h"
#include <string.h>

static int nibble(char c)
{
    if (c >= '0' && c <= '9') return c - '0';
    if (c >= 'A' && c <= 'F') return c - 'A' + 10;
    if (c >= 'a' && c <= 'f') return c - 'a' + 10;
    return -1;
}

bool trilink_route_hex_decode(const char *text, uint8_t *output, size_t length)
{
    if (text == NULL || output == NULL || strlen(text) != length * 2U) return false;
    for (size_t i = 0; i < length; ++i) {
        int hi = nibble(text[i*2U]), lo = nibble(text[i*2U+1U]);
        if (hi < 0 || lo < 0) return false;
        output[i] = (uint8_t)((hi << 4) | lo);
    }
    return true;
}

void trilink_route_hex_encode(const uint8_t *data, size_t length, char *text)
{
    static const char digits[] = "0123456789ABCDEF";
    for (size_t i = 0; i < length; ++i) {
        text[i*2U] = digits[data[i] >> 4U];
        text[i*2U+1U] = digits[data[i] & 15U];
    }
    text[length*2U] = '\0';
}

static bool number(const char *text, uint32_t *value)
{
    uint8_t bytes[4];
    if (!trilink_route_hex_decode(text, bytes, 4U)) return false;
    *value = (uint32_t)bytes[0] << 24U | (uint32_t)bytes[1] << 16U
        | (uint32_t)bytes[2] << 8U | bytes[3];
    return *value != 0U;
}

static void parse(char *line, trilink_route_command_t *cmd)
{
    char *tokens[11];
    unsigned count = 0;
    char *next = line;
    while (next != NULL && count < 11U) {
        tokens[count++] = next;
        char *space = strchr(next, ' ');
        if (space != NULL) *space++ = '\0';
        next = space;
    }
    if (next != NULL || count < 3U || strcmp(tokens[0], "TRILINK/3")) return;
    const size_t nonce_size = strlen(tokens[2]);
    if (nonce_size == 0U || nonce_size >= sizeof(cmd->nonce)) return;
    for (size_t i = 0; i < nonce_size; ++i) {
        char c = tokens[2][i];
        if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'z')
            || (c >= 'A' && c <= 'Z') || c == '-' || c == '_')) return;
    }
    memcpy(cmd->nonce, tokens[2], nonce_size+1U);
    if (count == 3U && strcmp(tokens[1], "INFO") == 0) cmd->kind = TRILINK_ROUTE_CMD_INFO;
    else if (count == 3U && strcmp(tokens[1], "CAPS") == 0) cmd->kind = TRILINK_ROUTE_CMD_CAPS;
    else if (count == 3U && strcmp(tokens[1], "TABLE") == 0) cmd->kind = TRILINK_ROUTE_CMD_TABLE;
    else if (count == 3U && strcmp(tokens[1], "POLL") == 0) cmd->kind = TRILINK_ROUTE_CMD_POLL;
    else if (count == 3U && strcmp(tokens[1], "RGBRESULT") == 0) cmd->kind = TRILINK_ROUTE_CMD_RGB_RESULT;
    else if (count == 4U && strcmp(tokens[1], "ROOM") == 0) {
        if(!trilink_route_hex_decode(tokens[3],cmd->data,132U)) return;
        cmd->length=132U; cmd->kind=TRILINK_ROUTE_CMD_ROOM;
    }
    else if (count == 4U && strcmp(tokens[1], "ROOMGET") == 0) {
        if(strlen(tokens[3])!=2 || !trilink_route_hex_decode(tokens[3],cmd->data,1U) || cmd->data[0]>18U) return;
        cmd->kind=TRILINK_ROUTE_CMD_ROOM_GET;
    }
    else if (count == 4U && strcmp(tokens[1], "RGBENABLE") == 0
             && (!strcmp(tokens[3],"0") || !strcmp(tokens[3],"1"))) {
        cmd->relay_enabled=tokens[3][0]=='1'; cmd->kind=TRILINK_ROUTE_CMD_RGB_ENABLE;
    }
    else if (count == 5U && strcmp(tokens[1], "RGB") == 0) {
        if(!trilink_route_hex_decode(tokens[3],cmd->destination,6U)
            || !trilink_route_hex_decode(tokens[4],cmd->data,3U)) return;
        cmd->length=3U; cmd->kind=TRILINK_ROUTE_CMD_RGB;
    }
    else if (count == 6U && strcmp(tokens[1], "CONSUME") == 0) {
        if (!trilink_route_hex_decode(tokens[3], cmd->destination, 6U)
            || !number(tokens[4], &cmd->boot) || !number(tokens[5], &cmd->message)) return;
        cmd->kind = TRILINK_ROUTE_CMD_CONSUME;
    }
    else if (count == 4U && strcmp(tokens[1], "RELAY") == 0
             && (!strcmp(tokens[3], "0") || !strcmp(tokens[3], "1"))) {
        cmd->kind = TRILINK_ROUTE_CMD_RELAY;
        cmd->relay_enabled = tokens[3][0] == '1';
    } else if (count >= 6U && count <= 11U && strcmp(tokens[1], "CONFIG") == 0) {
        if (!number(tokens[3], &cmd->config.room) || !number(tokens[4], &cmd->config.epoch)) return;
        cmd->config.count = (uint8_t)(count - 5U);
        for (unsigned i = 0; i < cmd->config.count; ++i)
            if (!trilink_route_hex_decode(tokens[5U+i], cmd->config.members[i], 6U)) return;
        cmd->kind = TRILINK_ROUTE_CMD_CONFIG;
    } else if (count == 5U && strcmp(tokens[1], "SEND") == 0) {
        const size_t len = strlen(tokens[4]);
        if (len == 0U || len % 2U != 0U || len > TRILINK_ROUTE_DATA_MAX * 2U
            || !trilink_route_hex_decode(tokens[3], cmd->destination, 6U)
            || !trilink_route_hex_decode(tokens[4], cmd->data, len / 2U)) return;
        cmd->length = len / 2U;
        cmd->kind = TRILINK_ROUTE_CMD_SEND;
    }
}

bool trilink_route_control_feed(trilink_route_parser_t *parser, uint8_t byte,
                                trilink_route_command_t *cmd)
{
    if (parser == NULL || cmd == NULL) return false;
    if (byte == '\n') {
        bool complete = parser->discard || parser->length != 0U;
        memset(cmd, 0, sizeof(*cmd));
        if (complete && (!strncmp(parser->line, "TRILINK/3", 9U)
            || !strncmp(parser->line, "TRILINK/2", 9U))) {
            cmd->kind = TRILINK_ROUTE_CMD_BAD;
            if (!parser->discard) {
                parser->line[parser->length] = '\0';
                parse(parser->line, cmd);
            }
        }
        memset(parser, 0, sizeof(*parser));
        return complete;
    }
    if (parser->discard) return false;
    if (byte == '\r' && !parser->carriage_return) {
        parser->carriage_return = true;
        return false;
    }
    if (parser->carriage_return || byte < 32U || byte > 126U
        || parser->length >= sizeof(parser->line)-1U) {
        parser->discard = true;
        return false;
    }
    parser->line[parser->length++] = (char)byte;
    return false;
}
