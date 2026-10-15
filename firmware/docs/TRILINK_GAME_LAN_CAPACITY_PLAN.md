# TriLink 三机游戏专网：体系结构与核心板容量计划

> Public copy, redacted for publication on 2026-10-15. Board1/Board2/Board3 are stable public labels, not transmitted MAC addresses. Physical MAC/USB serial/PnP instance identifiers, local COM assignments and backup hashes are withheld. Original private records remain unchanged. Historical local evidence paths are not public downloads.

状态：设计基线，等待 NCM 单板实测与三板闭环  
目标硬件：HW-678A / ESP32-S3-WROOM-1-N16R8  
目标 SDK：ESP-IDF v5.5.5，`esp32s3`  

## 1. 结论与范围

本项目不实现“任意以太网帧透明桥接”，也不在三台电脑上部署 VPN。本文件保留
“零安装浏览器游戏”的完整版容量目标：每台电脑通过原生 USB 连接一块 ESP32-S3；
S3 以 USB CDC-NCM 虚拟网卡向本机提供 DHCP、HTTP 和 WebSocket 服务，三块 S3 再
通过固定频道的 ESP-NOW 加密单播交换游戏协议帧。

v0.5 的纯文本/按需内容 MVP 先用更轻的原生 USB CDC ACM + 便携客户端验证 PC 闭环，
NCM 是后续替换 backend，不与 CDC ACM 同时作为默认配置常驻。两条路线共用
`wakenic_ports`、TriLink 无线层和 CONTENT/FILE 格式，不形成两套空口协议。

三块板烧录同一固件。无线数据面为三节点全互联，控制面动态选举一个 Leader；
Leader S3 维护权威游戏状态，因此三台电脑都是对等的显示/输入客户端，没有固定
“主机电脑”。第一版 Leader 丢失时停止当前回合并回到大厅，不迁移半局状态。

```text
Browser A <-> USB-NCM <-> S3-A \
Browser B <-> USB-NCM <-> S3-B --- ESP-NOW full mesh
Browser C <-> USB-NCM <-> S3-C /
                               one elected S3 Leader
```

这一边界保证：

- 用户只需插入核心板并打开固定本地地址；
- 电脑原 Wi-Fi、互联网和默认路由不被接管；
- TCP、HTTP、WebSocket 和 1500 字节 NCM 帧终止在本机 S3，不跨 ESP-NOW；
- 无线侧始终只处理最大 200 字节的受控消息，不承担 ARP/IPv6/mDNS 泛洪；
- 无云服务器、无域名、无路由器，月租为 0；
- 游戏页面、房间逻辑、节点发现、选主和可靠性策略均由核心板承载。

## 2. 已有证据，不是估算

### 2.1 核心板与工具链

既有工程已锁定并构建以下基线：

| 项目 | 已验证值 |
|---|---:|
| SoC / 模组 | ESP32-S3 / ESP32-S3-WROOM-1-N16R8 |
| CPU 配置 | 双核，当前轻量工程 160 MHz |
| Flash | 16 MiB，80 MHz，DIO |
| PSRAM | 8 MiB，80 MHz，Octal/OPI |
| RTOS tick | 1000 Hz |
| SDK | ESP-IDF v5.5.5 |
| 调试/下载 | CH343，UART0 GPIO43/44 |
| 应用 USB | 原生 USB 口，留给 TinyUSB NCM |
| 状态灯 | GPIO48 WS2812，已有独立任务实现 |
| 无线基线 | ESP-NOW STA 模式，频道 6 |

一号板曾实测 MAC `Board1`、16 MiB XMC Flash
（JEDEC `0x207018`）和 8 MiB PSRAM；二号板曾实测 MAC
`Board2`、16 MiB Flash 和 8 MiB PSRAM，并与一号板完成双向
PING/PONG/STATUS。`0x207018` 设备需要沿用既有
`xmc_7018_compat` bootloader 兼容组件和 DIO 配置。

第三块板尚无本项目保存的物理容量、JEDEC ID、MAC 和 USB PnP 记录，不能仅因外观
相同而视为通过。它必须经过第 12 节的硬件门。

### 2.2 三个实际链接产物的容量上界

以下数字来自现有 `.map`/`.bin`，均由同一 ESP-IDF v5.5.5 工具链生成：

| 工程 | 功能规模 | 应用镜像 | DIRAM 使用 | DIRAM 剩余 |
|---|---|---:|---:|---:|
| WakeNIC v0.2 骨架 | 状态机、硬件检查、诊断 | 178,672 B | 61,063 B（17.87%） | 280,697 B |
| TriLink v0.3 骨架 | v0.2 + ESP-NOW、协议、两 Peer、HELLO/心跳 | 752,512 B | 118,951 B（34.81%） | 222,809 B |
| TriLink v0.4 精简 RX | v0.3 + 固定池、借用 view、service codec | 753,024 B | 122,543 B（35.86%） | 219,217 B |
| TriLink v0.5 内容 seam | v0.4 + 已编译未链接的 CONTENT/FILE wire codec | 753,040 B | 122,543 B（35.86%） | 219,217 B |
| 双板桥 pc_dongle | Wi-Fi、ESP-NOW、USB 串口、LED | 781,456 B | 126,951 B（37.15%） | 214,809 B |
| 小智 HW-678A | Wi-Fi、音频、Opus、显示、WebSocket 等 | 2,554,448 B | 157,143 B（约 46%） | 184,617 B |

小智工程还已在同一 16 MiB 核心板上验证两个 4,128,768 B OTA 应用分区。
这说明 TriLink 的 Flash 和静态 RAM 有充足物理上界，但不能代替加入 NCM 后的运行时
内部堆测试。

当前 v0.5 加固骨架共 777 项主机检查通过；全新目录完整构建和结构容量门通过，
应用 753,040 B、bootloader 20,944 B。CONTENT/FILE 只增加协议编译单元，最终 map
未链接，静态 DIRAM 相对 v0.4 不变。v0.4 固定池优化得到的启动后保守净省至少
7,224 B 内部 RAM，仍需用实物 heap 日志复核。

## 3. 用户侧形态

### 3.1 正式目标

1. 每台电脑把一块 S3 的原生 USB 口插入电脑。
2. 系统枚举 CDC-NCM 网卡；S3 DHCP 给电脑分配本地地址。
3. 电脑访问固定地址 `http://172.31.255.1`。
4. 页面由 S3 Flash 提供，并从浏览器主动建立到本机 S3 的 WebSocket。
5. 三块 S3 自动发现、选主，页面显示 1/3、2/3、3/3、Leader 和链路统计。
6. 三人全部 Ready 后开始游戏。

每台 S3—电脑链路都是独立点到点网段，可以重复使用：

- S3：`172.31.255.1/30`
- PC：由 DHCP 分配 `172.31.255.2/30`
- 不发送默认网关和 DNS，避免改变电脑正常互联网路由
- 主路径使用固定 IP，不把 mDNS 成功作为验收条件

网页和 WebSocket 都由电脑主动访问本机板卡，因此不要求电脑监听来自另一台电脑的
入站 UDP 端口。自动弹出浏览器不是 USB-NCM 的可靠标准能力；如需要，可提供一个
只负责打开固定 URL 的便携启动器，但组网逻辑不得移到该程序。

### 3.2 兼容性降级

如果某台目标 Windows 无法使用 NCM 系统类驱动，则临时退回已存在的
USB Serial/JTAG 数据通道和单文件便携客户端。`pc_link` 接口保持不变，
`cluster_core`、游戏状态机和 ESP-NOW 协议不修改。降级是硬件 bring-up 手段，
不是最终演示形态。

## 4. 无线拓扑与主机模型

### 4.1 为什么不做完全无主锁步

三节点完全无主锁步会把一次丢包变成三端共同停顿，并增加时钟漂移、重复输入、
重连补状态和分歧检测的难度。固定指定某块板为主又会引入标签和单点使用规则。

本项目采用：

- 数据面：三块板彼此直接单播，全互联，无转发跳数；
- 控制面：动态 Leader；
- 游戏面：Leader 权威 tick 和碰撞结果；
- 可用性：无固定主板，Leader 失效后重新选举；
- 一致性：不足两个在线节点时暂停回合，不允许孤立节点独立判胜。

### 4.2 简化选主规格

1. 启动后在频道 6 进入 1.5 s 发现窗，每 200 ms 广播一次 `HELLO`。
2. 节点身份使用完整 Wi-Fi STA MAC；USB-NCM MAC 从本机 MAC 派生为本地管理地址。
3. 首次大厅中，在线节点按完整 MAC 字典序选择最小者作为候选 Leader。
4. 候选者生成随机 `session_id`，携带递增 `term` 发出 `LEADER_OFFER`。
5. 三节点中取得至少 2 票后生效；每个节点在一个 term 只投一票。
6. Leader 每 100 ms 单播心跳；连续 600 ms 未收到则进入 0~120 ms 随机退避并重选。
7. 游戏进行中，新出现的更小 MAC 不抢占健康 Leader，只在下次大厅或失效后参与选举。
8. 所有游戏帧携带 `session_id`、`term`、`round_id` 和序号，拒绝旧会话迟到包。

这里不是实现完整 Raft。固定三节点、无持久化半局恢复时，只保留防双主所需的任期、
一次投票和 2/3 法定人数。

### 4.3 三块板的零配置成组

课程演示的三块板作为一个套装，在烧录时写入相同 `group_id` 和组密钥；运行时不让
用户输入 SSID 或配对码。`HELLO` 广播只携带 group、MAC、boot nonce、能力位和短
认证标签，不传密钥。发现后，双方以排序后的两个完整 MAC 和组密钥派生 16 B LMK，
建立 ESP-NOW 加密单播 Peer。密钥不得写入普通 INFO 日志。

第一版允许三块板由同一发布包预配置。按 BOOT 键临时成组、密钥轮换和第四块备用板
加入属于增强项；不要为了现场“自动配对”先引入 BLE、SoftAP 或云端账号。

## 5. 游戏与消息规格

首个演示游戏采用 30×20 网格的三人光迹对战：输入离散、碰撞确定、状态可压缩，
不会让图形和物理引擎掩盖网络设计。

- 权威逻辑 tick：20 Hz，`vTaskDelayUntil()` 或核心循环绝对时基；
- 浏览器绘制：60 Hz，本地插值，不提高无线发送率；
- 输入：方向、Ready、重新开始；
- 第一版 Leader 迁移：终止本回合并回大厅；
- 增强版：Follower 保存最近 keyframe 和输入日志，再实现半局恢复。

无线应用帧硬上限继续保持 200 B。计划头部上限 28 B，应用 payload 上限 172 B：

| 消息 | 总帧预算 | 可靠性 |
|---|---:|---|
| `HELLO` / `HEARTBEAT` | 44~56 B | 周期覆盖，不重传 |
| `VOTE` / `READY` / `ROUND_START` | 48~64 B | ACK、超时、最多 3 次重传 |
| `INPUT` | 36~44 B | 序号去重，携带当前和上一次输入，不排队补旧输入 |
| `STATE_DELTA` | 52~76 B | 最新状态优先，丢失不重传 |
| `STATE_KEYFRAME` | 不超过 196 B | 每 500 ms，两次连续发送或显式 ACK |

30×20 网格按每格 2 bit 编码为 150 B；加三名玩家头部/方向约 12 B，仍可放入
172 B payload。无需为游戏状态使用 ESP-NOW v2 长帧或无线分片。

## 6. 空口预算

按最坏的稳定三人回合估算：

| 流量 | 计算 | 应用字节率 |
|---|---:|---:|
| 两个远端输入到 Leader | `2 × 20 × 44` | 1,760 B/s |
| Leader 给两个 Follower 的状态增量 | `2 × 20 × 76` | 3,040 B/s |
| 2 Hz 完整 keyframe | `2 × 2 × 196` | 784 B/s |
| 心跳、ACK、偶发控制 | 预算 | 1,500 B/s |
| 合计 | — | 约 7.1 KB/s |

将 802.11/ESP-NOW 开销、MAC ACK、竞争和重传按两倍以上保守放大，工程空口预算定为
15 KB/s。相对 ESP-NOW 默认 1 Mbit/s 原始速率仍有明显余量。

状态更新必须使用两个加密单播，不用广播承载游戏状态。广播只负责发现；它没有
ESP-NOW 加密和单播确认。每块板只需维护另外两个加密 Peer，低于 ESP-IDF 默认
加密 Peer 容量。

量化验收目标（不是现有实测值）：

- 30 分钟稳定回合中，RX/TX 队列溢出均为 0；
- 控制消息在最多 3 次发送后应用层成功率为 100%；
- 同桌环境板间 RTT：P95 小于 30 ms，P99 小于 50 ms；
- 20 Hz tick 迟到超过 10 ms 的比例低于 0.1%；
- Leader 失效后 1 s 内回到具有法定人数的新大厅。

若射频环境不能满足 RTT 门槛，先降低 keyframe 频率和状态发送量，再评估频道；
不通过增大无界队列来掩盖拥塞。

## 7. Flash 分区与镜像预算

建议沿用已在小智工程验证过的双 4032 KiB OTA 边界，但不复制其 8 MiB 资源分区：

| 分区 | 地址 | 大小 | 用途 |
|---|---:|---:|---|
| `nvs` | `0x9000` | 64 KiB | 组 ID、Peer、频道、统计配置 |
| `otadata` | `0x19000` | 8 KiB | OTA 选择数据 |
| `phy_init` | `0x1B000` | 4 KiB | RF 校准数据 |
| `ota_0` | `0x20000` | 4032 KiB | 应用 A |
| `ota_1` | `0x410000` | 4032 KiB | 应用 B |
| `coredump` | `0x800000` | 512 KiB | 故障闭环，可在课程版关闭 |
| 未分配 | `0x880000` 起 | 7.5 MiB | 保留，不为“用满 Flash”随意切分 |

网页 HTML/CSS/JS 经 gzip 后硬限制为 256 KiB，并嵌入应用镜像 `.rodata`，不在启动时
整体复制到 RAM。这样 UI 与固件版本原子更新，也省去 SPIFFS/LittleFS 组件和资源
兼容问题。

镜像硬门槛：

- `app.bin <= 2,000,000 B`；
- 相对 4,128,768 B OTA 分区至少保留约 51.5%；
- bootloader 必须通过镜像头、checksum、分区边界和 XMC JEDEC 兼容验证；
- 超过 2 MB 时必须先输出 `idf.py size-components` 归因，不直接扩大分区掩盖增长。

参照值：ESP-NOW/USB 串口双板桥约 0.78 MB；完整小智应用约 2.55 MB。TriLink
目标落在二者之间，并用 2 MB 硬门限制无关依赖扩张。

## 8. 内部 RAM 与 PSRAM 预算

ESP32-S3 链接器在当前配置下报告可用 DIRAM 总计 341,760 B。静态门槛与运行时门槛
必须同时满足：

### 8.1 静态链接门

- `idf_size` DIRAM 使用不超过 196,608 B（192 KiB，约 57.5%）；
- 链接后至少保留约 145 KiB 给启动后的动态对象和系统堆；
- 项目自写单函数静态栈帧不超过 1024 B；
- 大于 512 B 的局部数组必须说明生命周期，优先改成固定 buffer pool。

### 8.2 运行时硬门

在 Wi-Fi、ESP-NOW、NCM、DHCP、HTTPD、WebSocket 和三 Peer 全部启动后：

| 指标 | 必须达到 |
|---|---:|
| internal free heap（稳定后） | >= 96 KiB |
| internal minimum free heap（30 分钟） | >= 64 KiB |
| internal largest free block | >= 32 KiB |
| PSRAM free heap（稳定后） | >= 6 MiB |
| PSRAM largest free block | >= 4 MiB |
| 任一任务栈高水位 | >= 分配栈 25%，且 >= 512 B |
| 热身 5 分钟后的持续堆下降 | 30 分钟内不得超过 4 KiB |

建议配置：

- `CONFIG_SPIRAM_MALLOC_RESERVE_INTERNAL=65536`；
- `CONFIG_SPIRAM_MALLOC_ALWAYSINTERNAL=4096`；
- Wi-Fi、TinyUSB、队列、协议帧和关键任务栈保留内部 RAM；
- 只有可丢弃诊断历史、长日志和未来回放缓冲显式放入 PSRAM；
- 不在 Wi-Fi/USB 回调、游戏 tick 中调用普通 `malloc/free`。

8 MiB PSRAM 不是完成本项目的必要条件，但提供了诊断和未来状态回放空间。项目不能
因存在 PSRAM 就把对 DMA、Wi-Fi 或低延迟路径有要求的对象随意迁出内部 RAM。

## 9. 固定队列和缓冲池预算

无线层只保存最大 200 B 的项目协议帧，不保存 NCM 以太网帧。v0.5 当前对象与后续
预算分开列出：

| 对象 | 单项预算 | 深度 | 合计 |
|---|---:|---:|---:|
| 当前 ESP-NOW RX 固定槽 | 210 B | 16 | 3,360 B |
| 当前 ready/free 索引数据 | 1 B | 16+16 | 32 B |
| 后续 ESP-NOW TX request | <=216 B | 8 | <=1,728 B |
| 核心 typed event | 48 B | 32 | 1,536 B |
| WebSocket TX | 208 B | 8 | 1,664 B |
| 可靠控制重传槽 | <=216 B | 4 | <=864 B |

RX 池编译期固定为 3,360 B，两个队列只搬运槽号。连同后续 Queue 控制块、Peer 表、
双缓冲游戏状态，项目固定协议内存池硬上限仍为 16 KiB 内部 RAM。池满时按消息等级
处理：

1. `FATAL`、选举和回合控制保留槽位；
2. 新 `STATE_DELTA` 覆盖旧 delta；
3. 输入只保留每玩家最新序号；
4. 记录 drop/high-water，不阻塞 Wi-Fi 回调；
5. 不动态扩队列。

## 10. 任务、核心与 USB 端点预算

保持当前“单一状态所有者”结构。v0.5 由 `trilink_node` 直接消费 RX 固定池，不再用
单独 `espnow_rx` 任务；文件传输真正加入前也不预留 TX worker。

| 上下文 | 栈预算 | 优先级 | 责任 |
|---|---:|---:|---|
| `trilink_node`（当前） | 4096 B | 5 | RX、成员表、HELLO/心跳；唯一 cluster owner |
| `app_main` / core reducer（当前） | 现有任务栈 | 5 | 启动状态与周期诊断 |
| bulk TX scheduler（后续按需） | 4096 B 上限 | 5 | 控制优先、窗口、完成与超时 |
| ESP HTTPD | 6144 B | 3 | 一个本地页面和一个 WebSocket |
| TinyUSB task | 4096 B | 组件配置 | NCM device |
| lwIP tcpip task | 3072 B | ESP-IDF 默认 | 本地 USB IP 栈 |
| status LED | 2048 B | 1 | GPIO48 指示，可关闭 |

后续完整功能的可控任务栈仍约 25--29 KiB，取决于是否需要独立 bulk TX scheduler；
Wi-Fi 内部任务另按运行时堆统计。诊断不创建单独任务。第一版保持 160 MHz；只有记录
到持续 tick miss 后才切换 240 MHz，并重新做功耗、温升和时序验证。

核心分配建议：Wi-Fi 驱动留在已验证的 Core 0；node owner 和后续 TX worker
放 Core 1；HTTPD 不得高于无线 owner。实际固定 affinity 前先保留一轮无 affinity
对照数据，避免凭直觉调度。

NCM-only 需要 EP0，以及 interrupt IN、bulk IN、bulk OUT，ESP32-S3 USB OTG 端点
容量足够。CH343 是板外 USB-UART，不占原生 OTG 端点。未来加入 HID wake 理论上只
再增加 interrupt IN，但必须另做复合描述符、USB suspend、供电与目标笔记本唤醒门，
不能用“端点数量放得下”替代远程唤醒实测。

## 11. 实施组件和状态机

```text
components/
  wakenic_board/       HW-678A、Flash/PSRAM/JEDEC/MAC 自检
  trilink_protocol/    wire frame、borrowed view、service、CONTENT/FILE wire codec
  trilink_radio/       ESP-NOW、固定 RX 池、Peer、串行 TX
  trilink_cluster/     发现、成员过期、后续任期和投票
  trilink_node/        单一 cluster owner 与 service dispatch seam
  pc_link_ncm/         TinyUSB NCM、DHCP、HTTP、WebSocket
  game_core/           纯 C reducer、20 Hz tick、keyframe/delta
  file_transfer/       后续固定窗口、ACK bitmap、总长/哈希
  web_assets/          gzip 页面，以 rodata 嵌入
  status_led/          可选 GPIO48 指示
```

主状态建议：

```text
BOOT -> HW_READY -> USB_READY -> RADIO_READY -> DISCOVERY
DISCOVERY -> FOLLOWER | CANDIDATE -> LEADER -> LOBBY -> PLAYING
PLAYING --lost quorum--> PAUSED -> ELECTION -> LOBBY
any --fatal invariant/capacity mismatch--> FAULT
```

所有回调只能复制有界数据并发布事件；纯 reducer 不依赖 ESP-IDF，继续走主机回归。
USB、ESP-NOW 和将来的 wake provider 都通过端口接口接入，不直接修改核心状态。

## 12. 分阶段验证与放行标准

### Gate A：纯软件与容量探针

- 扩展 reducer 主机测试：所有发现顺序、重复票、旧 term、失去法定人数、Leader
  丢失、旧 round 数据、队列满策略；
- clean build，不复用旧 `sdkconfig`；
- 自动检查 16 MiB/DIO/80 MHz、8 MiB Octal/80 MHz、分区边界、镜像头；
- 自动执行 `idf_size` 并检查第 7、8 节静态门；
- Web bundle gzip 后不超过 256 KiB；
- 不接板也能通过，不声称 NCM 或 RF 已工作。

### Gate B：逐板身份与十分钟核心 soak

三块板逐一、一次只接一块：

- 记录 CH343 与原生 USB 的 VID/PID、序列实例、ContainerId、位置路径；
- 记录 MAC、JEDEC ID、物理 Flash、PSRAM、reset reason；
- 确认每块均为 16 MiB + 8 MiB，否则停止，不烧入统一发布镜像；
- 十分钟无 panic、brownout、WDT、复位或单调内存下降；
- 原始串口日志分别归档，不能用板 1/2 的历史日志代替板 3。

### Gate C：NCM-only 单板

- 三台目标电脑分别完成枚举、DHCP、固定 IP 页面、WebSocket 双向数据；
- 连续插拔 10 次，设备实例稳定，无未知设备；
- 传输 1 MiB 校验数据，CRC 一致；
- 原网络默认路由不改变；
- 休眠/恢复后允许重新枚举，但不得死锁或泄漏。

### Gate D：三板 ESP-NOW

- 广播发现后建立两个加密单播 Peer；
- 节点表在 3 s 内稳定为 3/3；
- 固定频道 6 先跑；记录 RSSI、RTT、发送完成、应用 ACK、duplicate、drop；
- 30 分钟双向压力满足第 6、8 节门槛；
- 依次拔掉每块板，确认任意当前 Leader 在 1 s 内被替代并回到大厅。

### Gate E：完整游戏

- 三浏览器各只能控制自己的玩家；
- Leader 为唯一碰撞和胜负来源；
- 人为丢弃 1%、5%、10% 状态包，验证 delta 丢失可由 keyframe 收敛；
- 控制消息重传不产生重复 Ready、重复开局或重复结算；
- 30 分钟游戏后所有队列 drop 为 0，栈/堆门槛仍通过。

### Gate F：可选 HID 唤醒

只有 NCM-only 稳定后再进入。分别验证 USB 供电、系统睡眠态、HID remote wake、
复合 NCM+HID 枚举；任何一项失败都不阻塞三机游戏专网。唤醒与游戏共用板卡，
但不共用状态机故障域。

## 13. 当前明确未验证的事项

- 本轮没有开发板连接，无法识别第三块板或采集新运行时高水位；
- 当前通过的是 TriLink v0.5 软件骨架：已含 ESP-NOW、200 B 帧、两 Peer 表、
  HELLO、未加密单播心跳和最低 MAC Leader；本轮没有上板，不能声称三板 RF 已通过；
- v0.5 已有零复制 view、service envelope、文本/资源和文件线协议，但 APP_DATA 尚未
  dispatch，仍不含 NCM、HTTPD、WebSocket、完整文件调度器、正式 quorum election、
  游戏和 HID wake；
- 历史 ESP-NOW 实测覆盖两块板约 20 s，30 分钟压力门仍未完成；
- 历史原生 USB 应用 COM 也尚未完成 PC 输入到 ESP-NOW 的实物闭环；
- NCM 在 ESP-IDF 代码和官方示例中存在，但尚未在这三台目标电脑上验证；
- RTT、tick miss、运行时内部堆门槛是放行目标，不是已有测量结果。

因此目前可以确认“物理容量足够、软件构成有可靠来源、预算闭合”，但只有 Gate B~E
全部保存实物证据后，才能宣称三块核心板可直接即插即用。
