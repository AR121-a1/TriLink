# 0.16.0 three-board flashing round

> Public copy, redacted for publication on 2026-10-15. Board1/Board2/Board3 are stable public labels, not transmitted MAC addresses. Physical MAC/USB serial/PnP instance identifiers, local COM assignments and backup hashes are withheld. Original private records remain unchanged. Historical local evidence paths are not public downloads.

Date: 2026-10-14, Asia/Shanghai. All three boards received the same existing build `20261012-173202-b0407e2b`, App 0.16.0 / ESP-IDF 5.5.5.

| Label in this round | Public board label | Programming interface | Native CDC observed | Flash/startup/CDC | Detailed record |
| --- | --- | --- | --- | --- | --- |
| 1 | `Board1` | CH343 [port-redacted] | [port-redacted] | Passed individually | [Board1](HARDWARE_0160_BOARD1_20261014.md) |
| 2 | `Board2` | CH343 [port-redacted] | [port-redacted] | Passed individually | [Board2](HARDWARE_0160_BOARD2_20261014.md) |
| 3 | `Board3` | CH340 [port-redacted] | [port-redacted] | Passed after native boot recovery | [Board3](HARDWARE_0160_BOARD3_20261014.md) |

The PC had one board attached at a time. Local COM assignments and physical identities are omitted from this public copy. Board1/Board2/Board3 are stable publication labels; historical per-round connection order is preserved separately and is not a firmware role. Real operation must still verify the actual device identity locally.

For every board, a fresh local `[0,0x110000)` backup passed independent Flash comparison, the existing partition table matched, all three writes and independent digests passed, and a bounded 35-second UART startup capture reached CORE_READY. Native CDC with DTR=false passed HELLO/CAPS/ROOMGET/SEARCH. Each read-only SEARCH observed zero peers.

Board2's first security query had a transport interruption; one retry passed before writing. Board3 initially entered ROM download mode after moving to native USB; a physical BOOT-release/RESET operation restored the application. Both cases have retained first-failure evidence. Neither required a different image or a second flash.

All three NCM interfaces matched Microsoft UsbNcm on this host but failed to start with Code 10/status `0xC0000483`. The first-board record contains the strong match to an upstream TinyUSB NTB-divisor repair. CDC is the accepted control path for this round; the NCM compatibility patch has not been applied.

Source App SHA-256 shared by all three:

```text
F5EDD8FB4D35C56614782453E63DAE310DF192F2B6E9CCB5383A366E73CD4143
```

These are three separate single-board observations, not a three-board Room or routed-data acceptance run. No Room mutation, RGB action, client UI acceptance, Windows 10 test, DHCP/web acceptance or long-load test was performed. The next multi-board test needs all participating nodes powered together and records from the actual source/target nodes. Firmware and backups were not uploaded; build-time receipts remain unchanged.
