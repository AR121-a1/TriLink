# 0.16.0 USB 节点接口重构

Publication addendum (2026-10-15): the original 0.16.0 PC package and selected firmware image package are distributed separately in [releases/v0.16.0/](../releases/v0.16.0/README.md); source is at the root and in `firmware/`. This supersedes the local-only/no-upload publication policy stated in the original 2026-10-12 record below, without changing its frozen results or flags. [Public validation summary](../releases/v0.16.0/validation-summary.json) is a derived portable record, not the original machine-bound receipt. The [2026-10-14 board overview](../firmware/docs/HARDWARE_0160_ROUND_20261014.md) records individual CDC successes and NCM Code 10 failure; it does not establish multi-board Room/RGB or web acceptance.


Companion documentation (2026-10-14): [Detailed architecture](ARCHITECTURE_0160.md) and [Development guide](DEVELOPMENT_GUIDE_0160.md). This release record remains the original binary/evidence baseline; the new guides do not constitute another build or hardware acceptance.

日期：2026-10-12。产品 `0.16.0-local`、固件 `0.16.0`。本次在 0.15.0 模块化基础上更换 USB 适配层，不更换 ESP-NOW 空口、Room 副本规则或 RGB 权限模型。当前文档是实现和验收契约；最终构建、包哈希及硬件状态见文末验证记录。

## 直接使用方式

| 主机 | 默认控制通道 | 可选入口 |
| --- | --- | --- |
| Windows 10 | 原生 USB CDC 串口，配套桌面客户端 | 不依赖 NCM 驱动，不要求安装第三方网卡驱动 |
| Windows 11 | 仍优先 CDC 串口 | 系统成功枚举 TriLink NCM 网卡后，可用本地管理网页；串口不可用且 NCM 握手验证成功时客户端可选该通道 |
| CH343/CH340 接口 | 保持烧录、恢复、UART 日志用途 | 不作为 Room/RGB 控制口 |

一次只连接一块 S3。NCM 本地节点地址固定 `192.168.240.1/24`，板端 DHCP 提供 `.2`–`.3` 的最小地址池，只有一台物理 USB 主机；不提供默认网关或 DNS，不桥接 Wi-Fi，不提供互联网访问。地址冲突时使用 CDC，不自动改主机路由。网页不会自动弹出；用户在 NCM 已枚举后主动打开 `http://192.168.240.1/`。

第一次从旧固件迁移必须重新烧录新固件，旧 USB Serial/JTAG 运行态改为 USB OTG/TinyUSB，Windows 可能分配新的 COM 编号。USB OTG 与内置 USB Serial/JTAG 共享 PHY，不能同时占用；保留 CH 接口恢复路径。本轮只生成固件，不自动烧录。

## 职责与数据流

```text
桌面功能插件 -> 共享 NodeSessionService -> 唯一 I/O owner
                                        -> CDC 行协议 ──┐
                                        -> NCM HTTP ──┐ │
本地管理网页 ----------------------------> HTTP mailbox │
                                                    ↓ ↓
固件 USB worker（唯一 bridge 回复消费者，串行仲裁）
    -> 原 node/bridge -> Room / 模块分发 / RGB -> ESP-NOW
```

- **固件 USB 组件**：复合描述符、CDC 字节流、NCM 帧适配、单事务路由及超时隔离。CDC 不以 DTR 为业务握手前提。接收缓冲有上限，不为每个请求创建任务。
- **固件 Web 组件**：Flash 内嵌 HTML/JS/CSS，单槽 mailbox、输入策略和本地 HTTP 服务；不得绕过 worker 直接消费 bridge 回复。
- **原节点层**：唯一真实状态与执行权限来源。6 邻居、6 成员、132 B 路由正文、已有单人 Room 和 RGB 许可逻辑保持。
- **桌面串口插件 1.4.0**：名称保留以维持既有插件依赖，内部复用原 owner 接入 CDC/NCM；没有新增平行轮询器或另一个业务服务。
- **真实 Room 插件 0.6.0 / 桌面 1.4.0**：显示连接类型，快照复制传输元数据；端点/身份/传输种类变化会失效旧意图。显示名称不改变真实发送端点。

`TriLinkDevice.TransportKind` 默认 `UsbSerial=0`，新增 `UsbNcm=1`；NCM 的 `PortName` 是 `NCM:<接口 GUID>` 而不是 COM 或任意 URL。`ManagementUrl` 仅供已验证 NCM 描述展示，不授予网络访问权限。插件不直接读取这个 URL 发业务请求，仍调用共享服务。

## 双通道安全语义

同一 MAC 优先发布 CDC 绑定，NCM 必须精确匹配指定 USB VID/PID/接口、网卡本地地址，再完成 HELLO/CAPS 握手。不能把所有以太网卡、普通 Wi-Fi 或同网段任意服务器当作 TriLink。

NCM 客户端通过绑定已验证接口和本地 IPv4 的 socket 访问固定板端地址，不使用系统代理、不跟随重定向、不自动重放 POST。身份指纹包含 PnP 和接口绑定，排队期间换板/换接口会拒绝旧工作。切换通道不等于自动重试上一条写入。

HTTP 会话令牌只在板端和客户端 RAM；不落盘，不写日志。服务检查目标/对端地址、Host、Origin 和浏览器来源，不提供 CORS。写接口另要求显式确认头。它是本地 USB 管理面的误用/跨站防护，**不是针对已控制本机的恶意程序的身份认证或加密协议**。

客户端每次 HTTP 请求有 3500 ms 总截止并在返回前复查；一次新连接的 session、HELLO、业务请求是独立事务，因此完整操作并不保证 3500 ms 内完成。固件仲裁 20 条命令/秒，上位机和网页争用可能返回限流/忙；可排队不等于无限并发、永不失败或实时硬期限。

命令和回执有固定长度与期限。固件超时后保留原事务所有者，迟到回复按 nonce 消费并丢弃，不能送给下个请求；节点层若永久无回复，控制通道失败关闭，不能将可能已执行的动作伪装成“已取消”。结果未知必须刷新实际状态，不自动换通道重发。

网页是轻量管理入口，不复制完整桌面插件生态；提供身份/能力、附近节点、当前 Room，以及明确确认的创建/退出操作。完整邀请、申请、审批、踢出、RGB 继续由桌面客户端承担。网页与桌面并发也受固件单 owner 约束。

## 预算与扩展

本地 HTTP 契约 v1：`GET /api/v1/session` 返回 `protocol/token/firmware/endpoint/uptime_ms`；`POST /api/v1/exchange` 接收一行带 LF 的既有 TRILINK 协议。请求体 <384 B，回复 <2048 B；不接收任意诊断/路由配置指令。允许 HELLO、SEARCH、CAPS、ROOMGET、RGBRESULT，以及已确认的 ROOM、RGB、RGBENABLE。写入额外携带 `X-TriLink-Confirm: execute` 与基于板端单调时钟的 `X-TriLink-Deadline`，防止 HTTP 请求在队列里过期后才执行。服务重启后旧令牌失效；手动刷新/重新连接重新建立会话，不重放旧写入。

保留 App ≤1 MiB、静态 DIRAM ≤192 KiB、项目单函数静态栈帧 ≤1 KiB 等原门限；新增 Web 静态资源总量 ≤32 KiB。实际用量由全新构建的 map、size、stack-usage 和打包文件记录，不以 Flash/PSRAM 标称容量推断可用量。静态构建数据不证明运行时堆峰值或 USB 插拔长期稳定。

TinyUSB CDC+NCM 使用 CDC 通知 IN、CDC bulk IN/OUT、NCM 通知 IN、NCM bulk IN/OUT，即 EP0 外 4 IN / 2 OUT。默认不加入 HID，避免无用端点和行为；唤醒另行核对电源/主机能力，不能因采用 TinyUSB 就宣称休眠唤醒成功。

后续文本/文件模块应继续复用节点分发器、能力声明和共享会话接口，按需增加数据通道；**NCM 只是 PC 到本块板的 USB 链路**，不自动将三台 PC 组成透明 IP 局域网，也不使 ESP-NOW 变成高速以太网。当前不增加 MSC/U 盘存储、文件落盘、OTA、动态 C 模块、文本 P2P 或实时模式功能声明。

## 交付和验收门槛

保持 0.15.0 和更早完整目录，退出旧实例须用托盘“退出”（X 仍隐藏）。新版整套替换 EXE、共享契约和九插件，不混 DLL。客户端 ZIP 不包含固件；固件仍为本地工程且不上传。

软件门槛：全新固定 SDK 固件构建、锁定依赖、原 host 测试、CDC 兼容夹具、HTTP 策略/仲裁/网页时序测试、客户端 Release/Debug、跨 C/C# CAPS 样本、离屏 UI 检查、独立包逐项哈希。

硬件门槛另列：Windows 10 CDC 枚举/收发，Windows 11 CDC+NCM 枚举和 DHCP，无默认路由/DNS污染，网页与客户端交错操作、拔插与超时、两板无线、三板 Room/RGB、长运行堆和任务栈。**未烧录的新固件不能获得这些验收结论。**

## 验证记录

最终固件构建 `20261012-173202-b0407e2b` 全新编译、构建配置与容量门禁通过；130 个冻结输入前后哈希一致，精确依赖锁随镜像输入记录。17 组 C host 测试、旧 USB 跨语言契约、8 个 chain/mesh 单测、实际嵌入 JS 的32项 fake DOM/HTTP/逻辑时间检查通过。

| 项目 | 0.16.0 实际值 | 保留门限 |
| --- | ---: | ---: |
| App | 898736 B | 1048576 B |
| 静态 DIRAM | 164979 B | 196608 B |
| 最大项目栈帧 | 784 B | 1024 B |
| Web 资源 | 14335 B | 32768 B |

相比 0.15.0 增加 App 91152 B、静态 DIRAM 15788 B。分区 SHA-256 仍为 `7F00B6C042A89B15B0CAC534F82ED988CAF29278FF5700B0C511EB1B5BB7C820`；当前 App SHA-256 `F5EDD8FB4D35C56614782453E63DAE310DF192F2B6E9CCB5383A366E73CD4143`。收据在 `../trilink_firmware/artifacts/builds/20261012-173202-b0407e2b/verification.json`。中间构建 `20261012-172931-287f46ea` 因复审修改触发源码冻结检查，未生成成功收据，不是交付镜像。

客户端最终冻结快照 `<TEMP>/tl0160-925e2074`，全量 Release/Debug 均完成；每套1244项：Core136、Serial259、HardwareRoom269、Modules64、Thunder444、Desktop34、Smoke/Guard38。新增 NCM 身份/绑定、协议边界、双通道去重、可选支路熔断和截止检查；保留既有队列/暂停/单机/跨页回归。跨 C/C# CAPS 使用本次固件 C bridge 生成的 `0.16.0` 样本，单独260项通过，不再次累加普通回归总数。

已人工查看最终 NCM 状态和最小窗口离屏图；网页完成实际 JS 的逻辑测试，未进行真实浏览器与 USB 联动验收。没有实板运行、灯光、驱动安装或 GitHub 发布结论，也未挪用旧 OpenSpeedy 20× 结果。

Original local runtime: `artifacts/NodeConsole-0.16.0/`; original package: `artifacts/packages/TriLink-NodeConsole-0.16.0-Release.zip`. The unchanged [public PC ZIP](../releases/v0.16.0/TriLink-NodeConsole-0.16.0-Release.zip) contains 22 files, is 179765 B, and has SHA-256 `8A91A312C6123F78DC6B664379D5EF8DABCD6204A7F46D44839557F363CC415F`. The original validation checked 102 source/build inputs, 22 runtime files, three relocated local shortcuts, nine manifests and per-entry ZIP hashes. Raw Release/Debug logs, offscreen images, test programs, CAPS records and machine-bound receipts remain in the local evidence archive. The [public validation summary](../releases/v0.16.0/validation-summary.json) is a selected portable derivative, not a link to that full archive.

旧0.15.0 ZIP 已复核，SHA-256 仍为 `BAA92BCD9ADB4DCBC8F9A5F7125ABD1DD7BE898A6091EDF9EAC242F01FB53DFB`。本轮没有结束用户进程，也没有启动正式新版去扫描实板。新 NCM 功能需要另外授权烧录新版固件；旧固件仍可使用桌面 CDC 兼容路径。
