#include "trilink_app.h"
#include <string.h>

void trilink_app_init(trilink_app_dispatcher_t *dispatcher)
{
    if (dispatcher != NULL) memset(dispatcher, 0, sizeof(*dispatcher));
}

bool trilink_app_register(trilink_app_dispatcher_t *dispatcher,
                          const trilink_app_module_t *module)
{
    if (dispatcher == NULL || module == NULL || module->receive == NULL
        || module->capability_bit == 0U
        || (module->capability_bit & (module->capability_bit - 1U)) != 0U
        || module->marker == 0U || module->version == 0U
        || module->wire_size < 2U || module->wire_size > TRILINK_ROUTE_DATA_MAX
        || dispatcher->count >= TRILINK_APP_MAX_MODULES) return false;
    for (unsigned i = 0; i < dispatcher->count; ++i) {
        const trilink_app_module_t *existing = &dispatcher->modules[i];
        if (existing->capability_bit == module->capability_bit
            || existing->marker == module->marker) return false;
    }
    dispatcher->modules[dispatcher->count++] = *module;
    return true;
}

bool trilink_app_receive(trilink_app_dispatcher_t *dispatcher,
                         const trilink_route_view_t *message)
{
    if (dispatcher == NULL || message == NULL || message->data == NULL
        || message->length < 2U || message->length > TRILINK_ROUTE_DATA_MAX) return false;
    for (unsigned i = 0; i < dispatcher->count; ++i) {
        trilink_app_module_t *module = &dispatcher->modules[i];
        if (message->data[0] != module->marker) continue;
        if (message->data[1] != module->version || message->length != module->wire_size) return false;
        return module->receive(module->context, message);
    }
    return false;
}

void trilink_app_tick(trilink_app_dispatcher_t *dispatcher, uint64_t now)
{
    if (dispatcher == NULL) return;
    for (unsigned i = 0; i < dispatcher->count; ++i) {
        trilink_app_module_t *module = &dispatcher->modules[i];
        if (module->tick != NULL) module->tick(module->context, now);
    }
}

uint8_t trilink_app_capabilities(const trilink_app_dispatcher_t *dispatcher)
{
    uint8_t mask = 0U;
    if (dispatcher != NULL) {
        for (unsigned i = 0; i < dispatcher->count; ++i)
            mask |= dispatcher->modules[i].capability_bit;
    }
    return mask;
}
