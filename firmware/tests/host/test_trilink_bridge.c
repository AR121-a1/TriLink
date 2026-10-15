#include "trilink_bridge.h"
#include "trilink_rgb.h"
bool trilink_rgb_init(void) { return true; }
bool trilink_rgb_enable(bool enabled) { (void)enabled; return true; }
bool trilink_rgb_enabled(void) { return false; }
bool trilink_rgb_faulted(void) { return false; }
bool trilink_rgb_begin(uint8_t r,uint8_t g,uint8_t b) { (void)r;(void)g;(void)b;return false; }
unsigned trilink_rgb_result(void) { return 0; }
#include <stdio.h>
#include <string.h>

static unsigned checks, failures, ack_count;
#define CHECK(x) do { ++checks; if (!(x)) { ++failures; \
    fprintf(stderr, "FAIL line %d: %s\n", __LINE__, #x); } } while (0)
static const uint8_t local[6] = {0x30,0,0,0,0,3};
static const uint8_t relay[6] = {0x20,0,0,0,0,2};
static char reply[TRILINK_BRIDGE_REPLY_SIZE];

esp_err_t trilink_node_queue_payload(const uint8_t next[6], const uint8_t *payload,
                                    size_t length, bool acknowledgement)
{
    trilink_route_view_t m;
    CHECK(trilink_route_decode(payload, length, &m));
    if (m.kind == TRILINK_ROUTE_TOPOLOGY) { CHECK(next[0] == 255U); return ESP_OK; }
    CHECK(acknowledgement && m.kind == TRILINK_ROUTE_ACK);
    CHECK(!memcmp(next, relay, 6U));
    ++ack_count;
    return ESP_OK;
}

static trilink_route_command_t parse(const char *line)
{
    trilink_route_parser_t parser = {0};
    trilink_route_command_t command = {0};
    for (size_t i = 0; line[i]; ++i) (void)trilink_route_control_feed(&parser, line[i], &command);
    return command;
}

static void request(const char *line)
{
    trilink_route_command_t command = parse(line);
    CHECK(trilink_bridge_submit(&command));
    trilink_bridge_process(100);
    CHECK(trilink_bridge_reply(reply));
}

static void input(uint32_t message, uint64_t now)
{
    const uint8_t data[] = {0x00, 0x0a, 0xff};
    trilink_route_view_t m = {.room=1, .epoch=1, .boot=100, .message=message,
        .source={0x10,0,0,0,0,1}, .destination={0x30,0,0,0,0,3},
        .remaining=3, .visited=3, .kind=TRILINK_ROUTE_DATA, .length=3, .data=data};
    uint8_t wire[TRILINK_PAYLOAD_MAX];
    size_t length;
    CHECK(trilink_route_encode(&m, wire, sizeof(wire), &length));
    trilink_bridge_input(relay, wire, length, now);
}

static void topology(void)
{
    trilink_bridge_neighbors(relay, 1U);
    uint8_t roster[37] = {3};
    const uint8_t members[3][6] = {{0x10,0,0,0,0,1},{0x20,0,0,0,0,2},{0x30,0,0,0,0,3}};
    memcpy(roster+1U, members, sizeof(members));
    uint32_t digest=trilink_crc32(roster,sizeof(roster));
    uint8_t body[6]; for(unsigned i=0;i<4U;++i) body[i]=(uint8_t)(digest>>(8U*i));
    body[5]=1U;
    for(unsigned i=0;i<2U;++i) {
        body[4]=i==0U ? 2U : 5U;
        trilink_route_view_t m={.room=1,.epoch=1,.boot=100U+i,.message=1,
            .remaining=(uint8_t)(i==0U?3U:4U),.visited=(uint8_t)(i==0U?3U:2U),
            .kind=TRILINK_ROUTE_TOPOLOGY,.length=6,.data=body};
        memcpy(m.source,members[i],6U);memcpy(m.destination,members[i],6U);
        uint8_t wire[TRILINK_PAYLOAD_MAX];size_t length;
        CHECK(trilink_route_encode(&m,wire,sizeof(wire),&length));
        trilink_bridge_input(relay,wire,length,100);
    }
}

int main(int argc, char **argv)
{
    CHECK(trilink_bridge_init(local, 300) == ESP_OK);
    request("TRILINK/3 CAPS cap\n");
    CHECK(!strcmp(reply,"\nTRILINK/3 CAPS cap 1 0.16.0 0000012C 84 06 06 01\n"));
    CHECK(ack_count==0U); /* Describe is read-only and available before Room/config. */
    if(argc==2 && !strcmp(argv[1],"--caps-fixture")) {fputs(reply,stdout);return failures?1:0;}
    request("TRILINK/3 CAPS cap extra\n");
    CHECK(strstr(reply," ERROR cap ")!=NULL);
    request("TRILINK/3 INFO initial\n");
    CHECK(strstr(reply,"configured=0")!=NULL && strstr(reply,"pending=00000000")!=NULL);
    request("TRILINK/3 CONFIG c 00000001 00000001 100000000001 200000000002 300000000003\n");
    CHECK(strstr(reply, " OK c CONFIG") != NULL);
    topology();
    request("TRILINK/3 CAPS cap\n");
    CHECK(!strcmp(reply,"\nTRILINK/3 CAPS cap 1 0.16.0 0000012C 84 06 06 01\n"));
    request("TRILINK/3 TABLE t\n");
    CHECK(strstr(reply, "count=3 local=3 neighbors=02") != NULL);
    CHECK(strstr(reply, "routes=2/2,2/1,0/0") != NULL);
    request("TRILINK/3 INFO i\n");
    CHECK(strstr(reply, "configured=1") != NULL && strstr(reply, "position=3") != NULL);
    input(1, 0);
    CHECK(ack_count == 1U);
    request("TRILINK/3 POLL p\n");
    CHECK(strstr(reply, " DATA p 100000000001 00000064 00000001 000AFF") != NULL);
    char original[TRILINK_BRIDGE_REPLY_SIZE];
    memcpy(original, reply, sizeof(original));
    request("TRILINK/3 POLL p\n");
    CHECK(!strcmp(original, reply));
    request("TRILINK/3 CONSUME c 100000000001 00000064 00000002\n");
    CHECK(strstr(reply, " ERROR ") != NULL);
    request("TRILINK/3 POLL p\n");
    CHECK(!strcmp(original, reply));
    request("TRILINK/3 CONSUME c 100000000001 00000064 00000001\n");
    CHECK(strstr(reply, " OK ") != NULL);
    request("TRILINK/3 CONSUME c 100000000001 00000064 00000001\n");
    CHECK(strstr(reply, " OK ") != NULL); /* lost consume response is retryable */
    request("TRILINK/3 POLL p\n");
    CHECK(strstr(reply, " EMPTY p") != NULL);
    input(1, 200);
    CHECK(ack_count == 2U);
    request("TRILINK/3 POLL p\n");
    CHECK(strstr(reply, " EMPTY p") != NULL); /* duplicate gets ACK, not another delivery */
    for (unsigned i = 2; i <= 10U; ++i) input(i, 300U+i);
    CHECK(ack_count == 10U); /* ninth queued message cannot be acknowledged */
    request("TRILINK/3 INFO i\n");
    CHECK(strstr(reply, "inbox=8") != NULL && strstr(reply, "full=1") != NULL);
    request("TRILINK/3 CONFIG c 00000001 00000002 100000000001 200000000002 300000000003\n");
    CHECK(strstr(reply, " ERROR ") != NULL); /* unread inbox prevents changing membership epoch */
    request("TRILINK/3 RELAY r 0\n");
    CHECK(strstr(reply, " OK ") != NULL); /* every member can toggle transit relay */
    trilink_route_command_t command = parse("TRILINK/3 INFO q\n");
    for (unsigned i = 0; i < 4U; ++i) CHECK(trilink_bridge_submit(&command));
    CHECK(!trilink_bridge_submit(&command));
    for (unsigned i = 0; i < 4U; ++i) trilink_bridge_process(1000);
    CHECK(trilink_bridge_submit(&command));
    trilink_bridge_process(1000); /* reply backpressure, must not lose the command */
    for (unsigned i = 0; i < 4U; ++i) CHECK(trilink_bridge_reply(reply));
    CHECK(!trilink_bridge_reply(reply));
    trilink_bridge_process(1000);
    CHECK(trilink_bridge_reply(reply));
    printf("trilink_bridge host tests: checks=%u failures=%u\n", checks, failures);
    return failures ? 1 : 0;
}
