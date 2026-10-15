/* In-memory radio + fake LED only; real bridge, Room and route implementations. */
#include "trilink_bridge.h"
#include "trilink_room.h"
#include "trilink_rgb.h"
#include <stdio.h>
#include <string.h>
static unsigned checks,failures,executions,driver_result,peer_status,peer_results;
static bool enabled,driver_busy,drop_results;
static const uint8_t a[6]={0x10,0,0,0,0,1}, b[6]={0x20,0,0,0,0,2};
static trilink_room_t room;
static trilink_route_t route;
static uint64_t now=1;
static char reply[TRILINK_BRIDGE_REPLY_SIZE];
#define CHECK(x) do {++checks;if(!(x)){++failures;fprintf(stderr,"line %d: %s\n",__LINE__,#x);}}while(0)
typedef struct { bool from_a; size_t length; uint8_t wire[TRILINK_PAYLOAD_MAX]; } event_t;
static event_t events[1024]; static unsigned head,tail;
bool trilink_rgb_init(void) {return true;}
bool trilink_rgb_enable(bool value) {if(driver_busy)return false;enabled=value;return true;}
bool trilink_rgb_enabled(void) {return enabled;}
bool trilink_rgb_faulted(void) {return false;}
bool trilink_rgb_begin(uint8_t r,uint8_t g,uint8_t blue)
{if(!enabled || driver_busy || r>32 || g>32 || blue>32)return false;++executions;driver_busy=true;return true;}
unsigned trilink_rgb_result(void)
{unsigned result=driver_result;if(result){driver_result=0;driver_busy=false;}return result;}
static bool enqueue(bool from_a,const uint8_t *wire,size_t length)
{
    if(tail>=1024 || length>TRILINK_PAYLOAD_MAX){CHECK(false);return false;}
    events[tail].from_a=from_a; events[tail].length=length;memcpy(events[tail++].wire,wire,length);return true;
}
esp_err_t trilink_node_queue_payload(const uint8_t next[6],const uint8_t *wire,size_t length,bool ack)
{(void)next;(void)ack;return enqueue(true,wire,length)?ESP_OK:ESP_ERR_NO_MEM;}
static bool room_send(void *ctx,const uint8_t *wire,size_t length)
{(void)ctx;return enqueue(false,wire,length);}
static bool route_send(void *ctx,const uint8_t next[6],const uint8_t *wire,size_t length,bool ack)
{(void)ctx;(void)next;(void)ack;return enqueue(false,wire,length);}
static bool route_receive(void *ctx,const trilink_route_view_t *packet)
{
    (void)ctx;
    if(packet->length!=19 || packet->data[0]!=0xA6) return false;
    if(packet->data[2]==2) {peer_status=packet->data[3];++peer_results;return true;}
    /* The peer deliberately acknowledges only transport for outgoing timeout tests. */
    return packet->data[2]==1;
}
static void route_complete(void *ctx,uint32_t id,trilink_route_result_t result)
{(void)ctx;(void)id;(void)result;}
static void room_changed(void *ctx)
{
    (void)ctx;
    trilink_route_io_t io={NULL,route_send,route_receive,route_complete};
    CHECK(trilink_route_init(&route,b,202,&io));
    if(room.active) {
        trilink_route_config_t config={.room=room.state.room,.epoch=room.state.revision,.count=room.state.count};
        for(unsigned i=0;i<config.count;++i)memcpy(config.members[i],room.state.members[i].mac,6);
        CHECK(trilink_route_configure(&route,&config));trilink_route_neighbors(&route,a,1);
    }
}
static void drain(void)
{
    while(head<tail) {
        event_t event=events[head++];
        if(event.from_a) {
            if(event.wire[0]==TRILINK_ROOM_SERVICE)trilink_room_input(&room,event.wire,event.length,now);
            else {
                trilink_route_view_t p;
                if(drop_results && trilink_route_decode(event.wire,event.length,&p) && p.kind==TRILINK_ROUTE_DATA) continue;
                trilink_route_input(&route,a,event.wire,event.length,now);
            }
        } else trilink_bridge_input(b,event.wire,event.length,now);
    }
    head=tail=0;
}
static void run(unsigned ms)
{
    for(unsigned elapsed=0;elapsed<ms;elapsed+=100) {
        now+=100;trilink_bridge_process(now);trilink_room_tick(&room,now);trilink_route_tick(&route,now);drain();
    }
}
static void request(const char *line)
{
    trilink_route_parser_t parser={0};trilink_route_command_t command={0};
    for(unsigned i=0;line[i];++i)(void)trilink_route_control_feed(&parser,(uint8_t)line[i],&command);
    CHECK(command.kind!=TRILINK_ROUTE_CMD_BAD && command.kind!=TRILINK_ROUTE_CMD_LEGACY);
    CHECK(trilink_bridge_submit(&command));run(100);CHECK(trilink_bridge_reply(reply));
}
static void room_command(trilink_room_packet_t *packet)
{
    uint8_t bytes[132];char hex[265],line[320];CHECK(trilink_room_encode(packet,bytes));
    trilink_route_hex_encode(bytes,132,hex);snprintf(line,sizeof(line),"TRILINK/3 ROOM t %s\n",hex);request(line);
}
static void put32(uint8_t *p,uint32_t value){for(unsigned i=0;i<4;++i)p[i]=(uint8_t)(value>>(8*i));}
static void send_rgb(uint32_t operation,uint8_t red)
{
    uint8_t payload[19]={0xA6,1,1,0};uint32_t id;
    put32(payload+4,room.state.incarnation);put32(payload+8,operation);put32(payload+12,room.state.revision);
    payload[16]=red;payload[17]=8;payload[18]=8;
    CHECK(trilink_route_submit(&route,a,payload,sizeof(payload),now,&id));run(300);
}
static void send_result(unsigned status)
{
    uint8_t payload[19]={0xA6,1,2,(uint8_t)status};uint32_t id;
    put32(payload+4,room.state.incarnation);put32(payload+8,2);put32(payload+12,room.state.revision);
    CHECK(trilink_route_submit(&route,a,payload,sizeof(payload),now,&id));run(300);
}
int main(void)
{
    CHECK(trilink_bridge_init(a,101)==ESP_OK);trilink_bridge_neighbors(b,1);
    trilink_room_io_t io={NULL,room_send,room_changed};CHECK(trilink_room_init(&room,b,202,&io));room_changed(NULL);
    trilink_room_packet_t cmd={.kind=ROOM_CREATE};memcpy(cmd.name,"Lab",4);room_command(&cmd);
    CHECK(strstr(reply," OK t ROOM")!=NULL);run(1200);
    CHECK(room.nearby[0].valid);
    cmd=room.nearby[0].packet;cmd.kind=ROOM_JOIN;memcpy(cmd.target,a,6);
    CHECK(trilink_room_command(&room,&cmd,now));run(300);
    request("TRILINK/3 ROOMGET t 07\n");CHECK(strstr(reply,"ROOMSTATE t 07 ")!=NULL);
    cmd.kind=ROOM_APPROVE;memcpy(cmd.target,b,6);room_command(&cmd);CHECK(strstr(reply," OK t ROOM")!=NULL);run(1300);
    CHECK(room.active && room.state.count==2);
    request("TRILINK/3 CAPS active\n");
    CHECK(!strcmp(reply,"\nTRILINK/3 CAPS active 1 0.16.0 00000065 84 06 06 01\n"));
    CHECK(!enabled && executions==0U && room.active && room.state.count==2U);
    request("TRILINK/3 CONFIG t 00000001 00000001 100000000001\n");CHECK(strstr(reply," ERROR ")!=NULL);
    request("TRILINK/3 ROOMGET t 00\n");CHECK(strstr(reply,"ROOMSTATE t 00 ")!=NULL);
    send_rgb(10,0);CHECK(executions==0 && peer_status==4); /* default disabled */
    request("TRILINK/3 RGBENABLE t 1\n");CHECK(strstr(reply," OK ")!=NULL);
    send_rgb(11,0);CHECK(executions==1 && peer_status==1 && driver_busy);
    request("TRILINK/3 RGBENABLE t 0\n");CHECK(strstr(reply," ERROR ")!=NULL);
    send_rgb(11,0);CHECK(executions==1); /* Same app identity, fresh route identity. */
    driver_result=1;run(400);CHECK(peer_status==2 && !driver_busy);
    send_rgb(11,0);CHECK(executions==1 && peer_status==2);
    unsigned previous_results=peer_results;
    send_rgb(11,1);CHECK(executions==1 && peer_results==previous_results); /* Conflicting body rejected. */
    run(5500); /* Let the rejected conflicting transport request time out. */
    send_rgb(12,0);CHECK(executions==2 && peer_status==1);
    driver_result=2;run(400);CHECK(peer_status==3); /* Driver failure is not completion. */
    request("TRILINK/3 RGB t 100000000001 000808\n");CHECK(strstr(reply," QUEUED ")!=NULL);
    run(200);request("TRILINK/3 RGBRESULT t\n");CHECK(strstr(reply,"00000001 1")!=NULL);
    trilink_route_parser_t pressure_parser={0};trilink_route_command_t pressure={0};
    const char *pressure_line="TRILINK/3 INFO pressure\n";
    for(unsigned i=0;pressure_line[i];++i)(void)trilink_route_control_feed(&pressure_parser,pressure_line[i],&pressure);
    for(unsigned i=0;i<4U;++i){CHECK(trilink_bridge_submit(&pressure));run(100);}
    driver_result=1;run(200);CHECK(!driver_busy); /* Module tick runs with all USB replies unread. */
    for(unsigned i=0;i<4U;++i)CHECK(trilink_bridge_reply(reply));
    CHECK(!trilink_bridge_reply(reply));
    request("TRILINK/3 RGBRESULT t\n");CHECK(strstr(reply,"00000001 2")!=NULL);
    request("TRILINK/3 RGB t 200000000002 000808\n");CHECK(strstr(reply," QUEUED ")!=NULL);
    run(10500);request("TRILINK/3 RGBRESULT t\n");CHECK(strstr(reply,"00000002 5")!=NULL); /* Transport ACK != execution. */
    request("TRILINK/3 RGB t 200000000002 210808\n");CHECK(strstr(reply," ERROR ")!=NULL);
    send_rgb(11,0);CHECK(executions==3 && !driver_busy); /* Old app request after another sender's job must not replay. */
    run(5500);
    send_result(1);request("TRILINK/3 RGBRESULT t\n");CHECK(strstr(reply,"00000002 5")!=NULL);
    send_result(2);request("TRILINK/3 RGBRESULT t\n");CHECK(strstr(reply,"00000002 2")!=NULL);
    send_result(3);request("TRILINK/3 RGBRESULT t\n");CHECK(strstr(reply,"00000002 2")!=NULL);
    printf("trilink_room_rgb integration tests: checks=%u failures=%u executions=%u (fake driver/radio)\n",checks,failures,executions);
    return failures?1:0;
}
