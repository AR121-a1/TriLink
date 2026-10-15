#include "trilink_bridge.h"
#include "trilink_room.h"
#include "trilink_rgb.h"
#include "trilink_app.h"
#include "trilink_firmware_version.h"

#include <inttypes.h>
#include <stdio.h>
#include <string.h>
#include "freertos/FreeRTOS.h"
#include "freertos/queue.h"

#define COMMAND_DEPTH 4U
#define REPLY_DEPTH 4U
#define INBOX_DEPTH 8U
typedef struct {
    uint8_t source[6], data[TRILINK_ROUTE_DATA_MAX];
    uint32_t boot, message;
    uint16_t length;
} inbox_t;
static trilink_route_t s_route;
static trilink_room_t s_room;
static trilink_app_dispatcher_t s_apps;
static uint8_t s_neighbors[6][6];
static size_t s_neighbor_count;
static uint32_t s_rgb_sequence=1, s_rgb_operation;
static uint8_t s_rgb_target[6];
static unsigned s_rgb_status;
static uint64_t s_rgb_deadline;
/* Highest accepted application operation per current member. Never evict a
 * current member to make space: delayed requests must not execute again after
 * another sender has replaced the single execution/result slot. */
static struct {
    uint8_t source[6]; uint32_t boot, incarnation, operation;
} s_rgb_seen[6];
static struct {
    bool valid, running, reply;
    uint8_t source[6], color[3]; uint32_t boot, operation, incarnation, revision;
    unsigned status;
} s_rgb_job;

static uint32_t read32(const uint8_t *p) { return (uint32_t)p[0]|(uint32_t)p[1]<<8U|(uint32_t)p[2]<<16U|(uint32_t)p[3]<<24U; }
static void write32(uint8_t *p,uint32_t n) {for(unsigned i=0;i<4;++i) p[i]=(uint8_t)(n>>(i*8U));}
static bool rgb_input(const trilink_route_view_t *m);
static QueueHandle_t s_commands, s_replies;
static StaticQueue_t s_command_control, s_reply_control;
static uint8_t s_command_storage[COMMAND_DEPTH * sizeof(trilink_route_command_t)];
static uint8_t s_reply_storage[REPLY_DEPTH * TRILINK_BRIDGE_REPLY_SIZE];
static inbox_t s_inbox[INBOX_DEPTH];
static unsigned s_inbox_head, s_inbox_count;
static trilink_route_command_t s_command;
static char s_reply[TRILINK_BRIDGE_REPLY_SIZE], s_hex[TRILINK_ROUTE_DATA_MAX*2U+1U];
static uint32_t s_result_id, s_reply_dropped;
static trilink_route_result_t s_result;
static uint8_t s_consumed_source[6];
static uint32_t s_consumed_boot, s_consumed_message;

static bool send_payload(void *context, const uint8_t next[6],
                          const uint8_t *payload, size_t length, bool acknowledgement)
{
    (void)context;
    return trilink_node_queue_payload(next, payload, length, acknowledgement) == ESP_OK;
}

static bool received(void *context, const trilink_route_view_t *m)
{
    (void)context;
    if(s_room.active) return trilink_app_receive(&s_apps,m);
    if (s_inbox_count >= INBOX_DEPTH) return false;
    inbox_t *entry = &s_inbox[(s_inbox_head+s_inbox_count) % INBOX_DEPTH];
    memcpy(entry->source, m->source, 6U);
    memcpy(entry->data, m->data, m->length);
    entry->boot = m->boot; entry->message = m->message; entry->length = m->length;
    ++s_inbox_count;
    return true;
}

static void complete(void *context, uint32_t message, trilink_route_result_t result)
{
    (void)context;
    s_result_id = message;
    s_result = result;
}

static bool room_send(void *context,const uint8_t *payload,size_t length)
{
    (void)context;
    static const uint8_t broadcast[6]={255,255,255,255,255,255};
    return send_payload(NULL,broadcast,payload,length,false);
}
static void room_changed(void *context)
{
    (void)context;
    for(unsigned i=0;i<6;++i)
        if(!s_room.active || s_rgb_seen[i].incarnation!=s_room.state.incarnation
            || trilink_room_member(&s_room.state,s_rgb_seen[i].source,s_rgb_seen[i].boot)<0)
            memset(&s_rgb_seen[i],0,sizeof(s_rgb_seen[i]));
    if(s_rgb_operation && s_rgb_status<=1) s_rgb_status=5;
    uint8_t local[6]; memcpy(local,s_route.local,6);
    uint32_t boot=s_route.boot, next=s_route.next_message;
    if(s_route.pending) complete(NULL,s_route.pending_message,TRILINK_ROUTE_TIMEOUT);
    trilink_route_io_t io={NULL,send_payload,received,complete};
    (void)trilink_route_init(&s_route,local,boot,&io); s_route.next_message=next;
    if(s_room.active) {
        trilink_route_config_t config={.room=s_room.state.room,.epoch=s_room.state.revision,.count=s_room.state.count};
        for(unsigned i=0;i<config.count;++i) memcpy(config.members[i],s_room.state.members[i].mac,6);
        (void)trilink_route_configure(&s_route,&config);
        trilink_route_neighbors(&s_route,&s_neighbors[0][0],s_neighbor_count);
    }
}
static bool rgb_input(const trilink_route_view_t *m)
{
    const uint8_t *p=m->data;
    if(m->length!=19 || p[0]!=0xA6 || p[1]!=1 || !s_room.active
       || trilink_room_member(&s_room.state,m->source,m->boot)<0
       || read32(p+4)!=s_room.state.incarnation || read32(p+12)!=s_room.state.revision || !read32(p+8)) return false;
    if(p[2]==2) {
        if(p[3]<1 || p[3]>4 || memcmp(m->source,s_rgb_target,6) || read32(p+8)!=s_rgb_operation) return false;
        /* Terminal outcomes are immutable; a late ACCEPTED cannot revive a
         * timed-out operation. Late terminal evidence can resolve UNKNOWN. */
        if(s_rgb_status>=2 && s_rgb_status<=4) return p[3]==1 || p[3]==s_rgb_status;
        if(p[3]!=1 || s_rgb_status<=1) s_rgb_status=p[3];
        return true;
    }
    if(p[2]!=1 || p[3]!=0 || p[16]>32 || p[17]>32 || p[18]>32) return false;
    if(s_rgb_job.valid && s_rgb_job.boot==m->boot && s_rgb_job.operation==read32(p+8)
        && !memcmp(s_rgb_job.source,m->source,6)) {
        if(s_rgb_job.incarnation!=read32(p+4) || s_rgb_job.revision!=read32(p+12)
            || memcmp(s_rgb_job.color,p+16,3)) return false;
        s_rgb_job.reply=true; return true;
    }
    if(s_rgb_job.running || s_rgb_job.reply) return false;
    int slot=-1;
    for(unsigned i=0;i<6;++i) {
        if(!s_rgb_seen[i].operation) {if(slot<0)slot=(int)i;}
        else if(s_rgb_seen[i].boot==m->boot && !memcmp(s_rgb_seen[i].source,m->source,6)) {
            if(read32(p+8)<=s_rgb_seen[i].operation) return false;
            slot=(int)i;break;
        }
    }
    if(slot<0) return false;
    memcpy(s_rgb_seen[slot].source,m->source,6);s_rgb_seen[slot].boot=m->boot;
    s_rgb_seen[slot].incarnation=read32(p+4);s_rgb_seen[slot].operation=read32(p+8);
    s_rgb_job.valid=true; memcpy(s_rgb_job.source,m->source,6);
    s_rgb_job.boot=m->boot; s_rgb_job.operation=read32(p+8);
    s_rgb_job.incarnation=read32(p+4); s_rgb_job.revision=read32(p+12);
    memcpy(s_rgb_job.color,p+16,3);
    s_rgb_job.running=trilink_rgb_begin(p[16],p[17],p[18]);
    s_rgb_job.status=s_rgb_job.running?1U:4U; s_rgb_job.reply=true;
    return true;
}
static void rgb_tick(uint64_t now)
{
    if(s_rgb_job.running) {
        unsigned result=trilink_rgb_result();
        if(result) {s_rgb_job.running=false;s_rgb_job.status=result==1?2U:3U;s_rgb_job.reply=true;}
    }
    if(s_rgb_job.reply && !s_route.pending && s_room.active
        && s_rgb_job.incarnation==s_room.state.incarnation && s_rgb_job.revision==s_room.state.revision) {
        if(!memcmp(s_rgb_job.source,s_route.local,6)) {s_rgb_status=s_rgb_job.status;s_rgb_job.reply=false;}
        else {
            uint8_t p[19]={0xA6,1,2,(uint8_t)s_rgb_job.status}; uint32_t id;
            write32(p+4,s_rgb_job.incarnation); write32(p+8,s_rgb_job.operation); write32(p+12,s_rgb_job.revision);
            if(trilink_route_submit(&s_route,s_rgb_job.source,p,sizeof(p),now,&id)) s_rgb_job.reply=false;
        }
    }
    if(s_rgb_operation && s_rgb_status<=1 && now>=s_rgb_deadline) s_rgb_status=5;
    if(s_rgb_job.reply && (!s_room.active || s_rgb_job.revision!=s_room.state.revision
        || s_rgb_job.incarnation!=s_room.state.incarnation)) s_rgb_job.reply=false;
}

static bool rgb_module_receive(void *context, const trilink_route_view_t *message)
{
    (void)context;
    return rgb_input(message);
}
static void rgb_module_tick(void *context, uint64_t now)
{
    (void)context;
    rgb_tick(now);
}

esp_err_t trilink_bridge_init(const uint8_t mac[6], uint32_t boot)
{
    trilink_route_io_t io = {NULL, send_payload, received, complete};
    if (!trilink_route_init(&s_route, mac, boot, &io)) return ESP_ERR_INVALID_ARG;
    trilink_room_io_t room_io={NULL,room_send,room_changed};
    if(!trilink_room_init(&s_room,mac,boot,&room_io) || !trilink_rgb_init()) return ESP_ERR_NO_MEM;
    trilink_app_init(&s_apps);
    const trilink_app_module_t rgb = {.capability_bit=TRILINK_APP_CAP_RGB,
        .marker=0xA6U,.version=1U,.wire_size=19U,.context=NULL,
        .receive=rgb_module_receive,.tick=rgb_module_tick};
    if(!trilink_app_register(&s_apps,&rgb)) return ESP_ERR_INVALID_ARG;
    s_commands = xQueueCreateStatic(COMMAND_DEPTH, sizeof(trilink_route_command_t),
                                     s_command_storage, &s_command_control);
    s_replies = xQueueCreateStatic(REPLY_DEPTH, TRILINK_BRIDGE_REPLY_SIZE,
                                    s_reply_storage, &s_reply_control);
    return s_commands != NULL && s_replies != NULL ? ESP_OK : ESP_ERR_NO_MEM;
}

bool trilink_bridge_submit(const trilink_route_command_t *command)
{
    return command != NULL && s_commands != NULL
        && xQueueSend(s_commands, command, 0) == pdTRUE;
}

bool trilink_bridge_reply(char output[TRILINK_BRIDGE_REPLY_SIZE])
{
    return output != NULL && s_replies != NULL && xQueueReceive(s_replies, output, 0) == pdTRUE;
}

void trilink_bridge_input(const uint8_t previous[6], const uint8_t *payload,
                          size_t length, uint64_t now)
{
    if(length>=1 && payload[0]==TRILINK_ROOM_SERVICE) {
        for(size_t i=0;i<s_neighbor_count;++i) if(!memcmp(previous,s_neighbors[i],6)) {
            trilink_room_input(&s_room,payload,length,now); return;
        }
        return;
    }
    trilink_route_input(&s_route, previous, payload, length, now);
}

void trilink_bridge_neighbors(const uint8_t *macs, size_t count)
{
    s_neighbor_count=count>6?6:count;
    if(s_neighbor_count) memcpy(s_neighbors,macs,s_neighbor_count*6);
    trilink_route_neighbors(&s_route, macs, count);
}

static int table_reply(const char *nonce, uint64_t now)
{
    int used = snprintf(s_reply, sizeof(s_reply), "\nTRILINK/3 TABLE %s count=%u local=%u neighbors=%02X members=",
        nonce, s_route.config.count, s_route.position+1U, s_route.neighbors);
    if (used < 0 || (size_t)used >= sizeof(s_reply)) return -1;
    for (unsigned i = 0; i < s_route.config.count; ++i) {
        char mac[13]; trilink_route_hex_encode(s_route.config.members[i], 6U, mac);
        int size = snprintf(s_reply+used, sizeof(s_reply)-(size_t)used, "%s%s", i ? "," : "", mac);
        if (size < 0 || (size_t)size >= sizeof(s_reply)-(size_t)used) return -1;
        used += size;
    }
    int size = snprintf(s_reply+used, sizeof(s_reply)-(size_t)used, " routes=");
    if (size < 0 || (size_t)size >= sizeof(s_reply)-(size_t)used) return -1;
    used += size;
    for (unsigned i = 0; i < s_route.config.count; ++i) {
        uint8_t next[6], hops = 0U; unsigned via = 0U;
        if (trilink_route_lookup(&s_route, s_route.config.members[i], now, next, &hops))
            for (unsigned j = 0; j < s_route.config.count; ++j)
                if (!memcmp(next, s_route.config.members[j], 6U)) via = j+1U;
        size = snprintf(s_reply+used, sizeof(s_reply)-(size_t)used, "%s%u/%u", i ? "," : "", via, hops);
        if (size < 0 || (size_t)size >= sizeof(s_reply)-(size_t)used) return -1;
        used += size;
    }
    if ((size_t)used + 2U > sizeof(s_reply)) return -1;
    s_reply[used++] = '\n'; s_reply[used] = '\0';
    return used;
}

void trilink_bridge_process(uint64_t now)
{
    trilink_room_tick(&s_room,now);
    trilink_route_tick(&s_route, now);
    trilink_app_tick(&s_apps,now);
    /* If the host is not consuming replies, do not execute more commands.
     * Incoming radio work continues; its inbox has independent backpressure. */
    if (uxQueueSpacesAvailable(s_replies) == 0U
        || xQueueReceive(s_commands, &s_command, 0) != pdTRUE) return;
    const char *nonce = s_command.nonce[0] ? s_command.nonce : "-";
    int size = 0;
    switch (s_command.kind) {
    case TRILINK_ROUTE_CMD_ROOM: {
        trilink_room_packet_t command;
        bool ok=s_inbox_count==0 && !s_route.pending && !s_rgb_job.running
            && trilink_room_decode(s_command.data,s_command.length,&command)
            && trilink_room_command(&s_room,&command,now);
        size=snprintf(s_reply,sizeof(s_reply),"\nTRILINK/3 %s %s ROOM\n",ok?"OK":"ERROR",nonce);
        break;
    }
    case TRILINK_ROUTE_CMD_ROOM_GET: {
        unsigned page=s_command.data[0]; const trilink_room_packet_t *packet=NULL;
        trilink_room_packet_t state=s_room.state;
        if(page>18) {size=snprintf(s_reply,sizeof(s_reply),"\nTRILINK/3 ERROR %s PAGE\n",nonce);break;}
        if(page==0) {
            state.kind=ROOM_SNAPSHOT; state.status=(s_room.active?1:0)
                | (trilink_room_synchronized(&s_room,now)?2:0) | (s_room.waiting?4:0)
                | (trilink_rgb_enabled()?8:0) | (s_room.join_confirmed?16:0)
                | (s_room.waiting && s_room.join_confirmed && s_room.attempts>=8?32:0)
                | (trilink_rgb_faulted()?64:0) | (trilink_room_retiring(&s_room,now)?128:0);
            packet=&state;
        } else {
            const trilink_room_slot_t *slot=page<=6?&s_room.nearby[page-1]
                : page<=12?&s_room.requests[page-7]:&s_room.invitations[page-13];
            if(slot->valid && now<slot->expires && slot->packet.kind!=ROOM_DENY) packet=&slot->packet;
        }
        uint8_t bytes[132];
        if(packet && trilink_room_encode(packet,bytes)) {
            trilink_route_hex_encode(bytes,132,s_hex);
            size=snprintf(s_reply,sizeof(s_reply),"\nTRILINK/3 ROOMSTATE %s %02X %s\n",nonce,page,s_hex);
        } else size=snprintf(s_reply,sizeof(s_reply),"\nTRILINK/3 EMPTY %s\n",nonce);
        break;
    }
    case TRILINK_ROUTE_CMD_RGB_ENABLE:
        size=snprintf(s_reply,sizeof(s_reply),"\nTRILINK/3 %s %s RGBENABLE\n",
            trilink_rgb_enable(s_command.relay_enabled)?"OK":"ERROR",nonce);
        break;
    case TRILINK_ROUTE_CMD_RGB: {
        uint8_t p[19]={0xA6,1,1,0}; uint32_t id=0;
        bool ok=s_room.active && !s_route.pending && s_rgb_sequence
            && (s_rgb_status>1 || !s_rgb_operation)
            && trilink_room_member(&s_room.state,s_command.destination,0)>=0
            && s_command.data[0]<=32 && s_command.data[1]<=32 && s_command.data[2]<=32;
        if(ok) {
            write32(p+4,s_room.state.incarnation); write32(p+8,s_rgb_sequence); write32(p+12,s_room.state.revision);
            memcpy(p+16,s_command.data,3);
            if(!memcmp(s_command.destination,s_route.local,6)) {
                trilink_route_view_t m={.boot=s_route.boot,.data=p,.length=19}; memcpy(m.source,s_route.local,6); ok=trilink_app_receive(&s_apps,&m);
            } else ok=trilink_route_submit(&s_route,s_command.destination,p,sizeof(p),now,&id);
            if(ok) {
                s_rgb_operation=s_rgb_sequence++; s_rgb_status=0; s_rgb_deadline=now+10000;
                memcpy(s_rgb_target,s_command.destination,6);
            }
        }
        size=snprintf(s_reply,sizeof(s_reply),"\nTRILINK/3 %s %s %08" PRIX32 "\n",ok?"QUEUED":"ERROR",nonce,ok?s_rgb_operation:0U);
        break;
    }
    case TRILINK_ROUTE_CMD_RGB_RESULT:
        size=snprintf(s_reply,sizeof(s_reply),"\nTRILINK/3 RGBRESULT %s %08" PRIX32 " %u\n",nonce,s_rgb_operation,s_rgb_status);
        break;
    case TRILINK_ROUTE_CMD_TABLE:
        size = table_reply(nonce, now);
        break;
    case TRILINK_ROUTE_CMD_CAPS:
        size=snprintf(s_reply,sizeof(s_reply),"\nTRILINK/3 CAPS %s %u %s %08" PRIX32 " %02X %02X %02X %02X\n",
            nonce,TRILINK_NODE_CONTRACT_VERSION,TRILINK_FIRMWARE_VERSION,s_route.boot,
            (unsigned)TRILINK_ROUTE_DATA_MAX,(unsigned)TRILINK_DISCOVERY_MAX_PEERS,
            (unsigned)TRILINK_ROUTE_MAX_MEMBERS,(unsigned)trilink_app_capabilities(&s_apps));
        break;
    case TRILINK_ROUTE_CMD_INFO: {
        char mac[13];
        trilink_route_hex_encode(s_route.local, 6U, mac);
        size = snprintf(s_reply, sizeof(s_reply),
            "\nTRILINK/3 INFO %s %s boot=%08" PRIX32 " configured=%u room=%08" PRIX32
            " epoch=%08" PRIX32 " position=%u relay=%u pending=%08" PRIX32
            " result=%08" PRIX32 ":%u forwarded=%" PRIu32 " received=%" PRIu32
            " duplicate=%" PRIu32 " rejected=%" PRIu32 " full=%" PRIu32
            " timeout=%" PRIu32 " inbox=%u reply_drop=%" PRIu32
            " members=%u neighbors=%02X route_format=2 topology=%" PRIu32 " no_route=%" PRIu32
            " transit_duplicate=%" PRIu32 "\n",
            nonce, mac, s_route.boot, s_route.configured, s_route.config.room,
            s_route.config.epoch, s_route.position+1U, s_route.relay_enabled,
            s_route.pending ? s_route.pending_message : 0U, s_result_id, (unsigned)s_result,
            s_route.forwarded, s_route.delivered, s_route.duplicates, s_route.rejected,
            s_route.queue_full, s_route.timeouts, s_inbox_count, s_reply_dropped,
            s_route.config.count, s_route.neighbors, s_route.topology_received,
            s_route.no_route, s_route.transit_duplicates);
        break;
    }
    case TRILINK_ROUTE_CMD_CONFIG:
        size = snprintf(s_reply, sizeof(s_reply), "\nTRILINK/3 %s %s CONFIG\n",
            !s_room.active && !s_room.waiting && (!s_inbox_count || (s_command.config.room == s_route.config.room
                && s_command.config.epoch == s_route.config.epoch))
                && trilink_route_configure(&s_route, &s_command.config) ? "OK" : "ERROR", nonce);
        break;
    case TRILINK_ROUTE_CMD_SEND: {
        uint32_t id = 0U;
        bool ok = !s_room.active && trilink_route_submit(&s_route, s_command.destination, s_command.data,
                                       s_command.length, now, &id);
        size = snprintf(s_reply, sizeof(s_reply), "\nTRILINK/3 %s %s %08" PRIX32 "\n",
                         ok ? "QUEUED" : "ERROR", nonce, id);
        break;
    }
    case TRILINK_ROUTE_CMD_RELAY: {
        bool ok = s_route.configured;
        if (ok) trilink_route_relay(&s_route, s_command.relay_enabled);
        size = snprintf(s_reply, sizeof(s_reply), "\nTRILINK/3 %s %s RELAY\n", ok ? "OK" : "ERROR", nonce);
        break;
    }
    case TRILINK_ROUTE_CMD_POLL:
        if (s_inbox_count == 0U)
            size = snprintf(s_reply, sizeof(s_reply), "\nTRILINK/3 EMPTY %s\n", nonce);
        else {
            inbox_t *entry = &s_inbox[s_inbox_head];
            char mac[13];
            trilink_route_hex_encode(entry->source, 6U, mac);
            trilink_route_hex_encode(entry->data, entry->length, s_hex);
            size = snprintf(s_reply, sizeof(s_reply), "\nTRILINK/3 DATA %s %s %08" PRIX32
                " %08" PRIX32 " %s\n", nonce, mac, entry->boot, entry->message, s_hex);
        }
        break;
    case TRILINK_ROUTE_CMD_CONSUME: {
        /* POLL is a repeatable peek. Only an explicit identity-matched consume
         * advances the inbox, so short USB writes/disconnects cannot lose data. */
        bool ok = s_consumed_boot == s_command.boot && s_consumed_message == s_command.message
            && memcmp(s_consumed_source, s_command.destination, 6U) == 0;
        if (!ok && s_inbox_count != 0U) {
            inbox_t *entry = &s_inbox[s_inbox_head];
            ok = entry->boot == s_command.boot && entry->message == s_command.message
                && memcmp(entry->source, s_command.destination, 6U) == 0;
            if (ok) {
                memcpy(s_consumed_source, entry->source, 6U);
                s_consumed_boot = entry->boot;
                s_consumed_message = entry->message;
                s_inbox_head = (s_inbox_head+1U) % INBOX_DEPTH;
                --s_inbox_count;
            }
        }
        size = snprintf(s_reply, sizeof(s_reply), "\nTRILINK/3 %s %s CONSUME\n", ok ? "OK" : "ERROR", nonce);
        break;
    }
    default:
        size = snprintf(s_reply, sizeof(s_reply), "\nTRILINK/3 ERROR %s BAD_REQUEST\n", nonce);
        break;
    }
    if (size <= 0 || (size_t)size >= sizeof(s_reply)
        || xQueueSend(s_replies, s_reply, 0) != pdTRUE) ++s_reply_dropped;
}
