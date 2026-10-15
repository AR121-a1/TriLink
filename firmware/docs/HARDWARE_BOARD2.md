# 二号机 v0.6.0 烧录与单板启动检查

> Public copy, redacted for publication on 2026-10-15. Board1/Board2/Board3 are stable public labels, not transmitted MAC addresses. Physical MAC/USB serial/PnP instance identifiers, local COM assignments and backup hashes are withheld. Original private records remain unchanged. Historical local evidence paths are not public downloads.

日期：2026-09-14。用户连接二号机并明确请求烧录后执行。
按 embedded-stm32-esp32 流程核对设备身份、安全状态和镜像，备份覆盖区后烧录，
再检查启动日志。本记录不代表多板互联或原生 USB 客户端验收通过。

## 已确认的设备

- 板号：二号机；CH343 编程接口 [port-redacted]。
- USB 实例：`USB\VID_1A86&PID_55D3\[instance-redacted]`。
- ESP32-S3 QFN56 revision v0.2，40 MHz 晶振。
- Public board label：`Board2`，与一号机不同。
- Flash manufacturer `c8`、device `4018`，16 MB，启动识别为 `gd`。
- 嵌入式 AP PSRAM 8 MB；Secure Boot / Flash Encryption 均为 Disabled。

## 烧录与恢复证据

使用与一号机相同的已验证固件包 `artifacts/builds/20260914-180702-aaf1e152/`。
烧录前复核三个镜像与构建记录一致，没有重新修改或编译固件。

| 文件 | 写入地址 | 长度 | 结果 |
| --- | --- | ---: | --- |
| bootloader.bin | 0x0 | 20944 B | Hash of data verified |
| partition-table.bin | 0x8000 | 3072 B | Hash of data verified |
| esp32s3_wakenic_p2p.bin | 0x10000 | 762960 B | Hash of data verified |

应用 SHA-256：`08C78061BB10F857304ACA3C129919EC30BC4A28A7FA53CB6D44E529BC5390D6`。
esptool 4.12.0，460800 baud，DIO / 80 MHz / 16 MB，烧录退出码为 0。

覆盖区备份为 `0x0, 0x110000)`，1114112 B，不是全部 16 MB Flash 的转储。
首次备份因日志管道提前结束而退出码非零，记录保留但不作为恢复依据；第二次重新读取
到独立文件，退出码、长度及 SHA-256 均通过检查后才执行写入。

- [有效覆盖区备份 (historical local-only file, not published: `../artifacts/hardware/board2-20260914-201718/before-first-0x110000-verified.bin`)
- 备份 SHA-256：`[backup-hash-redacted]`
- 芯片信息 (historical local-only file, not published: `../artifacts/hardware/board2-20260914-201718/identity.log`)
- 安全状态 (historical local-only file, not published: `../artifacts/hardware/board2-20260914-201718/security.log`)
- 成功备份日志 (historical local-only file, not published: `../artifacts/hardware/board2-20260914-201718/backup-verified.log`)
- 首次备份日志（不作为成功依据） (historical local-only file, not published: `../artifacts/hardware/board2-20260914-201718/backup.log`)
- 烧录日志 (historical local-only file, not published: `../artifacts/hardware/board2-20260914-201718/flash.log`)
- 复位后 35 秒启动日志 (historical local-only file, not published: `../artifacts/hardware/board2-20260914-201718/boot-35s.log`)

仅擦写三个镜像对应扇区，没有全片擦除或 eFuse 操作，没有修改 SDK。
NVS/PHY 分区不在烧录覆盖范围；首次启动由 ESP-IDF 自动更新了 PHY 校准数据。

## 启动检查结果

- App version 0.6.0，ESP-IDF v5.5.5；`BOOT → INIT → CORE_READY`。
- Flash 16777216 B，PSRAM 8388608 B；PSRAM memory test OK。
- `TRILINK/1 native USB Serial/JTAG ready; max_commands=20/s`。
- ESP-NOW channel=6，rx_pool=16，slot_bytes=210，frame_max=200，peer_limit=6。
- 6 组诊断样本，uptime 从 5302 ms 单调递增到 30302 ms。
- free_heap 253187 B 持平，min_heap 220920 B；PSRAM free 8386080 B 持平。
- 栈低水位：USB task 892 B，node task 1820 B，控制器 1388 B。
- 最后一组诊断：邻居 0/6，radio_tx_ok=60，tx_fail/tx_timeout/rx_drop 为 0。
- 35 秒捕获窗口中未见 panic、断言失败、看门狗触发或重启。
- 日志断言复核 PASS：设备 MAC、版本、容量、USB 后端、6 邻居上限、启动状态、
  三段写入校验、6 组时间单调样本、堆内存稳定及备份长度/哈希均符合预期。

## 尚未验证

当前枚举到的是 CH343 [port-redacted]，没有 S3 原生 USB 数据接口，USB commands=0。
因此实机 HELLO / SEARCH、客户端识别弹窗、USB 输入压力仍待连接原生 USB 数据口验证。
日志未发现其他无线节点；多板互发现、双向业务、30 分钟稳定性及 6 邻居容量实测均未完成。

固件和备份仅保留本地，没有上传。构建包 verification.json 保留软件构建时的状态，
本文件独立记录二号机的真实烧录和启动结果。
