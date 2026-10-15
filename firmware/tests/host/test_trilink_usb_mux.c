#include "trilink_usb_mux.h"
#include <assert.h>
#include <stdio.h>
#include <string.h>

int main(void)
{
    trilink_usb_mux_t mux = {0};
    assert(!trilink_usb_mux_begin(NULL, TRILINK_USB_OWNER_CDC, 0, 1, "a", 0));
    assert(!trilink_usb_mux_begin(&mux, TRILINK_USB_OWNER_NONE, 0, 1, "a", 0));
    assert(!trilink_usb_mux_begin(&mux, TRILINK_USB_OWNER_WEB, 1, 1, "", 0));
    assert(!trilink_usb_mux_begin(&mux, TRILINK_USB_OWNER_WEB, 1, 1, "a b", 0));
    assert(!trilink_usb_mux_begin(&mux, TRILINK_USB_OWNER_WEB, 1, 1, "a\n", 0));
    char long_nonce[34]; memset(long_nonce, 'n', sizeof(long_nonce)); long_nonce[33] = 0;
    assert(!trilink_usb_mux_begin(&mux, TRILINK_USB_OWNER_WEB, 1, 1, long_nonce, 0));
    long_nonce[32] = 0;
    assert(trilink_usb_mux_begin(&mux, TRILINK_USB_OWNER_WEB, 17, 3, long_nonce, 100));
    assert(mux.ticket == 17 && mux.epoch == 3 && mux.owner == TRILINK_USB_OWNER_WEB);
    assert(!trilink_usb_mux_begin(&mux, TRILINK_USB_OWNER_CDC, 0, 4, "next", 110));
    assert(!trilink_usb_mux_timeout(&mux, 99));
    assert(!trilink_usb_mux_timeout(&mux, 2099));
    assert(trilink_usb_mux_timeout(&mux, 2100));
    assert(!trilink_usb_mux_timeout(&mux, 2200));
    assert(mux.owner == TRILINK_USB_OWNER_WEB && mux.expired);
    assert(!trilink_usb_mux_begin(&mux, TRILINK_USB_OWNER_CDC, 0, 4, "next", 1000000));
    char response[100]; snprintf(response, sizeof(response), "\nTRILINK/3 OK %s ROOM\n", long_nonce);
    assert(trilink_usb_mux_matches(&mux, response));
    assert(!trilink_usb_mux_matches(&mux, "TRILINK/3 OK next ROOM"));
    trilink_usb_mux_release(&mux);
    assert(mux.owner == TRILINK_USB_OWNER_NONE && mux.nonce[0] == 0);
    assert(trilink_usb_mux_begin(&mux, TRILINK_USB_OWNER_CDC, 0, 8, "req", 3000));
    assert(trilink_usb_mux_matches(&mux, "TRILINK/3 ERR req BUSY"));
    assert(trilink_usb_mux_matches(&mux, "TRILINK/3 EMPTY req\n"));
    assert(trilink_usb_mux_matches(&mux, "TRILINK/3 EMPTY req"));
    assert(!trilink_usb_mux_matches(&mux, "TRILINK/3 EMPTY request"));
    assert(!trilink_usb_mux_matches(&mux, "TRILINK/3 EMPTY re"));
    assert(!trilink_usb_mux_matches(&mux, "TRILINK/1 EMPTY req"));
    assert(!trilink_usb_mux_matches(&mux, "TRILINK/3  req"));
    assert(!trilink_usb_mux_matches(&mux, "TRILINK/3 OK"));
    trilink_usb_mux_release(&mux);
    assert(!trilink_usb_mux_matches(&mux, "TRILINK/3 EMPTY req"));
    puts("PASS USB mux: admission, exact deadline, timeout quarantine, owner and nonce isolation");
    return 0;
}
