#include "trilink_route.h"
#include <stdio.h>
#include <string.h>

static unsigned checks, failures;
#define CHECK(x) do { ++checks; if (!(x)) { ++failures; \
    fprintf(stderr, "FAIL line %d: %s\n", __LINE__, #x); } } while (0)
typedef struct {
    unsigned from, to; size_t size; uint8_t wire[TRILINK_PAYLOAD_MAX];
} packet_t;
typedef struct {
    trilink_route_t route;
    unsigned index, receipts, completions, timeouts;
    uint8_t data[TRILINK_ROUTE_DATA_MAX]; size_t length;
} node_t;
static node_t nodes[6];
static packet_t queue[4096], last_data;
static unsigned head, tail, count, data_hops;
static bool radio[6][6], drop_ack, duplicate, fail_send, fail_receive;
static uint64_t clock_ms;
static const uint8_t macs[6][6] = {
    {0x10,0,0,0,0,1},{0x20,0,0,0,0,2},{0x30,0,0,0,0,3},
    {0x40,0,0,0,0,4},{0x50,0,0,0,0,5},{0x60,0,0,0,0,6}
};
static bool enqueue(unsigned from, unsigned to, const uint8_t *wire, size_t size)
{
    if (!radio[from][to]) return true; /* queued frame lost on physical link */
    if (tail >= 4096U) { CHECK(false); return false; }
    packet_t *p = &queue[tail++];
    p->from = from; p->to = to; p->size = size;
    memcpy(p->wire, wire, size);
    return true;
}
static bool send_packet(void *context, const uint8_t next[6], const uint8_t *wire,
                         size_t size, bool acknowledgement)
{
    node_t *node = context;
    if (fail_send) return false;
    trilink_route_view_t m;
    CHECK(trilink_route_decode(wire, size, &m));
    CHECK(size <= TRILINK_PAYLOAD_MAX);
    if (acknowledgement && drop_ack) return true;
    if (next[0] == 255U) {
        CHECK(m.kind == TRILINK_ROUTE_TOPOLOGY);
        for (unsigned i = 0; i < count; ++i)
            if (i != node->index && !enqueue(node->index, i, wire, size)) return false;
        return true;
    }
    int to = -1;
    for (unsigned i = 0; i < count; ++i) if (!memcmp(next, macs[i], 6U)) to = (int)i;
    CHECK(to >= 0 && (unsigned)to != node->index);
    if (to < 0) return false;
    ++data_hops;
    bool ok = enqueue(node->index, (unsigned)to, wire, size);
    return ok && (!duplicate || enqueue(node->index, (unsigned)to, wire, size));
}
static bool receive_data(void *context, const trilink_route_view_t *m)
{
    node_t *node = context;
    if (fail_receive) return false;
    ++node->receipts; node->length = m->length;
    memcpy(node->data, m->data, m->length);
    return true;
}
static void completed(void *context, uint32_t message, trilink_route_result_t result)
{
    node_t *node = context; CHECK(message != 0U);
    if (result == TRILINK_ROUTE_ACCEPTED) ++node->completions;
    else ++node->timeouts;
}
static void reset(unsigned members)
{
    count = members; clock_ms = 0U; head = tail = data_hops = 0U;
    drop_ack = duplicate = fail_send = fail_receive = false;
    memset(nodes, 0, sizeof(nodes)); memset(radio, 0, sizeof(radio));
    trilink_route_config_t config = {.room=0x12345678U, .epoch=7U, .count=(uint8_t)members};
    for (unsigned i = 0; i < members; ++i) memcpy(config.members[i], macs[members-i-1U], 6U);
    for (unsigned i = 0; i < members; ++i) {
        nodes[i].index = i;
        trilink_route_io_t io = {&nodes[i], send_packet, receive_data, completed};
        CHECK(trilink_route_init(&nodes[i].route, macs[i], 100U+i, &io));
        CHECK(trilink_route_configure(&nodes[i].route, &config));
        CHECK(nodes[i].route.position == i); /* unordered roster canonicalized */
    }
}
static void edge(unsigned a, unsigned b, bool connected) { radio[a][b] = radio[b][a] = connected; }
static void pump(void)
{
    unsigned guard = 0U;
    while (head < tail && guard++ < 4096U) {
        packet_t p = queue[head++];
        trilink_route_view_t m;
        if (trilink_route_decode(p.wire, p.size, &m) && m.kind == TRILINK_ROUTE_DATA
            && !memcmp(m.destination, macs[p.to], 6U)) last_data = p;
        trilink_route_input(&nodes[p.to].route, macs[p.from], p.wire, p.size, clock_ms);
    }
    CHECK(head == tail); head = tail = 0U;
}
static void refresh_neighbors(void)
{
    for (unsigned i = 0; i < count; ++i) {
        uint8_t peers[6][6]; unsigned size = 0;
        for (unsigned j = 0; j < count; ++j) if (radio[j][i]) memcpy(peers[size++], macs[j], 6U);
        trilink_route_neighbors(&nodes[i].route, &peers[0][0], size);
    }
}
static void advance(unsigned milliseconds)
{
    unsigned steps = (milliseconds + 99U) / 100U;
    for (unsigned step = 0; step < steps; ++step) {
        clock_ms += 100U; refresh_neighbors();
        for (unsigned i = 0; i < count; ++i) trilink_route_tick(&nodes[i].route, clock_ms);
        pump();
    }
}
static uint32_t submit(unsigned from, unsigned to)
{
    const uint8_t data[] = {0,10,13,255,0x55};
    uint32_t message = 0U;
    CHECK(trilink_route_submit(&nodes[from].route, macs[to], data, sizeof(data), clock_ms, &message));
    CHECK(message != 0U);
    return message;
}
static void codec_tests(void)
{
    uint8_t data[TRILINK_ROUTE_DATA_MAX], wire[TRILINK_PAYLOAD_MAX];
    memset(data, 0xa5, sizeof(data));
    trilink_route_view_t m = {.room=1,.epoch=2,.boot=3,.message=4,
        .source={0x10,0,0,0,0,1},.destination={0x20,0,0,0,0,2},
        .remaining=4,.visited=1,.kind=TRILINK_ROUTE_DATA,.length=sizeof(data),.data=data};
    size_t size;
    CHECK(TRILINK_ROUTE_DATA_MAX == 132U);
    CHECK(trilink_route_encode(&m, wire, sizeof(wire), &size) && size == TRILINK_PAYLOAD_MAX);
    trilink_route_view_t decoded;
    CHECK(trilink_route_decode(wire, size, &decoded));
    CHECK(decoded.data == wire+40U && !memcmp(decoded.data, data, sizeof(data)));
    trilink_frame_view_t frame = {.type=TRILINK_MSG_APP_DATA,.group_id=1,.boot_id=2,
        .sequence=3,.payload_len=(uint16_t)size,.payload=wire};
    uint8_t outer[TRILINK_FRAME_MAX_SIZE]; size_t outer_size;
    CHECK(trilink_frame_encode_view(&frame, outer, sizeof(outer), &outer_size) && outer_size == 200U);
    CHECK(trilink_frame_decode_view(outer, outer_size, &frame) == TRILINK_DECODE_OK);
    outer[70] ^= 1U;
    CHECK(trilink_frame_decode_view(outer, outer_size, &frame) == TRILINK_DECODE_BAD_CRC);
    for (size_t i = 0; i < size; ++i) CHECK(!trilink_route_decode(wire, i, &decoded));
    wire[1] = 1U; CHECK(!trilink_route_decode(wire, size, &decoded)); wire[1] = 2U;
    wire[38] = 1U; CHECK(!trilink_route_decode(wire, size, &decoded)); wire[38] = 0U;
    CHECK(!trilink_route_encode(&m, wire, size-1U, &size));
    m.remaining=5U; CHECK(!trilink_route_encode(&m, wire, sizeof(wire), &size));
    m.remaining=4U; m.visited=0U; CHECK(!trilink_route_encode(&m, wire, sizeof(wire), &size));
    m.visited=0x81U; CHECK(!trilink_route_encode(&m, wire, sizeof(wire), &size));
    m.visited=1U; m.attempt=4U; CHECK(!trilink_route_encode(&m, wire, sizeof(wire), &size));
    m.attempt=0U; m.kind=TRILINK_ROUTE_ACK;
    CHECK(!trilink_route_encode(&m, wire, sizeof(wire), &size));
    m.length=0U; m.data=NULL;
    CHECK(trilink_route_encode(&m, wire, sizeof(wire), &size));
    CHECK(!trilink_route_decode(NULL, size, &decoded));
}
static void transport_tests(void)
{
    reset(3); edge(0,1,true); edge(1,2,true); advance(1500);
    uint8_t next[6], hops;
    CHECK(trilink_route_lookup(&nodes[0].route, macs[2], clock_ms, next, &hops)
        && hops == 2U && !memcmp(next, macs[1], 6U));
    unsigned before = data_hops;
    submit(0,2); CHECK(nodes[0].completions == 0U); pump();
    CHECK(data_hops-before == 4U && nodes[1].route.forwarded == 2U);
    CHECK(nodes[2].receipts == 1U && nodes[0].completions == 1U);
    submit(2,0); pump(); CHECK(nodes[0].receipts == 1U && nodes[2].completions == 1U);
    trilink_route_relay(&nodes[1].route, false); advance(1300);
    CHECK(!trilink_route_lookup(&nodes[0].route, macs[2], clock_ms, next, &hops));
    submit(0,2); advance(4900);
    CHECK(nodes[0].timeouts == 1U && nodes[2].receipts == 1U);
    trilink_route_relay(&nodes[1].route, true); advance(1500);
    submit(0,2); pump(); CHECK(nodes[0].completions == 2U && nodes[2].receipts == 2U);
    /* A relay-disabled node can still be an endpoint. */
    trilink_route_relay(&nodes[0].route, false); advance(1200);
    submit(2,0); pump(); CHECK(nodes[2].completions == 2U);

    reset(6);
    /* Middle identity 0 or 5 is no different from any other relay. */
    const unsigned order[6] = {2,5,0,4,1,3};
    for (unsigned i=0; i<5U; ++i) edge(order[i],order[i+1U],true);
    advance(1500);
    CHECK(trilink_route_lookup(&nodes[2].route, macs[3], clock_ms, next, &hops) && hops == 5U);
    submit(2,3); pump(); submit(3,2); pump();
    CHECK(nodes[2].completions == 1U && nodes[3].completions == 1U && data_hops == 20U);
    for (unsigned i=0; i<count; ++i) if (i != 2U && i != 3U) CHECK(nodes[i].route.forwarded == 4U);

    reset(3); edge(0,1,true); edge(1,2,true); advance(1500);
    drop_ack=true; submit(0,2); pump();
    CHECK(nodes[2].receipts == 1U && nodes[0].route.pending);
    drop_ack=false; advance(1200);
    CHECK(nodes[2].receipts == 1U && nodes[2].route.duplicates == 1U && nodes[0].completions == 1U);
    /* Lost downstream DATA must pass the relay again in a new retry round. */
    reset(3); edge(0,1,true); edge(1,2,true); advance(1500);
    submit(0,2);
    radio[1][2]=false; pump(); radio[1][2]=true; advance(1200);
    CHECK(nodes[2].receipts == 1U && nodes[0].completions == 1U);
}
static void multipath_tests(void)
{
    reset(4); edge(0,1,true); edge(1,3,true); edge(0,2,true); edge(2,3,true); advance(1500);
    drop_ack=true; submit(0,3); pump();
    packet_t saved = last_data;
    trilink_route_view_t m;
    CHECK(trilink_route_decode(saved.wire, saved.size, &m));
    m.visited = 1U|4U; m.remaining=3U;
    CHECK(trilink_route_encode(&m, saved.wire, sizeof(saved.wire), &saved.size));
    trilink_route_input(&nodes[3].route, macs[2], saved.wire, saved.size, clock_ms);
    CHECK(nodes[3].receipts == 1U && nodes[3].route.duplicates == 1U);
    saved.wire[saved.size-1U] ^= 1U;
    trilink_route_input(&nodes[3].route, macs[2], saved.wire, saved.size, clock_ms);
    CHECK(nodes[3].receipts == 1U && nodes[3].route.rejected >= 1U);
    /* Same logical message retries over a different path after topology repair. */
    edge(0,1,false); drop_ack=false; advance(2400);
    uint8_t next[6], hops;
    CHECK(trilink_route_lookup(&nodes[0].route, macs[3], clock_ms, next, &hops)
        && hops == 2U && !memcmp(next, macs[2], 6U));
    CHECK(nodes[0].completions == 1U && nodes[3].receipts == 1U);
    duplicate=true; submit(3,0); pump();
    CHECK(nodes[0].receipts == 1U && nodes[3].completions == 1U);
    CHECK(nodes[2].route.transit_duplicates >= 1U);
}
static void boundary_tests(void)
{
    reset(2); edge(0,1,true); advance(1500);
    trilink_route_config_t bad = nodes[0].route.config;
    bad.count=7U; CHECK(!trilink_route_configure(&nodes[0].route,&bad));
    bad=nodes[0].route.config; memcpy(bad.members[1],bad.members[0],6U);
    CHECK(!trilink_route_configure(&nodes[0].route,&bad));
    bad=nodes[0].route.config; bad.members[0][0]=0x99U;
    CHECK(!trilink_route_configure(&nodes[0].route,&bad));
    uint8_t next[6], hops;
    CHECK(!trilink_route_lookup(&nodes[0].route,macs[0],clock_ms,next,&hops));
    /* Duplicate config cannot reset identity or dedup state. */
    drop_ack=true; uint32_t id=submit(0,1); pump();
    CHECK(trilink_route_configure(&nodes[0].route,&nodes[0].route.config));
    CHECK(nodes[0].route.pending && nodes[0].route.pending_message == id);
    bad=nodes[0].route.config; bad.epoch++;
    CHECK(!trilink_route_configure(&nodes[0].route,&bad));
    advance(4900); CHECK(nodes[0].timeouts == 1U);
    CHECK(trilink_route_configure(&nodes[0].route,&bad));
    CHECK(nodes[0].route.next_message == 2U);
    bad.epoch--; CHECK(!trilink_route_configure(&nodes[0].route,&bad));

    reset(2); edge(0,1,true); advance(1500); fail_receive=true;
    submit(0,1); pump();
    CHECK(nodes[1].route.queue_full == 1U && nodes[1].receipts == 0U);
    fail_receive=false; advance(1200);
    CHECK(nodes[1].receipts == 1U && nodes[0].completions == 1U);
    reset(2); edge(0,1,true); advance(1500); fail_send=true;
    submit(0,1); advance(4900);
    CHECK(nodes[0].timeouts == 1U && nodes[1].receipts == 0U);
    reset(2); edge(0,1,true); advance(1500);
    for (unsigned i=0;i<TRILINK_ROUTE_CACHE_SIZE;++i) { submit(0,1); pump(); }
    CHECK(nodes[1].receipts == TRILINK_ROUTE_CACHE_SIZE);
    submit(0,1); pump(); CHECK(nodes[0].route.pending && nodes[1].route.queue_full == 1U);
    advance(4900); CHECK(nodes[0].timeouts == 1U);
    advance(26000); submit(0,1); pump();
    CHECK(nodes[1].receipts == TRILINK_ROUTE_CACHE_SIZE+1U);
    nodes[0].route.next_message=0U;
    uint8_t data=0; CHECK(!trilink_route_submit(&nodes[0].route,macs[1],&data,1U,clock_ms,&id));

    reset(3); edge(0,1,true); edge(1,2,true); advance(1500);
    drop_ack=true; submit(0,2); pump();
    trilink_route_view_t ack = {.room=nodes[0].route.config.room,.epoch=7,.boot=999,.message=1,
        .remaining=3,.visited=6,.kind=TRILINK_ROUTE_ACK};
    memcpy(ack.source,macs[2],6U); memcpy(ack.destination,macs[0],6U);
    uint8_t wire[TRILINK_PAYLOAD_MAX]; size_t length;
    CHECK(trilink_route_encode(&ack,wire,sizeof(wire),&length));
    trilink_route_input(&nodes[0].route,macs[1],wire,length,clock_ms);
    CHECK(nodes[0].route.pending && nodes[0].completions == 0U);
    /* Loops/foreign sender/wrong Room never execute at the destination. */
    packet_t packet=last_data;
    packet.wire[4]^=1U;
    trilink_route_input(&nodes[2].route,macs[1],packet.wire,packet.size,clock_ms);
    CHECK(nodes[2].receipts == 1U);
    packet=last_data; packet.wire[37]|=4U;
    trilink_route_input(&nodes[2].route,macs[1],packet.wire,packet.size,clock_ms);
    CHECK(nodes[2].receipts == 1U);
    trilink_route_input(&nodes[2].route,macs[5],last_data.wire,last_data.size,clock_ms);
    CHECK(nodes[2].receipts == 1U);
}
static unsigned reference_distance(unsigned source, unsigned destination)
{
    unsigned q[6], dist[6]={0}, head_ref=0,tail_ref=0;
    bool seen[6]={false}; q[tail_ref++]=source; seen[source]=true;
    while(head_ref<tail_ref) {
        unsigned here=q[head_ref++];
        for(unsigned i=0;i<count;++i) if(!seen[i] && radio[here][i] && radio[i][here]) {
            seen[i]=true; dist[i]=dist[here]+1U;
            if(i==destination) return dist[i];
            q[tail_ref++]=i;
        }
    }
    return 0U;
}
static void graph_tests(void)
{
    /* Deterministic graph suite: every source/target, connected and partitioned,
     * six nodes. Small random layouts avoid baking chain-order assumptions in. */
    uint32_t seed=0x5a17U;
    for(unsigned graph=0;graph<128U;++graph) {
        reset(6);
        for(unsigned a=0;a<6U;++a) for(unsigned b=a+1U;b<6U;++b) {
            seed=seed*1664525U+1013904223U; edge(a,b,(seed>>30U)!=0U);
        }
        advance(1500);
        for(unsigned a=0;a<6U;++a) for(unsigned b=0;b<6U;++b) if(a!=b) {
            uint8_t next[6],hops=0;
            bool found=trilink_route_lookup(&nodes[a].route,macs[b],clock_ms,next,&hops);
            unsigned expected=reference_distance(a,b);
            CHECK(found == (expected!=0U)); CHECK(hops==expected);
            if(found) {
                unsigned before=nodes[b].receipts;
                submit(a,b); pump();
                CHECK(nodes[b].receipts==before+1U && !nodes[a].route.pending);
            }
        }
    }
}
static void topology_tests(void)
{
    reset(3); edge(0,1,true); edge(1,2,true); advance(1500);
    uint8_t next[6],hops;
    CHECK(!trilink_route_lookup(&nodes[0].route,macs[2],clock_ms+4000U,next,&hops));
    /* A fresh direct hearing in one direction does not create a usable edge. */
    reset(2); radio[0][1]=true; advance(1500);
    CHECK(!trilink_route_lookup(&nodes[0].route,macs[1],clock_ms,next,&hops));
    CHECK(!trilink_route_lookup(&nodes[1].route,macs[0],clock_ms,next,&hops));
    reset(2); edge(0,1,true); advance(1500);
    trilink_route_link_t before=nodes[0].route.links[1];
    uint8_t body[6];
    uint32_t digest=nodes[0].route.membership_digest;
    for(unsigned i=0;i<4U;++i) body[i]=(uint8_t)(digest>>(8U*i));
    body[4]=1U;body[5]=1U;
    trilink_route_view_t m={.room=0x12345678U,.epoch=7,.boot=before.boot,.message=before.sequence,
        .remaining=4,.visited=2,.kind=TRILINK_ROUTE_TOPOLOGY,.length=6,.data=body};
    memcpy(m.source,macs[1],6U);memcpy(m.destination,macs[1],6U);
    uint8_t wire[TRILINK_PAYLOAD_MAX];size_t length;
    CHECK(trilink_route_encode(&m,wire,sizeof(wire),&length));
    trilink_route_input(&nodes[0].route,macs[1],wire,length,clock_ms+500U);
    CHECK(nodes[0].route.links[1].expires==before.expires);
    m.boot=555;m.message=1;
    CHECK(trilink_route_encode(&m,wire,sizeof(wire),&length));
    trilink_route_input(&nodes[0].route,macs[1],wire,length,clock_ms+600U);
    CHECK(nodes[0].route.links[1].boot==555U);
    m.boot=before.boot;m.message=before.sequence+100U;
    CHECK(trilink_route_encode(&m,wire,sizeof(wire),&length));
    trilink_route_input(&nodes[0].route,macs[1],wire,length,clock_ms+700U);
    CHECK(nodes[0].route.links[1].boot==555U);
    body[0]^=1U;m.boot=999;
    CHECK(trilink_route_encode(&m,wire,sizeof(wire),&length));
    trilink_route_input(&nodes[0].route,macs[1],wire,length,clock_ms+800U);
    CHECK(nodes[0].route.links[1].boot==555U);
}
int main(void)
{
    codec_tests(); transport_tests(); multipath_tests(); boundary_tests(); topology_tests(); graph_tests();
    printf("trilink_route host tests: checks=%u failures=%u route_bytes=%u data_max=%u\n",
        checks,failures,(unsigned)sizeof(trilink_route_t),(unsigned)TRILINK_ROUTE_DATA_MAX);
    return failures ? 1 : 0;
}
