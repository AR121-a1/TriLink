# 一号机 v0.6.0 烧录与单板启动检查

> Public copy, redacted for publication on 2026-10-15. Board1/Board2/Board3 are stable public labels, not transmitted MAC addresses. Physical MAC/USB serial/PnP instance identifiers, local COM assignments and backup hashes are withheld. Original private records remain unchanged. Historical local evidence paths are not public downloads.

日期：2026-09-14。用户连接一号机并明确请求烧录后执行。
按 embedded-stm32-esp32 验收流程，先核对真实端口、芯片身份、安全状态与镜像哈希，
再备份覆盖区、写入并检查启动日志；不将单板启动检查当作完整三板验收。

## 已确认的设备

- 板号：一号机。
- 烧录接口：CH343，[port-redacted]。
- USB 实例：`USB\VID_1A86&PID_55D3\[instance-redacted]`。
- ESP32-S3 QFN56，revision v0.2，40 MHz 晶振。
- Public board label：`Board1`。
- XMC Flash JEDEC `20 70 18`，16 MB；嵌入式 AP PSRAM 8 MB。
- Secure Boot / Flash Encryption 均为 Disabled；未更改 eFuse。

## 烧录与恢复证据

使用固件包 `artifacts/builds/20260914-180702-aaf1e152/`，烧录前复核应用、bootloader
及分区表与已验证构建一致。

| 文件 | 写入地址 | 长度 | 结果 |
| --- | --- | ---: | --- |
| bootloader.bin | 0x0 | 20944 B | Hash of data verified |
| partition-table.bin | 0x8000 | 3072 B | Hash of data verified |
| esp32s3_wakenic_p2p.bin | 0x10000 | 762960 B | Hash of data verified |

烧录前读取 `0x0, 0x110000)`，备份 1114112 B，完整覆盖本次擦写区域。
备份不是全部 16 MB Flash 的转储。写入只擦除了对应三个区域，没有执行 erase_flash。
原 NVS/PHY 分区不在此次擦写范围；首次启动由 ESP-IDF 自动更新了 PHY 校准数据。

- [原区域备份 (historical local-only file, not published: `../artifacts/hardware/board1-20260914-201119/before-first-0x110000.bin`)
- 备份 SHA-256：`[backup-hash-redacted]`
- 芯片信息 (historical local-only file, not published: `../artifacts/hardware/board1-20260914-201119/identity.log`)
- 安全状态 (historical local-only file, not published: `../artifacts/hardware/board1-20260914-201119/security.log`)
- 备份日志 (historical local-only file, not published: `../artifacts/hardware/board1-20260914-201119/backup.log`)
- 烧录日志 (historical local-only file, not published: `../artifacts/hardware/board1-20260914-201119/flash.log`)
- 复位后 35 秒启动日志 (historical local-only file, not published: `../artifacts/hardware/board1-20260914-201119/boot-35s.log`)

## 启动检查结果

- App version 0.6.0，ESP-IDF v5.5.5，DIO / 80 MHz。
- 物理容量自检：Flash 16777216 B，PSRAM 8388608 B；PSRAM memory test OK。
- `TRILINK/1 native USB Serial/JTAG ready; max_commands=20/s`。
- ESP-NOW channel=6、rx_pool=16、frame_max=200、peer_limit=6。
- `BOOT → INIT → CORE_READY`。
- 6 组诊断样本，uptime 单调递增到 30299 ms；未见 panic、看门狗触发或重启。
- free_heap 253319 B 持平，min_heap 221052 B；PSRAM free 8386080 B 持平。
- USB task 栈低水位 892 B；node task 1820 B；控制器 1388 B。
- 只有一号机在线，邻居为 0/6；radio TX 61 次成功、fail/timeout/drop 为 0。

已知日志说明：XMC 启动流程仍先输出原 SDK 的 startup fail，再由本工程已有的
`xmc_7018` 兼容补丁接受准确 JEDEC ID，随后正常加载应用；没有为消除日志而改动 SDK。
`native_usb=reserved` / `usb_port=stub_registered` 是遗留诊断文字，不能用于判断 USB
后端是否运行；本版实际后端已由 trilink_usb 启动。后续可统一更新诊断命名。

## 尚未验证

当前只枚举到 CH343 [port-redacted]，没有原生 USB 数据 COM，USB commands=0。
因此实机 HELLO / SEARCH、客户端弹窗、USB 输入压力、三板无线互发现、30 分钟稳定性
及 6 邻居容量实测均仍待验收。下一步连接板上 S3 原生 USB 数据口，不把 CH343 当数据口。

本轮没有上传固件或备份，没有修改已验证镜像；构建目录中的 verification.json
保留当时的软件验证状态，本记录单独补充一号机烧录事实。
