# 2026-10-09 本轮第2台：v0.10.2烧写、启动与单侧发现观察

> Public copy, redacted for publication on 2026-10-15. Board1/Board2/Board3 are stable public labels, not transmitted MAC addresses. Physical MAC/USB serial/PnP instance identifiers, local COM assignments and backup hashes are withheld. Original private records remain unchanged. Historical local evidence paths are not public downloads.

用户按“电脑同时只连接一块ESP32”的规则请求烧写第2台。目标取本轮唯一连接的设备，实际为CH343 [port-redacted]、MAC `Board1`；它在2026-09-14记录中称一号机，本轮称第2台。历史编号不改，COM号和连接顺序不固定为固件角色。

## 目标与镜像

- 烧录口：CH343 [port-redacted]；USB实例`USB\VID_1A86&PID_55D3\[instance-redacted]`。
- 芯片：ESP32-S3 QFN56 rev0.2，40MHz晶振；XMC Flash JEDEC `20 70 18`，16MiB；嵌入式AP PSRAM 8MiB。
- Secure Boot / Flash Encryption均Disabled；未改eFuse、未配置密钥。
- 沿用既有v0.10.2 / ESP-IDF v5.5.5验证包`artifacts/builds/20261001-025538-49e98315`。三BIN长度/SHA256及108个源码/测试输入再次比对一致，不重新编译、不升级SDK。
- 本机覆盖区备份内的3072B分区表与目标镜像byte/hash完全相同；NVS `0x9000/0x6000`、PHY `0xf000/0x1000`、factory `0x10000/0x100000`保持，无OTA槽或资源分区。

| 地址 | 镜像 | 字节数 | 写入及独立校验 |
| --- | --- | ---: | --- |
| 0x0 | bootloader.bin | 20944 | Hash verified / digest matched |
| 0x8000 | partition-table.bin | 3072 | Hash verified / digest matched |
| 0x10000 | esp32s3_wakenic_p2p.bin | 806896 | Hash verified / digest matched |

esptool4.12.0、460800baud，DIO/80MHz/16MB。写入只擦除`0x0–0x5fff`、`0x8000–0x8fff`、`0x10000–0xd4fff`，没有全片`erase_flash`、强制写入、安全配置或eFuse操作。NVS/PHY不在烧录命令擦写范围；**启动时SDK实际保存了新的RF校准记录**，因此不宣称运行数据或校准记录在启动后保持逐字节不变。

## XMC兼容路径

ROM工具日志依次出现XMC启动失败警告、恢复尝试和`XMC flash chip boot-up fix successful!`，随后备份、写入、独立校验均成功。

UART bootloader启动出现已有SDK错误：

```text
E (31) bootloader_flash: XMC flash startup fail
W (33) xmc_7018: accepting verified XMC JEDEC ID 20 70 18
```

后续加载应用、PSRAM测试及CORE_READY正常。此处只有准确JEDEC `0x207018`、已有窄补丁接受及正常后续启动同时成立时，才解释为已知兼容路径；不能将其他Flash错误笼统忽略。

补丁保持原样：[xmc_7018_compat.c](../bootloader_components/xmc_7018_compat/xmc_7018_compat.c)只接受`0x207018`，保留原成功及其他错误；[CMakeLists.txt](../bootloader_components/xmc_7018_compat/CMakeLists.txt)保留whole-archive和`--wrap=bootloader_flash_xmc_startup`。没有为消除日志而修改SDK或更换bootloader。

## 恢复与证据

烧写前读取`0,0x110000)`，备份1114112B，覆盖本次擦写区域并保存原NVS/PHY；不是全16MiB转储。备份SHA256再次独立核对：`[backup-hash-redacted]`。备份可能含本机配置，只保存在本地，不上传。

- [机器收据 (historical local-only file, not published: `../artifacts/hardware/20261009-single-2-473570d4/flash-verification.json`)
- 芯片/Flash身份 (historical local-only file, not published: `../artifacts/hardware/20261009-single-2-473570d4/identity.log`)、安全状态 (historical local-only file, not published: `../artifacts/hardware/20261009-single-2-473570d4/security.log`)
- 备份日志 (historical local-only file, not published: `../artifacts/hardware/20261009-single-2-473570d4/backup.log`)、烧写日志 (historical local-only file, not published: `../artifacts/hardware/20261009-single-2-473570d4/flash.log`)、独立Flash校验 (historical local-only file, not published: `../artifacts/hardware/20261009-single-2-473570d4/verify-flash.log`)
- 35秒UART启动日志 (historical local-only file, not published: `../artifacts/hardware/20261009-single-2-473570d4/boot-35s.log`)

三镜像来源SHA256分别为bootloader `83fbab2ddf0c7008a36c03a9d5815bde68c4dc20a626f6a055c4b06002bb4446`、partition `7f00b6c042a89b15b0cac534f82ed988caf29278ff5700b0c511eb1b5bb7c820`、App `4c93dd75640e94fc28c63992667a78d770baa03439165f86da1f71579db3952c`。完整来源、地址及实际Flash校验结果见机器收据；esptool按照DIO/80MHz/16MB参数处理image header及验证摘要。

## 实际启动与无线观察

RTS复位后115200baud记录35秒，UART已经关闭，没有发送应用、Room或RGB命令。

- v0.10.2 / IDF5.5.5；Flash16777216B、PSRAM8388608B；PSRAM内存测试OK。
- 原生USB控制后端初始化、ESP-NOW信道6启动，进入`BOOT -> INIT -> CORE_READY`。
- 仅一次ROM启动；6个core采样uptime5292→30292ms。除上述已解释XMC行外，未见panic、WDT、brownout、异常回溯或重复启动。
- 内部free_heap224419→224431B后持平；min192148B、largest147456B。PSRAMfree/min8386080B稳定，largest8257536B。
- 报告的最低栈余量：USB892B、node1804B、TX1924B、控制器1380B；不能当作长期业务压力峰值。
- 本板在约1437ms发现本轮第1台`Board2`，首次RSSI=-48dBm；6条诊断持续peers=1/6，RX/TX成功15→90。
- 每条RX诊断的acquired/released相等，rx_ready/in_use为0；TX fail/timeout、RX drop/ownership error为0，队列无积压。
- 日志的`leader=... policy=lowest-mac`和`discovery_lowest_mac`是发现层旧最低MAC诊断，**不是Room leader**；没有创建或加入Room。
- RGB默认未启用，无启用/执行命令或物理LED验收。`native_usb=reserved`及`usb_port=stub_registered`是遗留诊断文字，不推翻明确的真实USB后端初始化日志。

## 完成范围与接续验收

已通过：目标身份/容量、安全状态、覆盖区备份、三段写入与独立校验、本板启动及35秒短时稳态；另观察到**第二台单侧发现第一台并接收无线发现/心跳流量**。没有第一台同期日志或端到端业务回执，不能扩大为双侧Room副本或完整多板业务验收。

本次UART的USB命令数为0；原生USB HELLO/SEARCH、PC客户端、Room准入/继任、业务传输ACK、物理RGB、六邻居、长期压力、资源包/自治任务/压缩/OTA仍不是本次通过项。上位机原生USB接续验收由独立客户端记录承载，不改固件。其他设备统一0.10.2后再做Room联调，不能混用旧0.6.0板作为新版Room验收。

按embedded-stm32-esp32的目标核对、配置保留和证据分层规则收束记录。此次没有更改固件编译输入/分区/SDK/BSP，没有上传固件或备份；历史构建收据Flashed=false保留构建当时事实，本次烧写由独立flash-verification.json记录。
