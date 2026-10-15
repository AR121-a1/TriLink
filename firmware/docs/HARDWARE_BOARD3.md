# 三号机 v0.6.0 烧录与单板启动检查

> Public copy, redacted for publication on 2026-10-15. Board1/Board2/Board3 are stable public labels, not transmitted MAC addresses. Physical MAC/USB serial/PnP instance identifiers, local COM assignments and backup hashes are withheld. Original private records remain unchanged. Historical local evidence paths are not public downloads.

日期：2026-09-14。用户请求烧录三号机，并在重新连接后继续执行。
按 embedded-stm32-esp32 流程确认身份和安全状态、备份覆盖区，再写入并检查启动。
此前只有一号机 CH343 在线时没有写入；本次已确认新的 CH340 设备及独立 STA MAC。

## 已确认的设备

- 板号：三号机；用户标注 CH340C，Windows 枚举为 `USB-SERIAL CH340 ([port-redacted])`。
- USB 实例：`USB\VID_1A86&PID_7523\[instance-redacted]`。
- ESP32-S3 QFN56 revision v0.2，40 MHz 晶振。
- Public board label：`Board3`，与一、二号机不同。
- Flash manufacturer `68`、device `4018`，16 MB，启动识别为 `boya`。
- 嵌入式 AP PSRAM 8 MB；Secure Boot / Flash Encryption 均为 Disabled。

## 烧录与恢复证据

使用与一、二号机相同的固件包 `artifacts/builds/20260914-180702-aaf1e152/`。
烧录前复核 bootloader、应用哈希及分区表与原构建一致，没有修改镜像或升级 SDK。

| 文件 | 写入地址 | 长度 | 结果 |
| --- | --- | ---: | --- |
| bootloader.bin | 0x0 | 20944 B | Hash of data verified |
| partition-table.bin | 0x8000 | 3072 B | Hash of data verified |
| esp32s3_wakenic_p2p.bin | 0x10000 | 762960 B | Hash of data verified |

应用 SHA-256：`08C78061BB10F857304ACA3C129919EC30BC4A28A7FA53CB6D44E529BC5390D6`。
esptool 4.12.0，460800 baud，DIO / 80 MHz / 16 MB；写入退出码 0，三段校验通过。

烧录前备份 `0x0, 0x110000)` 共 1114112 B，读取退出码和文件长度均正常，
写入前再次核对备份 SHA-256。这是覆盖区备份，不是全部 16 MB Flash 的转储。

- [覆盖区备份 (historical local-only file, not published: `../artifacts/hardware/board3-20260914-203703/before-first-0x110000.bin`)
- 备份 SHA-256：`[backup-hash-redacted]`
- 芯片信息 (historical local-only file, not published: `../artifacts/hardware/board3-20260914-203703/identity.log`)
- 安全状态 (historical local-only file, not published: `../artifacts/hardware/board3-20260914-203703/security.log`)
- 备份日志 (historical local-only file, not published: `../artifacts/hardware/board3-20260914-203703/backup.log`)
- 烧录日志 (historical local-only file, not published: `../artifacts/hardware/board3-20260914-203703/flash.log`)
- 复位后 35 秒启动日志 (historical local-only file, not published: `../artifacts/hardware/board3-20260914-203703/boot-35s.log`)

只擦写三个镜像对应扇区，没有全片擦除、eFuse 操作或 SDK 修改。
NVS/PHY 分区不在烧录覆盖范围；首次启动由 ESP-IDF 自动更新了 PHY 校准数据。

## 启动检查结果

- App version 0.6.0，ESP-IDF v5.5.5；`BOOT → INIT → CORE_READY`。
- Flash 16777216 B、PSRAM 8388608 B；PSRAM memory test OK。
- `TRILINK/1 native USB Serial/JTAG ready; max_commands=20/s`。
- ESP-NOW channel=6、rx_pool=16、slot_bytes=210、frame_max=200、peer_limit=6。
- 6 组诊断样本，uptime 从 5305 ms 单调递增到 30305 ms。
- free_heap 首样本 253111 B，其后 5 组均为 253119 B；min_heap 220844 B。
- PSRAM free 8386080 B 持平；栈低水位：USB 892 B、node 1808 B、控制器 1388 B。
- 最后一组诊断：邻居 0/6，radio_tx_ok=61，tx_fail/tx_timeout/rx_drop 为 0。
- 捕获窗口中只有一次 ROM 启动，未见 panic、断言失败、看门狗触发或重启。
- 自动日志断言复核 PASS：版本、MAC、容量、USB 后端、无线配置、运行状态、
  三段写入校验、6 组时间递增/内存样本及备份长度/哈希均符合预期。

固件中的 `board=HW-678A`、`debug=CH343/UART0` 来自复用基线的静态标签，
不是对本板 USB 桥芯片的自动检测；真实桥接接口以 Windows 的 CH340 枚举为准。
`native_usb=reserved` / `usb_port=stub_registered` 也是遗留诊断文字，
实际原生 USB 后端已由 trilink_usb 启动。此次未为更改标签而重编译固件。

## 尚未验证

枚举到的仅为 CH340 [port-redacted]，没有 S3 原生 USB 数据接口，USB commands=0。
实机 HELLO / SEARCH、客户端识别弹窗及 USB 输入压力仍待连接原生 USB 数据口验证。
日志没有发现其他无线节点；三板互发现、双向业务、30 分钟稳定性及 6 邻居容量实测未完成。

固件、备份及日志均保留本地，没有上传。构建包 verification.json 保留当时的软件验证状态，
本记录独立补充三号机烧录与启动事实。
