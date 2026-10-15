# Third-party notices for TriLink 0.16.0

These notices accompany the original firmware images built with ESP-IDF 5.5.5, esp_tinyusb 2.0.0 and TinyUSB 0.18.0~2. Texts were copied from that build's installed SDK, managed components and target toolchain. The project does not vendor the whole SDK or component cache.

| Material | Preserved notices |
| --- | --- |
| ESP-IDF 5.5.5 | `licenses/esp-idf-5.5.5/LICENSE` and component-specific notices |
| FreeRTOS | Its original kernel license |
| lwIP | Its original COPYING |
| ESP Wi-Fi, PHY and coexistence libraries | Their original license files |
| newlib / picolibc | SDK and target-toolchain copying notices |
| mbedTLS | Its original combined license |
| WPA supplicant | COPYING and README containing its distribution terms |
| http-parser / micro-ecc | Their original notices |
| esp_tinyusb / TinyUSB | Apache-2.0 / MIT license texts from the pinned components |
| led_strip 3.0.3 | Original Apache-2.0 license; provenance/local changes remain in `firmware/components/led_strip/` |
| FatFs / Xtensa HAL | Original header attribution/permission excerpts, identified by source |
| GCC target runtime | COPYING3, COPYING3.LIB and COPYING.RUNTIME from the matching Xtensa toolchain |

Some notices are included conservatively for SDK build components even when unused code is discarded by the linker. Inclusion does not claim that all SDK functionality is enabled.

The firmware download includes the same notice bundle. Preserve these materials when redistributing the corresponding images. This publication adds no project-wide license for TriLink's own code; dependency notices apply to their named components.

Upstream references: [ESP-IDF v5.5.5](https://github.com/espressif/esp-idf/tree/v5.5.5), [esp-usb](https://github.com/espressif/esp-usb), [TinyUSB](https://github.com/hathach/tinyusb), [GCC](https://gcc.gnu.org/). Exact managed-component versions and hashes are in `firmware/dependencies.lock`.
