#pragma once
#include "trilink_route.h"

/* Composition-time registration only. Calls and callbacks run on the node
 * owner; no allocation, task, code loading or retained borrowed RX pointer. */
#define TRILINK_APP_MAX_MODULES 4U
#define TRILINK_APP_CAP_RGB 0x01U

typedef struct {
    uint8_t capability_bit, marker, version;
    uint16_t wire_size;
    void *context;
    bool (*receive)(void *context, const trilink_route_view_t *message);
    void (*tick)(void *context, uint64_t now);
} trilink_app_module_t;

typedef struct {
    trilink_app_module_t modules[TRILINK_APP_MAX_MODULES];
    uint8_t count;
} trilink_app_dispatcher_t;

void trilink_app_init(trilink_app_dispatcher_t *dispatcher);
bool trilink_app_register(trilink_app_dispatcher_t *dispatcher,
                          const trilink_app_module_t *module);
bool trilink_app_receive(trilink_app_dispatcher_t *dispatcher,
                         const trilink_route_view_t *message);
void trilink_app_tick(trilink_app_dispatcher_t *dispatcher, uint64_t now);
uint8_t trilink_app_capabilities(const trilink_app_dispatcher_t *dispatcher);
