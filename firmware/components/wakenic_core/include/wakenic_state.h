#pragma once

#include <stdbool.h>
#include <stdint.h>

#ifdef __cplusplus
extern "C" {
#endif

typedef enum {
    WAKENIC_STATE_BOOT = 0,
    WAKENIC_STATE_INIT,
    WAKENIC_STATE_CORE_READY,
    WAKENIC_STATE_FAULT,
    WAKENIC_STATE_COUNT,
} wakenic_state_t;

typedef enum {
    WAKENIC_EVENT_APP_BOOT = 0,
    WAKENIC_EVENT_BOARD_SELF_TEST_OK,
    WAKENIC_EVENT_WIFI_PORT_REGISTERED,
    WAKENIC_EVENT_USB_PORT_REGISTERED,
    WAKENIC_EVENT_FATAL,
    WAKENIC_EVENT_COUNT,
} wakenic_event_type_t;

typedef struct {
    wakenic_event_type_t type;
    uint32_t correlation_id;
    int32_t status;
} wakenic_event_t;

typedef struct {
    wakenic_state_t state;
    bool board_ready;
    bool wifi_port_registered;
    bool usb_port_registered;
} wakenic_state_model_t;

typedef enum {
    WAKENIC_REDUCE_ACCEPTED = 0,
    WAKENIC_REDUCE_DUPLICATE,
    WAKENIC_REDUCE_FAULTED,
    WAKENIC_REDUCE_INVALID,
} wakenic_reduce_result_t;

void wakenic_state_model_init(wakenic_state_model_t *model);

/* Pure reducer: status == 0 means success; no RTOS or ESP-IDF dependency. */
wakenic_reduce_result_t wakenic_state_reduce(
    wakenic_state_model_t *model,
    const wakenic_event_t *event);

const char *wakenic_state_name(wakenic_state_t state);

const char *wakenic_event_name(wakenic_event_type_t event);

#ifdef __cplusplus
}
#endif
