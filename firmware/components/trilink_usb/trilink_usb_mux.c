#include "trilink_usb_mux.h"

#include <string.h>

bool trilink_usb_mux_begin(trilink_usb_mux_t *mux, trilink_usb_owner_t owner,
                           uint32_t ticket, uint32_t epoch, const char *nonce,
                           uint64_t now)
{
    if (!mux || !nonce || mux->owner != TRILINK_USB_OWNER_NONE
        || (owner != TRILINK_USB_OWNER_CDC && owner != TRILINK_USB_OWNER_WEB)) return false;
    size_t size = 0;
    while (nonce[size]) {
        if (size == TRILINK_USB_MUX_NONCE_MAX || nonce[size] <= ' '
            || (unsigned char)nonce[size] > 126U) return false;
        ++size;
    }
    if (!size) return false;
    mux->owner = owner;
    mux->ticket = ticket;
    mux->epoch = epoch;
    mux->started = now;
    mux->expired = false;
    memcpy(mux->nonce, nonce, size + 1U);
    return true;
}

bool trilink_usb_mux_timeout(trilink_usb_mux_t *mux, uint64_t now)
{
    if (!mux || mux->owner == TRILINK_USB_OWNER_NONE || mux->expired
        || now < mux->started || now - mux->started < TRILINK_USB_TRANSACTION_TIMEOUT_MS) return false;
    mux->expired = true;
    return true;
}

bool trilink_usb_mux_matches(const trilink_usb_mux_t *mux, const char *reply)
{
    if (!mux || !reply || mux->owner == TRILINK_USB_OWNER_NONE) return false;
    if (*reply == '\n') ++reply;
    if (strncmp(reply, "TRILINK/3 ", 10U)) return false;
    reply += 10U;
    const char *verb = reply;
    while (*reply && *reply != ' ' && *reply != '\n') ++reply;
    if (reply == verb || *reply != ' ') return false;
    ++reply;
    const size_t size = strlen(mux->nonce);
    return !strncmp(reply, mux->nonce, size)
        && (reply[size] == ' ' || reply[size] == '\n' || reply[size] == '\0');
}

void trilink_usb_mux_release(trilink_usb_mux_t *mux)
{
    if (mux) memset(mux, 0, sizeof(*mux));
}
