#pragma once

#include <stdbool.h>
#include <stddef.h>
#include <stdint.h>

#define TRILINK_USB_TRANSACTION_TIMEOUT_MS 2000U
#define TRILINK_USB_MUX_NONCE_MAX 32U

typedef enum {
    TRILINK_USB_OWNER_NONE = 0,
    TRILINK_USB_OWNER_CDC,
    TRILINK_USB_OWNER_WEB
} trilink_usb_owner_t;

/* One bridge command at a time, shared by every physical host transport.
 * A timeout does NOT release ownership: the late bridge reply must be consumed
 * before another bridge command can be admitted. This fails closed if the
 * node worker stops, instead of sending an old result to a new requester. */
typedef struct {
    trilink_usb_owner_t owner;
    uint32_t ticket;
    uint32_t epoch;
    uint64_t started;
    bool expired;
    char nonce[TRILINK_USB_MUX_NONCE_MAX + 1U];
} trilink_usb_mux_t;

bool trilink_usb_mux_begin(trilink_usb_mux_t *mux, trilink_usb_owner_t owner,
                           uint32_t ticket, uint32_t epoch, const char *nonce,
                           uint64_t now);
bool trilink_usb_mux_timeout(trilink_usb_mux_t *mux, uint64_t now);
bool trilink_usb_mux_matches(const trilink_usb_mux_t *mux, const char *reply);
void trilink_usb_mux_release(trilink_usb_mux_t *mux);
