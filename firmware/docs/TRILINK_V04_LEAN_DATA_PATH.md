# TriLink v0.4 精简数据路径与扩展契约

> 历史验证记录：当前工程为 v0.5；v0.4 的 RX 优化仍保留，最新 CONTENT/FILE
> 协议和容量结果见 `TRILINK_V05_CONTENT_EXCHANGE.md`。

## 1. 决策结果

v0.4 不改变三板同固件、固定频道、ESP-NOW 自动发现和最低 MAC 临时 Leader 的产品
方向，只处理两个会阻碍后续数据传输的问题：

1. v0.3 的一个包要经过 radio 队列、radio 任务、node 队列和 owning decoder；
2. 172 B payload 没有稳定的多业务命名空间，文件功能容易侵入游戏/集群协议。

最终选择是“单一固定 RX 池 + 槽号队列 + 借用型 view + 4 B service envelope”。没有
新增常驻 worker、注册表、堆分配或完整文件状态机。

## 2. RX 热路径对比

### v0.3

```text
driver data
  -> callback local event memcpy
  -> radio queue copies 210 B item
  -> radio task copies 210 B item out
  -> node callback memcpy
  -> node queue copies 210 B item
  -> node task copies 210 B item out
  -> frame decoder copies payload
```

小包也要按定长 queue item 搬运整项。两个项目任务各保留 4,096 B 栈，两个队列各保留
16 个 210 B 数据项。

### v0.4

```text
driver data
  -> callback gets one free slot index
  -> one memcpy into pool slot
  -> ready queue copies one uint8_t index
  -> node acquires const slot pointer
  -> frame/service views point into that slot
  -> node releases the slot index
```

最大 payload 的整帧/负载级复制由最多七次降为一次。索引队列仍提供跨核同步和突发
排队，但每次只复制 1 B。CRC、group、HELLO 和后续 service 校验都在同一个借用期内
完成。

## 3. 固定池所有权

每个槽的实际结构为：

```c
uint8_t source_mac[6];
uint16_t data_len;
int8_t rssi;
uint8_t data[200];
```

编译器得到 `sizeof(trilink_radio_rx_frame_t) == 210`，16 槽共 3,360 B。状态机为：

```text
FREE --callback reserves--> FILLING --copy complete--> READY
READY --node acquire--> ACQUIRED --node release--> FREE
```

约束如下：

- callback 只允许零等待取得 free 槽和投递 ready 槽；无槽立即 drop；
- callback 不解析、不日志格式化、不分配内存、不等待发送；
- `trilink_node` 是唯一 RX consumer；
- acquire 成功后必须原指针 release，不能释放内部地址或副本；
- frame/service view 的生命周期不得超过 RX 槽借用期；
- 状态迁移受跨核 critical section 保护；重复释放、外部指针或状态异常进入
  `rx_ownership_errors`；
- 诊断同时给出累计 `rx_acquired/rx_released` 与瞬时 `rx_ready/rx_in_use`。

固定池位于内部 RAM。它处于 Wi-Fi 热路径，不迁入 PSRAM；PSRAM 留给以后可丢弃的
回放或诊断数据。

## 4. RAM 与栈核算

最终 v0.4 clean map 与 v0.3 基线对比：

| 指标 | v0.3 | v0.4 | 变化 |
|---|---:|---:|---:|
| 应用镜像 | 752,512 B | 753,024 B | +512 B |
| 静态 DIRAM | 118,951 B | 122,543 B | +3,592 B |
| 静态 DIRAM 门余量 | 77,657 B | 74,065 B | -3,592 B |
| radio RX callback 静态帧 | 256 B | 48 B | -208 B |
| node task 静态帧 | 272 B | 64 B | -208 B |
| process RX 静态帧 | 224 B | 64 B | -160 B |
| encode local frame 静态帧 | 224 B | 64 B | -160 B |
| 专用 radio RX task | 4,096 B 栈 | 无 | 删除 |

静态 DIRAM 增加不是总内存倒退，而是把接收池和索引队列显式放进链接期 BSS。v0.3
启动时至少动态申请：

```text
radio RX queue data = 16 * 210 = 3,360 B
node  RX queue data = 16 * 210 = 3,360 B
radio RX task stack             = 4,096 B
                                  --------
最低动态数据/栈                 = 10,816 B
```

用 v0.4 静态 DIRAM 增量 3,592 B 抵扣，启动后内部 RAM 的保守净改善至少为 7,224 B。
这个下界没有把两套动态 Queue 控制块和被删任务 TCB 算入，因此实际应略高；但只有
板上 `heap_caps_get_free_size/largest_free_block` 的同阶段对照才叫实测。

node 任务仍保留 4,096 B 栈。静态帧变小不足以证明完整调用链在 Wi-Fi 错误、长日志和
超时分支上的高水位；三板 30 分钟日志通过前不继续压栈。

## 5. 零复制 wire view

`trilink_frame_decode_view()` 完成 magic、版本、头长、类型、flags、payload 长度、总长
和 CRC32 检查，然后让 `payload` 指向输入 wire buffer。原有 owning
`trilink_frame_decode()` 保留，并建立在 view decoder 上，供确实需要长期持有数据的
调用者使用。

TX 同样提供 `trilink_frame_encode_view()`：调用者可以直接给出 payload 指针，避免先
复制进 `trilink_frame_t.payload` 再编码。当前 HELLO/HEARTBEAT 已切换到此接口。

## 6. 业务复用信封

所有后续通用数据走 `TRILINK_MSG_APP_DATA`，其 payload 为：

| Offset | 长度 | 字段 | 规则 |
|---:|---:|---|---|
| 0 | 1 | service id | 0 非法；1 游戏；2 文件；3 诊断；32..255 用户扩展 |
| 1 | 1 | opcode | service 私有命令 |
| 2 | 2 | data length | 小端；必须精确等于剩余长度 |
| 4 | N | data | `0..168 B` |

它不包含通用 `transfer_id`，因为游戏输入不应为文件语义付出固定开销。文件组件以后在
自己的 data 内定义分片头；游戏、诊断和用户服务不受影响。

`trilink_service.c` 是单独编译单元。当前固件没有 APP_DATA dispatcher，因此链接 map
确认 codec 对象未进入最终镜像；需要它的下一版一旦引用即可链接，无额外组件和协议
版本改动。

## 7. 文件传输的最小后续实现

下一步不要一次缓存整文件，也不要把 USB/NCM 1500 B 帧原样塞入 ESP-NOW。建议新增
`trilink_file_transfer`，只拥有固定窗口：

```text
PC record / local web request
  -> FILE_OPEN: transfer id, file bytes, optional name/hash metadata
  -> FILE_CHUNK: transfer id, chunk index/offset, <= fixed chunk payload
  -> FILE_ACK: cumulative index + missing bitmap
  -> FILE_CLOSE: final bytes + digest result
  -> FILE_CANCEL: explicit reason
```

建议第一版参数：

- 4 个在途分片，超时只重发缺失 bitmap 指出的分片；
- `transfer_id` 32 bit，每个 peer 同时最多一个传输；
- 分片内容直接来自固定 TX 槽，不复制整文件；
- 控制/心跳优先于 bulk，文件发送受 token/window 背压；
- 接收端边到边校验总长度，最终可选 SHA-256；
- 断电续传、目录、任意路径写入先不做；
- 对小文件和课程演示先使用 168 B envelope 上限，吞吐实测后再决定是否增加独立大 MTU
  bulk 帧版本。

当前同步 TX 只承载低频 HELLO/心跳。等 FILE service 真正落地时，再增加一个有明确
控制优先级的 TX scheduler；v0.4 不为空功能支付任务栈和队列常驻成本。

## 8. 与计网课程的对应

这条实现不再只是 IoT 功能拼装，可直接形成课程实验指标：

- 数据链路：MAC 寻址、广播发现、单播 peer、CRC 检错、固定 MTU；
- 协议复用：type + service id 分层、显式字节序、严格长度封装；
- 可靠传输扩展：序号、ACK、重传、窗口、背压、重复包与回绕；
- 分布式状态：成员超时、重启 epoch、Leader 视图与后续 quorum 边界；
- 性能工程：copy 次数、队列 item 大小、固定池、栈帧、丢包与高水位；
- 安全边界：CRC 与认证区分，后续 PMK/LMK、配对和 anti-replay 独立加入。

## 9. 软件验证结果

- reducer：185/0；
- frame/view：113/0；
- service 与 APP_DATA 组合：62/0；
- cluster：132/0；
- 合计：492 checks，0 failures；
- ESP-IDF v5.5.5 全新目录、无 ccache build：PASS；
- 52 application components、minimal build、XMC wrapper、镜像/分区/hash：PASS；
- 应用 753,024 / 2,000,000 B；
- 静态 DIRAM 122,543 / 196,608 B；
- 固定 RX pool 3,360 B；专用 radio RX task 不存在；
- RX callback 48 / 64 B 门；node task 64 / 96 B 门；
- service codec 编译对象存在、当前 app 未链接：PASS。

最终证据目录：

```text
<private-work-root>\verify_trilink_v040_final_20260913
```

## 10. 尚未验证

本轮没有连接或烧录三块开发板。以下结论仍必须由实物给出：

- 三板同时发现、双向单播、Leader 一致与断连重选；
- RX 固定池在真实 burst/RSSI 条件下是否 drop；
- `rx_acquired == rx_released` 且 `rx_in_use` 回到 0；
- Wi-Fi 启动后实际 internal heap、最大块、node 栈高水位；
- 30 分钟无泄漏、WDT、panic、复位；
- 后续 USB-NCM、文件 service 和 2.4G/HID 唤醒闭环。
