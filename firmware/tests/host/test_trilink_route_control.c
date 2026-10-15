#include "trilink_route_control.h"
#include <stdio.h>
#include <string.h>

static unsigned checks, failures;
#define CHECK(x) do { ++checks; if (!(x)) { ++failures; \
    fprintf(stderr, "FAIL line %d: %s\n", __LINE__, #x); } } while (0)

static trilink_route_command_t parse(const char *text)
{
    trilink_route_parser_t p = {0};
    trilink_route_command_t c = {0};
    for (size_t i = 0; text[i]; ++i) (void)trilink_route_control_feed(&p, (uint8_t)text[i], &c);
    return c;
}

int main(void)
{
    trilink_route_command_t c = parse("TRILINK/3 INFO abc\r\n");
    CHECK(c.kind == TRILINK_ROUTE_CMD_INFO && !strcmp(c.nonce, "abc"));
    c = parse("TRILINK/3 CAPS cap_1-2\r\n");
    CHECK(c.kind == TRILINK_ROUTE_CMD_CAPS && !strcmp(c.nonce, "cap_1-2"));
    c = parse("TRILINK/1 HELLO abc\n");
    CHECK(c.kind == TRILINK_ROUTE_CMD_LEGACY);
    c = parse("TRILINK/3 CONFIG n 12345678 AABBCCDD 100000000001 200000000002 300000000003\n");
    CHECK(c.kind == TRILINK_ROUTE_CMD_CONFIG);
    CHECK(c.config.room == 0x12345678U && c.config.epoch == 0xAABBCCDDU);
    CHECK(c.config.members[2][5] == 3U);
    CHECK(c.config.count == 3U);
    c = parse("TRILINK/3 CONFIG n 12345678 AABBCCDD 100000000001 200000000002 300000000003 400000000004 500000000005 600000000006\n");
    CHECK(c.kind == TRILINK_ROUTE_CMD_CONFIG && c.config.count == 6U && c.config.members[5][5] == 6U);
    c = parse("TRILINK/3 CONFIG n 12345678 AABBCCDD 100000000001\n");
    CHECK(c.kind == TRILINK_ROUTE_CMD_CONFIG && c.config.count == 1U);
    CHECK(parse("TRILINK/3 CONFIG n 12345678 AABBCCDD 100000000001 200000000002 300000000003 400000000004 500000000005 600000000006 700000000007\n").kind == TRILINK_ROUTE_CMD_BAD);
    CHECK(parse("TRILINK/2 INFO old\n").kind == TRILINK_ROUTE_CMD_BAD);
    CHECK(parse("TRILINK/3 TABLE t\n").kind == TRILINK_ROUTE_CMD_TABLE);
    c = parse("TRILINK/3 SEND n 300000000003 000A0Dff\n");
    CHECK(c.kind == TRILINK_ROUTE_CMD_SEND && c.length == 4U);
    CHECK(c.data[0] == 0 && c.data[1] == 10 && c.data[2] == 13 && c.data[3] == 255);
    c = parse("TRILINK/3 RELAY n 0\n");
    CHECK(c.kind == TRILINK_ROUTE_CMD_RELAY && !c.relay_enabled);
    c = parse("TRILINK/3 RELAY n 1\n");
    CHECK(c.kind == TRILINK_ROUTE_CMD_RELAY && c.relay_enabled);
    c = parse("TRILINK/3 POLL n\n");
    CHECK(c.kind == TRILINK_ROUTE_CMD_POLL);
    c = parse("TRILINK/3 CONSUME n 100000000001 ABCDEF12 00000005\n");
    CHECK(c.kind == TRILINK_ROUTE_CMD_CONSUME && c.boot == 0xABCDEF12U && c.message == 5U);
    const char *bad[] = {
        "TRILINK/3 INFO\n", "TRILINK/3 INFO \n", "TRILINK/3 INFO a extra\n",
        "TRILINK/3 CAPS\n", "TRILINK/3 CAPS \n", "TRILINK/3 CAPS c extra\n",
        "TRILINK/3 CAPS c 1\n", "TRILINK/3 CAPS c/x\n", "TRILINK/2 CAPS c\n",
        "TRILINK/3 CAPS 123456789012345678901234567890123\n",
        "TRILINK/3 SEND n 300000000003 0\n", "TRILINK/3 SEND n 300000000003 gg\n",
        "TRILINK/3 SEND n 300000000003 \n", "TRILINK/3 SEND n 300 00\n",
        "TRILINK/3 RELAY n 2\n", "TRILINK/3 INFO n\rb\n",
        "TRILINK/3 INFO n\r\r\n", "TRILINK/3 INFO a\tb\n",
        "TRILINK/3 INFO n\xFF\n", "TRILINK/3 CONFIG n 00000000 00000001 100000000001 200000000002 300000000003\n",
        "TRILINK/3 CONFIG n 00000001 00000001 100000000001 200000000002 300000000003 extra\n",
        "TRILINK/3 INFO 123456789012345678901234567890123\n",
        "TRILINK/3 CONSUME n 100000000001 00000000 00000001\n",
        "TRILINK/3 CONSUME n 100000000001 00000001 00000000\n",
    };
    for (unsigned i = 0; i < sizeof(bad)/sizeof(bad[0]); ++i) CHECK(parse(bad[i]).kind == TRILINK_ROUTE_CMD_BAD);
    char line[512], hex[TRILINK_ROUTE_DATA_MAX*2U+1U];
    uint8_t data[TRILINK_ROUTE_DATA_MAX];
    for (unsigned i = 0; i < sizeof(data); ++i) data[i] = (uint8_t)i;
    trilink_route_hex_encode(data, sizeof(data), hex);
    (void)snprintf(line, sizeof(line), "TRILINK/3 SEND 12345678901234567890123456789012 300000000003 %s\n", hex);
    c = parse(line);
    CHECK(c.kind == TRILINK_ROUTE_CMD_SEND && c.length == sizeof(data));
    CHECK(!memcmp(c.data, data, sizeof(data)));
    (void)snprintf(line,sizeof(line),"TRILINK/3 ROOM r %s\n",hex);
    c=parse(line); CHECK(c.kind==TRILINK_ROUTE_CMD_ROOM && c.length==132);
    for(unsigned i=0;i<=18;++i) {
        (void)snprintf(line,sizeof(line),"TRILINK/3 ROOMGET r %02X\n",i);
        c=parse(line); CHECK(c.kind==TRILINK_ROUTE_CMD_ROOM_GET && c.data[0]==i);
    }
    CHECK(parse("TRILINK/3 ROOMGET r 13\n").kind==TRILINK_ROUTE_CMD_BAD);
    CHECK(parse("TRILINK/3 ROOMGET r 0\n").kind==TRILINK_ROUTE_CMD_BAD);
    CHECK(parse("TRILINK/3 ROOM r 00\n").kind==TRILINK_ROUTE_CMD_BAD);
    CHECK(parse("TRILINK/3 RGBENABLE r 1\n").kind==TRILINK_ROUTE_CMD_RGB_ENABLE);
    CHECK(parse("TRILINK/3 RGBENABLE r 2\n").kind==TRILINK_ROUTE_CMD_BAD);
    CHECK(parse("TRILINK/3 RGBRESULT r\n").kind==TRILINK_ROUTE_CMD_RGB_RESULT);
    c=parse("TRILINK/3 RGB r 300000000003 000808\n");
    CHECK(c.kind==TRILINK_ROUTE_CMD_RGB && c.length==3 && c.data[2]==8);
    CHECK(parse("TRILINK/3 RGB r 300000000003 0008\n").kind==TRILINK_ROUTE_CMD_BAD);
    CHECK(parse("TRILINK/3 RGB r 300000000003 00080800\n").kind==TRILINK_ROUTE_CMD_BAD);
    trilink_route_parser_t parser = {0};
    const char *prefix = "TRILINK/3 INFO ";
    for (unsigned i = 0; prefix[i]; ++i) (void)trilink_route_control_feed(&parser, prefix[i], &c);
    for (unsigned i = 0; i < 1024U; ++i) CHECK(!trilink_route_control_feed(&parser, 'A', &c));
    CHECK(trilink_route_control_feed(&parser, '\n', &c) && c.kind == TRILINK_ROUTE_CMD_BAD);
    CHECK(parser.length == 0U && !parser.discard);
    const char *next = "TRILINK/3 INFO recovered\n";
    for (unsigned i = 0; next[i]; ++i) (void)trilink_route_control_feed(&parser, next[i], &c);
    CHECK(c.kind == TRILINK_ROUTE_CMD_INFO && !strcmp(c.nonce, "recovered"));
    const char *caps_recovered = "TRILINK/3 CAPS recovered\n";
    for (unsigned i=0;caps_recovered[i];++i) (void)trilink_route_control_feed(&parser,caps_recovered[i],&c);
    CHECK(c.kind == TRILINK_ROUTE_CMD_CAPS && !strcmp(c.nonce,"recovered"));
    printf("trilink_route_control host tests: checks=%u failures=%u\n", checks, failures);
    return failures ? 1 : 0;
}
