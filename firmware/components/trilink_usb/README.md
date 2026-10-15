# TriLink 0.16.0 USB transport boundary

This component replaces the fixed USB Serial/JTAG **application data** driver
with a TinyUSB full-speed composite device. UART0/CH343 remains the diagnostic
and flashing path; there is no second owner of the internal USB PHY.

## Fixed descriptors

| Property | Value |
| --- | --- |
| VID / PID | `303A:4011`, development identity only, not a production allocation |
| bcdDevice | `0160` |
| Product | `TriLink USB Node` |
| Serial | Original Wi-Fi/ESP-NOW MAC as 12 uppercase hexadecimal characters |
| CDC | Interface `00/01`, string `TriLink CDC`, endpoints `81`, `02/82` |
| NCM | Interface `02/03`, string `TriLink NCM`, endpoints `83`, `04/84` |
| Power / wake | Bus powered, declared 500 mA, no remote-wakeup claim |

Four IN and two OUT endpoints fit the S3 endpoint budget. No HID, MSC, DFU,
USB host, external PHY or filesystem is added. Windows 10 can keep using CDC
even when no NCM driver is present. NCM acceptance must be verified on the
actual Windows 11/Linux host; successful compilation is not enumeration proof.

The serial data protocol stays `TRILINK/1` and `TRILINK/3`. Read/write readiness
does not use DTR/RTS or `tud_cdc_connected()`. TinyUSB FIFO persistence is
explicitly disabled; its `cdcd_reset` clears RX and TX on a USB bus reset.
The worker also clears partial parsers after an epoch change or 500 ms gap.
Replies from a previous enumeration epoch are discarded rather than written to
the newly enumerated CDC session.

## Local-only IP endpoint

S3 address is `192.168.240.1/24`. DHCP leases are `.2` through `.3`: IDF 5.5.5
rejects a range whose start equals its end, so two addresses are the smallest
explicit range. Router and DNS options are disabled before DHCP starts, gateway
is zero, and forwarding/NAT/IPv6 are disabled. This is not a Wi-Fi STA bridge,
internet gateway or transparent ESP-NOW Ethernet tunnel.

The host NCM adapter and S3 IP endpoint use different locally administered MACs
derived from the physical MAC. The NCM string is initialized before TinyUSB
installation, then `tinyusb_net_init` installs the same host MAC at string
index 6 (the pinned CDC1+NCM descriptor ordering).

RX has two fixed, aligned 1514-byte frame slots. TinyUSB RX buffers are never
retained; copying one Ethernet frame allows lwIP to release its exact slot
through `driver_free_rx_buffer`. Exhaustion drops a frame rather than allocating
an unbounded queue. TX calls the synchronous TinyUSB API from the sole lwIP
owner with a 20 ms wait budget; no asynchronous per-frame allocation is used.
NCM has two 2048-byte NTBs per direction. TCP/IP and TinyUSB internal allocations
still exist and require runtime heap/stack measurements on hardware.

## One command owner for CDC and HTTP

`trilink_usb.c` alternates CDC and HTTP admission. Both share one 20 commands/s
limit and one node-bridge transaction. HTTP handlers only enqueue into their
bounded mailbox; they never call the node bridge. Each HTTP ticket is completed
on every taken-path outcome, including parse rejection, rate limiting, expired
intent, bridge rejection and result timeout.

The pure-C `trilink_usb_mux` binds owner/ticket/nonce/enumeration epoch. After a
2000 ms timeout it returns `RESULT_UNKNOWN`, but **retains** bridge ownership
until the matching late reply is drained. It never releases ownership merely
because the caller timed out, and it never automatically replays a mutation.
If the node task stops replying, this intentionally fails closed while local
HELLO/SEARCH and diagnostics remain available.

## Reproducible dependencies / source evidence

- ESP-IDF `5.5.5`, unchanged board defaults/partitions.
- `espressif/esp_tinyusb ==2.0.0`.
- `espressif/tinyusb ==0.18.0~2`.
- `dependencies.lock` records the downloaded component hashes.
- [Official IDF 5.5.5 NCM example](https://github.com/espressif/esp-idf/tree/v5.5.5/examples/peripherals/usb/device/tusb_ncm) supplied API guidance only; its Wi-Fi forwarding logic is not copied.
- [esp_tinyusb 2.0.0 source](https://github.com/espressif/esp-usb/tree/b8c36cdd1e748ad6f7b4b2b556b6b028a0baa61d/device/esp_tinyusb) establishes descriptor index, CDC DTR-independent I/O, and synchronous NCM ownership.

Do not flash as a side effect of building. Physical acceptance must separately
cover CDC on Win10, composite enumeration/DHCP on Win11, DTR=false, unplug/replug,
existing network route preservation, HTTP/CDC contention, and RF operation.
