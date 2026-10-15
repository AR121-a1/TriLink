#include "trilink_room.h"
#include <stdio.h>
#include <string.h>
#include <stdlib.h>
static unsigned checks, failures;
#define CHECK(x) do { ++checks; if(!(x)) { ++failures; fprintf(stderr,"line %d: %s\n",__LINE__,#x); } } while(0)
static trilink_room_t nodes[7];
static bool links[7][7];
typedef struct { unsigned from; uint8_t bytes[136]; size_t length; } event_t;
static event_t events[8192]; static unsigned head,tail; static uint64_t now;
static event_t last[9]; static unsigned drop_kind;
static bool send_packet(void *context,const uint8_t *wire,size_t length)
{
    if(tail>=8192 || length>136) { CHECK(false); return false; }
    events[tail].from=(unsigned)(uintptr_t)context; events[tail].length=length;
    memcpy(events[tail++].bytes,wire,length); return true;
}
static void changed(void *context) { (void)context; }
static void drain(void)
{
    while(head<tail) {
        event_t event=events[head++];
        if(event.bytes[4]<=ROOM_CONFIRM) last[event.bytes[4]]=event;
        if(event.bytes[4]==drop_kind) continue;
        for(unsigned i=0;i<7;++i) if(links[event.from][i]) trilink_room_input(&nodes[i],event.bytes,event.length,now);
    }
    head=tail=0;
}
static void run(unsigned ms)
{
    for(unsigned elapsed=0;elapsed<ms;elapsed+=100) {
        now+=100;
        for(unsigned i=0;i<7;++i) trilink_room_tick(&nodes[i],now);
        drain();
    }
}
static void init(bool chain)
{
    now=1; head=tail=0; drop_kind=0; memset(last,0,sizeof(last)); memset(links,0,sizeof(links));
    for(unsigned i=0;i<7;++i) {
        uint8_t mac[6]={0x10,0,0,0,0,(uint8_t)(i+1)};
        trilink_room_io_t io={(void *)(uintptr_t)i,send_packet,changed};
        CHECK(trilink_room_init(&nodes[i],mac,100+i,&io));
        for(unsigned j=0;j<7;++j) links[i][j]=i!=j && (!chain || i+1==j || j+1==i);
    }
}
static void create(void)
{
    trilink_room_packet_t command={.kind=ROOM_CREATE}; memcpy(command.name,"Lab",4);
    CHECK(trilink_room_command(&nodes[0],&command,now)); run(1200);
}
static void join(unsigned index)
{
    trilink_room_packet_t cmd=nodes[0].state; cmd.kind=ROOM_JOIN;
    memcpy(cmd.target,nodes[0].local,6);
    CHECK(trilink_room_command(&nodes[index],&cmd,now)); run(300);
    cmd=nodes[0].state; cmd.kind=ROOM_APPROVE; memcpy(cmd.target,nodes[index].local,6);
    CHECK(trilink_room_command(&nodes[0],&cmd,now)); run(1200);
    CHECK(nodes[index].active); CHECK(trilink_room_synchronized(&nodes[0],now));
}
static void codec(void)
{
    init(false); create(); uint8_t bytes[132]; trilink_room_packet_t out;
    CHECK(trilink_room_encode(&nodes[0].state,bytes));
    CHECK(trilink_room_decode(bytes,132,&out)); CHECK(out.count==1);
    for(unsigned i=0;i<132;++i) CHECK(!trilink_room_decode(bytes,i,&out));
    bytes[131]=1; CHECK(!trilink_room_decode(bytes,132,&out)); bytes[131]=0;
    bytes[50]=1; CHECK(!trilink_room_decode(bytes,132,&out)); bytes[50]=0;
    bytes[123]=1; CHECK(!trilink_room_decode(bytes,132,&out));
}
static void membership(bool chain)
{
    init(chain); create(); for(unsigned i=1;i<6;++i) join(i);
    CHECK(nodes[0].state.count==6);
    for(unsigned i=0;i<6;++i) { CHECK(nodes[i].active); CHECK(nodes[i].state.revision==nodes[0].state.revision); }
    trilink_room_packet_t cmd=nodes[0].state; cmd.kind=ROOM_JOIN; memcpy(cmd.target,nodes[0].local,6);
    CHECK(!trilink_room_command(&nodes[6],&cmd,now));
    cmd=nodes[0].state; cmd.kind=ROOM_KICK; memcpy(cmd.target,nodes[2].local,6);
    CHECK(!trilink_room_command(&nodes[1],&cmd,now));
    cmd.kind=ROOM_LEAVE; CHECK(trilink_room_command(&nodes[0],&cmd,now)); run(1500);
    CHECK(!nodes[0].active); CHECK(trilink_room_is_leader(&nodes[1]));
    CHECK(nodes[1].state.count==5 && nodes[1].state.term==2);
    CHECK(trilink_room_synchronized(&nodes[1],now));
}
static void partition_and_recovery(void)
{
    init(true); create(); join(1); join(2);
    links[1][2]=links[2][1]=false; run(4500);
    CHECK(!trilink_room_synchronized(&nodes[0],now));
    trilink_room_packet_t cmd=nodes[0].state; cmd.kind=ROOM_LEAVE;
    CHECK(!trilink_room_command(&nodes[0],&cmd,now));
    CHECK(nodes[2].active && !trilink_room_is_leader(&nodes[2]));
    links[1][2]=links[2][1]=true; run(1500);
    CHECK(trilink_room_synchronized(&nodes[0],now));
    cmd=nodes[2].state; cmd.kind=ROOM_LEAVE; CHECK(trilink_room_command(&nodes[2],&cmd,now)); run(1500);
    CHECK(!nodes[2].active && nodes[0].state.count==2);
    cmd=nodes[0].state; cmd.kind=ROOM_KICK; memcpy(cmd.target,nodes[1].local,6);
    CHECK(trilink_room_command(&nodes[0],&cmd,now)); run(1200);
    CHECK(!nodes[0].active && !nodes[1].active);
}
static void retired_room_before_reuse(void)
{
    for(unsigned attempt=0;attempt<2;++attempt) {
        init(false); create(); join(1); join(2);
        trilink_room_packet_t other={.kind=ROOM_CREATE}; memcpy(other.name,"Other",6);
        CHECK(trilink_room_command(&nodes[6],&other,now)); run(1200);
        trilink_room_packet_t cmd=nodes[0].state; cmd.kind=ROOM_LEAVE;
        CHECK(trilink_room_command(&nodes[0],&cmd,now));
        trilink_room_packet_t retired=nodes[0].state;
        drop_kind=ROOM_SNAPSHOT; run(1200); /* First handover announcements are lost. */
        CHECK(!nodes[0].active && nodes[1].active && !trilink_room_is_leader(&nodes[1]));
        cmd=nodes[6].state; cmd.kind=attempt==0 ? ROOM_CREATE : ROOM_JOIN;
        memcpy(cmd.target,nodes[6].local,6);
        CHECK(!trilink_room_command(&nodes[0],&cmd,now));
        CHECK(!nodes[0].waiting && nodes[0].state.room==retired.room
            && nodes[0].state.revision==retired.revision && nodes[0].state.term==retired.term);
        drop_kind=0; run(1500); /* Keeping the old snapshot permits bounded recovery. */
        CHECK(trilink_room_is_leader(&nodes[1]) && nodes[1].state.count==2);
        CHECK(nodes[2].active && nodes[2].state.revision==nodes[1].state.revision);
        CHECK(trilink_room_synchronized(&nodes[1],now));
        now=nodes[0].retired_until-1; cmd.kind=ROOM_CREATE;
        CHECK(!trilink_room_command(&nodes[0],&cmd,now));
        ++now;
        CHECK(trilink_room_command(&nodes[0],&cmd,now));
        CHECK(trilink_room_is_leader(&nodes[0]) && nodes[0].state.room!=retired.room);
    }
}
static void invite_reject(void)
{
    init(false); create(); join(1);
    trilink_room_packet_t cmd=nodes[1].state; cmd.kind=ROOM_INVITE; memcpy(cmd.target,nodes[2].local,6);
    CHECK(trilink_room_command(&nodes[1],&cmd,now)); drain();
    CHECK(nodes[2].invitations[0].valid && !nodes[2].active);
    cmd=nodes[0].state; cmd.kind=ROOM_JOIN; memcpy(cmd.target,nodes[0].local,6);
    CHECK(trilink_room_command(&nodes[2],&cmd,now)); run(200);
    cmd=nodes[0].state; cmd.kind=ROOM_REJECT; memcpy(cmd.target,nodes[2].local,6);
    CHECK(trilink_room_command(&nodes[0],&cmd,now));
    head=tail=0; /* First DENY is lost: retry must not recreate an approval request. */
    CHECK(nodes[2].waiting); run(1200);
    CHECK(!nodes[2].waiting && !nodes[2].active);
    CHECK(nodes[0].state.count==2);
    cmd.kind=ROOM_APPROVE; CHECK(!trilink_room_command(&nodes[0],&cmd,now));
}
static void expired_applicant(void)
{
    init(false); create();
    trilink_room_packet_t cmd=nodes[0].state; cmd.kind=ROOM_JOIN;
    memcpy(cmd.target,nodes[0].local,6);
    CHECK(trilink_room_command(&nodes[1],&cmd,now)); run(8500);
    CHECK(!nodes[1].waiting && !nodes[1].active);
    cmd=nodes[0].state; cmd.kind=ROOM_APPROVE; memcpy(cmd.target,nodes[1].local,6);
    CHECK(trilink_room_command(&nodes[0],&cmd,now));
    CHECK(nodes[0].state.count==1); /* Approval is an offer, not a membership commit. */
    run(18000);
    CHECK(nodes[0].state.count==1 && !nodes[1].active);
    CHECK(trilink_room_synchronized(&nodes[0],now));
}
static void begin_join(void)
{
    trilink_room_packet_t cmd=nodes[0].state; cmd.kind=ROOM_JOIN;
    memcpy(cmd.target,nodes[0].local,6);
    CHECK(trilink_room_command(&nodes[1],&cmd,now)); run(200);
}
static void approve(void)
{
    trilink_room_packet_t cmd=nodes[0].state; cmd.kind=ROOM_APPROVE;
    memcpy(cmd.target,nodes[1].local,6);
    CHECK(trilink_room_command(&nodes[0],&cmd,now));
}
static void admission_failures(void)
{
    init(false);create();begin_join();drop_kind=ROOM_OFFER;approve();drain();
    CHECK(nodes[0].state.count==1 && !nodes[1].join_confirmed);
    drop_kind=0;run(1800); /* JOIN retry retrieves the same offer. */
    CHECK(nodes[1].active && trilink_room_synchronized(&nodes[0],now));
    event_t confirmation=last[ROOM_CONFIRM];
    trilink_room_input(&nodes[0],confirmation.bytes,confirmation.length,now);
    run(2500);trilink_room_input(&nodes[0],confirmation.bytes,confirmation.length,now);
    CHECK(nodes[0].state.count==2); /* Duplicate even beyond flood cache does not re-add. */

    init(false);create();begin_join();drop_kind=ROOM_CONFIRM;approve();run(18000);
    CHECK(nodes[1].waiting && nodes[1].join_confirmed && nodes[1].attempts==8);
    CHECK(nodes[0].state.count==1 && trilink_room_synchronized(&nodes[0],now));
    trilink_room_packet_t cmd={.kind=ROOM_CREATE};
    CHECK(!trilink_room_command(&nodes[1],&cmd,now));
    drop_kind=0;cmd.kind=ROOM_RETRY;
    CHECK(trilink_room_command(&nodes[1],&cmd,now));run(1500);
    CHECK(!nodes[1].waiting && !nodes[1].active); /* Expired offer: correlated DENY resolves unknown. */
    CHECK(!trilink_room_command(&nodes[1],&cmd,now));

    init(false);create();begin_join();drop_kind=ROOM_SNAPSHOT;approve();run(10000);
    CHECK(nodes[0].state.count==2 && !nodes[1].active && nodes[1].join_confirmed);
    CHECK(nodes[1].attempts==8 && nodes[1].waiting);
    CHECK(!trilink_room_synchronized(&nodes[0],now));
    run(3000);CHECK(nodes[1].attempts==8); /* No endless confirmation timer traffic. */
    drop_kind=0;cmd.kind=ROOM_RETRY;
    CHECK(trilink_room_command(&nodes[1],&cmd,now));run(1500);
    CHECK(nodes[1].active && !nodes[1].waiting && trilink_room_synchronized(&nodes[0],now));

    init(false);create();begin_join();approve();
    now+=9000;drain(); /* Delayed offer before the next local tick must still expire. */
    CHECK(!nodes[1].join_confirmed && nodes[0].state.count==1);
    run(100);CHECK(!nodes[1].waiting);

    init(false);create();begin_join();drop_kind=ROOM_CONFIRM;approve();run(200);
    trilink_room_packet_t wrong;
    CHECK(trilink_room_decode(last[ROOM_CONFIRM].bytes+4,132,&wrong));
    wrong.operation+=10000;wrong.authority[0]^=1;
    uint8_t wire[136]={5,TRILINK_ROOM_OPCODE,132,0};CHECK(trilink_room_encode(&wrong,wire+4));
    trilink_room_input(&nodes[0],wire,sizeof(wire),now);drain();
    CHECK(nodes[0].state.count==1 && nodes[1].join_confirmed); /* Wrong challenge neither commits nor cancels real intent. */
    drop_kind=0;run(1800);CHECK(nodes[1].active && nodes[0].state.count==2);

    init(false);create();begin_join();
    cmd=nodes[0].state;cmd.kind=ROOM_REJECT;memcpy(cmd.target,nodes[1].local,6);
    CHECK(trilink_room_command(&nodes[0],&cmd,now));drain();
    event_t old_denial=last[ROOM_DENY];CHECK(!nodes[1].waiting);
    begin_join();now+=2100;
    trilink_room_input(&nodes[1],old_denial.bytes,old_denial.length,now);
    CHECK(nodes[1].waiting);approve();run(1500);CHECK(nodes[1].active);

    init(false);create();begin_join();
    event_t legacy=last[ROOM_JOIN];legacy.bytes[1]=1;
    memset(nodes[0].requests,0,sizeof(nodes[0].requests));
    trilink_room_input(&nodes[0],legacy.bytes,legacy.length,now+3000);
    CHECK(!nodes[0].requests[0].valid); /* v1 wire is explicitly rejected, not mixed. */
}
static void rejoin_delayed_membership(void)
{
    init(false); create(); join(1); join(2);
    event_t old_membership=last[ROOM_SNAPSHOT];
    uint32_t old_revision=nodes[1].state.revision;
    trilink_room_packet_t cmd=nodes[0].state;
    cmd.kind=ROOM_KICK; memcpy(cmd.target,nodes[1].local,6);
    CHECK(trilink_room_command(&nodes[0],&cmd,now)); run(2400);
    CHECK(!nodes[1].active && nodes[0].state.count==2);
    uint32_t removal_revision=nodes[1].state.revision;
    CHECK(removal_revision>old_revision);
    drop_kind=ROOM_CONFIRM; begin_join(); approve(); run(1200);
    event_t confirmation=last[ROOM_CONFIRM];
    CHECK(nodes[1].waiting && nodes[1].join_confirmed && !nodes[1].active);
    CHECK(nodes[0].state.count==2);
    /* Legitimate old membership outlives the two-second flood dedup window. */
    trilink_room_input(&nodes[1],old_membership.bytes,old_membership.length,now);
    CHECK(!nodes[1].active && nodes[1].waiting && nodes[1].join_confirmed);
    CHECK(nodes[1].state.revision==removal_revision);
    run(1200); /* Current non-member advert must not cancel the pending ticket. */
    CHECK(!nodes[1].active && nodes[1].waiting && nodes[1].join_confirmed);
    trilink_room_input(&nodes[0],confirmation.bytes,confirmation.length,now);
    drop_kind=0; run(2400);
    CHECK(nodes[0].state.count==3 && nodes[1].active && !nodes[1].waiting);
    CHECK(nodes[1].state.revision==nodes[0].state.revision);
    CHECK(trilink_room_synchronized(&nodes[0],now));
    cmd=nodes[0].state; cmd.kind=ROOM_KICK; memcpy(cmd.target,nodes[2].local,6);
    CHECK(trilink_room_command(&nodes[0],&cmd,now)); run(1200);
    CHECK(nodes[0].state.count==2 && nodes[1].active && !nodes[2].active);
    CHECK(trilink_room_synchronized(&nodes[0],now));
}
static void admission_while_membership_advances(void)
{
    init(false); create(); join(2);
    event_t baseline=last[ROOM_SNAPSHOT];
    drop_kind=ROOM_CONFIRM; begin_join(); approve(); run(1200);
    event_t confirmation=last[ROOM_CONFIRM];
    CHECK(nodes[1].waiting && nodes[1].join_confirmed);
    /* Other members may enter before this applicant's confirmation arrives. */
    for(unsigned i=0;i<7;++i) links[1][i]=links[i][1]=false;
    drop_kind=0; join(3);
    event_t newer=last[ROOM_SNAPSHOT];
    trilink_room_input(&nodes[1],newer.bytes,newer.length,now);
    CHECK(nodes[1].waiting && nodes[1].join_confirmed && !nodes[1].active);
    CHECK(nodes[1].join_revision_floor==nodes[0].state.revision);
    trilink_room_input(&nodes[1],baseline.bytes,baseline.length,now);
    CHECK(nodes[1].waiting && nodes[1].join_confirmed && !nodes[1].active);
    CHECK(nodes[1].join_revision_floor==nodes[0].state.revision);
    trilink_room_input(&nodes[0],confirmation.bytes,confirmation.length,now);
    for(unsigned i=0;i<7;++i) if(i!=1) links[1][i]=links[i][1]=true;
    run(2400);
    CHECK(nodes[0].state.count==4 && nodes[1].active && !nodes[1].waiting);
    CHECK(nodes[1].state.revision==nodes[0].state.revision);
    CHECK(trilink_room_synchronized(&nodes[0],now));
}
static void stale_advert_before_rejoin(void)
{
    init(false); create(); join(1); join(2);
    event_t stale=last[ROOM_SNAPSHOT];
    trilink_room_packet_t cmd=nodes[0].state;
    cmd.kind=ROOM_KICK; memcpy(cmd.target,nodes[1].local,6);
    CHECK(trilink_room_command(&nodes[0],&cmd,now)); run(2400);
    trilink_room_input(&nodes[1],stale.bytes,stale.length,now);
    unsigned matching=0;
    for(unsigned i=0;i<6;++i) if(nodes[1].nearby[i].valid
        && nodes[1].nearby[i].packet.room==nodes[0].state.room) {
        ++matching; CHECK(nodes[1].nearby[i].packet.revision==nodes[0].state.revision);
    }
    CHECK(matching==1);
    join(1); /* Discovery keeps the new non-member baseline, not stale membership. */
    CHECK(nodes[1].state.revision==nodes[0].state.revision);
}
int main(void)
{
    codec(); membership(false); membership(true); partition_and_recovery(); retired_room_before_reuse(); invite_reject(); expired_applicant(); admission_failures();
    rejoin_delayed_membership(); admission_while_membership_advances(); stale_advert_before_rejoin();
    init(false);create();join(1);
    trilink_room_packet_t stale=nodes[0].state;join(2);
    stale.kind=ROOM_KICK;memcpy(stale.target,nodes[1].local,6);
    CHECK(!trilink_room_command(&nodes[0],&stale,now));
    CHECK(nodes[0].state.count==3);
    stale.kind=ROOM_LEAVE;memcpy(stale.source,nodes[1].local,6);stale.boot=nodes[1].boot;
    stale.operation=9999;memcpy(stale.target,nodes[0].local,6);
    uint8_t wire[136]={5,TRILINK_ROOM_OPCODE,132,0};CHECK(trilink_room_encode(&stale,wire+4));
    trilink_room_input(&nodes[0],wire,sizeof(wire),now);
    CHECK(nodes[0].state.count==3); /* Delayed previous-revision leave cannot remove a current member. */
    init(false); create();
    trilink_room_packet_t cmd=nodes[0].state;cmd.kind=ROOM_JOIN;memcpy(cmd.target,nodes[0].local,6);
    CHECK(trilink_room_command(&nodes[2],&cmd,now));run(200);
    CHECK(nodes[0].requests[0].valid);
    cmd=nodes[0].state;cmd.kind=ROOM_LEAVE;CHECK(trilink_room_command(&nodes[0],&cmd,now));run(10000);
    create();
    cmd=nodes[0].state;cmd.kind=ROOM_APPROVE;memcpy(cmd.target,nodes[2].local,6);
    CHECK(!trilink_room_command(&nodes[0],&cmd,now)); /* Requests never leak between incarnations. */
    printf("trilink_room host tests: checks=%u failures=%u room_bytes=%u\n",checks,failures,(unsigned)sizeof(trilink_room_t));
    return failures ? 1 : 0;
}
