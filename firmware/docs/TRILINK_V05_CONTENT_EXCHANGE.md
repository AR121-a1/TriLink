# TriLink v0.5 纯文本与按需内容交换协议

状态：线协议和主机测试已完成，当前固件运行路径尚未启用 APP_DATA 分发  
硬件：HW-678A / ESP32-S3-WROOM-1-N16R8  
SDK：ESP-IDF v5.5.5，target `esp32s3`

## 1. 结论

“文本总结，按需请求原始数据”适合作为三机专网的第二个课程应用，而且与现有游戏骨架
不冲突。最终吸收的不是一套独立 JSON/文件协议栈，而是两个复用现有
`TRILINK_MSG_APP_DATA` 的轻量服务：

```text
内容面（无中心）
  TEXT / SUMMARY / GET_RANGE / ERROR

传输面（发布者与请求者直接单播）
  FILE_OPEN / FILE_CHUNK / FILE_ACK / FILE_CLOSE / FILE_CANCEL
```

资源仍由发布它的电脑保存；ESP32-S3 只做寻址、严格解码、分片转发和以后加入的固定
小窗口重传。三块 S3 不保存整文件，不经过云端、域名、MQTT Broker 或游戏 Leader。

这里的“P2P”是三块板在同一 2.4 GHz 信道上的直接链路，不是跨互联网 NAT 穿透。
如果以后要求异地电脑唤醒/取数，仍要单独解决双方发现、NAT、身份认证和离线消息；
不能把“没有商用服务器”误写成“公网无需任何可达协调点”。

当前 v0.5 只把上述格式实现成纯 C 无状态 codec，并独立编译和测试。应用没有引用它们，
所以链接 map 证明 codec 没进入运行镜像，不新增任务、队列、堆对象或常驻 buffer。

## 2. 对原设想的逐项取舍

| 原设想 | 决策 | 原因和落点 |
|---|---|---|
| 纯 UTF-8 文本 | 吸收 | `CONTENT/TEXT`，单帧最多 168 B；严格拒绝截断、过长编码、代理区、超出 Unicode 和内嵌 NUL |
| 发布文件名、大小、摘要、SHA-256 | 吸收 | `CONTENT/SUMMARY`；摘要只描述资源，原文仍在发布 PC |
| 点击摘要后取原数据 | 吸收 | `GET_RANGE(resource_id, offset, length)`，随后由发布者开启 FILE 会话 |
| 广播摘要 | 修改 | 广播只保留低频 `HELLO`；三节点摘要默认向两个已发现 Peer 各单播一次，便于以后加密、ACK 和隐私控制 |
| 每包再放 `src_id/dst_id/message_id` | 拒绝 | ESP-NOW 帧和接收元数据已有源/目的 MAC；现有头已有 `boot_id + sequence`。消息身份使用 `(source MAC, boot_id, sequence)`，重复字段会浪费 MTU并产生双重身份不一致 |
| 空口传完整 JSON | 拒绝 | PC/UI 边界可用 JSON；空口用固定小端字段和原始 UTF-8。这样无需在 S3 常驻 JSON parser，也避免重复字段名占带宽 |
| ESP32 缓存完整文件 | 拒绝 | 16 MiB Flash/8 MiB PSRAM 放得下不等于应该复制；会引入目录、磨损、断电一致性和权限问题。S3 只保留有界窗口，源 PC 是资源服务器 |
| 每个 DATA 都 stop-and-wait ACK | 修改 | 文件块用累计确认加 32 bit bitmap；第一版发送窗口仍限制为 4，避免一个丢包让整个链路停顿，也不为 32 槽支付 RAM |
| MQTT | 拒绝 | MQTT 是 Client/Server 协议，通常需要 Broker，和三板无中间商的目标冲突 |
| 完整 CoAP/UDP/IP | 暂不引入 | 借用 CoAP 的资源 GET 和 block-wise 思想，但原始 ESP-NOW 链路不需要为此加入 UDP/IP、URI parser 和缓存语义 |
| VPN/WebRTC/公网 P2P | 独立后续路线 | 它们解决 IP/NAT 场景，不能提高桌面三板 ESP-NOW 的即插即用性；混入本 MVP 会同时引入信令、凭据和公网故障域 |
| USB CDC、NCM、HTTP 同时加入 | 拒绝 | 它们是 PC 接入的替代后端，不是必须同时常驻。先实测原生 USB CDC ACM + 便携客户端；只有“零安装浏览器”成为硬需求时再换 NCM/HTTP/WebSocket |
| CH343 串口直接承载应用流量 | 不作为正式路径 | CH343/UART0 当前承担烧录和日志，混入二进制记录会破坏诊断边界；原生 USB 口才是应用数据候选 |

内容发布与游戏选主也不冲突：

```text
物理/链路层：三块 S3 全互联
内容服务：发布者就是资源源端，无全局主机
游戏会话：允许临时权威主机或后续 S3 Leader
唤醒：独立 provider，只把休眠 PC 恢复为可服务状态
```

如果发布 PC 正在休眠，S3 最多能保留摘要并返回 `BUSY`/触发独立唤醒流程；它不能在
没有原数据副本时继续提供完整资源。这是方案的真实可用性边界。

## 3. 复用现有 200 B 帧，而不是另造头

现有 TriLink 空中帧：

```text
base header 24 B
  magic/version/type/flags/payload_len/group_id/boot_id/term/sequence
APP_DATA payload 0..172 B
CRC32 4 B
```

`APP_DATA` 内已有 4 B service envelope：

```text
service_id:u8 | opcode:u8 | data_len:u16-le | data[0..168]
```

v0.5 只新增 `service_id=4 CONTENT`，并正式定义既有 `service_id=2 FILE`。源 MAC 来自
ESP-NOW 接收元数据，目的 MAC由单播发送 API 给出，因此不在上述 200 B 中重复传输。
CRC32 仍只负责随机错误检测，不是认证、授权或防重放。

官方 ESP-NOW 文档说明 v1 帧上限为 250 B、v2 上限为 1470 B，并明确指出 MAC 层发送
成功不保证应用已收到，可靠业务仍要自行 ACK、超时重传和去重。本项目继续限制为
200 B，是为了固定内存、可审计复制和兼容较小帧，不是把 200 B 误认为芯片硬上限。

## 4. CONTENT 线格式

### 4.1 `TEXT`

```text
data = utf8[1..168]
```

文本不带 NUL 结尾；收件人由 ESP-NOW 单播目的 MAC 决定。发送给全组等价于向两个
Peer 各发一次，不使用默认明文广播。

### 4.2 `SUMMARY`

```text
sha256[32]
total_size:u32-le
media_type:u8
flags:u8                 bit0 = fetchable
name_len:u8
summary_len:u8
name:utf8[name_len]      name_len >= 1
summary:utf8[summary_len]
```

固定字段 40 B，名称和摘要合计最多 128 B；加 4 B service envelope 后仍恰好不超过
172 B。SHA-256 是原始字节的不可变资源 ID；名字可以重复或变化，不能作为主键。
S3 在解码阶段只检查 ID 非全零，真正的 digest 必须由接收 PC 在文件结束时重新计算。

`media_type` 只是 UI 提示，当前定义 binary、UTF-8 text、JSON、image 和 game data；
它不能绕过接收端文件类型和路径策略。

### 4.3 `GET_RANGE`

```text
sha256[32]
offset:u32-le
length:u32-le            length >= 1, offset + length 不得溢出
```

字节范围请求允许 PC 只取文本正文、缩略数据或断点后的片段，不必先传完整文件。若
资源不存在、区间非法、PC 休眠/繁忙或策略拒绝，源端返回 `ERROR`。

### 4.4 `ERROR`

```text
request_sequence:u32-le  对应请求的 TriLink sequence
code:u8                  NOT_FOUND/BAD_RANGE/DENIED/BUSY/BAD_REQUEST/IO
detail:utf8[0..163]
```

错误正文是面向用户的短提示；协议行为由数值 code 决定，不能解析自然语言来控制状态机。

## 5. FILE 线格式

| opcode | service data | 完整 APP_DATA payload | 语义 |
|---|---:|---:|---|
| `OPEN` | 44 B | 48 B | 非零 transfer ID、SHA-256、本次传输长度、分片大小、窗口 |
| `CHUNK` | 10 B + `1..158 B` | 最大 172 B | transfer ID、字节 offset、长度、原始字节 |
| `ACK` | 12 B | 16 B | transfer ID、累计 base chunk、32 bit 已收 bitmap |
| `CLOSE` | 9 B | 13 B | 实收总长度和 OK/hash/size/IO 状态 |
| `CANCEL` | 5 B | 9 B | 用户、超时、协议错误或资源变化 |

`CHUNK` 的 158 B 原数据占满 200 B TriLink 帧时，项目协议效率为 `158/200 = 79%`；
这是项目帧内比例，不含 802.11/ESP-NOW 空口开销。

ACK 语义为：所有小于 `base_chunk` 的块均已收到；bitmap 的 bit `n` 表示
`base_chunk + n` 已收到。协议字段允许最大窗口 32，第一版调度器仍只启用 4 个在途块。

建议流程：

```text
B -> A  CONTENT/GET_RANGE(resource, offset, length)
A -> B  FILE/OPEN(transfer, resource, transfer_size, chunk=158, window=4)
A -> B  FILE/CHUNK x up to 4
B -> A  FILE/ACK(base, bitmap)
         ...只重发缺失块...
B -> A  FILE/CLOSE(received_size, digest status)
任一端  FILE/CANCEL(reason)
```

`OPEN/CLOSE/CANCEL` 应使用基帧的 `ACK_REQUIRED`；高频 `CHUNK` 使用 FILE bitmap ACK，
不再给每个块叠加 stop-and-wait。对 `GET_RANGE` 响应，`transfer_id` 直接取该 GET 的
TriLink `sequence`，从而无需再增加一个相关 ID；它只在请求者 Peer/boot epoch 内解释。
`transfer_size` 是本次区间传输的字节数，资源完整大小仍来自 SUMMARY，CHUNK 的 offset
仍是资源内绝对偏移。

## 6. 当前资源占用实测

全新目录、无 ccache 的 ESP-IDF v5.5.5 build：

| 指标 | v0.4.0 | v0.5.0 | 变化 |
|---|---:|---:|---:|
| 应用镜像 | 753,024 B | 753,040 B | +16 B（版本/phase 元数据） |
| 静态 DIRAM | 122,543 B | 122,543 B | 0 B |
| 静态 DIRAM 门余量 | 74,065 B | 74,065 B | 0 B |
| 固定 RX pool | 3,360 B | 3,360 B | 0 B |
| 项目新增任务/队列/运行池 | 0 | 0 | 0 |

map 和构建树分别证明：

- `trilink_service.c.obj`、`trilink_content.c.obj`、`trilink_file_wire.c.obj` 各编译一次；
- 三个对象均未链接到当前应用；
- 对象级 `size` 分别为 312 B、2,120 B、1,673 B，合计 4,105 B text，data/bss 均为 0；
- 4,105 B 是“整组函数未来全被引用”的对象级参考上界，不是当前镜像增量；启用哪些
  函数、链接器按 section 裁掉多少，必须由当时的最终 map 重新测量；
- 新 codec 最大单函数静态栈帧 64 B；全项目最大值仍是 `log_diagnostics` 的 272 B。

因此 v0.5 的做法是先冻结并验证 wire contract，而不提前支付 USB、调度器、重传窗口和
文件缓存的常驻成本。

## 7. 启用时的内存合同

后续真正传文件时仍须满足：

- 设备级第一版只允许一个 bulk 会话，避免两个 Peer 同时挤占同步 TX；
- 发送窗口默认 4，按完整 200 B 帧估算最多 800 B 原始重传槽，加元数据后硬限 1 KiB；
- PC 供数要有背压；S3 不得为了等 PC 而无限排队，也不得从 ESP-NOW callback 阻塞读取 USB；
- 接收块在借用 RX 槽期间校验并尽快送入 PC 链路，归还槽后不能保存 view 指针；
- 控制、心跳和游戏输入优先于 bulk；文件发送必须被 token/window 限速；
- 可选摘要缓存最多 8 项、每项固定 200 B，总计 1,600 B；默认先不启用；
- SHA-256 采用流式上下文，不申请文件大小等量缓冲；热路径 buffer 留在内部 RAM；
- 任何新增 task、queue、TinyUSB/lwIP 依赖都重新跑静态 DIRAM、运行时 heap 和栈高水位门。

## 8. 计网课程技术主线

该功能不是“ESP32 传字符串”的 IoT 展示，课程论证应沿协议分层和可测指标展开：

- 数据链路层：2.4 GHz 固定信道、完整 MAC 身份、HELLO 广播与 Peer 单播、固定 200 B
  MTU、CRC 检错、丢包和重复帧；
- 复用/封装：基帧 type、service ID、opcode、显式小端和严格长度构成三层 wire contract；
- 传输可靠性：字节分片、累计确认、选择确认 bitmap、超时重传、4 槽滑动窗口、背压、
  控制流量优先；
- 应用层：类似 REST 的资源摘要、按 ID/字节区间 GET、结构化响应错误；
- 分布式结构：内容无中心且“发布者即源站”，游戏临时权威主机另算，节点发现不等于
  一致性协议；
- 性能实验：goodput、协议效率、RTT P50/P95/P99、丢包率、重传率、窗口 1/2/4 对吞吐
  和游戏延迟的影响、RX pool high-water 与 RAM 占用；
- 安全边界：CRC 与 SHA-256 都不提供来源认证；后续组密钥、ESP-NOW 加密单播、
  anti-replay 和 PC 路径授权要作为独立实验变量。

本系统没有实现 IP 路由或多跳网络层；`group_id` 只是项目覆盖层的组隔离字段。报告中
应如实说明这一点，并把“为什么三节点全互联不需要路由、扩展到多跳时要增加什么”作为
设计比较，而不是把 Peer 表冒充路由表。

## 9. 最短实施顺序

1. 先用当前运行路径完成三块板发现、双向单播、Leader 一致和 30 分钟 RX pool soak；
2. 在 `wakenic_ports` 后实现一种 PC 链路：优先原生 USB CDC ACM，不动 CH343 日志；
3. 在 `trilink_node` 加零队列 dispatcher，只启用 `TEXT/SUMMARY/ERROR`，完成三 PC 纯文本闭环；
4. 加 `GET_RANGE` 和一个 4 槽、单会话 FILE scheduler，先传 1 KiB、64 KiB，再传 1 MiB；
5. 人为丢弃/乱序/重复 1%、5%、10% 分片，验证 bitmap、超时、去重和最终 SHA-256；
6. 只有便携客户端不能满足展示要求时，才把 PC backend 替换为 NCM + 本地网页；
7. HID/2.4G dongle 唤醒独立验证；唤醒失败不能拖垮已运行的内容/游戏专网。

## 10. 当前验证与边界

- host：内容 codec 142 checks、文件 wire codec 142 checks；连同既有套件总计
  777 checks，0 failures；
- clean ESP-IDF build、镜像/分区/XMC wrapper、PowerShell 7 与 5.1 容量门均 PASS；
- 应用 SHA-256：
  `24BAE8748E9F217EB291E250D8BA22186F9C20507287FDB0DCEE7F60A07CBE90`；
- bootloader SHA-256：
  `D132F00DDD7CF9E2C2B279D3167211FAE36DADC4BA7FB13317640C5CAF5ABC1A`；
- 证据目录：
  `<private-work-root>\verify_trilink_v050_final_20260913b`；
- 本轮未接板、未烧录，APP_DATA dispatcher、PC USB 数据面、真实丢包重传、三机文本
  闭环和吞吐仍未验证。

## 11. 参考边界

- [ESP-IDF ESP-NOW programming guide](https://docs.espressif.com/projects/esp-idf/en/stable/esp32s3/api-reference/network/esp_now.html)
- [RFC 7252: The Constrained Application Protocol](https://www.rfc-editor.org/info/rfc7252/)
- [RFC 7959: Block-Wise Transfers in CoAP](https://www.rfc-editor.org/info/rfc7959/)
- [OASIS MQTT Version 5.0](https://docs.oasis-open.org/mqtt/mqtt/v5.0/os/mqtt-v5.0-os.html)

本工程只借用 CoAP 的资源/分块思想，并未声称实现 CoAP；也没有实现 MQTT。
