# v0.7 两跳传输底座：实现与验收边界

> Public copy, redacted for publication on 2026-10-15. Board1/Board2/Board3 are stable public labels, not transmitted MAC addresses. Physical MAC/USB serial/PnP instance identifiers, local COM assignments and backup hashes are withheld. Original private records remain unchanged. Historical local evidence paths are not public downloads.

日期：2026-09-16。对应总方案 A0/A 的传输部分；首版最低目标尚未达成。

## 已实现

- 同一 ESP32-S3 固件，原有 ESP-IDF v5.5.5、N16R8、频道 6、XMC 兼容补丁不变。
- 链序由 USB 显式配置，`1 ↔ 2 ↔ 3`；两端只能沿链发送，即使实际摆得很近也不走直达捷径。
- ROUTE service 9/version 1；4 B 服务头 + 32 B 路由头 + 最多 136 B 业务数据，加上外层 28 B，仍不超过 200 B。
- 路由头保留 room、epoch、原始 source、最终 destination、源 boot、message、跳数、类型、长度。多字节空口字段小端编码；USB 数字为常规八位十六进制文本。
- 逐跳目的 MAC 与原始身份分离，检查成员及上一跳方向，最多两跳；中继不广播业务帧。
- ACK 沿反方向返回，匹配请求方 boot/message 和目标身份。ACK 表示目标固件收件箱接受数据，**不是 PC 已收到或设备动作已完成**。
- 每个源仅一条在途消息；间隔 1200 ms，最多尝试 4 次，正常节点调度下约 4.8 秒超时。TIMEOUT 表示结果未知，不能据此证明对方没收到。
- 32 条、30 秒去重缓存；重复 DATA 重发 ACK，不再次入收件箱。缓存/收件箱满时不 ACK，不淘汰仍有效的去重记录。当前适合低速验证，不是高吞吐传输协议。
- 8 项收件箱；POLL 为可重复读取，CONSUME 匹配源/boot/message 后才移除，最近一次 CONSUME 可幂等重试。
- RX 回调仍只拷贝到原有 16 槽固定池。节点任务借用/解析/释放；异步 TX 队列保存自己的副本。
- TX worker 独占阻塞发送，8 个普通槽 + 4 个 ACK 槽；ACK 最多连续处理 4 项后给普通队列机会。排队帧 600 ms 过期，防止发送陈旧积压。
- USB 4 项命令队列和 4 项回复队列；无线收件箱与 USB 回复背压分开。输入严格限制字符、长度、参数数量和速率；兼容 TRILINK/1 HELLO/SEARCH。

## 没有实现，也不声称通过

- Room 的真实创建、申请/审批、成员传播和权威副本同步：仍需将 PC 真实网络后端接到该传输。USB CONFIG 仅是传输测试夹具，不等同于入房，也不证明分布式 Room。
- PC 桌面仍未切换为真实 Room；测试时关闭桌面程序/串口监控，避免抢占数据 COM。
- 自动发现两跳路由、路由租约和启动后自动恢复配置：当前固定链、配置只在 RAM；2 号重启后需重发同一 CONFIG。掉电不清除 PC Room 的设计要求仍待上层实现。
- 业务权限、密钥、ESP-NOW 加密和端到端认证：MAC/room/CRC 不是安全认证，仅限受控实验。不得接门锁、电机等危险执行器。
- RGB 双闪、控制租约、文件、OTA、云端功能尚未接入该新链路。旧工程灯效不代表新版本已具备。
- 三块板烧录、原生 USB 实测、射频超范围、运行时任务栈/堆和 30 分钟压力测试：未完成。

## 代码分层

| 路径 | 职责 |
|---|---|
| `components/wakenic_board`、`bootloader_components` | 原有 BSP/Flash 兼容；本次不改 |
| `components/trilink_protocol/trilink_route.c` | 可主机测试的 codec、路由、重试/去重状态机 |
| `components/trilink_protocol/trilink_route_control.c` | 有界 USB v2 命令解析 |
| `components/trilink_node/trilink_bridge.c` | RTOS 命令/回复队列、收件箱、结果查询 |
| `components/trilink_node/trilink_node.c` | 实际 APP_DATA 分发及 TX worker |
| `components/trilink_usb/trilink_usb.c` | 唯一 USB 驱动读写者，兼容 v1/v2 |
| `tests/host/test_trilink_route*.c` | 无 1–3 直连的三节点逻辑测试；不模拟真实射频 |
| `tests/host/test_trilink_bridge.c`、`fakes/` | 测真实 bridge 源码；队列适配器不证明 RTOS 并发 |
| `tools/chain_probe.py` | 真实 USB 三板诊断，不是最终桌面应用 |

## USB v2 契约

每行 LF 或 CRLF；nonce 为 1–32 个字母/数字/`_`/`-`；MAC 为无冒号的 12 位十六进制。
与 v1 一起受每秒 20 条命令限制。超速可能无回复，主机必须有超时；不要不断重发 SEND。

```text
TRILINK/2 INFO <nonce>
TRILINK/2 CONFIG <nonce> <room8hex> <epoch8hex> <node1mac> <node2mac> <node3mac>
TRILINK/2 SEND <nonce> <destinationmac> <1至136字节的hex数据>
TRILINK/2 POLL <nonce>
TRILINK/2 CONSUME <nonce> <sourcemac> <sourceboot8hex> <message8hex>
TRILINK/2 RELAY <nonce> <0或1>
```

CONFIG 要求三个唯一单播 MAC 包含本机、room/epoch 非零。首次配置后冻结；同配置重复提交无副作用。
要换链/房间须重启并使用新的 epoch。配置不是密码，不写入 NVS。

SEND 返回 `QUEUED nonce message`，不代表送达；通过 INFO 的 `pending` 和 `result=message:状态` 查询。
状态 0 尚无结果，1 目标固件收件箱接受，2 超时/结果未知。只保留最近一个最终结果，调用方应先取结果再提交下一条。
若 SEND 的 USB 回复丢失，先查 INFO，不直接重新 SEND，以免产生新的消息号。

POLL 返回 `DATA nonce source boot message hex` 或 `EMPTY nonce`；业务数据验证完再 CONSUME。
重复 POLL 不出队；CONSUME 错误身份不出队。收件箱和去重状态在断电时丢失，超过去重窗口的迟到重放不保证只投递一次。
因此不得把这一层的收件箱去重用作非幂等设备动作的“恰好一次”保证。

RELAY 仅中间节点允许；关闭只禁止过境转发，不禁止它作为终端收发；已经交给无线驱动的包无法撤销。

## 软件验证

在固件目录：

```powershell
.\tools\test_host.ps1 -Compiler 'D:\x86_64-8.1.0-release-posix-seh-rt_v6-rev0\mingw64\bin\gcc.exe'
.\tools\test_usb_contract.ps1
& 'C:\Espressif\tools\python\v5.5.5\venv\Scripts\python.exe' -B .\tests\host\test_chain_probe.py
.\tools\build_local.ps1
```

构建不自动烧录。结果见 `artifacts/builds/<编号>/verification.json`、构建日志及逐文件 SHA-256 源清单。
容量检查继续检查静态栈帧和 RX 池，并明确要求 route/service 已链接、content/file 未激活；静态检查不等于运行时无栈溢出。

## 三板实测入口（尚未执行）

1. 核对三块 S3 的 MAC、板型、烧录口。烧录相同 v0.7 镜像，不擦 NVS/eFuse。
2. 各板原生 USB 数据口接电脑；关闭会占用这些 COM 的桌面/监控器。CH343/CH340 口仅作烧录日志口，不当成数据口。
3. 先本机三端测试：将下例中的 COM 数据口替换为实测值，顺序就是 1、2、3。测试会通过 USB 配置测试链序，不改变系统串口配置。

```powershell
# [port-redacted]/102/103 只是占位符，不能直接照用！
& 'C:\Espressif\tools\python\v5.5.5\venv\Scripts\python.exe' .\tools\chain_probe.py `
  --ports [port-redacted] [port-redacted] [port-redacted] --room 54524931 --epoch 00000001
```

该探针检查：真实身份、1→2→3 数据与回执、3→2→1 最大长度数据、关闭中继超时、恢复中继后新消息成功。
它只使用串口，不使用 PC 网络。最终 JSON 明确 `rf_out_of_range_verified=false`、`distributed_room_verified=false`。

4. 后续分机 Room 实测：补好真实后端后，每台 PC 独立实例，经无线完成加入和同步。
5. 实际远距离/衰减环境中，单独验证 1、3 的直连探测不可用，而 1–2、2–3 可用，再做中继开关对照。固定链本来就禁止走捷径，所以单凭当前探针的关闭中继失败，**不能证明超出射频范围**。
6. 最后记录 30 分钟压力、断开/重启、栈高水位、堆最低值、队列峰值/丢弃和路径计数。通过这些门槛前，不关闭总方案 A 的验收项。
