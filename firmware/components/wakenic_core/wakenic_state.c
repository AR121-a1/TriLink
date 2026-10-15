#include "wakenic_state.h"

#include <stddef.h>

static const char *const s_state_names[WAKENIC_STATE_COUNT] = {
    [WAKENIC_STATE_BOOT] = "BOOT",
    [WAKENIC_STATE_INIT] = "INIT",
    [WAKENIC_STATE_CORE_READY] = "CORE_READY",
    [WAKENIC_STATE_FAULT] = "FAULT",
};

static const char *const s_event_names[WAKENIC_EVENT_COUNT] = {
    [WAKENIC_EVENT_APP_BOOT] = "APP_BOOT",
    [WAKENIC_EVENT_BOARD_SELF_TEST_OK] = "BOARD_SELF_TEST_OK",
    [WAKENIC_EVENT_WIFI_PORT_REGISTERED] = "WIFI_PORT_REGISTERED",
    [WAKENIC_EVENT_USB_PORT_REGISTERED] = "USB_PORT_REGISTERED",
    [WAKENIC_EVENT_FATAL] = "FATAL",
};

void wakenic_state_model_init(wakenic_state_model_t *model)
{
    if (model == NULL) {
        return;
    }

    *model = (wakenic_state_model_t) {
        .state = WAKENIC_STATE_BOOT,
    };
}

static wakenic_reduce_result_t accept_init_event(
    wakenic_state_model_t *model,
    wakenic_event_type_t type)
{
    bool *flag = NULL;

    switch (type) {
    case WAKENIC_EVENT_BOARD_SELF_TEST_OK:
        flag = &model->board_ready;
        break;
    case WAKENIC_EVENT_WIFI_PORT_REGISTERED:
        flag = &model->wifi_port_registered;
        break;
    case WAKENIC_EVENT_USB_PORT_REGISTERED:
        flag = &model->usb_port_registered;
        break;
    default:
        return WAKENIC_REDUCE_INVALID;
    }

    if (*flag) {
        return WAKENIC_REDUCE_DUPLICATE;
    }

    *flag = true;
    if (model->board_ready && model->wifi_port_registered
        && model->usb_port_registered) {
        model->state = WAKENIC_STATE_CORE_READY;
    }

    return WAKENIC_REDUCE_ACCEPTED;
}

wakenic_reduce_result_t wakenic_state_reduce(
    wakenic_state_model_t *model,
    const wakenic_event_t *event)
{
    if (model == NULL) {
        return WAKENIC_REDUCE_INVALID;
    }

    if ((unsigned)model->state >= WAKENIC_STATE_COUNT) {
        model->state = WAKENIC_STATE_FAULT;
        return WAKENIC_REDUCE_FAULTED;
    }

    if ((event == NULL) || ((unsigned)event->type >= WAKENIC_EVENT_COUNT)) {
        return WAKENIC_REDUCE_INVALID;
    }

    if ((event->type == WAKENIC_EVENT_FATAL) || (event->status != 0)) {
        model->state = WAKENIC_STATE_FAULT;
        return WAKENIC_REDUCE_FAULTED;
    }

    switch (model->state) {
    case WAKENIC_STATE_BOOT:
        if (event->type != WAKENIC_EVENT_APP_BOOT) {
            return WAKENIC_REDUCE_INVALID;
        }
        model->state = WAKENIC_STATE_INIT;
        return WAKENIC_REDUCE_ACCEPTED;

    case WAKENIC_STATE_INIT:
        return accept_init_event(model, event->type);

    case WAKENIC_STATE_CORE_READY:
    case WAKENIC_STATE_FAULT:
    case WAKENIC_STATE_COUNT:
        return WAKENIC_REDUCE_INVALID;
    }

    model->state = WAKENIC_STATE_FAULT;
    return WAKENIC_REDUCE_FAULTED;
}

const char *wakenic_state_name(wakenic_state_t state)
{
    if ((unsigned)state >= WAKENIC_STATE_COUNT) {
        return "UNKNOWN_STATE";
    }
    return s_state_names[state];
}

const char *wakenic_event_name(wakenic_event_type_t event)
{
    if ((unsigned)event >= WAKENIC_EVENT_COUNT) {
        return "UNKNOWN_EVENT";
    }
    return s_event_names[event];
}
