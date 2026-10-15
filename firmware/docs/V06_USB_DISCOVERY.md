# v0.6 USB 控制与 6 邻居发现

> Public copy, redacted for publication on 2026-10-15. Board1/Board2/Board3 are stable public labels, not transmitted MAC addresses. Physical MAC/USB serial/PnP instance identifiers, local COM assignments and backup hashes are withheld. Original private records remain unchanged. Historical local evidence paths are not public downloads.

## 来源与范围

基线：`<private-work-root>\wakenic_hardening_ascii` 的 v0.5.0。
本次只复制源代码、测试、工具与历史文档；未复制 sdkconfig、build 缓存或二进制。
原工程只读保留；早期 `work/esp32s3_wakenic_p2p_hardening` 仍为 Phase 0，不是本版入口。

USB 接口参考已验证的
`esp32s3_usb_espnow_bridge/components/usb_data_link/usb_data_link.c` (historical local reference; not included)，
沿用驱动安装和读写 API，但改为单 USB worker、固定长度事务及有界输入策略。
已核对本机 v5.5.5 `esp_driver_usb_serial_jtag` 的头文件与驱动实现。

## 运行结构

```text
PC v0.13 串口插件
  ↕ TRILINK/1 行协议（数据走原生 USB，日志走 CH343）
trilink_usb：一个 3072 B 栈 worker，解析 / 限速 / 一次事务写出
  ↓ 复制只读快照；不发无线，不持有无线帧
trilink_node：邻居表唯一写入者
  ↕ 6 槽邻居表；最低 MAC 仅为历史发现诊断，不是 Room leader
trilink_radio：ESP-NOW，16 槽固定 RX 池，原帧格式不变
```

邻居表与对外快照容量来自 `TRILINK_DISCOVERY_MAX_PEERS=6`。
应用目标 `TRILINK_CLUSTER_NODE_COUNT=3` 保留；HELLO 无线格式与老 v0.5 兼容性不变。
当前为固定 group、固定信道的发现缓存查询；不依 RSSI 抢占、不分页，后续再设计扫描架构。
满表时已知邻居仍可刷新；未知第 7 个不会替换现有条目，待条目过期后再接纳。

## USB 契约

```text
TRILINK/1 HELLO 1234abcd
TRILINK/1 DEVICE 1234abcd 10:00:00:00:00:01 VHJpTGluay0xMDAwMDAwMDAwMDE= 00000003

TRILINK/1 SEARCH 1234abcd
TRILINK/1 PEER 20:00:00:00:00:02 <Base64名称> -42 - - - 1
TRILINK/1 END 1234abcd
```

名称为 `TriLink-<完整12位MAC>` 的 Base64 UTF-8。能力位 `0x3` 表示单播底座与
USB 控制口已启动，不表示文件、Room、游戏或 HID 已实现。
SEARCH 返回 0～6 行 PEER 及恰好一个 END；构造回复时二次滤除超时条目。
回复带前导 LF，帮助恢复行边界；nonce 精确回显。
未就绪返回 `ERROR <nonce> NOT_READY`；非法命令返回 `ERROR - BAD_REQUEST`。
每 1 秒窗口超过 20 条的命令静默丢弃并计入诊断，下一窗口可重试。

USB 不等待扫描，只取一次缓存；写入驱动不等于 PC 已处理。当前 PEER 行没有 nonce，
不支持同一端口并发 SEARCH；客户端仍需完善端口协调及失败事务清理。
MAC/group/CRC 均不是认证，仅用于可信课程实验环境。不执行任意主机命令。

## 资源与验证

- 无线 RX 池仍为 3360 B，不因 6 邻居增大。
- 新 USB worker 栈 3072 B，RX/TX ring 有效容量共 1536 B。
- USB line/reply/chunk 为 64/512/64 B；另有少量状态、邻居快照和 RTOS/驱动对象开销。
- 仅启动时分配 task/driver rings，命令处理不 malloc，原文件不驻留 S3。
- Flash、静态 DIRAM、`.su` 栈帧以 `verification.json` 为准。静态余量不是运行时 heap。

主机验证覆盖：旧协议/reducer；6 槽满表、第 7 个拒绝、刷新与过期释放；CRLF/粘包/逐字节输入、
32 字符 nonce、未知指令、非法字节、1 KiB 超长行及恢复；六条最长结果、空结果、过期结果、
输出容量；固件 C 回复交给实际 v0.13 C# 解析器。

当前未连接 ESP32/CH343：PnP 只发现 [port-redacted]～[port-redacted] 蓝牙串口。未烧录、擦除 Flash、
修改 eFuse、OTA 或上传 GitHub。三板无线稳定性和 6 邻居实物测试均不能宣称通过。

## 下一道硬件门

### 2026-09-14 本轮已完成的软件验证

- C 主机测试：855 checks，0 failures（7 个测试套件）。
- 固件 C → 实际客户端 v0.13 C#：DEVICE=1、PEER=6、END=1，PASS。
- ESP-IDF v5.5.5 编译、镜像与分区验证、容量验证：PASS。
- 初次全新配置构建捕获名称缓冲截断问题；修复后在同一干净构建树增量完成，
  `FreshBuild=false` 如实记录，没有把最终结果标成第二次全新构建。
- 固件包：`../artifacts/builds/20260914-180702-aaf1e152/`。
- 46 份构建输入 SHA-256 与本目录当前源码一致；最终应用及 bootloader 包内哈希复核一致。

| 指标 | v0.5 基线 | v0.6 实测 |
| --- | ---: | ---: |
| 应用镜像 | 753040 B | 762960 B（+9920 B） |
| 静态 DIRAM | 122543 B | 126843 B（+4300 B） |
| 192 KiB 静态预算余量 | 74065 B | 69765 B |
| 无线 RX 池 | 3360 B | 3360 B |
| 无线 RX 回调静态栈帧 | 48 B | 48 B |
| node 主循环静态栈帧 | 64 B | 64 B |

新 USB worker 自身静态栈帧 240 B，分配任务栈 3072 B；项目最大静态栈帧
272 B。上述 `.su` 不是调用链峰值或实测 HWM，仍必须过硬件门。
原 1 MiB 应用分区剩余 285616 B；bootloader 20944 B。
应用 SHA-256：`08C78061BB10F857304ACA3C129919EC30BC4A28A7FA53CB6D44E529BC5390D6`。

完整烧录文件位于同一固件包目录：`bootloader.bin` 对应 `0x0`，
`partition-table.bin` 对应 `0x8000`，`esp32s3_wakenic_p2p.bin` 对应 `0x10000`。
不要把应用 BIN 当作从地址 0 开始的合并镜像。须先确认目标板和端口，再执行烧录。

### 实物待验收

1. 唯一绑定每块板的 CH343 与原生 USB，记录 MAC/端口映射，再烧录；禁止猜历史 COM 号。
2. 核对版本、N16R8 自检、USB/Wi-Fi ready 和无 panic/WDT。
3. 真实数据口验证 HELLO nonce、SEARCH 两邻居、拔出过期和重新插入。
4. DTR/RTS false、重复开关串口、半包、超长输入、限速恢复，确认 USB 压力不破坏 RF。
5. 三板至少 30 分钟，记录 free/min/largest heap、stack HWM、RF drop、USB tx_fail/rate_drop。
6. 之后再接 APP_DATA 文本与真实 Room。扩大扫描列表时再比较发现/连接表分离、分页和淘汰策略。

原生 USB Serial/JTAG 是固定功能硬件；需要 CDC+HID 时再替换为 TinyUSB/USB OTG，
不能与当前后端同时占用内部 PHY。
依据：[Espressif USB Serial/JTAG](https://docs.espressif.com/projects/esp-idf/en/release-v5.3/esp32s3/api-guides/usb-serial-jtag-console.html)。
