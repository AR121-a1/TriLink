# Public firmware source and build notes

Publication preparation: 2026-10-15. Firmware version: 0.16.0.

## Repository layout and publication boundary

The public repository places the Windows client at the repository root and the
firmware in `firmware/`. Shared architecture and development instructions are in
[the architecture reference](../docs/ARCHITECTURE_0160.md) and
[the development guide](../docs/DEVELOPMENT_GUIDE_0160.md).

The firmware source closure includes `bootloader_components/`, `components/`,
`main/`, `tools/`, `tests/`, `CMakeLists.txt`, `sdkconfig.defaults`, and
`dependencies.lock`. Preserve the pinned ESP-IDF 5.5.5, esp_tinyusb 2.0.0 and
TinyUSB 0.18.0~2 dependencies. Keep the vendored led_strip license, provenance,
upstream checksum record and local patch description together with its source.

Generated SDK caches, build trees and raw hardware evidence are not source
inputs to upload. In particular, Flash backups can contain NVS/PHY or local
configuration and remain private. Historical `artifacts/...` paths in the
hardware documents identify excluded local evidence, not public downloads.
Statements such as "not uploaded" in dated records describe that historical
operation; the 2026-10-15 publication request supersedes the former source and
firmware-image exclusion, not the protection of backups or private logs.

## Build prerequisites and commands

Use Windows PowerShell with ESP-IDF 5.5.5, host GCC, Python, Node.js and Visual
Studio / Build Tools with MSBuild and Roslyn. The existing default SDK/compiler
paths describe the original workstation; pass overrides on another machine.
No command below flashes hardware or opens a serial port.

From the `firmware/` directory:

```powershell
# C-generated USB fixtures checked by the real PC protocol parser.
.\tools\test_usb_contract.ps1 -Compiler 'D:\tools\mingw64\bin\gcc.exe'

# Explicit overrides are supported; this root contains src/Plugins/...
.\tools\test_usb_contract.ps1 -Compiler 'D:\tools\mingw64\bin\gcc.exe' `
  -ClientDirectory '..' -CscPath 'C:\BuildTools\MSBuild\Current\Bin\Roslyn\csc.exe'

# Full source build: replace example tool paths with actual installations.
.\tools\build_local.ps1 `
  -IdfProfile 'C:\Espressif\tools\Microsoft.v5.5.5.PowerShell_profile.ps1' `
  -HostCompiler 'D:\tools\mingw64\bin\gcc.exe' `
  -HostPython 'C:\Espressif\tools\python\v5.5.5\venv\Scripts\python.exe' `
  -HostNode 'node.exe'
```

The USB contract test auto-selects the first existing parser in this order:

1. The firmware directory's parent repository root.
2. Sibling `trilink_game_branch` (original development layout).
3. Sibling `trilink_min_client` (legacy layout).

An explicit `-ClientDirectory` is authoritative and is not silently replaced
when invalid. `-CscPath` is also authoritative. Without it, the test locates
MSBuild installations using Visual Studio Installer's `vswhere.exe`, then the
current developer environment's `VSINSTALLDIR`, and validates the actual
`MSBuild/Current/Bin/Roslyn/csc.exe` file. A missing compiler fails clearly; it
does not download or install tools. `build_local.ps1` forwards both overrides.

The test fixture's historical success message names the "v0.13" wire contract.
In this checkout it checks that legacy TRILINK/1 contract using the selected
current client parser; it is not a claim to have built the old client product.

## Frozen images versus publication tooling

The published 0.16.0 images are the existing build
`20261012-173202-b0407e2b`, not a new build made on publication day. Its original
130-input source manifest remains evidence of that build. Application C,
component CMake files, SDK defaults and the dependency lock are unchanged.

Exactly two build-input scripts were adapted for the public repository layout:

| File | Publication-only change |
| --- | --- |
| `tools/test_usb_contract.ps1` | Locate client parser in the public/root and legacy layouts; discover Roslyn or accept `-CscPath` |
| `tools/build_local.ps1` | Accept and forward optional `-ClientDirectory` and `-CscPath` |

These two files intentionally differ from the frozen binary source manifest.
Documentation links and `.gitignore` were also updated, and this guide was
added. Do not label the public tree as a byte-identical 130-input reproduction
of the historical build. A future full build gets its own new source manifest
and receipt; changing publication tooling alone does not qualify the firmware
image or NCM interoperability again.

| Existing image | Flash offset | Bytes | SHA-256 |
| --- | --- | ---: | --- |
| `bootloader.bin` | `0x0` | 20944 | `08A35F2DF9F39B70A33E8EB7C481FC971231299663E59ACE48A41ED49C5E74C7` |
| `partition-table.bin` | `0x8000` | 3072 | `7F00B6C042A89B15B0CAC534F82ED988CAF29278FF5700B0C511EB1B5BB7C820` |
| `esp32s3_wakenic_p2p.bin` | `0x10000` | 898736 | `F5EDD8FB4D35C56614782453E63DAE310DF192F2B6E9CCB5383A366E73CD4143` |

The three images are not a full Flash backup. Publishing them does not
authorize flashing, erasing NVS or altering eFuses. The existing build uses
ESP32-S3, DIO, 80 MHz Flash and 16 MB physical Flash, with the unchanged 1 MiB
factory application partition.

## Hardware status retained

The [2026-10-14 three-board record](docs/HARDWARE_0160_ROUND_20261014.md)
documents separate single-board Flash, startup and native CDC checks. The same
image produced NCM Code10 on this host for all three boards. No NCM patch or new
firmware build is part of the publication portability change. CDC is the
accepted control path for that round; DHCP/web, multi-board Room/RGB, Windows
10 and long-load acceptance must not be inferred from it.
