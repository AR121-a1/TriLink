# TriLink 固件 v0.16.0 — TinyUSB CDC+NCM 本地管理接口

> Public copy, redacted for publication on 2026-10-15. Board1/Board2/Board3 are stable public labels, not transmitted MAC addresses. Physical MAC/USB serial/PnP instance identifiers, local COM assignments and backup hashes are withheld. Original private records remain unchanged. Historical local evidence paths are not public downloads.

2026-10-15 publication preparation: firmware source and the existing 0.16.0 three-image package are now in the requested publication scope. [Public build notes](PUBLIC_BUILD.md) describe the repository layout, portable test tooling, exact differences from the frozen build inputs, and excluded private evidence. No application C, SDK/configuration/lock or NCM behavior was changed, and no new firmware build or hardware operation was performed for publication.

2026-10-14 flashing round complete: all three boards separately passed Flash verification, startup and native CDC checks. Board3 exposes native CDC after recovering native download mode with BOOT released and RESET pressed. NCM Code10 reproduced on all three on this host. [Round overview](docs/HARDWARE_0160_ROUND_20261014.md) links the separate redacted records; multi-board Room/RGB acceptance is still pending.

2026-10-14 Board2, using CH343 for programming, now runs the same 0.16.0 image. Backup, three writes/independent verification, 35-second startup and native CDC read-only checks passed. First security-read interruption was retained and retried successfully before writing. NCM again reports Code10 on this host. See [board2 record](docs/HARDWARE_0160_BOARD2_20261014.md).

2026-10-14 hardware follow-up: Board1 was backed up, flashed with the existing 0.16.0 image, independently verified and observed booting for 35 seconds. Native CDC passed HELLO/CAPS/ROOMGET/SEARCH with DTR=false. **NCM matched Microsoft UsbNcm but failed to start (Code 10 / 0xC0000483); DHCP/web are not accepted.** No code/SDK/partition/eFuse change or upload occurred during that hardware operation. See [hardware record](docs/HARDWARE_0160_BOARD1_20261014.md). Earlier build-only flags remain historical.

2026-10-14 documentation: the sibling client project contains the shared [0.16.0 architecture reference](../docs/ARCHITECTURE_0160.md) and [development guide](../docs/DEVELOPMENT_GUIDE_0160.md). They describe firmware task ownership, module integration, USB/HTTP contracts, build gates and hardware acceptance. No firmware source, image or verification receipt changed for this documentation update.

2026-10-12：在保留节点/Room/RGB 骨干的前提下，将原生 USB 升级为 TinyUSB 复合 CDC+NCM。CDC 为 Windows 10 兼容路径；NCM 是本机局部管理网段，不桥接 ESP-NOW/Wi-Fi，不提供互联网路由。新增 `trilink_web` 内嵌页面、受限 HTTP 和单槽 mailbox，仍由唯一 USB worker 调用原 bridge。固定 ESP-IDF 5.5.5、esp_tinyusb 2.0.0、tinyusb 0.18.0~2；新增 `dependencies.lock` 纳入构建输入。当前实现和最终证据见同级 [0.16.0 记录](../docs/RELEASE_0160.md)。本轮不烧录、不操作实板、不上传；以下版本号、容量与硬件记录均为历史。

2026-10-11：实现 P2P 平台改造阶段 A 的固件部分。固定4槽应用注册/分发与自治 tick 接口已接入现有 RGB；19 B RGB 协议、Room/route、执行许可、去重与结果语义保留。原生 USB 新增只读 `TRILINK/3 CAPS <nonce>`，发现能力 bit7 表示支持该描述，当前模块 mask 只有 RGB `01`。文本收发、文件流、资源库、实时通道和 OTA 尚未启用。全新 ESP-IDF 5.5.5 构建、15组主机测试与容量检查通过；App 807584 B、静态 DIRAM 149191 B，新增注册表84 B，分区表与原0.10.2包哈希一致。未烧录、未操作COM/GPIO；软件/fake测试不代表实板验收。见 [0.15.0契约与验证](docs/V015_NODE_PLATFORM.md)。下方记录保留各日期历史状态。

2026-10-11 职责调整见同级客户端文档 [固件与上位机职责分配](../docs/P2P_PLATFORM_RESPONSIBILITIES.md)。当前方向为 S3 自治通信/执行、公共应用分发、P2P 有界易失传输；PC 提供交互和数据源/接收端。normal/bulk/realtime 调度策略及新业务接口待实现，本次仅更新架构文档，固件源码/镜像和运行版本不变。

2026-10-09：本轮第2台（[port-redacted]、MAC `Board1`，历史一号机）也完成既有v0.10.2覆盖区备份、三段写入/独立校验及35秒启动检查，XMC `20 70 18`已有窄兼容补丁路径正常，SDK自动更新RF校准记录。本板单侧观察到第1台`Board2`的发现/心跳流量，peers=1/6；这不是Room leader/双侧业务或完整多板验收。固件编译输入、分区与SDK未改；原生USB上位机接续验收单独记录。见 [第2台烧录记录](docs/HARDWARE_SINGLE2_20261009.md)、[验证汇总](docs/VALIDATION.md)。

2026-10-09：用户采用“电脑同时只连接一块ESP32”规则，本轮第1台（[port-redacted]、MAC `Board2`）已烧写现有v0.10.2，覆盖区备份、三段写入/独立校验及35秒UART单板启动通过。它在历史记录中称二号机，本轮按连接顺序称第1台；固件角色不写死。无分区迁移、全片擦除或RGB执行；原生USB客户端/Room/多板联调仍未验收。见 [本次烧录记录](docs/HARDWARE_SINGLE1_20261009.md)。下方“未烧录”均为各日期历史状态，原构建收据保持不可改的当时事实。

2026-10-01 review 修复：申请时保存成员版本下限，旧快照不能提前结束本次加入；等待期间更新的未入群名单只提高下限。有效发现广告不降版本，恢复链路后可完成 CONFIRM/成员 ACK 并继续管理。Room 空口仍为 service5/opcode2；三板验收统一 0.10.2。14 组 C 测试与全新 IDF 构建通过，App 仅增加 96 B，Room/静态 DIRAM 未增加。见 [本次修复](docs/V0102_REVIEW_FIXES.md)、[验证记录](docs/VALIDATION.md)。本轮仍未烧录、未上传、未操作 GPIO；真实桌面及三板验收待完成。

2026-10-01 v0.10.1 历史修复：补上 RMT 非阻塞提交、100 ms 完成等待与故障锁存；驱动失败后禁用 RGB 至重启，停止/删除失败则保留资源，不再修改像素或提交灯效。退出的 leader 保留 10 秒旧 Room 公告快照，期间不能创建或加入新 Room，避免交接丢包后被新状态覆盖。配套 PC 模块 **0.2.1** 显示故障与交接窗口；Room opcode2 / RGB 空口格式兼容 0.10.0。当日建议统一 0.10.1，当前以首段 0.10.2 为准。见 [故障与交接修复](docs/V0101_FAULT_CLOSURE.md)。SDK/BSP/引脚/分区/六成员容量不变；当日未烧录、未上传、未操作 GPIO。

2026-09-22：OFFER/CONFIRM后才提交成员，申请过期后迟到批准不再直接增加成员；确认后结果未知时有界重试并保留状态，USB支持手动重试。**Room空口opcode升级为2，三板必须统一0.10.0**。配套PC模块0.2.0；见 [当前协议](docs/V010_ADMISSION.md)。旧USB识别、SDK/BSP/引脚/六成员容量不变。本轮不烧录、不上传，三板验收仍待进行。以下日期段落保留作历史。

2026-09-20：增加Room本地命令/无线LEAVE的revision/term校验、六成员RGB应用序号水位、终态保护，以及Room变化时未决RGB的未知状态。保持SDK/BSP/引脚与容量上限不变，不烧录、不上传；见 [v0.9协议与增补](docs/V09_ROOM_RGB.md)。

2026-09-17：在六成员自动路由底座上接入 S3 内存 Room 副本、准入/审批/邀请、正常退出顺序继任、缺确认时冻结，以及 RGB 显式启用与应用执行回执。PC 入口为同级客户端的“扩展模块 → 真实 Room / RGB”。

当前准入协议见 [v0.10 Room](docs/V010_ADMISSION.md)，所有权与基础编码的历史说明见 [v0.9 Room / RGB](docs/V09_ROOM_RGB.md)。代码、主机测试和 IDF 完整构建通过；**未烧录本版、未完成三板实测、未上传**。RGB 默认禁用，三号板 GPIO48 尚未确认。

## v0.8 历史基线（不是当前功能完成表）

2026-09-16：传输成员最多 **6 个，包含本机**；直连发现仍最多 **6 个邻居，不含本机**。
用邻居表、成员表和有界链路状态自动选路，任意成员可中继；不再配置固定三节点链序。
支持最多五跳、跨路径去重、有限重试和 TABLE 查询；空口帧仍 200 B，业务载荷最多 132 B。

当前说明与协议见 [v0.8 自动路由](docs/V08_AUTO_ROUTING.md)，构建证据见
[验证记录](docs/VALIDATION.md)。诊断入口为 `tools/mesh_probe.py` / `TRILINK/3`；
旧 `TRILINK/1` PC 识别兼容，旧 v0.7 业务协议不兼容。

**尚未完成：真实 Room 入群/审批/副本同步、RGB 应用、三板实测。** CONFIG 仍是显式传输测试夹具。
本轮未烧录、未上传。客户端 Room 容量已改为六人，但真实后端未接通，不能把模拟房间当硬件成功。

## v0.7 历史基线（以下不是当前版本能力限制）

2026-09-16：在 v0.6 基线上增加真实 APP_DATA 分发、独立无线 TX worker、
固定三节点链式转发、端到端收件箱 ACK、有限重试/去重，以及原生 USB v2 测试入口。
详细用法和当前限制见 [两跳传输说明](docs/V07_CHAIN_TRANSPORT.md)。

**当前并未完成首版验收。** 这是一跳/两跳传输底座，三端通过 USB 显式配置同一
测试 Room 标识与链序；不是实际 Room 申请、审批或权威成员同步。桌面 Room 仍为
模拟后端，未修改。RGB 设备执行、加密、三板烧录、跨射频范围和长期压力验收仍待完成。

所有板仍使用同一固件，MAC/角色不写死；`chain_probe.py --ports ... --epoch ...`
读取真实身份后配置顺序。脚本不烧录，不连接 PC 网络，不模拟硬件成功。
`build_local.ps1` 运行全部主机测试、旧 USB 契约测试及探针测试后编译并校验镜像。

## v0.6 历史基线（下述版本范围不是 v0.7 完成清单）

本目录是新的本地固件主工程，与 `../trilink_min_client` 同级。固件独立版本
为 0.6.0，对接客户端 0.13.0 的 `TRILINK/1` 文本契约；不上传 GitHub。
旧工程与旧固件均保留，不覆盖、不自动烧录。

## 本版范围

- 所有 S3 使用同一固件，身份与名称由各自 STA MAC 确定，不写死 A/B/C。
- 原生 USB Serial/JTAG 数据口响应 `HELLO → DEVICE`、`SEARCH → PEER* → END`。
- 最多发现并返回 **6 个邻居**，不含本机；满表拒收第 7 个，离线 2 秒过期释放。
- 邻居发现数量与三人 Room 上限分离。三节点已足够的诊断判断不会等到 7 台。
- 固定信道 6、固定项目 group，HELLO 500 ms、单播心跳 1000 ms；不进行全信道 Wi-Fi 扫描。
- 只查询节点任务发布的真实快照；不合成假设备。尚无真实 Room 时三个 room 字段均为 `-`。
- USB 解析缓冲 64 B，回复缓冲 512 B，RX/TX 环形缓冲 512/1024 B；每个 1 秒窗口最多处理 20 条命令。
- 超长、非法字符、未知命令整行拒绝；半条命令 500 ms 无新输入后重置。
- 保留原有 16 槽 / 3360 B 无线 RX 池和单一节点所有者；文件 codec 继续休眠，不缓存文件。

**尚未完成：**真实跨电脑 Room 加入/邀请/继承、文本 APP_DATA 运行时转发、文件传输、
认证加密、HID 唤醒、NCM 虚拟网卡及三板实测。客户端模拟演示通过不等于这些硬件功能完成。

## 硬件与连接

沿用 HW-678A / ESP32-S3-WROOM-1-N16R8：16 MiB Flash、8 MiB Octal PSRAM，
ESP-IDF **v5.5.5**，DIO/80 MHz，原有 XMC `0x207018` bootloader 兼容补丁与分区不变。

- **CH343/UART0 口**：烧录和日志，115200；不是客户端数据口。
- **S3 原生 USB 口**：客户端数据，使用 Espressif 固定硬件 VID/PID，不自定义描述符。
- 客户端按 `VID_303A` 筛选并用 nonce 验证身份，不伪造键盘输入、不依赖 USB 自动运行 EXE。
- 本轮选择已验证的固定功能 CDC 通道，而非 TinyUSB。将来需要 `CDC+HID` 时再替换 USB 后端。

## 验证与构建

在本目录 PowerShell 运行：

```powershell
.\tools\test_host.ps1 -Compiler 'D:\x86_64-8.1.0-release-posix-seh-rt_v6-rev0\mingw64\bin\gcc.exe'
.\tools\test_usb_contract.ps1
.\tools\build_local.ps1
```

`build_local.ps1` 先运行 C 测试和实际客户端 C# 解析器兼容测试，再将源文件复制至
ASCII 临时目录构建；逐文件 SHA-256 核对镜像与原文件。SDK 不升级，不改旧工程。
结果在 `artifacts/builds/<构建编号>/`，包含 BIN、构建日志、源文件哈希及 `verification.json`。
测试程序独立在 `build_host_tests/` 与 `artifacts/tests/`，不混入固件包。

全新构建为默认值；修复后可显式使用 `-ReuseStage <之前输出的准确路径>` 增量构建。
脚本不烧录、不清除 NVS、不修改 eFuse，也不建立固件远程仓库。

首次硬件验收：确认唯一 CH343 口 → 烧录相同固件 → 各电脑连接原生 USB →
打开客户端 → 身份识别 → 搜索 → 每台显示另两台。6 邻居容量由主机测试验证；
实测 6 邻居需要额外节点，不能用三块板宣称已完成。

详细信息见 [v0.6 更新与验收](docs/V06_USB_DISCOVERY.md)。旧文档仅作历史证据，
不要把旧“USB stub / 两邻居”描述当成本版状态。
