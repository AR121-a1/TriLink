#include "trilink_room.h"
#include <string.h>

_Static_assert(sizeof(trilink_room_t) <= 6144U, "Room state exceeds 6 KiB");
static uint32_t get32(const uint8_t *p) { return (uint32_t)p[0] | (uint32_t)p[1]<<8U | (uint32_t)p[2]<<16U | (uint32_t)p[3]<<24U; }
static void put32(uint8_t *p, uint32_t n) { for (unsigned i=0; i<4; ++i) p[i]=(uint8_t)(n>>(8U*i)); }
static bool equal(const uint8_t *a, const uint8_t *b) { return memcmp(a,b,6)==0; }
int trilink_room_member(const trilink_room_packet_t *p, const uint8_t mac[6], uint32_t boot)
{
    for (unsigned i=0; i<p->count; ++i)
        if (equal(p->members[i].mac,mac) && (!boot || boot==p->members[i].boot)) return (int)i;
    return -1;
}
static bool valid(const trilink_room_packet_t *p)
{
    if (!p || p->ttl>5 || p->count>6 || p->formed>1 || p->name[23]!=0) return false;
    if (!((p->kind>=ROOM_SNAPSHOT && p->kind<=ROOM_CONFIRM)
          || (p->kind>=ROOM_CREATE && p->kind<=ROOM_RETRY))) return false;
    for (unsigned i=0;i<p->count;++i) {
        if (!trilink_route_mac_valid(p->members[i].mac) || !p->members[i].boot) return false;
        for (unsigned j=0;j<i;++j) if (equal(p->members[i].mac,p->members[j].mac)) return false;
    }
    return true;
}
bool trilink_room_encode(const trilink_room_packet_t *p, uint8_t out[132])
{
    if (!out || !valid(p)) return false;
    memset(out,0,132); out[0]=p->kind; out[1]=p->ttl; out[2]=p->count; out[3]=p->formed;
    memcpy(out+4,p->source,6); put32(out+10,p->boot); put32(out+14,p->operation);
    put32(out+18,p->room); put32(out+22,p->incarnation); put32(out+26,p->revision); put32(out+30,p->term);
    memcpy(out+34,p->authority,6);
    for (unsigned i=0;i<p->count;++i) { memcpy(out+40+i*10,p->members[i].mac,6); put32(out+46+i*10,p->members[i].boot); }
    memcpy(out+100,p->name,24); memcpy(out+124,p->target,6); out[130]=p->status;
    return true;
}
bool trilink_room_decode(const uint8_t *in, size_t len, trilink_room_packet_t *p)
{
    if (!in || !p || len!=132 || in[131]!=0 || in[2]>6) return false;
    memset(p,0,sizeof(*p)); p->kind=in[0]; p->ttl=in[1]; p->count=in[2]; p->formed=in[3];
    memcpy(p->source,in+4,6); p->boot=get32(in+10); p->operation=get32(in+14);
    p->room=get32(in+18); p->incarnation=get32(in+22); p->revision=get32(in+26); p->term=get32(in+30);
    memcpy(p->authority,in+34,6);
    for(unsigned i=0;i<p->count;++i) { memcpy(p->members[i].mac,in+40+i*10,6); p->members[i].boot=get32(in+46+i*10); }
    for(unsigned i=40+p->count*10;i<100;++i) if(in[i]) return false;
    memcpy(p->name,in+100,24); memcpy(p->target,in+124,6); p->status=in[130];
    return valid(p);
}
bool trilink_room_init(trilink_room_t *r, const uint8_t mac[6], uint32_t boot, const trilink_room_io_t *io)
{
    if(!r || !trilink_route_mac_valid(mac) || !boot || !io || !io->send || !io->changed) return false;
    memset(r,0,sizeof(*r)); memcpy(r->local,mac,6); r->boot=boot; r->sequence=1; r->io=*io; return true;
}
bool trilink_room_is_leader(const trilink_room_t *r)
{ return r->active && r->state.count && equal(r->local,r->state.members[0].mac); }
bool trilink_room_retiring(const trilink_room_t *r, uint64_t now)
{ return !r->active && r->state.room && equal(r->local,r->state.authority) && now<r->retired_until; }
bool trilink_room_synchronized(const trilink_room_t *r, uint64_t now)
{
    if (!trilink_room_is_leader(r)) return false;
    for (unsigned i=1;i<r->state.count;++i)
        if (!r->acknowledgements[i] || now>=r->acknowledgements[i]) return false;
    return true;
}
static bool key(const trilink_room_packet_t *a, const trilink_room_packet_t *b)
{ return a->room && a->room==b->room && a->incarnation==b->incarnation; }
static bool emit(trilink_room_t *r, trilink_room_packet_t *p, bool originated)
{
    uint8_t bytes[136]; size_t length;
    if (originated) {
        if (!r->sequence) return false;
        memcpy(p->source,r->local,6); p->boot=r->boot; p->operation=r->sequence++; p->ttl=5;
    }
    if (!trilink_room_encode(p,bytes+4)) return false;
    trilink_service_view_t service={TRILINK_ROOM_SERVICE,TRILINK_ROOM_OPCODE,132,bytes+4};
    /* Header only; avoid overlapping memcpy in the service encoder. */
    bytes[0]=service.service_id; bytes[1]=service.opcode; bytes[2]=132; bytes[3]=0; length=136;
    if (!r->io.send(r->io.context,bytes,length)) { ++r->queue_full; return false; }
    return true;
}
static void changed(trilink_room_t *r, uint64_t now)
{
    r->active=trilink_room_member(&r->state,r->local,r->boot)>=0;
    memset(r->acknowledgements,0,sizeof(r->acknowledgements));
    r->next_advert=now; r->retired_until=now+10000;
    r->waiting=false; r->join_confirmed=false; r->join_revision_floor=0;
    r->io.changed(r->io.context);
}
static void mutate(trilink_room_t *r, uint64_t now)
{
    memcpy(r->state.authority,r->local,6); ++r->state.revision;
    r->state.kind=ROOM_SNAPSHOT;
    if (r->state.formed && r->state.count<=1) r->state.count=0;
    changed(r,now);
}
static void remove_member(trilink_room_t *r, unsigned index, uint64_t now)
{
    if (!index) ++r->state.term;
    for(unsigned i=index+1;i<r->state.count;++i) r->state.members[i-1]=r->state.members[i];
    --r->state.count; mutate(r,now);
}
static void remember_slot(trilink_room_slot_t slots[6], const trilink_room_packet_t *p, uint64_t now, uint64_t duration, bool rooms)
{
    int free_slot=-1;
    for(unsigned i=0;i<6;++i) {
        if(!slots[i].valid || now>=slots[i].expires) { if(free_slot<0) free_slot=(int)i; }
        else if (rooms ? key(&slots[i].packet,p) : equal(slots[i].packet.source,p->source)) {
            /* A delayed flood must not downgrade the live discovery baseline. */
            if (rooms && p->revision<slots[i].packet.revision) return;
            free_slot=(int)i; break;
        }
    }
    if(free_slot>=0) { slots[free_slot].packet=*p; slots[free_slot].expires=now+duration; slots[free_slot].valid=true; }
}
/* JOIN/OFFER/CONFIRM/DENY reuse revision/term as a stable applicant ticket/boot.
 * Packet operation remains a fresh flood-dedup identity on each bounded retry. */
static bool ticket(const trilink_room_packet_t *a, const trilink_room_packet_t *b)
{ return key(a,b) && a->revision && a->revision==b->revision && a->term==b->term; }
static bool answer(trilink_room_t *r, const trilink_room_packet_t *request, uint8_t kind)
{
    trilink_room_packet_t reply=*request; reply.kind=kind;
    memcpy(reply.target,request->source,6); return emit(r,&reply,true);
}
bool trilink_room_command(trilink_room_t *r, const trilink_room_packet_t *p, uint64_t now)
{
    if (!r || !valid(p) || !r->sequence) return false;
    /* The retired leader must retain its bounded handover/dissolution
     * announcements before a new room can overwrite the sole snapshot. */
    if ((p->kind==ROOM_CREATE || p->kind==ROOM_JOIN) && trilink_room_retiring(r,now)) return false;
    if(p->kind==ROOM_RETRY) {
        if(!r->waiting || !r->join_confirmed || r->active || r->attempts<8) return false;
        r->attempts=0; r->next_request=now; return true;
    }
    if (p->kind==ROOM_CREATE) {
        if(r->active || r->waiting) return false;
        memset(r->requests,0,sizeof(r->requests));
        memset(&r->state,0,sizeof(r->state)); r->state.kind=ROOM_SNAPSHOT;
        r->state.room=r->sequence++; r->state.incarnation=r->boot;
        r->state.revision=1; r->state.term=1; r->state.count=1;
        memcpy(r->state.name,p->name,24); memcpy(r->state.authority,r->local,6);
        memcpy(r->state.members[0].mac,r->local,6); r->state.members[0].boot=r->boot;
        changed(r,now); return true;
    }
    if(p->kind==ROOM_JOIN) {
        if(r->active || r->waiting) return false;
        for(unsigned i=0;i<6;++i) if(r->nearby[i].valid && now<r->nearby[i].expires
            && key(&r->nearby[i].packet,p) && r->nearby[i].packet.count>0
            && r->nearby[i].packet.count<6 && equal(p->target,r->nearby[i].packet.members[0].mac)
            && trilink_room_member(&r->nearby[i].packet,r->local,r->boot)<0) {
            r->request=r->nearby[i].packet; r->request.kind=ROOM_JOIN;
            r->join_revision_floor=r->request.revision;
            if(key(&r->state,&r->request) && r->state.revision>r->join_revision_floor)
                r->join_revision_floor=r->state.revision;
            memcpy(r->request.target,p->target,6);
            r->request.revision=r->sequence++; r->request.term=r->boot;
            memset(r->request.authority,0,6);
            r->join_confirmed=false; r->request_until=now+8000;
            r->waiting=true; r->attempts=0; r->next_request=now; return true;
        }
        return false;
    }
    /* A PC selection is a versioned intention, not an unconditional mutation. */
    if (!r->active || !key(&r->state,p) || p->revision!=r->state.revision
        || p->term!=r->state.term) return false;
    if (p->kind==ROOM_INVITE) {
        if(r->state.count>=6 || trilink_room_member(&r->state,p->target,0)>=0 || !trilink_route_mac_valid(p->target)) return false;
        trilink_room_packet_t invite=r->state; invite.kind=ROOM_INVITE; memcpy(invite.target,p->target,6);
        return emit(r,&invite,true);
    }
    if (p->kind==ROOM_LEAVE && !trilink_room_is_leader(r)) {
        if(r->waiting) return false;
        r->request=*p; memcpy(r->request.target,r->state.members[0].mac,6);
        r->waiting=true; r->attempts=0; r->next_request=now; r->request_until=now+8000; return true;
    }
    if (!trilink_room_synchronized(r,now) || r->state.revision==UINT32_MAX || r->state.term==UINT32_MAX) return false;
    if(p->kind==ROOM_LEAVE) { remove_member(r,0,now); return true; }
    if(p->kind==ROOM_KICK) {
        int member=trilink_room_member(&r->state,p->target,0);
        if(member<=0) return false;
        remove_member(r,(unsigned)member,now); return true;
    }
    for(unsigned i=0;i<6;++i) if(r->requests[i].valid && now<r->requests[i].expires
        && (r->requests[i].packet.kind==ROOM_JOIN || r->requests[i].packet.kind==ROOM_OFFER)
        && key(&r->requests[i].packet,&r->state)
        && equal(r->requests[i].packet.source,p->target)) {
        if(p->kind==ROOM_REJECT) {
            if(!answer(r,&r->requests[i].packet,ROOM_DENY)) return false;
            /* Retain a bounded negative decision for retried/lost JOIN/DENY. */
            r->requests[i].packet.kind=ROOM_DENY; r->requests[i].expires=now+15000; return true;
        }
        if(p->kind!=ROOM_APPROVE || r->requests[i].packet.kind!=ROOM_JOIN || r->state.count>=6) return false;
        trilink_room_packet_t offer=r->requests[i].packet;
        memset(offer.authority,0,6); put32(offer.authority,r->sequence);
        if(!answer(r,&offer,ROOM_OFFER)) return false;
        /* Do not reserve a member or freeze the room for an expired applicant. */
        offer.kind=ROOM_OFFER; r->requests[i].packet=offer; return true;
    }
    return false;
}
static bool same_state(const trilink_room_packet_t *a, const trilink_room_packet_t *b)
{
    if(!key(a,b) || a->revision!=b->revision || a->term!=b->term || a->count!=b->count || a->formed!=b->formed
       || memcmp(a->name,b->name,24) || !equal(a->authority,b->authority)) return false;
    for(unsigned i=0;i<a->count;++i) if(!equal(a->members[i].mac,b->members[i].mac) || a->members[i].boot!=b->members[i].boot) return false;
    return true;
}
static bool handover(const trilink_room_packet_t *old, const trilink_room_packet_t *next)
{
    if(next->term!=old->term+1 || next->revision!=old->revision+1 || !equal(next->authority,old->members[0].mac)) return false;
    if(old->count<=2) return next->count==0;
    if(next->count!=old->count-1) return false;
    for(unsigned i=0;i<next->count;++i)
        if(!equal(next->members[i].mac,old->members[i+1].mac) || next->members[i].boot!=old->members[i+1].boot) return false;
    return true;
}
static void snapshot(trilink_room_t *r, const trilink_room_packet_t *p, uint64_t now)
{
    if (!p->revision || !p->term || !trilink_route_mac_valid(p->authority)) return;
    bool source_ok=equal(p->source,p->authority)
        || (p->count && equal(p->source,p->members[0].mac) && p->boot==p->members[0].boot);
    if(!source_ok) return;
    remember_slot(r->nearby,p,now,TRILINK_ROOM_LIVE_MS,true);
    if(r->active && key(&r->state,p)) {
        if(p->revision<r->state.revision) return;
        if(p->revision==r->state.revision) { if(!same_state(&r->state,p)) return; }
        else {
            if(trilink_room_is_leader(r)) return;
            bool same_leader=p->term==r->state.term && equal(p->authority,r->state.members[0].mac)
                && equal(p->source,r->state.members[0].mac) && p->boot==r->state.members[0].boot
                && (!p->count || equal(p->members[0].mac,r->state.members[0].mac));
            if(!same_leader && !handover(&r->state,p)) return;
            r->state=*p; changed(r,now);
        }
    } else if(!r->active && r->waiting && key(p,&r->request)
        && equal(p->source,r->request.target) && p->count && equal(p->source,p->members[0].mac)
        && p->boot==r->request.members[0].boot) {
        /* A new commit must advance past every known non-member revision.
         * Inactive applicants also reject old snapshots beyond flood dedup TTL. */
        if(p->revision<=r->join_revision_floor) return;
        if(trilink_room_member(p,r->local,r->boot)<0) {
            r->join_revision_floor=p->revision; return;
        }
        if(!r->join_confirmed) return;
        r->state=*p; changed(r,now);
    }
    else return;
    if(r->active && !trilink_room_is_leader(r)) {
        trilink_room_packet_t ack=r->state; ack.kind=ROOM_ACK;
        memcpy(ack.target,r->state.members[0].mac,6); (void)emit(r,&ack,true);
    }
}
void trilink_room_input(trilink_room_t *r, const uint8_t *wire, size_t length, uint64_t now)
{
    trilink_service_view_t svc; trilink_room_packet_t p;
    if(!r || !trilink_service_decode_view(wire,length,&svc) || svc.service_id!=TRILINK_ROOM_SERVICE || svc.opcode!=TRILINK_ROOM_OPCODE
       || !trilink_room_decode(svc.data,svc.data_len,&p) || p.kind>ROOM_CONFIRM
       || !trilink_route_mac_valid(p.source) || !p.boot || !p.operation || !p.room || !p.incarnation
       || equal(p.source,r->local)) return;
    uint8_t canonical[132]; uint8_t ttl=p.ttl; p.ttl=0; (void)trilink_room_encode(&p,canonical); p.ttl=ttl;
    uint32_t digest=trilink_crc32(canonical,sizeof(canonical)); int free_slot=-1; bool duplicate=false;
    for(unsigned i=0;i<32;++i) {
        trilink_room_seen_t *s=&r->seen[i];
        if(now>=s->expires) { if(free_slot<0) free_slot=(int)i; }
        else if(equal(s->mac,p.source) && s->boot==p.boot && s->operation==p.operation) {
            if(s->digest!=digest || p.ttl<=s->ttl) return;
            s->ttl=p.ttl; duplicate=true; free_slot=(int)i; break;
        }
    }
    if(free_slot<0) { ++r->queue_full; return; }
    if(!duplicate) {
        trilink_room_seen_t *s=&r->seen[free_slot]; memcpy(s->mac,p.source,6);
        s->boot=p.boot; s->operation=p.operation; s->digest=digest; s->ttl=p.ttl; s->expires=now+2000;
        if(p.kind==ROOM_SNAPSHOT) snapshot(r,&p,now);
        else if((p.kind==ROOM_DENY || p.kind==ROOM_OFFER) && r->waiting && !r->active
            && ticket(&p,&r->request) && equal(p.target,r->local) && equal(p.source,r->request.target)
            && p.boot==r->request.members[0].boot) {
            if(p.kind==ROOM_DENY && equal(p.authority,r->request.authority)) {r->waiting=false; r->join_confirmed=false;}
            else if(p.kind==ROOM_OFFER && get32(p.authority) && !p.authority[4] && !p.authority[5]
                && !r->join_confirmed && now<r->request_until) {
                /* Once confirmation might be delivered, timeout means UNKNOWN,
                 * never permission to join/create a different room. */
                r->join_confirmed=true; r->request.kind=ROOM_CONFIRM;
                memcpy(r->request.authority,p.authority,6);
                r->attempts=0; r->next_request=now;
            }
        }
        else if(p.kind==ROOM_INVITE && equal(p.target,r->local) && !r->active
            && trilink_room_member(&p,p.source,p.boot)>=0)
            remember_slot(r->invitations,&p,now,15000,false);
        else if(trilink_room_is_leader(r) && key(&r->state,&p) && equal(p.target,r->local)) {
            int index=trilink_room_member(&r->state,p.source,p.boot);
            if(p.kind==ROOM_ACK && index>0 && same_state(&r->state,&p))
                r->acknowledgements[index]=now+TRILINK_ROOM_LIVE_MS;
            else if(p.kind==ROOM_CONFIRM && p.revision && p.term==p.boot && index<0) {
                trilink_room_slot_t *offer=NULL;
                for(unsigned i=0;i<6;++i) if(r->requests[i].valid && now<r->requests[i].expires
                    && r->requests[i].packet.kind==ROOM_OFFER && ticket(&r->requests[i].packet,&p)
                    && equal(r->requests[i].packet.authority,p.authority)
                    && r->requests[i].packet.boot==p.boot && equal(r->requests[i].packet.source,p.source)) offer=&r->requests[i];
                if(offer && trilink_room_member(&r->state,p.source,0)<0 && r->state.count<6
                    && trilink_room_synchronized(r,now) && r->state.revision<UINT32_MAX) {
                    trilink_room_member_t *m=&r->state.members[r->state.count++];
                    memcpy(m->mac,p.source,6); m->boot=p.boot;
                    r->state.formed=1; offer->valid=false; mutate(r,now);
                } else {
                    if(offer) {offer->packet.kind=ROOM_DENY; offer->expires=now+15000;}
                    (void)answer(r,&p,ROOM_DENY);
                }
            }
            else if(p.kind==ROOM_JOIN && p.revision && p.term==p.boot
                && trilink_room_member(&r->state,p.source,0)<0 && r->state.count<6) {
                bool handled=false;
                for(unsigned i=0;i<6;++i) if(r->requests[i].valid && now<r->requests[i].expires
                    && r->requests[i].packet.boot==p.boot && equal(r->requests[i].packet.source,p.source)) {
                    const trilink_room_packet_t *prior=&r->requests[i].packet;
                    if(prior->revision>p.revision) {handled=true;break;}
                    if(ticket(prior,&p) && prior->kind!=ROOM_JOIN) {
                        (void)answer(r,prior,prior->kind); handled=true; break;
                    }
                }
                if(!handled) remember_slot(r->requests,&p,now,15000,false);
            }
            else if(p.kind==ROOM_LEAVE && index>0 && trilink_room_synchronized(r,now)
                && p.revision==r->state.revision && p.term==r->state.term
                && r->state.revision<UINT32_MAX) remove_member(r,(unsigned)index,now);
        }
    }
    if(p.ttl) { --p.ttl; (void)emit(r,&p,false); }
}
void trilink_room_tick(trilink_room_t *r, uint64_t now)
{
    if(!r) return;
    for(unsigned i=0;i<6;++i) {
        if(now>=r->nearby[i].expires) r->nearby[i].valid=false;
        if(now>=r->requests[i].expires) r->requests[i].valid=false;
        if(now>=r->invitations[i].expires) r->invitations[i].valid=false;
    }
    if(now>=r->next_advert && (trilink_room_is_leader(r) || trilink_room_retiring(r,now))) {
        trilink_room_packet_t advert=r->state; advert.kind=ROOM_SNAPSHOT;
        (void)emit(r,&advert,true); r->next_advert=now+1000;
    }
    if(r->waiting && !r->join_confirmed && now>=r->request_until) r->waiting=false;
    if(r->waiting && now>=r->next_request) {
        if(r->attempts>=8) return; /* Confirmed-but-unresolved: manual retry only. */
        trilink_room_packet_t request=r->request; (void)emit(r,&request,true);
        ++r->attempts; r->next_request=now+1000;
    }
}
