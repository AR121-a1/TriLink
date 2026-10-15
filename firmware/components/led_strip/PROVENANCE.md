# Local driver provenance

Originally copied without source edits from the existing ESP32-S3 USB/ESP-NOW reference project's
`remote_node/managed_components/espressif__led_strip` on 2026-09-17.
Package manifest: Espressif led_strip 3.0.3, upstream commit
`7cd447361ca2f0a1c01aa3089e3031f6171b6c7e`, path `led_strip`.
The original LICENSE, CHECKSUMS.json and component manifest are retained.
No SDK upgrade, package download or upstream reference project edit was performed.

The application executor follows the HW-678A BSP's GPIO48, WS2812 GRB, one LED,
10 MHz RMT setup. The third board's LED wiring is unverified: local opt-in is
required on every boot and no output pin is configured before the first enabled job.

2026-10-01: TriLink locally patched `src/led_strip_rmt_dev.c` for nonblocking
submission, a 100 ms completion wait, channel cleanup and fault-latched payload
ownership. The upstream `CHECKSUMS.json` remains provenance for the original
package, not a checksum assertion for the patched source. The current project's
build source-manifest records actual patched file hashes. See
[local patch scope](LOCAL_PATCHES.md). SDK and the reference project were not edited.
