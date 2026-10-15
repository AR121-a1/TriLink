#include "wakenic_state.h"

#include <stdio.h>
#include <string.h>

static unsigned s_checks;
static unsigned s_failures;

#define CHECK(expression)                                                     \
    do {                                                                      \
        ++s_checks;                                                           \
        if (!(expression)) {                                                  \
            ++s_failures;                                                     \
            fprintf(stderr, "FAIL %s:%d: %s\n", __FILE__, __LINE__,          \
                    #expression);                                             \
        }                                                                     \
    } while (0)

static wakenic_event_t make_event(wakenic_event_type_t type, int32_t status)
{
    const wakenic_event_t event = {
        .type = type,
        .correlation_id = 7U,
        .status = status,
    };
    return event;
}

static wakenic_reduce_result_t reduce(wakenic_state_model_t *model,
                                      wakenic_event_type_t type,
                                      int32_t status)
{
    const wakenic_event_t event = make_event(type, status);
    return wakenic_state_reduce(model, &event);
}

static void boot_to_init(wakenic_state_model_t *model)
{
    wakenic_state_model_init(model);
    CHECK(reduce(model, WAKENIC_EVENT_APP_BOOT, 0)
          == WAKENIC_REDUCE_ACCEPTED);
    CHECK(model->state == WAKENIC_STATE_INIT);
}

static void test_all_readiness_orders(void)
{
    static const wakenic_event_type_t permutations[][3] = {
        {WAKENIC_EVENT_BOARD_SELF_TEST_OK,
         WAKENIC_EVENT_WIFI_PORT_REGISTERED,
         WAKENIC_EVENT_USB_PORT_REGISTERED},
        {WAKENIC_EVENT_BOARD_SELF_TEST_OK,
         WAKENIC_EVENT_USB_PORT_REGISTERED,
         WAKENIC_EVENT_WIFI_PORT_REGISTERED},
        {WAKENIC_EVENT_WIFI_PORT_REGISTERED,
         WAKENIC_EVENT_BOARD_SELF_TEST_OK,
         WAKENIC_EVENT_USB_PORT_REGISTERED},
        {WAKENIC_EVENT_WIFI_PORT_REGISTERED,
         WAKENIC_EVENT_USB_PORT_REGISTERED,
         WAKENIC_EVENT_BOARD_SELF_TEST_OK},
        {WAKENIC_EVENT_USB_PORT_REGISTERED,
         WAKENIC_EVENT_BOARD_SELF_TEST_OK,
         WAKENIC_EVENT_WIFI_PORT_REGISTERED},
        {WAKENIC_EVENT_USB_PORT_REGISTERED,
         WAKENIC_EVENT_WIFI_PORT_REGISTERED,
         WAKENIC_EVENT_BOARD_SELF_TEST_OK},
    };

    for (size_t order = 0;
         order < (sizeof(permutations) / sizeof(permutations[0]));
         ++order) {
        wakenic_state_model_t model;
        boot_to_init(&model);
        for (size_t index = 0; index < 3U; ++index) {
            CHECK(reduce(&model, permutations[order][index], 0)
                  == WAKENIC_REDUCE_ACCEPTED);
            CHECK(model.state
                  == ((index == 2U) ? WAKENIC_STATE_CORE_READY
                                    : WAKENIC_STATE_INIT));
        }
        CHECK(model.board_ready);
        CHECK(model.wifi_port_registered);
        CHECK(model.usb_port_registered);
    }
}

static void test_invalid_before_boot(void)
{
    wakenic_state_model_t model;
    wakenic_state_model_init(&model);
    CHECK(reduce(&model, WAKENIC_EVENT_BOARD_SELF_TEST_OK, 0)
          == WAKENIC_REDUCE_INVALID);
    CHECK(model.state == WAKENIC_STATE_BOOT);
    CHECK(!model.board_ready);
}

static void test_early_readiness_is_not_latched(void)
{
    const wakenic_event_type_t readiness[] = {
        WAKENIC_EVENT_BOARD_SELF_TEST_OK,
        WAKENIC_EVENT_WIFI_PORT_REGISTERED,
        WAKENIC_EVENT_USB_PORT_REGISTERED,
    };
    wakenic_state_model_t model;
    wakenic_state_model_init(&model);

    for (size_t index = 0;
         index < (sizeof(readiness) / sizeof(readiness[0]));
         ++index) {
        CHECK(reduce(&model, readiness[index], 0) == WAKENIC_REDUCE_INVALID);
    }
    CHECK(model.state == WAKENIC_STATE_BOOT);
    CHECK(!model.board_ready);
    CHECK(!model.wifi_port_registered);
    CHECK(!model.usb_port_registered);

    CHECK(reduce(&model, WAKENIC_EVENT_APP_BOOT, 0)
          == WAKENIC_REDUCE_ACCEPTED);
    for (size_t index = 0;
         index < (sizeof(readiness) / sizeof(readiness[0]));
         ++index) {
        CHECK(reduce(&model, readiness[index], 0)
              == WAKENIC_REDUCE_ACCEPTED);
    }
    CHECK(model.state == WAKENIC_STATE_CORE_READY);
}

static void test_duplicate_is_idempotent(void)
{
    const wakenic_event_type_t readiness[] = {
        WAKENIC_EVENT_BOARD_SELF_TEST_OK,
        WAKENIC_EVENT_WIFI_PORT_REGISTERED,
        WAKENIC_EVENT_USB_PORT_REGISTERED,
    };

    for (size_t index = 0;
         index < (sizeof(readiness) / sizeof(readiness[0]));
         ++index) {
        wakenic_state_model_t model;
        boot_to_init(&model);
        CHECK(reduce(&model, readiness[index], 0)
              == WAKENIC_REDUCE_ACCEPTED);
        CHECK(reduce(&model, readiness[index], 0)
              == WAKENIC_REDUCE_DUPLICATE);
        CHECK(model.state == WAKENIC_STATE_INIT);
    }

    wakenic_state_model_t app_boot_model;
    boot_to_init(&app_boot_model);
    CHECK(reduce(&app_boot_model, WAKENIC_EVENT_APP_BOOT, 0)
          == WAKENIC_REDUCE_INVALID);
    CHECK(app_boot_model.state == WAKENIC_STATE_INIT);
}

static void test_failed_status_enters_fault(void)
{
    const wakenic_event_type_t failures[] = {
        WAKENIC_EVENT_BOARD_SELF_TEST_OK,
        WAKENIC_EVENT_WIFI_PORT_REGISTERED,
        WAKENIC_EVENT_USB_PORT_REGISTERED,
    };

    for (size_t index = 0;
         index < (sizeof(failures) / sizeof(failures[0]));
         ++index) {
        wakenic_state_model_t model;
        boot_to_init(&model);
        CHECK(reduce(&model, failures[index], -1)
              == WAKENIC_REDUCE_FAULTED);
        CHECK(model.state == WAKENIC_STATE_FAULT);
    }

    wakenic_state_model_t boot_model;
    wakenic_state_model_init(&boot_model);
    CHECK(reduce(&boot_model, WAKENIC_EVENT_APP_BOOT, -1)
          == WAKENIC_REDUCE_FAULTED);
    CHECK(boot_model.state == WAKENIC_STATE_FAULT);

    wakenic_state_model_t duplicate_failure;
    boot_to_init(&duplicate_failure);
    CHECK(reduce(&duplicate_failure, WAKENIC_EVENT_BOARD_SELF_TEST_OK, 0)
          == WAKENIC_REDUCE_ACCEPTED);
    CHECK(reduce(&duplicate_failure, WAKENIC_EVENT_BOARD_SELF_TEST_OK, -1)
          == WAKENIC_REDUCE_FAULTED);
    CHECK(duplicate_failure.state == WAKENIC_STATE_FAULT);

    wakenic_state_model_t ready_failure;
    boot_to_init(&ready_failure);
    CHECK(reduce(&ready_failure, WAKENIC_EVENT_BOARD_SELF_TEST_OK, 0)
          == WAKENIC_REDUCE_ACCEPTED);
    CHECK(reduce(&ready_failure, WAKENIC_EVENT_WIFI_PORT_REGISTERED, 0)
          == WAKENIC_REDUCE_ACCEPTED);
    CHECK(reduce(&ready_failure, WAKENIC_EVENT_USB_PORT_REGISTERED, 0)
          == WAKENIC_REDUCE_ACCEPTED);
    CHECK(reduce(&ready_failure, WAKENIC_EVENT_APP_BOOT, -1)
          == WAKENIC_REDUCE_FAULTED);
    CHECK(ready_failure.state == WAKENIC_STATE_FAULT);
}

static void test_fatal_from_every_state(void)
{
    wakenic_state_model_t models[4];
    wakenic_state_model_init(&models[0]);
    boot_to_init(&models[1]);
    boot_to_init(&models[2]);
    CHECK(reduce(&models[2], WAKENIC_EVENT_BOARD_SELF_TEST_OK, 0)
          == WAKENIC_REDUCE_ACCEPTED);
    CHECK(reduce(&models[2], WAKENIC_EVENT_WIFI_PORT_REGISTERED, 0)
          == WAKENIC_REDUCE_ACCEPTED);
    CHECK(reduce(&models[2], WAKENIC_EVENT_USB_PORT_REGISTERED, 0)
          == WAKENIC_REDUCE_ACCEPTED);
    boot_to_init(&models[3]);
    CHECK(reduce(&models[3], WAKENIC_EVENT_FATAL, 0)
          == WAKENIC_REDUCE_FAULTED);

    for (size_t index = 0; index < 4U; ++index) {
        CHECK(reduce(&models[index], WAKENIC_EVENT_FATAL, 0)
              == WAKENIC_REDUCE_FAULTED);
        CHECK(models[index].state == WAKENIC_STATE_FAULT);
    }
}

static void test_closed_states_and_invalid_inputs(void)
{
    wakenic_state_model_t model;
    boot_to_init(&model);
    CHECK(reduce(&model, WAKENIC_EVENT_BOARD_SELF_TEST_OK, 0)
          == WAKENIC_REDUCE_ACCEPTED);
    CHECK(reduce(&model, WAKENIC_EVENT_WIFI_PORT_REGISTERED, 0)
          == WAKENIC_REDUCE_ACCEPTED);
    CHECK(reduce(&model, WAKENIC_EVENT_USB_PORT_REGISTERED, 0)
          == WAKENIC_REDUCE_ACCEPTED);
    CHECK(reduce(&model, WAKENIC_EVENT_APP_BOOT, 0)
          == WAKENIC_REDUCE_INVALID);
    CHECK(model.state == WAKENIC_STATE_CORE_READY);

    const wakenic_event_t invalid_event = make_event(WAKENIC_EVENT_COUNT, 0);
    CHECK(wakenic_state_reduce(&model, &invalid_event)
          == WAKENIC_REDUCE_INVALID);
    CHECK(wakenic_state_reduce(NULL, &invalid_event)
          == WAKENIC_REDUCE_INVALID);
    CHECK(wakenic_state_reduce(&model, NULL) == WAKENIC_REDUCE_INVALID);

    model.state = WAKENIC_STATE_COUNT;
    const wakenic_event_t valid_event = make_event(WAKENIC_EVENT_APP_BOOT, 0);
    CHECK(wakenic_state_reduce(&model, &valid_event)
          == WAKENIC_REDUCE_FAULTED);
    CHECK(model.state == WAKENIC_STATE_FAULT);

    model.state = WAKENIC_STATE_COUNT;
    CHECK(wakenic_state_reduce(&model, &invalid_event)
          == WAKENIC_REDUCE_FAULTED);
    CHECK(model.state == WAKENIC_STATE_FAULT);
}

static void test_fault_is_absorbing(void)
{
    wakenic_state_model_t model;
    boot_to_init(&model);
    CHECK(reduce(&model, WAKENIC_EVENT_FATAL, -1)
          == WAKENIC_REDUCE_FAULTED);

    for (unsigned type = 0; type < WAKENIC_EVENT_COUNT; ++type) {
        const wakenic_reduce_result_t successful =
            reduce(&model, (wakenic_event_type_t)type, 0);
        CHECK(successful
              == ((type == WAKENIC_EVENT_FATAL) ? WAKENIC_REDUCE_FAULTED
                                                : WAKENIC_REDUCE_INVALID));
        CHECK(model.state == WAKENIC_STATE_FAULT);

        CHECK(reduce(&model, (wakenic_event_type_t)type, -1)
              == WAKENIC_REDUCE_FAULTED);
        CHECK(model.state == WAKENIC_STATE_FAULT);
    }
}

static void test_names(void)
{
    CHECK(strcmp(wakenic_state_name(WAKENIC_STATE_CORE_READY), "CORE_READY")
          == 0);
    CHECK(strcmp(wakenic_event_name(WAKENIC_EVENT_FATAL), "FATAL") == 0);
    CHECK(strcmp(wakenic_state_name(WAKENIC_STATE_COUNT), "UNKNOWN_STATE")
          == 0);
    CHECK(strcmp(wakenic_event_name(WAKENIC_EVENT_COUNT), "UNKNOWN_EVENT")
          == 0);
}

int main(void)
{
    test_all_readiness_orders();
    test_invalid_before_boot();
    test_early_readiness_is_not_latched();
    test_duplicate_is_idempotent();
    test_failed_status_enters_fault();
    test_fatal_from_every_state();
    test_closed_states_and_invalid_inputs();
    test_fault_is_absorbing();
    test_names();

    printf("wakenic_state host tests: checks=%u failures=%u\n",
           s_checks,
           s_failures);
    return (s_failures == 0U) ? 0 : 1;
}
