# TriLink USB 与 Room 最小契约

状态：客户端已实现，等待 ESP32-S3 原生 USB 后端对接和三板实测。

## 1. 层次边界

```text
WinForms UI
    |
Room commands / replicated snapshot
    |
PC link: native USB CDC ACM (UTF-8 line control protocol in MVP)
    |
local S3: APP_DATA Room service dispatcher
    |
ESP-NOW unicast/broadcast between three S3 nodes
```

USB 文本协议不直接跨 ESP-NOW。S3 应把 USB 命令转为定长二进制 Room service 消息；无线回调只复制有界帧并投递队列。

## 2. 识别握手

PC 每次打开候选 CDC 后生成随机 nonce：

```text
PC -> S3
TRILINK/1 HELLO 7fa02c11

S3 -> PC
TRILINK/1 DEVICE 7fa02c11 10:00:00:00:00:01 5Li755S1IEEvIFMzLUE= 00000003
```

字段顺序：

1. 协议标记 `TRILINK/1`；
2. 消息类型；
3. nonce 原样回显；
4. 完整节点 MAC/Node ID；
5. UTF-8 名称的 Base64；
6. 8 位十六进制能力位。

客户端只有在 nonce、版本、字段数和能力字段全部有效时才弹出，不凭 VID/PID 直接认定设备。

## 3. 搜索附近设备

```text
PC -> S3
TRILINK/1 SEARCH 4ab61f20

S3 -> PC
TRILINK/1 PEER 20:00:00:00:00:02 5Li755S1IEIvIFMzLUI= -43 - - - 1
TRILINK/1 PEER 30:00:00:00:00:03 5Li755S1IEMvIFMzLUM= -51 R-01 Qee6hOeahCBSb29t 10:00:00:00:00:01 1
TRILINK/1 END 4ab61f20
```

`PEER` 字段：Node ID、Base64 名称、RSSI、Room ID 或 `-`、Base64 Room 名或 `-`、leader ID 或 `-`、在线标志。

最终固件必须把 SEARCH 放在任务上下文处理，不从 USB 回调同步等待无线扫描。

## 4. Room 权限与不冲突语义

Room 使用“对等复制、单协调者”模型：

- 所有成员保存同一份成员表、待处理申请和事件序列；
- leader 不是数据主机，只是当前 term 的成员关系协调者；
- creator 是 term 1 的初始 leader；
- `JOIN_REQUEST` 不立即改变成员表；
- `JOIN_APPROVE`、`JOIN_REJECT`、`KICK` 仅接受当前 leader 发起的事件；
- `INVITE` 可由任意当前成员发送；受邀者接受后转为 `JOIN_REQUEST`；
- leader 正常退出时，按最小 `join_order` 选继任者，并令 `term = term + 1`；
- `join_order` 加入后不可修改，退出后不可复用；
- 一人新建 Room 处于 `WAITING`，允许被搜索和加入；
- Room 曾达到两人后状态为 `FORMED`，随后降到一人立即广播 `ROOM_DISSOLVE` 并清除本地映射。

意外掉电时，三节点 Room 的成员变更应要求至少两个当前成员确认；否则网络分区可能产生两个 leader。最小客户端目前只验证显式退出，失联 quorum 是固件阶段的独立验收门。

## 5. 无线 Room service 建议

沿用现有 `TRILINK_MSG_APP_DATA` 和 4 字节 service envelope，后续为 Room 分配一个未占用的 service ID。不要增加新的基础帧格式。

建议 opcode：

| Opcode | 含义 |
|---|---|
| `ROOM_ADVERTISE` | 广播 Room ID、term、leader、成员数 |
| `JOIN_REQUEST` | 申请者向 leader 请求进入 |
| `JOIN_DECISION` | leader 同意或拒绝 |
| `INVITE` | 任意成员点对点邀请 |
| `MEMBER_EVENT` | 加入、退出、踢出、继任事件 |
| `SNAPSHOT_REQUEST` | 发现修订落后时请求快照 |
| `SNAPSHOT` | 全成员状态快照 |
| `ROOM_DISSOLVE` | 正式 Room 降为一人 |

最大三人时，一个紧凑快照可控制在当前 168 字节 service data 上限内：

```text
room_id(4) + term(4) + revision(4) + lifecycle(1)
+ leader_mac(6) + member_count(1)
+ 3 * [member_mac(6) + join_order(2)]
+ pending_count(1)
+ 2 * [request_id(4) + candidate_mac(6) + inviter_mac(6)]
= 77 bytes
```

即使再加 32 字节 Room 名和少量标志仍低于 168 字节，不需要分片，也不应引入 JSON、lwIP 或 WebSocket。

## 6. 顺序与副本规则

每个可改变 Room 状态的事件携带：

```text
room_id, term, revision, actor_mac, operation_id
```

- 同一 term 内只接受连续递增 revision；
- 重复 operation ID 幂等丢弃；
- revision 缺口触发 `SNAPSHOT_REQUEST`；
- 低 term 事件丢弃；
- 新 leader 的第一条事件必须附带前一副本摘要；
- leader 只串行化成员关系，不接管聊天、文件和游戏状态；这些数据仍由发布者点对点发送并按需要复制。

