#include "trilink_control.h"

#include <inttypes.h>
#include <stdarg.h>
#include <stdio.h>
#include <string.h>

static bool nonce_valid(const char *nonce)
{
    if (nonce == NULL) {
        return false;
    }
    for (size_t i = 0; i <= TRILINK_CONTROL_NONCE_MAX; ++i) {
        const unsigned char byte = (unsigned char)nonce[i];
        if (byte == 0U) {
            return i != 0U;
        }
        if ((byte < 33U) || (byte > 126U)) {
            return false;
        }
    }
    return false;
}

static void parse_line(char *line, trilink_control_command_t *command)
{
    const char *nonce = NULL;
    if (strncmp(line, "TRILINK/1 HELLO ", 16U) == 0) {
        command->kind = TRILINK_CONTROL_HELLO;
        nonce = line + 16U;
    } else if (strncmp(line, "TRILINK/1 SEARCH ", 17U) == 0) {
        command->kind = TRILINK_CONTROL_SEARCH;
        nonce = line + 17U;
    }
    if (!nonce_valid(nonce)) {
        command->kind = TRILINK_CONTROL_INVALID;
        return;
    }
    memcpy(command->nonce, nonce, strlen(nonce) + 1U);
}

bool trilink_control_feed(trilink_control_parser_t *parser, uint8_t byte,
                          trilink_control_command_t *command)
{
    if ((parser == NULL) || (command == NULL)) {
        return false;
    }
    if (byte == '\n') {
        const bool complete = parser->discard || (parser->length != 0U);
        memset(command, 0, sizeof(*command));
        if (complete) {
            command->kind = TRILINK_CONTROL_INVALID;
            if (!parser->discard) {
                parser->line[parser->length] = '\0';
                parse_line(parser->line, command);
            }
        }
        memset(parser, 0, sizeof(*parser));
        return complete;
    }
    if (parser->discard) {
        return false;
    }
    if ((byte == '\r') && !parser->carriage_return) {
        parser->carriage_return = true;
        return false;
    }
    if (parser->carriage_return || (byte < 32U) || (byte > 126U)
        || (parser->length >= sizeof(parser->line) - 1U)) {
        parser->discard = true;
        return false;
    }
    parser->line[parser->length++] = (char)byte;
    return false;
}

static bool mac_valid(const uint8_t *mac)
{
    unsigned aggregate = 0;
    for (size_t i = 0; i < TRILINK_MAC_LEN; ++i) {
        aggregate |= mac[i];
    }
    return ((mac[0] & 1U) == 0U) && (aggregate != 0U);
}

static void identity(const uint8_t *mac, char mac_text[18], char name_token[29])
{
    static const char alphabet[] =
        "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789+/";
    char name[21];
    (void)snprintf(mac_text, 18U, "%02X:%02X:%02X:%02X:%02X:%02X",
                   mac[0], mac[1], mac[2], mac[3], mac[4], mac[5]);
    (void)snprintf(name, sizeof(name), "TriLink-%02X%02X%02X%02X%02X%02X",
                   mac[0], mac[1], mac[2], mac[3], mac[4], mac[5]);
    /* 20 ASCII bytes -> 28 Base64 bytes, one '=' padding byte. */
    size_t out = 0U;
    for (size_t i = 0; i < 20U; i += 3U) {
        const uint32_t a = (uint8_t)name[i];
        const uint32_t b = (i + 1U < 20U) ? (uint8_t)name[i + 1U] : 0U;
        const uint32_t c = (i + 2U < 20U) ? (uint8_t)name[i + 2U] : 0U;
        const uint32_t value = (a << 16U) | (b << 8U) | c;
        name_token[out++] = alphabet[(value >> 18U) & 63U];
        name_token[out++] = alphabet[(value >> 12U) & 63U];
        name_token[out++] = (i + 1U < 20U) ? alphabet[(value >> 6U) & 63U] : '=';
        name_token[out++] = (i + 2U < 20U) ? alphabet[value & 63U] : '=';
    }
    name_token[out] = '\0';
}

static bool append(char *output, size_t capacity, size_t *used,
                    const char *format, ...)
{
    if (*used >= capacity) {
        return false;
    }
    va_list args;
    va_start(args, format);
    const int result = vsnprintf(output + *used, capacity - *used, format, args);
    va_end(args);
    if ((result < 0) || ((size_t)result >= capacity - *used)) {
        return false;
    }
    *used += (size_t)result;
    return true;
}

bool trilink_control_reply(const trilink_control_command_t *command,
                           const trilink_discovery_snapshot_t *snapshot,
                           uint64_t now_ms, char *output, size_t capacity,
                           size_t *output_len)
{
    if (output_len != NULL) {
        *output_len = 0U;
    }
    if ((command == NULL) || (snapshot == NULL) || (output == NULL)
        || (output_len == NULL) || (capacity == 0U)) {
        return false;
    }
    output[0] = '\0';
    size_t used = 0U;
    char mac[18], name[29];
    bool ok = false;
    if (((command->kind != TRILINK_CONTROL_HELLO)
         && (command->kind != TRILINK_CONTROL_SEARCH))
        || !nonce_valid(command->nonce)) {
        ok = append(output, capacity, &used, "\nTRILINK/1 ERROR - BAD_REQUEST\n");
    } else if (!snapshot->ready) {
        ok = append(output, capacity, &used, "\nTRILINK/1 ERROR %s NOT_READY\n",
                     command->nonce);
    } else if (!mac_valid(snapshot->local_mac)
               || (snapshot->peer_count > TRILINK_DISCOVERY_PEERS)) {
        return false;
    } else if (command->kind == TRILINK_CONTROL_HELLO) {
        identity(snapshot->local_mac, mac, name);
        ok = append(output, capacity, &used,
                     "\nTRILINK/1 DEVICE %s %s %s %08" PRIX32 "\n",
                     command->nonce, mac, name, snapshot->capabilities);
    } else {
        ok = append(output, capacity, &used, "\n");
        for (size_t i = 0; ok && (i < snapshot->peer_count); ++i) {
            const trilink_discovery_peer_t *peer = &snapshot->peers[i];
            if (!mac_valid(peer->mac) || (peer->rssi < -127) || (peer->rssi > 0)
                || (memcmp(peer->mac, snapshot->local_mac, TRILINK_MAC_LEN) == 0)
                || (now_ms < peer->last_seen_ms)
                || (now_ms - peer->last_seen_ms >= TRILINK_DISCOVERY_TTL_MS)) {
                continue;
            }
            identity(peer->mac, mac, name);
            ok = append(output, capacity, &used,
                         "TRILINK/1 PEER %s %s %d - - - 1\n",
                         mac, name, peer->rssi);
        }
        ok = ok && append(output, capacity, &used, "TRILINK/1 END %s\n",
                           command->nonce);
    }
    if (!ok) {
        output[0] = '\0';
        return false;
    }
    *output_len = used;
    return true;
}
