#include "trilink_control.h"
#include <stdio.h>
#include <string.h>

static unsigned checks, failures;
#define CHECK(x) do { ++checks; if (!(x)) { ++failures; \
    fprintf(stderr, "FAIL line %d: %s\n", __LINE__, #x); } } while (0)

static trilink_control_command_t parse(const char *text)
{
    trilink_control_parser_t parser = {0};
    trilink_control_command_t command = {0};
    for (size_t i = 0; text[i] != '\0'; ++i) {
        (void)trilink_control_feed(&parser, (uint8_t)text[i], &command);
    }
    return command;
}

static trilink_discovery_snapshot_t fixture(void)
{
    trilink_discovery_snapshot_t snapshot = {
        .ready = true, .local_mac = {0x10, 0, 0, 0, 0, 1},
        .capabilities = 3U, .peer_count = 6U,
    };
    for (size_t i = 0; i < 6U; ++i) {
        snapshot.peers[i].mac[0] = 0x20U;
        snapshot.peers[i].mac[5] = (uint8_t)(i + 2U);
        snapshot.peers[i].rssi = (int8_t)(-42 - (int)i);
        snapshot.peers[i].last_seen_ms = 100U;
    }
    return snapshot;
}

static size_t occurrences(const char *text, const char *needle)
{
    size_t count = 0;
    while ((text = strstr(text, needle)) != NULL) {
        ++count;
        text += strlen(needle);
    }
    return count;
}

static void parser_tests(void)
{
    trilink_control_command_t cmd = parse("TRILINK/1 HELLO 1234abcd\n");
    CHECK(cmd.kind == TRILINK_CONTROL_HELLO);
    CHECK(strcmp(cmd.nonce, "1234abcd") == 0);
    cmd = parse("TRILINK/1 SEARCH x\r\n");
    CHECK(cmd.kind == TRILINK_CONTROL_SEARCH);
    CHECK(strcmp(cmd.nonce, "x") == 0);
    const char *bad[] = {
        "TRILINK/1 HELLO \n", "TRILINK/1 SEARCH\n", "HELLO a\n",
        "TRILINK/2 HELLO a\n", "TRILINK/1 JOIN x\n", "TRILINK/1 HELLO a b\n",
        "TRILINK/1 HELLO a\tb\n", "TRILINK/1 HELLO a\rb\n",
        "TRILINK/1 HELLO a\r\r\n", "TRILINK/1 HELLO \xFF\n",
        "TRILINK/1 HELLO 123456789012345678901234567890123\n",
    };
    for (size_t i = 0; i < sizeof(bad) / sizeof(bad[0]); ++i) {
        cmd = parse(bad[i]);
        CHECK(cmd.kind == TRILINK_CONTROL_INVALID);
        CHECK(cmd.nonce[0] == '\0');
    }
    cmd = parse("TRILINK/1 HELLO 12345678901234567890123456789012\n");
    CHECK(cmd.kind == TRILINK_CONTROL_HELLO);
    CHECK(strlen(cmd.nonce) == 32U);
    trilink_control_parser_t parser = {0};
    CHECK(!trilink_control_feed(NULL, 'a', &cmd));
    CHECK(!trilink_control_feed(&parser, 'a', NULL));
    CHECK(!trilink_control_feed(&parser, '\n', &cmd));
    bool premature_line = false;
    for (size_t i = 0; i < 1024U; ++i) {
        premature_line |= trilink_control_feed(&parser, 'x', &cmd);
    }
    CHECK(!premature_line);
    CHECK(trilink_control_feed(&parser, '\n', &cmd));
    CHECK(cmd.kind == TRILINK_CONTROL_INVALID);
    CHECK(parser.length == 0U && !parser.discard);
    const char *two = "TRILINK/1 HELLO a\nTRILINK/1 SEARCH b\n";
    unsigned lines = 0;
    for (size_t i = 0; i < strlen(two); ++i) {
        if (trilink_control_feed(&parser, (uint8_t)two[i], &cmd)) {
            ++lines;
            CHECK(cmd.kind == (lines == 1U ? TRILINK_CONTROL_HELLO : TRILINK_CONTROL_SEARCH));
        }
    }
    CHECK(lines == 2U);
    CHECK(!trilink_control_feed(&parser, 0U, &cmd));
    CHECK(trilink_control_feed(&parser, '\n', &cmd));
    CHECK(cmd.kind == TRILINK_CONTROL_INVALID);
}

static void reply_tests(void)
{
    trilink_discovery_snapshot_t snap = fixture();
    trilink_control_command_t cmd = parse("TRILINK/1 HELLO 1234abcd\n");
    char reply[TRILINK_CONTROL_REPLY_SIZE];
    size_t len = 0;
    CHECK(trilink_control_reply(&cmd, &snap, 100U, reply, sizeof(reply), &len));
    CHECK(strcmp(reply, "\nTRILINK/1 DEVICE 1234abcd 10:00:00:00:00:01 VHJpTGluay0xMDAwMDAwMDAwMDE= 00000003\n") == 0);
    CHECK(len == strlen(reply));
    cmd = parse("TRILINK/1 SEARCH 12345678901234567890123456789012\n");
    for (size_t i = 0; i < 6U; ++i) snap.peers[i].rssi = -127;
    CHECK(trilink_control_reply(&cmd, &snap, 2099U, reply, sizeof(reply), &len));
    CHECK(occurrences(reply, "TRILINK/1 PEER ") == 6U);
    CHECK(occurrences(reply, "TRILINK/1 END ") == 1U);
    CHECK(len < 512U);
    const size_t needed = len + 1U;
    CHECK(!trilink_control_reply(&cmd, &snap, 2099U, reply, needed - 1U, &len));
    CHECK(len == 0U && reply[0] == '\0');
    CHECK(trilink_control_reply(&cmd, &snap, 2099U, reply, needed, &len));
    CHECK(trilink_control_reply(&cmd, &snap, 2100U, reply, sizeof(reply), &len));
    CHECK(occurrences(reply, " PEER ") == 0U);
    CHECK(occurrences(reply, " END ") == 1U);
    CHECK(trilink_control_reply(&cmd, &snap, 99U, reply, sizeof(reply), &len));
    CHECK(occurrences(reply, " PEER ") == 0U);
    snap.peers[0].mac[0] = 1U;
    memset(snap.peers[1].mac, 0, 6U);
    memcpy(snap.peers[2].mac, snap.local_mac, 6U);
    snap.peers[3].rssi = 1;
    snap.peers[4].rssi = -128;
    CHECK(trilink_control_reply(&cmd, &snap, 100U, reply, sizeof(reply), &len));
    CHECK(occurrences(reply, " PEER ") == 1U);
    snap.peer_count = 7;
    CHECK(!trilink_control_reply(&cmd, &snap, 100U, reply, sizeof(reply), &len));
    snap.peer_count = 0;
    CHECK(trilink_control_reply(&cmd, &snap, 100U, reply, sizeof(reply), &len));
    CHECK(occurrences(reply, " PEER ") == 0U);
    snap.ready = false;
    CHECK(trilink_control_reply(&cmd, &snap, 100U, reply, sizeof(reply), &len));
    CHECK(strstr(reply, "NOT_READY\n") != NULL);
    cmd.kind = TRILINK_CONTROL_INVALID;
    CHECK(trilink_control_reply(&cmd, &snap, 100U, reply, sizeof(reply), &len));
    CHECK(strstr(reply, "ERROR - BAD_REQUEST\n") != NULL);
    CHECK(!trilink_control_reply(NULL, &snap, 100U, reply, sizeof(reply), &len));
    CHECK(!trilink_control_reply(&cmd, NULL, 100U, reply, sizeof(reply), &len));
    CHECK(!trilink_control_reply(&cmd, &snap, 100U, NULL, sizeof(reply), &len));
    CHECK(!trilink_control_reply(&cmd, &snap, 100U, reply, 0U, &len));
    CHECK(!trilink_control_reply(&cmd, &snap, 100U, reply, sizeof(reply), NULL));
}

int main(int argc, char **argv)
{
    if (argc == 2 && strcmp(argv[1], "--fixture") == 0) {
        trilink_discovery_snapshot_t snap = fixture();
        const char *commands[] = {"TRILINK/1 HELLO 1234abcd\n", "TRILINK/1 SEARCH 1234abcd\n"};
        for (size_t i = 0; i < 2U; ++i) {
            trilink_control_command_t cmd = parse(commands[i]);
            char output[TRILINK_CONTROL_REPLY_SIZE];
            size_t length;
            if (!trilink_control_reply(&cmd, &snap, 100U, output, sizeof(output), &length)) return 1;
            fputs(output, stdout);
        }
        return 0;
    }
    parser_tests();
    reply_tests();
    printf("trilink_control host tests: checks=%u failures=%u\n", checks, failures);
    return failures == 0U ? 0 : 1;
}
