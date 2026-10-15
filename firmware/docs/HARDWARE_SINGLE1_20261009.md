# 2026-10-09 本轮第1台：v0.10.2烧写与单板启动

> Public copy, redacted for publication on 2026-10-15. Board1/Board2/Board3 are stable public labels, not transmitted MAC addresses. Physical MAC/USB serial/PnP instance identifiers, local COM assignments and backup hashes are withheld. Original private records remain unchanged. Historical local evidence paths are not public downloads.

用户明确要求烧写并澄清：从现在起电脑同一时间只连接一块ESP32。目标按本轮唯一连接设备选择，同时记录实际MAC；不把COM号或历史板号当固定角色。此次[port-redacted]/MAC `Board2`，历史2026-09-14记录称二号机；本轮称第1台，旧板号记录不改。

## 目标与镜像

- 编程口：CH343 [port-redacted]；USB实例`USB\VID_1A86&PID_55D3\[instance-redacted]`。
- 芯片：ESP32-S3 QFN56 rev0.2，40MHz晶振；GD Flash 16MiB（JEDEC c8/4018），嵌入式AP PSRAM 8MiB。
- Secure Boot / Flash Encryption均Disabled；不修改eFuse、不进行密钥配置。
- 固件：v0.10.2、ESP-IDF v5.5.5，使用既有验证包`artifacts/builds/20261001-025538-49e98315`。108个源码/测试输入和三镜像SHA256再次核对一致；不重新编译或升级SDK。
- 仍为单factory布局；本机备份中的3072B分区表与目标镜像byte/hash完全相同，无分区迁移、资源分区或OTA槽。

| 地址 | 镜像 | 字节数 | 写入及独立校验 |
| --- | --- | --- | --- |
| 0x0 | bootloader.bin | 20944 | Hash verified / digest matched |
| 0x8000 | partition-table.bin | 3072 | Hash verified / digest matched |
| 0x10000 | esp32s3_wakenic_p2p.bin | 806896 | Hash verified / digest matched |

esptool4.12.0、460800baud，DIO/80MHz/16MB。只擦写`0x0–0x5fff`、`0x8000–0x8fff`、`0x10000–0xd4fff`；没有`erase_flash`、强制写入、安全设置或eFuse操作。NVS `0x9000/0x6000`和PHY `0xf000/0x1000`不在烧录命令擦写范围；应用正常启动可能按SDK逻辑更新运行数据，不宣称启动后这些字节绝对不变。

## 恢复与证据

写入前已备份`0,0x110000)`，1114112B，覆盖本次擦写范围并保留原NVS/PHY；不是全16MiB转储。备份SHA256：`[backup-hash-redacted]`。备份可能含本机配置，只在本地保留，不上传。

- [机器收据 (historical local-only file, not published: `../artifacts/hardware/20261009-single-1-74a701bd/flash-verification.json`)
- 芯片/Flash身份 (historical local-only file, not published: `../artifacts/hardware/20261009-single-1-74a701bd/identity.log`)、安全状态 (historical local-only file, not published: `../artifacts/hardware/20261009-single-1-74a701bd/security.log`)
- 备份日志 (historical local-only file, not published: `../artifacts/hardware/20261009-single-1-74a701bd/backup.log`)、烧写日志 (historical local-only file, not published: `../artifacts/hardware/20261009-single-1-74a701bd/flash.log`)、独立Flash校验 (historical local-only file, not published: `../artifacts/hardware/20261009-single-1-74a701bd/verify-flash.log`)
- 35秒UART启动日志 (historical local-only file, not published: `../artifacts/hardware/20261009-single-1-74a701bd/boot-35s.log`)

## 实际启动结果

RTS复位后115200baud观察35秒，串口已关闭，不发送应用或RGB命令。

- 项目版本0.10.2/IDF5.5.5；Flash16777216B、PSRAM8388608B；PSRAM内存测试OK。
- 原生USB控制后端初始化、ESP-NOW信道6启动；进入`BOOT -> INIT -> CORE_READY`。
- 仅一次ROM启动；6个core采样uptime5264→30264ms。未见panic、WDT、brownout、异常回溯或重复启动。
- 内部free_heap224319→224323B后持平；min_heap192048B，largest147456B。PSRAMfree/min8386080B稳定，largest8257536B。
- 栈高水位报告的最低余量：USB892B、node1808B、TX1928B、控制器1380B。仅为空闲短时数据，不代替压力峰值。
- 广播TX成功10→60；fail/timeout、RX drop/ownership error均0，队列无积压，附近节点0/6。
- RGB默认未启用，未发送启用/执行命令；没有物理LED验收。

## 完成范围

本轮通过：目标身份/容量、安全状态、覆盖区备份、写入/独立校验、单板启动与35秒空闲稳态。未做：原生USB HELLO/SEARCH、PC客户端联调、Room/多板互通、物理RGB、六邻居、长期压力、资源包/自治任务/OTA。其余设备须统一0.10.2后再进行Room联调，不能将旧0.6.0板混用作为新版Room验收。

按照embedded-stm32-esp32的目标核对、配置保留与证据分层执行；没有修改固件源码/分区/SDK/BSP，没有上传固件或备份。历史构建收据中的Flashed=false保留当时事实，本次实际烧录由上述独立硬件收据记录。
