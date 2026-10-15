# TriLink 0.16.0 development release

Publication date: 2026-10-15. Original PC/firmware builds: 2026-10-12. Individual hardware observations: 2026-10-14.

## Downloads and source

- [Windows client ZIP](TriLink-NodeConsole-0.16.0-Release.zip): the original verified complete 22-file portable runtime.
- [ESP32-S3 firmware ZIP](TriLink-Firmware-0.16.0.zip): original bootloader, partition table and application images, relative flashing map, build-input manifest and dependency notices.
- [Artifact manifest](manifest.json): exact sizes, SHA-256 and original firmware offsets.
- [SHA256SUMS](SHA256SUMS): portable client, firmware package and validation-summary checksums.
- [Public validation summary](validation-summary.json): a derived portable summary, with machine paths and physical-device identifiers omitted.
- [Client source](../../src/), [firmware source](../../firmware/), [architecture](../../docs/ARCHITECTURE_0160.md) and [development guide](../../docs/DEVELOPMENT_GUIDE_0160.md).
- [Third-party notices](../../third_party/README.md).

The source tag is `v0.16.0`. This publication supersedes the earlier client-only/no-firmware-upload policy. Older dated records describe their original state. Flash backups, NVS/PHY dumps, user module configuration, raw machine logs, SDK caches and development scratch files are not release content.

## Current functionality and known issue

The PC console retains the plugin platform, shared node session, bounded foreground queue, controlled polling and Room/RGB UI. The S3 owns discovery, routing, volatile Room state and the existing RGB handler. The USB layer includes CDC and NCM, with a bounded local HTTP management interface.

All three boards individually passed backup, programming, independent Flash verification, 35-second idle startup and native CDC HELLO/CAPS/ROOMGET/SEARCH. The CDC path is the accepted control path from these observations.

**NCM is not accepted:** on the tested Windows host, all three NCM adapters matched Microsoft UsbNcm but failed with Code 10 / `0xC0000483`. The pinned TinyUSB has a strong match to the upstream NTB-divisor compatibility issue. The fix has not been backported or tested in these images. A compiled web server does not establish usable NCM/DHCP/web access.

This is a development/pre-release snapshot. Windows 10 hardware, interactive client/device integration for this image, multi-board Room/RGB, loaded runtime resources and optical output remain unverified. Text P2P, files, HID wake, MSC and OTA are not implemented capabilities.

## PC use

Extract the entire client ZIP into a new directory. It requires Windows and .NET Framework 4.8. Exit the old instance through its tray Exit command before starting the new complete runtime. X hides to the tray. Do not mix EXE and DLL versions.

Use the S3 native USB data port for CDC control. CH343/CH340 is the programming/recovery UART. A new firmware USB identity can receive a new COM number; identify the actual attached device rather than copying a historical port number.

## Firmware use

Target: ESP32-S3 with the tested 16 MiB Flash / 8 MiB octal PSRAM configuration, DIO / 80 MHz Flash. The factory App partition remains 1 MiB; this layout has no OTA slots. Read the package README, inspect your board and back up its affected regions before using the three images. No device-specific Flash dump is distributed.

The firmware ZIP documents esptool 4.12.0 commands. Do not apply the package to another MCU, encrypted/secure-boot target or different partition layout by adding force options. After switching from UART to native USB, release BOOT and press RESET if the board still appears in ROM download mode.

## Source and tooling provenance

The released BINs and PC ZIP are the original verified artifacts; this upload is not a new firmware build. Their original build-input manifests describe the Oct12 inputs. Source publication keeps the application C/C# code and pinned firmware configuration, while the public firmware contract-test entry is adapted to locate the client parser in this repository and discover Roslyn instead of depending on an old local sibling folder. See [public firmware build notes](../../firmware/PUBLIC_BUILD.md) for exact tool-only differences.

Public summaries do not overwrite the original local receipts. Firmware source/image hashes, software checks and later single-board observations remain distinct.
