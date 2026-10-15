#pragma once

#include "esp_err.h"
#include "wakenic_state.h"

#ifdef __cplusplus
extern "C" {
#endif

esp_err_t wakenic_core_init(void);

/*
 * Task-context API. FATAL is queued at the front but can still fail when the
 * queue is full; every caller must handle a failed enqueue.
 */
esp_err_t wakenic_core_post(wakenic_event_type_t type,
                            uint32_t correlation_id,
                            int32_t status);

/* Single-owner entry point: call exactly once from app_main; never returns. */
void wakenic_core_run(void);

#ifdef __cplusplus
}
#endif
