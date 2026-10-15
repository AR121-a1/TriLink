# TriLink v0.3 三节点代码骨架

> 历史基线：当前实现已升级为 v0.4 固定 RX 池和借用型 view；请先阅读
> `TRILINK_V04_LEAN_DATA_PATH.md`。下文保留用于复核优化前的双队列设计与容量数字。

## 1. 本轮交付边界

v0.3 的目标不是一次实现 USB 网卡、游戏和唤醒，而是先把三块 ESP32-S3 之间最难
返工的底层骨架固定下来：空中帧格式、回调并发边界、两邻居生命周期、同固件自动
发现和确定性 Leader 视图。这样后续 USB-NCM 或小游戏只新增端口和消息处理，不重写
无线层。

当前能够证明的是“源代码可回归、ESP-IDF 可完整链接、镜像与容量满足静态门”。没有
三块实物日志时，不能宣称三板发现、单播、RSSI、超时或 Leader 切换已经通过硬件门。

## 2. 运行时架构

```text
                         +----------------------+
                         | app_main / core loop |
                         | board + boot state   |
                         +----------+-----------+
                                    |
                          start / readiness event
                                    |
                         +----------v-----------+
                         | trilink_node task    |
                         | sole cluster owner   |
                         +----+------------+----+
                              |            |
                    encode/decode      pure state update
                              |            |
                 +------------v--+     +---v----------------+
                 | protocol      |     | cluster            |
                 | LE + CRC32    |     | 2 peers + expiry   |
                 +------------+--+     | lowest-MAC leader  |
                              |        +--------------------+
                         send / queued receive
                              |
                 +------------v-----------------------------+
                 | trilink_radio                            |
                 | Wi-Fi STA, ESP-NOW, callback -> RX queue |
                 | one in-flight TX guarded by mutex/sem    |
                 +------------------------------------------+
```

无线驱动回调不解析帧、不写成员表、不等待锁，只把最多 200 B 数据复制到深度 16 的队列。
`trilink_radio_rx` 再投递到节点深度 16 队列；`trilink_node` 是唯一会改变成员表和 Leader
视图的任务。双队列多一次定长拷贝，但把 Wi-Fi 回调、链路适配和集群状态彻底隔离，
两组事件合计仍远低于 16 KiB 协议池门槛。

## 3. 空中帧 v1

所有多字节字段均显式小端编码，不发送 packed C struct。完整帧上限 200 B，固定头
24 B，尾部 CRC32 4 B，应用 payload 上限 172 B。

| Offset | 长度 | 字段 | 当前含义 |
|---:|---:|---|---|
| 0 | 2 | magic | `0x4C54`，线上字节 `54 4C` |
| 2 | 1 | version | `1` |
| 3 | 1 | header length | `24`，防结构漂移 |
| 4 | 1 | type | HELLO、HEARTBEAT、Leader/游戏预留类型 |
| 5 | 1 | flags | ACK required / retransmit 预留 |
| 6 | 2 | payload length | `0..172` |
| 8 | 4 | group id | 默认 `0x31474C54`（`TLG1`） |
| 12 | 4 | boot id | 每次启动随机，区分同一 MAC 重启 |
| 16 | 4 | term | v0.3 为 0，留给正式多数派选举 |
| 20 | 4 | sequence | 发送端单调序号，按半区间处理回绕 |
| 24 | N | payload | 消息内容 |
| 24+N | 4 | CRC32 | 覆盖头和 payload，仅检错 |

HELLO payload 固定 8 B：能力位 4 B、HELLO 周期 2 B、最大节点数 1 B、保留 1 B。
v0.3 只宣告已经启动的 `ESPNOW_UNICAST` 能力；USB、游戏与 HID 位不会提前冒充可用。

## 4. 三节点发现与 Leader 规则

每块板刷相同镜像后执行：

1. 以 STA 模式启动 Wi-Fi，不连接 AP，关闭省电并固定频道 6；
2. 加入广播 Peer，每 500 ms 发送 HELLO；
3. 从 ESP-NOW 接收元数据取得真实 source MAC，并按 group id 过滤；
4. 第一次看到有效 MAC 时占用两个固定 Peer 槽之一，并建立未加密单播 Peer；
5. 每 1000 ms 向当前两个 Peer 发送单播 HEARTBEAT；
6. HELLO/HEARTBEAT 在 2000 ms 内未刷新则删除成员和底层单播 Peer；
7. 在“本机 + 当前有效 Peer”中选择字典序最低 MAC 作为临时 Leader。

最低 MAC 规则让三节点在成员视图相同后无需配置就得到相同结果，适合第一轮傻瓜式
bring-up。它不等价于 Raft：部分可见时可能出现不同 Leader 视图，没有 term、投票、
法定人数提交或脑裂写保护。因此后续游戏权威状态必须在现有 `term` 字段上补正式
election/quorum，不能把 v0.3 日志中的 `leader=` 当作强一致证明。

同 boot id 的重复序号可刷新在线时间；严格旧序号标记为 `STALE`，既不倒退序号也不
刷新在线时间。boot id 改变会立即标记 `RESTARTED`，使重新上电节点快速回网。正式
加密阶段还要加入认证和 anti-replay；CRC32 不承担安全职责。

## 5. 与成功双板工程的关系

只读参照工程 `esp32s3_usb_espnow_bridge` (historical local reference; not included) 已在两块 HW-678A 上完成过
PING/PONG/STATUS 双向互传。本骨架保留其经过实物验证的关键做法：

- ESP-IDF v5.5.5、ESP32-S3、16 MiB DIO Flash、8 MiB Octal PSRAM；
- Wi-Fi STA + `WIFI_PS_NONE` + 固定频道 6；
- ESP-NOW 回调定长入队，任务上下文处理；
- mutex 保证只有一个在途发送，binary semaphore 接收发送完成，500 ms 超时；
- 显式小端协议和 CRC32；
- 对实测 XMC JEDEC `0x207018` 的精确 bootloader wrapper。

三节点帧格式、成员表、序号规则、Peer 过期、Leader 规则与节点编排均是新实现。旧
桥接应用的 PING/PONG 业务代码没有复制，参考工程也未被修改。

## 6. 静态资源闭合

v0.3 固定项目对象：

| 对象 | 单项上限 | 深度 | 数据区约占用 |
|---|---:|---:|---:|
| radio RX event | <=216 B | 16 | <=3456 B |
| node RX event | <=216 B | 16 | <=3456 B |
| core event | 小型 typed event | 16 | <1 KiB |
| cluster peers | 固定 2 项 | 1 | <128 B |

两类 RX event 用 `_Static_assert` 固定上限。无线 RX task 和 node task 各分配 4096 B
栈；本轮 `.su` 静态审计最大单函数栈帧为 `trilink_node_task` 的 272 B，低于 1024 B
门。完整 clean build 的应用为 752,512 B，静态 DIRAM 118,951 B；相对项目 2 MB / 
192 KiB 门分别保留 1,247,488 B 和 77,657 B。

这些是链接期数字，不是启动后 Wi-Fi 堆、任务栈高水位或 30 分钟泄漏结论。运行时
门仍需实物日志验证。

## 7. 软件验证矩阵

| 门 | 结果 | 覆盖 |
|---|---|---|
| reducer host test | PASS 185/0 | 原启动状态机不回归 |
| protocol host test | PASS 91/0 | 大小端、边界、坏头、坏长度、坏 CRC、HELLO |
| cluster host test | PASS 132/0 | 六种视图、重复/旧序号、重启、表满、过期、重选 |
| ESP-IDF clean build | PASS | v5.5.5、ESP32-S3、52 components、`-Werror` |
| image/provenance | PASS | 16 MiB/DIO/80 MHz、分区、checksum/hash、XMC wrapper |
| static capacity | PASS | 752512 B app、118951 B DIRAM、272 B 最大栈帧 |
| board flash/RF | PENDING | 本轮无板连接，未执行 flash |

## 8. 下一接口，不提前实现

- `pc_link_ncm`：把每台 PC 的浏览器/客户端输入终止在本机 S3；不把 1500 B 以太帧
  原样塞进 ESP-NOW；
- `game_core`：将输入和 keyframe/delta 装入现有 172 B payload；
- `cluster_election`：term、vote、quorum、Leader 丢失暂停和 anti-replay；
- `wake_provider`：HID remote wake 或外置 2.4G 键盘 dongle，仅通过事件接口接入；
- 安全：PMK/LMK 加密单播、设备配对和密钥轮换。

上述模块加入前，先完成三块板的身份记录、三节点 30 分钟 soak 与 Leader 断连实验。
