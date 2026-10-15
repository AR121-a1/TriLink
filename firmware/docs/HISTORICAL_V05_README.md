# ESP32-S3 WakeNIC-P2P / TriLink (v0.5 historical snapshot)

这是面向三台笔记本“插上即组网”的独立 ESP-IDF 工程。当前 v0.5.0 保留 v0.4
三节点 ESP-NOW 发现、两邻居单播心跳和最低 MAC 临时 Leader，同时把接收热路径压缩为
单任务、固定内存和只读视图。新增的纯文本、资源摘要、字节范围请求和文件分片 codec
复用同一个 APP_DATA 信封；它们经过编译和主机测试，但当前未链接，不提前占用运行时资源。

当前仍不是成品 USB 游戏/内容网卡：APP_DATA dispatcher、原生 USB PC 数据链路、
浏览器小游戏、正式选举、加密和 HID/2.4G dongle 唤醒尚未接入。CRC32 只用于检错，
不代表认证；当前单播仍是未加密的 bring-up 路径。

## 当前启动路径

```text
app_main
  -> HW-678A N16R8 Flash/PSRAM 自检
  -> 保留原生 USB 端口（stub）
  -> Wi-Fi STA / channel 6 / ESP-NOW
  -> HELLO 广播发现（500 ms）
  -> 固定两邻居表 + 单播心跳（1000 ms）
  -> 最低 MAC Leader 视图
  -> 核心诊断循环
```

三块板刷相同固件即可工作，不写死 MAC，不要求路由器、云服务或商用中继。
`CORE_READY` 只表示板级自检、USB 扩展缝和 ESP-NOW 后端已启动；只有
`cluster=READY` 才表示本机当前看到了另外两块板。最低 MAC 只是无配置启动规则，
不是多数派共识协议。

## v0.5 接收路径

```text
ESP-NOW callback
  -> 从 16 槽固定池取得空槽
  -> 对驱动数据做一次有界复制（<= 200 B）
  -> ready queue 只投递 1 B 槽号
  -> trilink_node 借用 const frame view
  -> CRC/头部校验直接引用池内 payload
  -> 处理完成后归还原槽
```

该路径沿用 v0.4：已删除 `trilink_radio_rx` 任务和 node 的第二套整帧队列。接收 payload
从最多七次整帧/负载复制降到一次必要复制；回调不分配内存、不阻塞、不解析协议，
成员表仍只有 `trilink_node` 一个 owner。每个槽有 `FREE/FILLING/READY/ACQUIRED`
所有权状态，重复归还和非法指针会进入独立统计。

## 后续数据接口

`TRILINK_MSG_APP_DATA` 的 payload 使用 4 B 业务信封：

| 字段 | 长度 | 含义 |
|---|---:|---|
| service id | 1 B | 1=游戏、2=文件、3=诊断、4=内容，32..255 留给扩展 |
| opcode | 1 B | 由具体业务定义 |
| data length | 2 B | 显式小端，必须与余下长度完全一致 |
| data | 0..168 B | 业务数据或文件分片头+分片内容 |

codec 使用非拥有型只读 view；后续服务可以在 RX 槽生命周期内直接读数据，不必再复制。
v0.5 已冻结两组 wire contract：

- `CONTENT`：UTF-8 文本、SHA-256 资源摘要、字节范围 GET 和结构化错误；
- `FILE`：OPEN、最大 158 B CHUNK、累计确认 + 32 bit bitmap、CLOSE 和 CANCEL。

当前应用尚未引用这些 codec。构建树证明三个对象均已编译，最终 map 证明均未链接，
所以没有新增任务、队列、堆对象或运行 buffer。完整调度器、4 槽重传窗口和 PC 数据面
留到硬件链路放行后实现。

## 组件边界

- `wakenic_board`：HW-678A / ESP32-S3-WROOM-1-N16R8 容量和身份检查；
- `wakenic_core`：typed-event 启动状态机与堆、栈、队列诊断；
- `wakenic_ports`：尚未实现的原生 USB seam；
- `trilink_protocol`：200 B 小端空中帧、CRC32、零复制 view、HELLO、CONTENT/FILE wire codec；
- `trilink_cluster`：纯 C 两邻居表、去重、过期、重启识别、最低 MAC Leader；
- `trilink_radio`：ESP-NOW、16 槽固定 RX 池、1 B 索引队列、串行 TX 和 Peer；
- `trilink_node`：唯一成员表 owner，直接 acquire/release RX 槽并驱动 HELLO、心跳和诊断。

当前架构见 `docs/ARCHITECTURE.md`；纯文本/按需原数据的评估、格式和占用见
`docs/TRILINK_V05_CONTENT_EXCHANGE.md`。`docs/TRILINK_V04_LEAN_DATA_PATH.md` 和
`docs/TRILINK_V03_SKELETON.md` 保留为历史基线。完整游戏路线和容量规划见
`docs/TRILINK_GAME_LAN_CAPACITY_PLAN.md`。

## 已锁定硬件与 SDK

- ESP-IDF v5.5.5，target `esp32s3`；
- HW-678A / ESP32-S3-WROOM-1-N16R8；
- 16 MiB DIO 80 MHz Flash，8 MiB Octal 80 MHz PSRAM；
- CH343/UART0（GPIO43/44）用于烧录和日志；
- 原生 USB 留给后续 NCM/HID；
- 对旧板实测出现的 XMC JEDEC `0x207018`，bootloader 只保留此 ID 的窄兼容。

## 主机回归

```powershell
.\tools\test_host.ps1
```

- WakeNIC reducer：185 checks；
- TriLink 帧协议及只读 view：113 checks；
- 业务复用信封及 APP_DATA 组合：63 checks；
- 纯文本、摘要、GET 和错误 codec：142 checks；
- 文件 OPEN/CHUNK/ACK/CLOSE/CANCEL wire codec：142 checks；
- 三节点成员与 Leader：132 checks；
- 合计 777 checks，0 failures。

测试均用 C11、`-Wall -Wextra -Werror -pedantic` 重新编译后执行。

## ESP-IDF 构建与验证

```powershell
$probe = Join-Path '<private-work-root>' `
  ('trilink-verify-' + [guid]::NewGuid().ToString('N'))
$profile = 'C:\Espressif\tools\Microsoft.v5.5.5.PowerShell_profile.ps1'

.\tools\build.ps1 -IdfProfile $profile `
  -BuildDirectory "$probe\build" `
  -SdkconfigPath "$probe\sdkconfig" `
  -NoCcache -RequireFresh

. $profile
.\tools\verify_build.ps1 `
  -BuildDirectory "$probe\build" `
  -SdkconfigPath "$probe\sdkconfig"
.\tools\verify_capacity.ps1 -BuildDirectory "$probe\build"
```

最终软件证据（2026-09-13）：应用 753,040 B，静态 DIRAM 122,543 B，最大项目函数
静态栈帧 272 B；固定 RX 池 3,360 B，Wi-Fi RX 回调 48 B，node 主循环 64 B。
相对 v0.4 镜像只增加 16 B，静态 DIRAM 不变。service/content/file 三个 codec 对象均
编译但未链接；其对象级 text 合计 4,105 B、data/bss 为 0，是未来全部启用时的参考
上界，不是本版镜像增量。

## 来源边界

无线初始化和发送并发方式来自本机已完成双板 PING/PONG/STATUS 的只读工程：

`esp32s3_usb_espnow_bridge` (historical local reference project; not included)

本工程保留其 Wi-Fi STA、关闭省电、频道 6、单在途发送、完成信号和 XMC wrapper 经验；
三节点协议、成员模型、固定池所有权、零复制 view 和 service envelope 均为本工程独立
实现。参考工程没有被修改。

## 下一轮实物门

1. 三块板逐块记录 MAC、JEDEC、Flash/PSRAM 和冷启动日志；
2. 三板同刷 v0.5.0，要求 3 s 内均达到 `cluster=READY` 且 Leader 一致；
3. 依次断开普通节点与 Leader，检查 2 s 过期和重选；
4. 30 分钟 soak 中要求 `radio_rx_drop=0`、`rx_owner_err=0`、`rx_in_use` 每轮回到 0；
5. 同时记录运行时 heap、最大空闲块和 node 栈高水位，再决定能否把 4096 B 栈下调；
6. RF 门通过后先接原生 USB CDC ACM 与 TEXT/SUMMARY，再加 4 槽文件窗口；NCM 作为浏览器形态备选；
7. HID/2.4G dongle 唤醒保持独立故障域，最后验证。

本轮没有连接开发板，也没有执行 `flash`。软件 PASS 不能替代三板 RF、运行时内存和
唤醒实测。
