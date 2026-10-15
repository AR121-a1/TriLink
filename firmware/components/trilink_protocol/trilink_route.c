#include "trilink_route.h"
#include <string.h>

_Static_assert(sizeof(trilink_route_t) <= 4096U, "route state exceeds 4 KiB budget");

static uint32_t get32(const uint8_t *p)
{
    return (uint32_t)p[0] | (uint32_t)p[1] << 8U
        | (uint32_t)p[2] << 16U | (uint32_t)p[3] << 24U;
}
static void put32(uint8_t *p, uint32_t value)
{
    for (unsigned i = 0; i < 4U; ++i) p[i] = (uint8_t)(value >> (i * 8U));
}
static uint8_t bit(unsigned i) { return (uint8_t)(1U << i); }
static unsigned bits(uint8_t mask)
{
    unsigned count = 0;
    while (mask) { count += mask & 1U; mask >>= 1U; }
    return count;
}
bool trilink_route_mac_valid(const uint8_t mac[6])
{
    if (mac == NULL || (mac[0] & 1U) != 0U) return false;
    unsigned value = 0;
    for (unsigned i = 0; i < 6U; ++i) value |= mac[i];
    return value != 0U;
}
static bool valid(const trilink_route_view_t *m)
{
    if (m == NULL || m->room == 0U || m->epoch == 0U
        || m->boot == 0U || m->message == 0U
        || !trilink_route_mac_valid(m->source) || !trilink_route_mac_valid(m->destination)
        || m->remaining > TRILINK_ROUTE_MAX_MEMBERS - 2U
        || m->visited == 0U || (m->visited & 0xc0U) != 0U
        || bits(m->visited) + m->remaining != TRILINK_ROUTE_MAX_MEMBERS - 1U
        || m->attempt >= TRILINK_ROUTE_ATTEMPTS || m->length > TRILINK_ROUTE_DATA_MAX)
        return false;
    if (m->kind == TRILINK_ROUTE_TOPOLOGY)
        return !memcmp(m->source, m->destination, 6U)
            && m->attempt == 0U && m->length == 6U && m->data != NULL;
    return memcmp(m->source, m->destination, 6U) != 0
        && ((m->kind == TRILINK_ROUTE_ACK && m->length == 0U)
            || (m->kind == TRILINK_ROUTE_DATA && m->length > 0U && m->data != NULL));
}
bool trilink_route_encode(const trilink_route_view_t *m,
                          uint8_t *out, size_t capacity, size_t *length)
{
    if (length != NULL) *length = 0;
    if (!valid(m) || out == NULL || length == NULL
        || capacity < TRILINK_SERVICE_HEADER_SIZE + TRILINK_ROUTE_HEADER_SIZE + m->length)
        return false;
    const uint16_t size = TRILINK_ROUTE_HEADER_SIZE + m->length;
    out[0] = TRILINK_SERVICE_ID_ROUTE; out[1] = TRILINK_ROUTE_FORMAT;
    out[2] = (uint8_t)size; out[3] = (uint8_t)(size >> 8U);
    uint8_t *p = out + TRILINK_SERVICE_HEADER_SIZE;
    put32(p, m->room); put32(p + 4U, m->epoch);
    memcpy(p + 8U, m->source, 6U); memcpy(p + 14U, m->destination, 6U);
    put32(p + 20U, m->boot); put32(p + 24U, m->message);
    p[28] = m->remaining; p[29] = m->kind;
    p[30] = (uint8_t)m->length; p[31] = (uint8_t)(m->length >> 8U);
    p[32] = m->attempt; p[33] = m->visited; p[34] = p[35] = 0U;
    if (m->length) memcpy(p + TRILINK_ROUTE_HEADER_SIZE, m->data, m->length);
    *length = TRILINK_SERVICE_HEADER_SIZE + size;
    return true;
}
bool trilink_route_decode(const uint8_t *payload, size_t length, trilink_route_view_t *m)
{
    trilink_service_view_t service;
    if (m == NULL) return false;
    memset(m, 0, sizeof(*m));
    if (!trilink_service_decode_view(payload, length, &service)
        || service.service_id != TRILINK_SERVICE_ID_ROUTE || service.opcode != TRILINK_ROUTE_FORMAT
        || service.data_len < TRILINK_ROUTE_HEADER_SIZE) return false;
    const uint8_t *p = service.data;
    m->room = get32(p); m->epoch = get32(p + 4U);
    memcpy(m->source, p + 8U, 6U); memcpy(m->destination, p + 14U, 6U);
    m->boot = get32(p + 20U); m->message = get32(p + 24U);
    m->remaining = p[28]; m->kind = p[29];
    m->length = (uint16_t)p[30] | (uint16_t)p[31] << 8U;
    m->attempt = p[32]; m->visited = p[33];
    m->data = m->length ? p + TRILINK_ROUTE_HEADER_SIZE : NULL;
    return p[34] == 0U && p[35] == 0U
        && service.data_len == TRILINK_ROUTE_HEADER_SIZE + m->length && valid(m);
}
static int member(const trilink_route_t *node, const uint8_t mac[6])
{
    if (mac == NULL) return -1;
    for (unsigned i = 0; i < node->config.count; ++i)
        if (!memcmp(node->config.members[i], mac, 6U)) return (int)i;
    return -1;
}
bool trilink_route_init(trilink_route_t *node, const uint8_t local[6],
                        uint32_t boot, const trilink_route_io_t *io)
{
    if (node == NULL || !trilink_route_mac_valid(local) || boot == 0U
        || io == NULL || io->send == NULL || io->receive == NULL || io->complete == NULL)
        return false;
    memset(node, 0, sizeof(*node));
    memcpy(node->local, local, 6U);
    node->boot = boot; node->next_message = 1U; node->topology_sequence = 1U;
    node->relay_enabled = true; node->io = *io;
    return true;
}
bool trilink_route_configure(trilink_route_t *node, const trilink_route_config_t *config)
{
    if (node == NULL || config == NULL || !config->room || !config->epoch
        || config->count == 0U || config->count > TRILINK_ROUTE_MAX_MEMBERS) return false;
    trilink_route_config_t canonical = {.room=config->room, .epoch=config->epoch, .count=config->count};
    for (unsigned i = 0; i < config->count; ++i) {
        if (!trilink_route_mac_valid(config->members[i])) return false;
        memcpy(canonical.members[i], config->members[i], 6U);
    }
    for (unsigned i = 0; i < canonical.count; ++i)
        for (unsigned j = i + 1U; j < canonical.count; ++j)
            if (memcmp(canonical.members[i], canonical.members[j], 6U) > 0) {
                uint8_t swap[6];
                memcpy(swap, canonical.members[i], 6U);
                memcpy(canonical.members[i], canonical.members[j], 6U);
                memcpy(canonical.members[j], swap, 6U);
            }
    int position = -1;
    for (unsigned i = 0; i < canonical.count; ++i) {
        if (i && !memcmp(canonical.members[i-1U], canonical.members[i], 6U)) return false;
        if (!memcmp(node->local, canonical.members[i], 6U)) position = (int)i;
    }
    if (position < 0) return false;
    if (node->configured) {
        const bool identical = canonical.room == node->config.room && canonical.epoch == node->config.epoch
            && canonical.count == node->config.count
            && !memcmp(canonical.members, node->config.members, sizeof(canonical.members));
        if (identical) return true;
        if (node->pending || canonical.room != node->config.room || canonical.epoch <= node->config.epoch)
            return false;
    }
    node->config = canonical; node->position = (uint8_t)position;
    node->configured = true; node->neighbors = 0U;
    memset(node->links, 0, sizeof(node->links));
    memset(node->seen, 0, sizeof(node->seen)); memset(node->transit, 0, sizeof(node->transit));
    uint8_t roster[1U + TRILINK_ROUTE_MAX_MEMBERS*6U] = {0};
    roster[0] = canonical.count;
    memcpy(roster+1U, canonical.members, sizeof(canonical.members));
    node->membership_digest = trilink_crc32(roster, sizeof(roster));
    node->topology_dirty = true; node->topology_at = 0U;
    return true;
}
void trilink_route_neighbors(trilink_route_t *node, const uint8_t *macs, size_t count)
{
    if (node == NULL || !node->configured || (macs == NULL && count)) return;
    uint8_t mask = 0U;
    for (size_t i = 0; i < count && i < TRILINK_DISCOVERY_MAX_PEERS; ++i) {
        const int index = member(node, macs + i*6U);
        if (index >= 0 && index != node->position) mask |= bit((unsigned)index);
    }
    if (node->neighbors != mask) { node->neighbors = mask; node->topology_dirty = true; }
}
void trilink_route_relay(trilink_route_t *node, bool enabled)
{
    if (node != NULL && node->relay_enabled != enabled) {
        node->relay_enabled = enabled; node->topology_dirty = true;
    }
}
static uint8_t adjacency(const trilink_route_t *node, unsigned index, uint64_t now)
{
    if (index == node->position) return node->neighbors;
    const trilink_route_link_t *row = &node->links[index];
    return row->valid && now < row->expires ? row->neighbors : 0U;
}
/* Recompute over <=6 vertices. Reciprocal fresh edges only; intermediate nodes
 * must advertise relay permission. A visited mask also guards transient loops. */
static int next_hop(const trilink_route_t *node, int destination, uint8_t excluded,
                     uint64_t now, uint8_t *hops)
{
    if (destination < 0 || destination == node->position) return -1;
    uint8_t queue[TRILINK_ROUTE_MAX_MEMBERS], distance[TRILINK_ROUTE_MAX_MEMBERS] = {0};
    uint8_t first[TRILINK_ROUTE_MAX_MEMBERS] = {0};
    uint8_t seen = (uint8_t)(excluded | bit(node->position));
    unsigned head = 0U, tail = 0U;
    queue[tail++] = node->position;
    while (head < tail) {
        unsigned here = queue[head++];
        if (here != node->position && !node->links[here].relay) continue;
        uint8_t neighbors = adjacency(node, here, now);
        for (unsigned i = 0; i < node->config.count; ++i) {
            if ((seen & bit(i)) || !(neighbors & bit(i))
                || !(adjacency(node, i, now) & bit(here))) continue;
            seen |= bit(i); distance[i] = distance[here] + 1U;
            first[i] = here == node->position ? (uint8_t)i : first[here];
            if ((int)i == destination) { if (hops) *hops = distance[i]; return first[i]; }
            queue[tail++] = (uint8_t)i;
        }
    }
    return -1;
}
bool trilink_route_lookup(const trilink_route_t *node, const uint8_t destination[6],
                          uint64_t now, uint8_t next[6], uint8_t *hops)
{
    if (hops) *hops = 0U;
    if (next) memset(next, 0, 6U);
    if (node == NULL || !node->configured || next == NULL) return false;
    int via = next_hop(node, member(node, destination), 0U, now, hops);
    if (via < 0) return false;
    memcpy(next, node->config.members[via], 6U);
    return true;
}
static bool send_wire(trilink_route_t *node, const uint8_t next[6], const trilink_route_view_t *m)
{
    uint8_t wire[TRILINK_PAYLOAD_MAX]; size_t length;
    if (!trilink_route_encode(m, wire, sizeof(wire), &length)) return false;
    if (!node->io.send(node->io.context, next, wire, length, m->kind == TRILINK_ROUTE_ACK)) {
        ++node->queue_full; return false;
    }
    return true;
}
static bool transmit(trilink_route_t *node, const trilink_route_view_t *m, uint64_t now)
{
    int via = next_hop(node, member(node, m->destination), m->visited, now, NULL);
    if (via < 0) { ++node->no_route; return false; }
    return send_wire(node, node->config.members[via], m);
}
static bool advertise(trilink_route_t *node, trilink_route_view_t *m)
{
    static const uint8_t broadcast[6] = {255,255,255,255,255,255};
    return node->neighbors != 0U && send_wire(node, broadcast, m);
}
void trilink_route_tick(trilink_route_t *node, uint64_t now)
{
    if (node == NULL || !node->configured) return;
    /* At most 10 originated control frames/s even under neighbor flapping.
     * Stagger periodic adverts by identity; no separate task or heap. */
    if (now >= node->topology_at && node->topology_sequence != 0U) {
        uint8_t data[6];
        put32(data, node->membership_digest);
        data[4] = node->neighbors; data[5] = node->relay_enabled ? 1U : 0U;
        trilink_route_view_t topology = {.room=node->config.room, .epoch=node->config.epoch,
            .boot=node->boot, .message=node->topology_sequence++, .remaining=4U,
            .kind=TRILINK_ROUTE_TOPOLOGY, .visited=bit(node->position), .length=6U, .data=data};
        memcpy(topology.source, node->local, 6U); memcpy(topology.destination, node->local, 6U);
        (void)advertise(node, &topology);
        node->topology_dirty = false;
        node->topology_at = now + TRILINK_ROUTE_TOPOLOGY_MS + node->position*31U;
    } else if (node->topology_dirty && node->topology_at > now + 100U) {
        node->topology_at = now + 100U;
    }
    if (!node->pending || now < node->retry_at) return;
    if (node->attempts >= TRILINK_ROUTE_ATTEMPTS) {
        node->pending = false; ++node->timeouts;
        node->io.complete(node->io.context, node->pending_message, TRILINK_ROUTE_TIMEOUT);
        return;
    }
    trilink_route_view_t m = {.room=node->config.room, .epoch=node->config.epoch,
        .boot=node->boot, .message=node->pending_message, .remaining=4U,
        .kind=TRILINK_ROUTE_DATA, .attempt=(uint8_t)node->attempts,
        .visited=bit(node->position), .length=node->length, .data=node->body};
    memcpy(m.source, node->local, 6U); memcpy(m.destination, node->destination, 6U);
    (void)transmit(node, &m, now);
    ++node->attempts; node->retry_at = now + TRILINK_ROUTE_RETRY_MS;
}
bool trilink_route_submit(trilink_route_t *node, const uint8_t destination[6],
                          const uint8_t *data, size_t length, uint64_t now, uint32_t *message)
{
    if (message) *message = 0U;
    if (node == NULL || !node->configured || node->pending || message == NULL
        || member(node, destination) < 0 || !memcmp(destination, node->local, 6U)
        || data == NULL || length == 0U || length > TRILINK_ROUTE_DATA_MAX || !node->next_message)
        return false;
    memcpy(node->destination, destination, 6U); memcpy(node->body, data, length);
    node->length = (uint16_t)length; node->pending_message = node->next_message++;
    *message = node->pending_message; node->pending = true;
    node->attempts = 0U; node->retry_at = now; trilink_route_tick(node, now);
    return true;
}
static void topology_input(trilink_route_t *node, trilink_route_view_t *m, unsigned source, uint64_t now)
{
    uint8_t mask = (uint8_t)((1U << node->config.count) - 1U);
    if (get32(m->data) != node->membership_digest || (m->data[4] & (uint8_t)~mask)
        || (m->data[4] & bit(source)) || m->data[5] > 1U) { ++node->rejected; return; }
    trilink_route_link_t *row = &node->links[source];
    if (row->valid) {
        if (m->boot == row->boot && m->message <= row->sequence) {
            if (m->message != row->sequence || now >= row->expires
                || m->remaining <= row->best_remaining || row->neighbors != m->data[4]
                || row->relay != (m->data[5] != 0U)) return;
            /* A shorter arriving path may propagate farther; duplicate LSA
             * never renews age. TTL improvement bounds repeated forwarding. */
            row->best_remaining = m->remaining;
            if (node->relay_enabled && m->remaining) {
                --m->remaining; m->visited |= bit(node->position);
                if (advertise(node, m)) ++node->topology_forwarded;
            }
            return;
        }
        if (m->boot != row->boot) {
            if (m->boot == row->retired_boot[0] || m->boot == row->retired_boot[1]) return;
            row->retired_boot[1] = row->retired_boot[0]; row->retired_boot[0] = row->boot;
        }
    }
    row->valid = true; row->boot = m->boot; row->sequence = m->message;
    row->neighbors = m->data[4]; row->relay = m->data[5] != 0U;
    row->best_remaining = m->remaining;
    row->expires = now + TRILINK_ROUTE_TOPOLOGY_TTL_MS;
    ++node->topology_received;
    if (node->relay_enabled && m->remaining) {
        --m->remaining; m->visited |= bit(node->position);
        if (advertise(node, m)) ++node->topology_forwarded;
    }
}
static bool same_message(const trilink_route_seen_t *entry, const trilink_route_view_t *m)
{
    return entry->boot == m->boot && entry->message == m->message
        && !memcmp(entry->source, m->source, 6U) && !memcmp(entry->destination, m->destination, 6U);
}
static void remember(trilink_route_seen_t *entry, const trilink_route_view_t *m,
                      uint32_t digest, uint64_t now)
{
    *entry = (trilink_route_seen_t){.valid=true, .boot=m->boot, .message=m->message,
        .digest=digest, .length=m->length, .kind=m->kind, .attempts_seen=bit(m->attempt),
        .expires=now + TRILINK_ROUTE_DEDUP_MS};
    memcpy(entry->source, m->source, 6U); memcpy(entry->destination, m->destination, 6U);
}
void trilink_route_input(trilink_route_t *node, const uint8_t previous[6],
                         const uint8_t *payload, size_t length, uint64_t now)
{
    if (node == NULL) return;
    trilink_route_view_t m;
    if (!node->configured || !trilink_route_decode(payload, length, &m)
        || m.room != node->config.room || m.epoch != node->config.epoch) {
        ++node->rejected; return;
    }
    int source = member(node, m.source), destination = member(node, m.destination), prior = member(node, previous);
    uint8_t mask = (uint8_t)((1U << node->config.count) - 1U);
    if (source < 0 || destination < 0 || prior < 0 || source == node->position
        || !(node->neighbors & bit((unsigned)prior)) || !(m.visited & bit((unsigned)source))
        || !(m.visited & bit((unsigned)prior)) || (m.visited & bit(node->position))
        || (m.visited & (uint8_t)~mask)) { ++node->rejected; return; }
    if (m.kind == TRILINK_ROUTE_TOPOLOGY) { topology_input(node, &m, (unsigned)source, now); return; }
    uint32_t digest = m.kind == TRILINK_ROUTE_DATA ? trilink_crc32(m.data, m.length) : 0U;
    if (destination != node->position) {
        if (!node->relay_enabled || !m.remaining) { ++node->rejected; return; }
        trilink_route_seen_t *slot = NULL;
        for (unsigned i = 0; i < TRILINK_ROUTE_CACHE_SIZE; ++i) {
            trilink_route_seen_t *entry = &node->transit[i];
            if (!entry->valid || now >= entry->expires) { if (!slot) slot = entry; }
            else if (same_message(entry, &m) && entry->kind == m.kind) {
                if (entry->digest != digest || entry->length != m.length) { ++node->rejected; return; }
                if (entry->attempts_seen & bit(m.attempt)) { ++node->transit_duplicates; return; }
                slot = entry;
                break;
            }
        }
        if (!slot) { ++node->queue_full; return; }
        --m.remaining; m.visited |= bit(node->position);
        if (transmit(node, &m, now)) {
            uint8_t attempts = slot->valid && now < slot->expires ? slot->attempts_seen : 0U;
            uint64_t expires = attempts ? slot->expires : now + TRILINK_ROUTE_TRANSIT_MS;
            remember(slot, &m, digest, now);
            slot->attempts_seen |= attempts; slot->expires = expires;
            ++node->forwarded;
        }
        return;
    }
    if (m.kind == TRILINK_ROUTE_ACK) {
        if (node->pending && m.boot == node->boot && m.message == node->pending_message
            && m.attempt < node->attempts && !memcmp(m.source, node->destination, 6U)) {
            node->pending = false;
            node->io.complete(node->io.context, m.message, TRILINK_ROUTE_ACCEPTED);
        } else ++node->rejected;
        return;
    }
    trilink_route_seen_t *slot = NULL;
    bool duplicate = false;
    for (unsigned i = 0; i < TRILINK_ROUTE_CACHE_SIZE; ++i) {
        trilink_route_seen_t *entry = &node->seen[i];
        if (!entry->valid || now >= entry->expires) { if (!slot) slot = entry; }
        else if (same_message(entry, &m)) {
            if (entry->digest != digest || entry->length != m.length) { ++node->rejected; return; }
            duplicate = true; ++node->duplicates; break;
        }
    }
    if (!duplicate) {
        if (!slot || !node->io.receive(node->io.context, &m)) { ++node->queue_full; return; }
        remember(slot, &m, digest, now); ++node->delivered;
    }
    memcpy(m.destination, m.source, 6U); memcpy(m.source, node->local, 6U);
    m.kind = TRILINK_ROUTE_ACK; m.remaining = 4U; m.visited = bit(node->position);
    m.length = 0U; m.data = NULL;
    /* ACK uses the request boot/message/attempt. A later retry is a fresh
     * forwarding attempt but never a second application delivery. */
    (void)transmit(node, &m, now);
}
