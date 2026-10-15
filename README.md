# TriLink 0.16.0-local（USB 节点控制台）

## Current public distribution - 2026-10-15

This distribution includes the Windows client source at the repository root, ESP32-S3 firmware source in [firmware/](firmware/), and selected validated 0.16.0 packages under [releases/v0.16.0/](releases/v0.16.0/README.md). The user's 2026-10-15 publication decision supersedes earlier client-only/no-firmware publication rules. Dated statements below describe their original run, not the current publication policy.

- [Windows portable client](releases/v0.16.0/TriLink-NodeConsole-0.16.0-Release.zip): extract the complete ZIP and start `TriLink.MinClient.exe`; Windows and .NET Framework 4.8 or compatible 4.8.1 runtime are required. X hides to tray; tray Exit terminates the process.
- [Firmware image package](releases/v0.16.0/TriLink-Firmware-0.16.0.zip): separate from the PC ZIP; inspect its instructions and board compatibility before any explicitly authorized flashing.
- [Public validation summary](releases/v0.16.0/validation-summary.json) and [publication manifest](releases/v0.16.0/manifest.json): derived portable records, not replacements for the original local build receipts.
- [Detailed architecture](docs/ARCHITECTURE_0160.md), [development guide](docs/DEVELOPMENT_GUIDE_0160.md), and [setup](docs/DEVELOPMENT_SETUP.md).

Current hardware boundary: three boards passed individual flashing/startup/native CDC reads on the tested host. NCM fails with Code 10 on that host; use CDC. Windows 10 CDC, NCM/DHCP/web, interactive desktop, multi-board 0.16.0 Room/RGB and loaded long-run resource acceptance are not established by those individual checks. Publishing existing packages does not add a new build or hardware result.

Historical local `artifacts/`, TEMP stages, screenshots, device backups and work handoffs are not published wholesale. Links to excluded historical evidence are retained as explicitly labeled non-clickable paths; selected current public records are linked above. Actual board MACs and machine-specific COM identifiers are replaced by Board1/Board2/Board3 and COM_PORT in public prose.


2026-10-14 board3 complete: `Board3` passed the same 0.16.0 Flash/startup/CDC checks; native COM_PORT works after download-mode recovery. All three boards are now individually verified on CDC; NCM Code10 remains on this host. [Local three-board overview](firmware/docs/HARDWARE_0160_ROUND_20261014.md) records portable board labels and verification limits.

2026-10-14 board2 update: a second S3 (`Board2`) passed flashing, startup and native CDC COM_PORT read-only checks with the same 0.16.0 image. NCM Code10 reproduced on this host. Both flashed boards have independent evidence; no new multi-board Room/RGB acceptance yet. [Local board2 record](firmware/docs/HARDWARE_0160_BOARD2_20261014.md).

2026-10-14 hardware follow-up: one S3 (`Board1`) now runs the existing 0.16.0 firmware. Backup, Flash verification, startup and native CDC read-only checks passed on COM_PORT. NCM has a Microsoft-driver Code10 failure; use the CDC path, not the web/NCM path. Client UI, multi-board Room/RGB and full hardware acceptance remain unverified for this version. See the local [hardware record](firmware/docs/HARDWARE_0160_BOARD1_20261014.md); the PC package is unchanged.

2026-10-14 documentation: [Detailed architecture reference](docs/ARCHITECTURE_0160.md) and [Development guide](docs/DEVELOPMENT_GUIDE_0160.md). Covers source ownership, protocols, plugin/module extension, resource limits, build/release, failure diagnosis and separate hardware acceptance. Documentation only; the existing 0.16.0 binaries and validation receipts are unchanged.

2026-10-12：客户端和本地新固件共同完成 **TinyUSB CDC+NCM 接口重构**。Windows 10 保留原生 USB 串口；支持 NCM 的系统可用板内轻量网页与 HTTP 管理通道。CDC 优先，两条通道复用既有共享会话和固件单命令入口，超时不跨通道重放。保留 Room/RGB、队列、轮询暂停和托盘行为。详见 [0.16.0 功能、架构与验证](docs/RELEASE_0160.md)。

Current portable package: [TriLink-NodeConsole-0.16.0-Release.zip](releases/v0.16.0/TriLink-NodeConsole-0.16.0-Release.zip), 22 files and 179765 B. Start `TriLink.MinClient.exe` from its complete extracted directory. The original local delivery receipt recorded 102 frozen inputs, 1244 checks per configuration and file hashes; the [public validation summary](releases/v0.16.0/validation-summary.json) exposes the selected derived record. `artifacts/NodeConsole-0.16.0/` was the local packaging output, not a directory supplied by a clean clone. Exit the old instance through its tray menu; do not mix DLL versions.

2026-10-12 构建时：新固件 App898736 B、静态 DIRAM164979 B，仍在原1MiB/192KiB门限内；当时没有烧录或实板验收。2026-10-14 单板烧录与 CDC 结果见顶部后续记录，NCM 未通过；没有上传 GitHub，PC ZIP 不含固件。NCM 是本块板的 USB 管理接口，不是透明三电脑网络；文本P2P、文件流、HID唤醒、OTA仍待后续实现。

## 0.15.0 历史基线

2026-10-11：客户端与新固件共同落实 [职责方案](docs/P2P_PLATFORM_RESPONSIBILITIES.md) 的阶段 A。固件 `0.15.0` 用固定4槽应用分发器承载已有 RGB，提供 `CAPS` 能力/预算描述；PC 把队列、刷新、快照和回执移入跨页面共享节点服务。desktop/serial `1.3.0`、hardware-room `0.5.0`，Host API `1.0`。详见 [0.15.0 实现、验证与更新](docs/RELEASE_0150.md)。

当前独立入口为 `artifacts/NodeConsole-0.15.0/TriLink.MinClient.exe`，便携包为 `artifacts/packages/TriLink-NodeConsole-0.15.0-Release.zip`，交付收据 (historical local evidence, not included: `artifacts/tests/NodeConsole-0150-StageA/delivery-receipt.json`)记录实际软件验证范围。新固件镜像在相邻本地固件工程，未烧录、未上传；PC ZIP 不包含固件。先托盘退出旧实例，再整套更新，不混用 DLL。

P2P 方向不做板端内容存储。小文本、连续文件流、扩展对齐与低延迟模式依次属于 B–E，当前不显示为已完成；既有文本工具仍只做本地检查，游戏仍为 PC/UDP。以下带旧版本的验证数字、硬件和加速结果为历史，不替代 0.15.0 验收。

## 历史基线（0.14.4 及更早）

历史 0.14.4 版本：desktop `1.2.3`、hardware-room `0.4.1`、serial `1.2.2`，Host API `1.0`，固件仍 `0.10.2`。0.14.4修复第五次自动刷新失败后残留的light/full刷新意图，以及单机CREATE转为Room后的同一物理RGB目标重复入队；出队Room/boot/权限复核保持。每页FIFO最多8待项、在途另计，后台逐笔USB返回后让路；leader约2秒/其他5秒轻量读，active SEARCH≥15秒，非并行串口或实时保证。

Release/Debug各1067项普通速度软件检查通过，其中HardwareRoom225项。冻结的同一v21 fake探针在0.14.3正常速度和20×均复现两项缺陷；0.14.4正常速度12场景/132断言、20×12场景/130断言均零失败，QPC倍率约1.0008/19.9970。长测26项零失败，推进30.0379逻辑分钟/90.113真实秒，三次暂停恢复、零写入；资源观测不证明零泄漏。OpenSpeedy对时间API不统一缩放，不是全1067项加速、性能测速或真实无线/GPIO验收；本轮不访问实板、不改/烧固件。详见 [时序补丁与验证](docs/TIMING_PATCH_0144.md)。

独立入口`artifacts/NodeConsole-0.14.4/TriLink.MinClient.exe`，便携包`artifacts/packages/TriLink-NodeConsole-0.14.4-Release.zip`已生成校验：22项/158299B，SHA256 `7519880EC0DDEE593E4D7B228FA50BAC53250683C59EF373561A535BA83DCC3F`；91输入、22运行文件＋3定位快捷方式及九插件清单通过，见交付收据 (historical local evidence, not included: `artifacts/tests/NodeConsole-Timing-0144/delivery-receipt.json`)。正式证据根`artifacts/tests/NodeConsole-Timing-0144/`；冻结副本仍保留且build-stage不是部署入口。先托盘退出旧实例再整套更新PC2，不混DLL或覆盖在用目录；尚未上传GitHub或创建Release。

历史0.14.3入口`artifacts/NodeConsole-0.14.3/TriLink.MinClient.exe`及便携包`artifacts/packages/TriLink-NodeConsole-0.14.3-Release.zip`保留：22项/154881B，SHA256 `663FE3241114AA142C9D5A5D1A418D3A3626E24FAD325E3A3B805B36EBAF1EA9`。当轮91冻结输入、22运行文件＋3本机快捷方式校验通过，普通Release/Debug各1034项与COM_PORT只读31/31保持历史；早期V1 20×仅49项Queue/Pause与自然期限，不覆盖后来v21新增场景。新前台写FIFO真实无线及默认人工审批时序仍未验收，以下0.14.2单板/双PC记录也不替代当前版本验收。

2026-10-09 历史0.14.2双PC准入：本机COM_PORT/Board1既有Room16/incarnation44360405，经真实首页对Board2 fresh ticket19批准一次，提交revision2/term1/count2/synchronized=True，顺序Board1 -> Board2；用户确认PC2同序2/6、leader Board1且普通成员，无远端raw/截图。快读助手绕过默认整页时序，不证明默认8秒人工审批稳定；助手退出后Room仍2人不证明PC2退出/掉电持久化，无CREATE/LEAVE/KICK/新RGB/固件写入。该轮保留Room，2人任退会解散，继任保房需3人，详见 [双PC历史](docs/TWO_PC_ROOM_VALIDATION_20261009.md)。985项与旧包不变；当时0.14.3 queue尚未完成，现状以本文首段与新队列记录为准。

2026-10-09 历史0.14.2：产品 `0.14.2-local`，桌面插件 `1.2.1`、串口 `1.2.2`、真实模块 `0.3.1`，Host API `1.0`；配套固件仍 `0.10.2`，不改固件、分区或SDK。RGB许可与执行分开，本机双闪无需成员选择；未入Room时明确确认创建并保留一人真实Room，执行只提交一次并跟踪原ID终态。真实页已识别且可见时默认只读刷新/5秒单飞，暂停/失败5次/换设备后不会偷偷恢复。Release/Debug各985项通过；COM_PORT/`Board1`已完成一次授权本机执行，`00000001`从受理到驱动终态2自动读回，随后恢复原许可关闭并保持原Room不变。此为自动主表单+真实USB+驱动回执，不是人工光学或用户桌面验收。统一见 [单板RGB闭环与主动发现记录](docs/SINGLE_NODE_CLOSURE_20261009.md)。

固件已有启动广播及后续500ms HELLO、1秒邻居心跳、6邻居/2秒过期和MAC peer自动注册；与逻辑Room准入不同。本端SEARCH看到`Board2`，先前只读RSSI=-51dBm、执行现场=-55dBm，仅单侧发现、对端能力未验证，不显示成“已加入/已连接Room”。另一电脑若只接CH343烧录口，须改接原生USB才能提交JOIN/审批，并更新整套新版客户端；本轮不要求重烧同版固件。成员可以邀请，准入/拒绝/踢出仍归leader；跨电脑JOIN/审批/继任尚未验收。

历史0.14.2入口：`artifacts/NodeConsole-0.14.2/TriLink.MinClient.exe`；便携包`artifacts/packages/TriLink-NodeConsole-0.14.2-Release-20261009.zip`已校验22项运行文件，151286B，SHA256 `8CA944FF0FA8C6E1A1E077F1E2BDD8D1A78C02544F8856CEDFF1F680DD49A532`。见[当轮记录](docs/SINGLE_NODE_CLOSURE_20261009.md)。该包不含0.14.3队列改动；0.14.1/934项与“当时未操作RGB”继续保留早轮历史。

2026-10-09 早轮0.14.1历史：真实 Room/RGB 已直接嵌入首页，真实模式不再显示模拟创建/搜索/退出按钮。产品 `0.14.1-local`，桌面插件 `1.2.0`、串口 `1.2.2`、真实模块 `0.3.0`，Host API `1.0`。复用同一个 USB 发现/命令服务；按钮按设备、角色、选择、容量与同步状态启用，并解释禁用原因。Release/Debug 各934项软件检查通过；原生 USB COM_PORT / `Board1` 已通过真实主表单的刷新、创建一人 Room、退出清除成员。附近节点0，未执行 RGB；多板审批/继承、物理亮灯和用户交互桌面仍待验收。当轮保留原生 USB，不改固件、不上传。详见 [早轮闭环与证据](docs/CLIENT_CLOSURE_20261009.md)。

早轮0.14.1交付入口：`artifacts/NodeConsole-0.14.1/TriLink.MinClient.exe`，保留为历史目录。当前操作方式见 [功能操作说明](docs/REAL_ROOM_RGB.md)，区分USB端口发现轮询、真实页状态刷新和无线Room准入。下方带日期的旧版本及测试数量为历史记录。

2026-10-05 方向重新定义：**ESP32自治节点 + 可选PC节点控制台**。发现、Room、简化任务/游戏与有限资源包运行尽量在板端；PC负责界面、制作/配置与包管理。PC DLL、板端资源/任务包、固件OTA是三种机制。本轮落地执行范围元数据/UI澄清，资源压缩/持久化/P2P复制/OTA未实现；旧游戏回归不证明新板端功能。见 [客户端新定义](docs/NODE_CONSOLE.md)。

2026-10-05 本地多人优化：已下载并核对官方 `thunder-game` 分支 `3cb3fda`，在独立源码目录将雷霆战机扩展为 **0.2.0 / 2–6人合作**。固定玩家/peer表、逐玩家输入/ACK、有界重传及GameWire v2，最大发包120B；仍由PC运行游戏、UDP承载，不改固件。没有合并主干或推送。本次状态见 [核对与优化](docs/THUNDER_REVIEW_20261005.md)，入口与预算见 [雷霆战机](docs/THUNDER.md)。

2026-10-05 主干基线源码同步：纳入9月22日至10月1日的 Room/RGB 收束与 review 修复，当前串口插件 **1.2.1**、真实 Room/RGB 模块 **0.2.1**，产品仍为 `0.14.0-local`、Host API `1.0`。主干此次同步仅包含源码、测试、构建工具和文档；不包含固件、EXE、运行配置、日志或截图，不新建标签或 Release。下方“未上传”描述均为对应日期的历史状态；软件构建与回归通过不代表真实桌面或三板验收通过。

10月5日主干基线同步目录完整Release构建通过（Windows PowerShell 5）：Core136、SerialLifecycle65、HardwareRoom47、Modules53、Desktop30、内置选择22项，七插件加载、UI渲染和运行目录检查通过；同时修正UTF-8中文清单与快捷方式脚本的编码处理。仅软件/夹具验证，未操作真实串口或固件。

2026-10-01 整体review修复：串口 **1.2.1**、真实模块 **0.2.1**、配套本地固件 **0.10.2**。修复重加入旧快照、同COM能力缓存不刷新和暂停/释放后旧扫描继续执行；内置构建验收固定安全模式，不被导入版本覆盖。客户端全量/串口定向及固件全新构建通过；实际桌面和三板验收待完成。本轮未上传、未烧录、未操作GPIO。详细证据、停止边界与下一步见 [本次修复记录](docs/REVIEW_FIXES_20261001.md)。

2026-10-01 前一轮本地收束：真实模块 **0.2.1** + 当时本地固件 **0.10.1**。RMT 提交不再等待队列空位，每个在途事务等待限 100 ms；驱动故障锁存后停用 RGB，客户端显示故障。退出的 leader 保留 10 秒旧 Room 公告，期间暂缓创建或加入。历史证据见 [10月1日故障收束](docs/CLOSURE_20261001.md)，当前版本以首段为准；当日记录的GitHub客户端提交为 [4bb5eca](https://github.com/AR121-a1/TriLink/commit/4bb5eca84d8e1fb80366c5903c8545af825fce37)。

2026-09-22 历史本地收束：当日真实模块 **0.2.0** + 配套本地固件 **0.10.0**，修复过期申请迟到批准，增加入群结果未知与手动确认重试。**三板需统一升级新版Room协议**；当日未烧录、真机验收或上传。历史清单和限制见 [阶段收束](docs/CLOSURE_20260922.md)，当前版本以首段为准。下述9月20日信息为上次已上传快照。

2026-09-20 客户端源码同步：真实 Room/RGB 模块 **0.1.1**，配套本地固件 **0.9.1**。客户端补上失效状态清除、保留选择与暂停取消批次；固件侧旧版本操作拒绝、RGB会话内防重放和终态保护的开发记录见 [加固记录](docs/HARDENING_20260920.md)。本仓库不包含固件源码或镜像。

面向三块 ESP32-S3 / 三台笔记本的 Windows 客户端骨架：插件平台、三节点房间演示与 USB CDC 发现接口。

Current repository scope: client source plus firmware 0.16.0 source and the selected packages listed above. Legacy single-board/RGB and limited two-PC records remain historical. P2P text, files, board resource packages/games and wake support remain future work; individual CDC verification is not multi-board Room/RGB acceptance.

| 文档 | 内容 |
| --- | --- |
| [功能说明](docs/FEATURES.md) | 当前功能、验证状态与限制 |
| [后续开发计划](docs/ROADMAP.md) | 下一阶段顺序、接口边界与验收条件 |
| [系统架构](docs/ARCHITECTURE.md) | 宿主、插件、服务、数据流和生命周期 |
| [节点控制台新定义](docs/NODE_CONSOLE.md) | S3自治/有限包、PC执行范围、压缩取舍与阶段验收 |
| [真实 Room / RGB](docs/REAL_ROOM_RGB.md) | 新模块入口、回执含义、三板验收与限制 |
| [0.15.0 公共底座](docs/RELEASE_0150.md) | 固件模块分发、CAPS、跨页共享节点服务与成套交付 |
| [0.14.4时序补丁](docs/TIMING_PATCH_0144.md) | 普通与20×同场景负例/复测、轮询熔断及RGB去重 |
| [0.14.3操作队列](docs/QUEUE_CLOSURE_0143.md) | 每页前台FIFO、后台让路、轻量节奏、取消/未知及本轮证据 |
| [0.14.2 单板RGB与发现](docs/SINGLE_NODE_CLOSURE_20261009.md) | 本机执行、许可/结果区分、自动只读刷新及本轮验收 |
| [0.14.2 双PC Room验证](docs/TWO_PC_ROOM_VALIDATION_20261009.md) | 一次真实准入、本机仪器/远端用户确认、8秒窗口与queue待优化 |
| [0.14.1 历史上位机闭环](docs/CLIENT_CLOSURE_20261009.md) | 早轮首页按钮、原生 USB 单板验收与独立交付 |
| [当前修复与验收](docs/REVIEW_FIXES_20261001.md) | review修复、停止边界、内置更新验证和剩余门 |
| [雷霆战机](docs/THUNDER.md) | 2–6人像素射击、局域网联机、资源预算及 ESP32 接口 |
| [版本记录](CHANGELOG.md) | 已发布与本地开发版的范围、版本规则 |

2026-10-05 源码快照：产品版本保持 `0.14.0-local`，模拟 Room 插件为 `1.1.0`；Git 正式版本标签仍为 `v0.13`。雷霆战机分支以最新主干源码为基线，不新建正式 Release 或上传预编译 EXE。Host API `1.0` 与插件版本独立管理。

Current source uses hardware-room 0.6.0 and desktop/serial 1.4.0 with Host API 1.0. Views share the node queue, pause state and receipts. Firmware 0.16.0 retains static RGB module dispatch and adds CDC/NCM adaptation; no dynamic native loader, persistent content store or OTA is implemented. Firmware source is now in `firmware/`; its separate pinned toolchain is still required.

新增可见入口：主窗口右侧 **扩展模块…**。支持详情、启停、导入更新、待重启状态、恢复内置，以及独立文本检查工具。见 [扩展模块管理](docs/MODULE_MANAGEMENT.md)。

本地修复（2026-09-14）：`trilink.serial`、`trilink.desktop` 更新至 `1.0.1`，
修复串口“无效查询”和普通模式误选模拟节点，宿主不变；尚未发布新 Git 标签。
原因、验证与接线要求见 [USB 扫描修复记录](docs/USB_SCAN_FIX.md)。

架构状态：**微内核 + 首方插件，可扩展。** 设计细节和新增插件流程见 [插件平台说明](docs/PLUGIN_PLATFORM.md)。

日常运行使用整套 `artifacts/NodeConsole-0.15.0/`；测试与交付证据根 `artifacts/tests/NodeConsole-0150-StageA/`。使用 `tools/build-isolated.ps1` 在唯一临时目录构建，不覆盖在用运行目录。旧 0.14.4 及更早目录和证据保持，见 [0.15.0 记录](docs/RELEASE_0150.md) 与 [目录说明](docs/DIRECTORY_LAYOUT.md)。

## 已完成

- Windows 单实例托盘客户端；按 X 隐藏到托盘，托盘右键“退出”才真正关闭，再次启动会恢复已有窗口；
- `WM_DEVICECHANGE` 触发扫描，加 3 秒低频兜底；串口扫描连续失败 5 次后自动暂停，可手动恢复；
- 使用 SetupAPI 只读枚举当前在线端口，不依赖 WMI；只探测 TriLink 描述、Espressif VID `303A` 或 `TRILINK_PORTS` 显式指定且在线的端口；
- nonce 回显握手，识别成功后托盘通知并恢复主窗口；
- 搜索附近节点、创建 Room、申请加入和成员邀请；
- leader 准入/拒绝/踢出，正常退出后按加入顺序继任并增加 `term`；
- 模拟插件保留独立快照/日志；真实模块读取各 S3 的成员内存副本，当前不含持久化日志和自动故障选举；
- 一人新 Room 可被发现，正式 Room 降为一人后解散；本机双闪若经确认新建一人Room，执行后保留，不自动退房；
- 插件 profile、清单、Host API 版本、服务注入、依赖拓扑和反向生命周期；
- 插件服务/effect 自动回滚、重复提供者和未声明服务访问保护；
- 每个插件 DLL 的 SHA-256 部署校验；
- 0.2.0多人扩展完整Release/Debug各794项通过，2/3/6独立进程UDP世界一致，单插件重建通过；下载的0.1.0基线489项仅保留为历史。证据见本次核对记录，软件、loopback与渲染不替代真实桌面和跨电脑/ESP32验收；
- 已验证只重建 `trilink.desktop` 时不重编宿主及其他插件，仍能通过完整加载和 UI 门。

## 插件组成

| 插件 | 职责 | 提供服务 |
| --- | --- | --- |
| `trilink.rooms` | Room、leader、成员关系、对等副本 | `trilink.room-network` |
| `trilink.serial` | USB CDC 发现、握手、搜索、受控轮询与共享业务命令 | `trilink.device-discovery`、`trilink.hardware-commands` |
| `trilink.simulation` | A/B/C 三节点数据源 | `trilink.simulation-control` |
| `trilink.desktop` | WinForms、托盘、模块管理 UI | `trilink.desktop-shell` |
| `trilink.modules` | 启停、导入、恢复、功能注册 | `trilink.module-management`、`trilink.module-features` |
| `trilink.text-tools` | 可选本地文本检查，按需创建 UI | 功能贡献：文本数据检查 |
| `trilink.hardware-room` | 真实成员管理、GPIO48 RGB 显式许可、本机/成员双闪与原ID结果跟踪 | 功能贡献：真实 Room / RGB；消费共享 `trilink.hardware-commands` |
| `trilink.game-link` | 按需、有界的局域网 UDP 游戏传输 | `trilink.game-link`；后续可替换为 ESP32 适配器 |
| `trilink.thunder` | 像素射击、2–6人合作、确定性输入同步 | 功能贡献：雷霆战机 |

EXE 只保留单实例、profile 选择和启动/停止。Room、串口、模拟器和 GUI 都不是宿主内置功能。

## 构建与运行

构建脚本不下载 NuGet 包，使用 `vswhere` 自动发现本机 Visual Studio / Build Tools 的 Roslyn，以及 .NET Framework 4.8 Developer Pack 的参考程序集。支持自定义安装目录；换电脑后的安装、调试和常见问题见 [开发环境配置](docs/DEVELOPMENT_SETUP.md)。

请在 **Windows PowerShell 5.1（powershell.exe）** 中执行；内置选择回归使用 .NET Framework，不使用 PowerShell 7（pwsh）。

```powershell
# 在克隆仓库的根目录执行（需 Visual Studio 2022 / Build Tools 与 .NET Framework 4.8 Developer Pack）
.\tools\build.ps1
```

产物结构：

```text
artifacts\Release\
  TriLink.MinClient.exe
  TriLink.Plugin.Abstractions.dll
  TriLink.PluginHost.dll
  profiles\desktop.profile.json
  plugins\<plugin-id>\plugin.json + plugin DLL
  启动 TriLink（三节点演示）.lnk
  启动 TriLink（真实硬件）.lnk
  启动 TriLink（安全恢复）.lnk
  module-data\                     首次修改模块配置时创建，不是测试数据
```

测试程序、截图和日志分别输出到 `artifacts/tests/Release/bin/`、`ui/`、`logs/`；插件编译暂存在 `artifacts/build/Release/plugins/`。`artifacts/archive/` 保存整理前的历史验证材料。完整构建与单插件更新都会检查运行目录只包含运行所需文件。

运行客户端或三节点演示：

```powershell
.\artifacts\Release\TriLink.MinClient.exe
.\artifacts\Release\TriLink.MinClient.exe --demo
```

接真实 S3 时双击 `启动 TriLink（真实硬件）.lnk`；仅演示房间规则时使用
`启动 TriLink（三节点演示）.lnk` 或显式点击“启用模拟”。普通模式没有设备时显示等待状态，
不会选中虚拟 A 或返回模拟搜索结果。快捷方式不会产生额外后台进程。

普通模式与演示模式使用相同生命周期：首次启动显示主窗口；按 X、点击主界面的“驻留后台”或托盘菜单“隐藏到托盘”均转入后台；双击托盘图标、选择“打开 TriLink”或再次启动 EXE 可恢复窗口。只有托盘右键“退出”才主动结束客户端，释放图标、后台服务和单实例锁；Windows 关机、注销以及自动截图完成时仍可正常退出。托盘图标属于同一个 `TriLink.MinClient.exe`，没有独立的托盘进程。

显式选择 profile：

```powershell
.\artifacts\Release\TriLink.MinClient.exe --profile desktop
```

## 只更新一个插件

完整构建至少成功一次后，修改哪个功能就只重建哪个插件：

```powershell
.\tools\build.ps1 -PluginId trilink.rooms
.\tools\build.ps1 -PluginId trilink.serial
.\tools\build.ps1 -PluginId trilink.simulation
.\tools\build.ps1 -PluginId trilink.desktop
```

每次会更新对应 DLL 和哈希，校验完整 profile，并用新进程做真实加载。更新前请从托盘退出客户端，更新后重启；第一版不做不可靠的 .NET Framework 程序集热卸载。

插件管理命令：

```powershell
.\tools\pluginctl.ps1 -Action List
.\tools\pluginctl.ps1 -Action Validate
```

## 三节点演示顺序

1. 选择 A 并创建 Room；
2. A 邀请 B，或切到 B 后申请加入 A；
3. 切回 A，在“入房申请”中同意 B；
4. 切到 B 邀请 C，证明普通成员也有邀请权；
5. C 接受邀请，A 同意准入；
6. A 退出后观察 B 继承 leader、`term` 增加且 Room 保留；
7. B 踢出 C，正式 Room 降为一人并自动解散。

右侧“模块概览”显示运行模块状态；主窗口右侧“扩展模块…”打开九模块的显式管理窗口，可启停、导入更新或打开具体功能。

## USB 识别协议

当前配套的独立本地 v0.10.2 固件沿用 S3 原生 USB Serial/JTAG 固定 CDC 数据接口，通过 Espressif
`VID_303A` 筛选，不要求自定义产品名。CH340/CH343 是烧录与日志口，不承载本版客户端协议。
开发阶段可以显式指定已经实现该协议的数据端口（不能借此把烧录口变为数据口）：

```powershell
$env:TRILINK_PORTS = 'COM7' # Example only: replace with the verified local data port.
.\artifacts\Release\TriLink.MinClient.exe
```

握手和搜索文本见 [USB 与 Room 契约](docs/USB_AND_ROOM_CONTRACT.md)。该文本只用于 PC 与本机 S3 的最小控制面，三块 S3 之间继续使用现有定长二进制 `APP_DATA` 帧。

## 当前硬件边界

串口枚举、握手解析、通知与轮询保护已有客户端实现和无硬件回归；Windows 在线端口枚举已在本机验证。
Historical pre-0.16.0 integration scope: 本地独立固件 v0.10.2 已实现 `HELLO/SEARCH`、真实 Room 管理、成员副本和 RGB 应用回执；客户端共享命令服务与真实模块已接入。固件源码、镜像和构建receipt不随本仓库分发。单板识别/创建/退出、本端发现附近设备已有记录；本轮RGB终态与物理观察按独立证据分开，多板联通仍待验证。因此：

- 模拟三节点 Room 闭环现在可直接运行；
- 真实 S3 插入识别应连接原生 USB 数据口后验证，至少两块 S3 同时上电才可能发现邻居；
- 真实 Room 软件后端已接入 S3/ESP-NOW，普通模式首页即为操作页面；扩展管理仍可检查模块状态，但无需绕到管理窗口才操作；
- 退出的 leader 正在重播旧公告时，请等待退出后10秒并刷新，再创建或加入；RGB驱动故障后先检查接线，协调退出Room再重启设备；
- Historical 0.14.x distribution scope: 本仓库不包含或自动烧录固件；0.14.4仅fake软件时序验证，不访问实板，不重复历史授权双闪、不创建/退出/踢人拆当前Room。0.14.2已读同ID驱动终态2并恢复许可，0.14.3仅真实只读，其人工光学仍未确认。

不建议退回 CH343 承载业务数据，否则调试日志、烧录和二进制业务会再次耦合。
