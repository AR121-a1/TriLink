#pragma once
#include "trilink_route_control.h"
#include "esp_err.h"

#define TRILINK_BRIDGE_REPLY_SIZE 512U

/* Init before node task. process/tick/input run ONLY on node owner task.
 * USB is sole producer of commands and sole consumer of text replies. */
esp_err_t trilink_bridge_init(const uint8_t mac[6], uint32_t boot);
bool trilink_bridge_submit(const trilink_route_command_t *command);
bool trilink_bridge_reply(char output[TRILINK_BRIDGE_REPLY_SIZE]);
void trilink_bridge_process(uint64_t now);
void trilink_bridge_neighbors(const uint8_t *macs, size_t count);
void trilink_bridge_input(const uint8_t previous[6], const uint8_t *payload,
                          size_t length, uint64_t now);

/* Node owner encodes outer frame; this copies before returning. */
esp_err_t trilink_node_queue_payload(const uint8_t destination[6],
                                    const uint8_t *payload, size_t length,
                                    bool acknowledgement);
