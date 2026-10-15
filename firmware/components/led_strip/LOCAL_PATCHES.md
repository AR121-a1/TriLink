# TriLink local RMT patch, 2026-10-01

Upstream baseline: Espressif led_strip 3.0.3, commit
`7cd447361ca2f0a1c01aa3089e3031f6171b6c7e`. License and upstream checksums retained.
Only `src/led_strip_rmt_dev.c` is patched; current hashes are in each build receipt's
source-manifest. No claim that the patched file matches its upstream checksum.

## Scope

- `rmt_transmit` sets `queue_nonblocking=1`.
- `rmt_tx_wait_all_done` waits 100 ms; this executor owns a single LED, submits
  sequentially and latches failures, so there is at most one inflight transaction.
- Transmit/wait errors still attempt `rmt_disable`; the original failure is returned.
- A failed stop retains channel state and pixel buffer. Fault-latched handles reject
  set/clear/refresh before any payload mutation. There is no new RMT transmission.
- Delete attempts stop before freeing; successfully deleted handles become NULL.
  The application quarantines any delete failure and never retries that cleanup or
  allocates a replacement in this boot, because IDF deletion can partially fail.
- A fault cannot be cleared by toggling enable; coordinate Room exit before a
  device restart. Failure does not guarantee the physical LED is off.

## Exact platform evidence

This is for ESP32-S3, ESP-IDF 5.5.5, GPIO48, WS2812 GRB, one LED, DMA=false.
The SDK's `soc/esp32s3/include/soc/soc_caps.h` defines
`SOC_RMT_SUPPORT_ASYNC_STOP=1`; `esp_driver_rmt/src/rmt_tx.c` uses an asynchronous
stop and zero-wait queue operations for this configuration. Other chips can compile
a polling stop path: this patch's bounded-stop conclusion must not be generalized.
Idle worker queue waits intentionally remain blocking; that worker has no pending
operation at that point.

Host fault tests compile the actual patched driver with fake RMT calls. They verify
API ordering and payload/resource ownership, not hardware/ISR timing or optical output.
The application worker is independently tested with fake FreeRTOS scheduling/LED calls.
