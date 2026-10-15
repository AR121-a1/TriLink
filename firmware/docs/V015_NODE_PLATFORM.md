# 0.15.0 公共应用分发与节点能力描述

> Public copy, redacted for publication on 2026-10-15. Board1/Board2/Board3 are stable public labels, not transmitted MAC addresses. Physical MAC/USB serial/PnP instance identifiers, local COM assignments and backup hashes are withheld. Original private records remain unchanged. Historical local evidence paths are not public downloads.

日期：2026-10-11（Asia/Shanghai）。范围是 [P2P平台职责设计](../../docs/P2P_PLATFORM_RESPONSIBILITIES.md) 的阶段 A 固件部分；本页描述已实现的软件，不表示硬件验收。

## 实现

`trilink_app` 是固定4槽、无动态分配、无独立任务的模块注册表。注册发生在 bridge 初始化时，描述包含能力位、业务标识、协议版本、固定 wire 长度与 input/tick 回调。接收者不保存无线 RX 借用指针，状态仍由 node 任务唯一拥有。未知标识、版本或长度在进入处理器前拒绝。

本版只注册 RGB：能力 mask `01`，标识 `A6`，版本 `01`，wire 长度19 B。本机与远端 RGB 都经过分发器，再调用既有 RGB 校验/状态机；RGB状态仍保留在 bridge，避免本轮重新改写 Room/route 的权限和去重链。成员 boot、incarnation/revision、本地许可、操作序号水位、重复操作与终态保护沿用原实现。CAPS 仅描述能力，不开启 RGB。

`trilink_app_tick` 位于 Room/route tick 之后、USB 回复队列背压检查之前。USB 回复满或 PC 不查询时，已受理的 RGB 状态机继续推进。没有新增持久化、分区、自动任务、业务发送协议或 SDK 依赖。

## CAPS 固定契约

请求：

```text
TRILINK/3 CAPS <nonce>\n
```

响应带原有恢复用前导 LF：

```text
\nTRILINK/3 CAPS <nonce> 1 0.15.0 <BOOT8HEX> 84 06 06 01\n
```

此处 `\n` 表示实际 LF。字段按空格分隔，nonce遵循原 V3 字符/32字符上限，响应恰好10个token，无自由文本：

| 字段 | 编码与当前值 |
| --- | --- |
| 协议与响应类型 | `TRILINK/3 CAPS` |
| nonce | 原请求值 |
| 描述契约版本 | 十进制 `1` |
| 固件版本 | `0.15.0` |
| boot | 8位大写十六进制、当前启动实例 |
| route body预算 | 2位十六进制 `84`，即132字节，还未扣未来应用头 |
| 邻居上限 | 2位十六进制 `06`，不含本机 |
| 成员上限 | 2位十六进制 `06`，含本机 |
| 内建模块mask | 2位十六进制 `01`，bit0为RGB |

发现能力新增 `TRILINK_CAP_NODE_MODULES = 1 << 7`，USB DEVICE及无线HELLO的实际能力均包含它；原Room/RGB bit6保留。PC可先检查bit7再请求CAPS，旧固件使用旧能力显示路径。新bit不宣称文本、文件、实时或OTA可用。CAPS在未入Room、未配置route时也可读取，不改变Room、路由、许可或任务状态。

## 验证与产物

标准入口：`tools/build_local.ps1`。本次为全新ASCII临时源镜像，ESP-IDF5.5.5、ESP32-S3 N16R8、DIO/80MHz、16MiB Flash、8MiB Octal PSRAM及XMC窄兼容补丁保持。

15组C主机测试全部通过，包含模块注册/重复/满额、严格分发、CAPS畸形命令与恢复、未入Room及已入Room描述、原Room/RGB准入/许可/重复/终态，以及4个USB回复槽满时RGB自治完成。无线/驱动均为fake。旧USB固件C→v0.13 C#解析器兼容检查与3个chain/5个mesh探针单测通过。

| 项目 | 本次测得 | 门限/旧包对比 |
| --- | ---: | --- |
| App | 807584 B | ≤1048576 B；较实际0.10.2包增加688 B |
| 静态 DIRAM | 149191 B | ≤196608 B；增加88 B |
| 应用注册表 | 84 B | ≤128 B |
| route / Room状态 | 3560 / 4472 B | 原4KiB/6KiB门限保留；旧包相同 |
| RX池 | 3360 B | 旧包相同 |
| 最大单函数栈帧 | 416 B | ≤1024 B |
| node / RX回调栈帧 | 64 / 48 B | 原96 / 64 B门限保留 |

CONTENT/FILE codec仍只编译、未链接到App；未新增每模块RTOS栈或radio RX任务。容量脚本新增dispatcher符号和128 B状态门，不放宽原容量/栈门限。

本次隔离产物目录：`artifacts/builds/20261011-103721-87a83230/`，其中 `verification.json` 为构建/容量收据，`source-manifest.csv` 为源镜像哈希，`host-tests.log` 与 `usb-legacy-contract.log` 保留测试输出，`node-caps-fixture.txt` 由真实bridge的host测试实现生成，可供PC解析器跨端验证。

- App SHA-256：`CF56480C9E8E9CD360D679C5C3F7264D7857D8DDAE894AAAA55A923CB2067917`。
- 分区表 SHA-256：`7F00B6C042A89B15B0CAC534F82ED988CAF29278FF5700B0C511EB1B5BB7C820`，与保留的 `20261001-025538-49e98315` 原0.10.2包完全相同：NVS `0x9000/24K`、PHY `0xf000/4K`、factory `0x10000/1M`。
- 收据：`FreshBuild=true`、`Flashed=false`、`HardwareVerified=false`、`RoomIntegrationVerified=false`。

源码和旧镜像均保留；没有访问COM、烧录、擦除NVS、操作GPIO或上传。剩余B–E与真实无线、长期负载、RGB执行验收分别推进，不将本次描述契约当作传输功能完成。
