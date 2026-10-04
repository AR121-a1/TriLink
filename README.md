# TriLink v0.14（开发源码快照）

2026-10-05 客户端源码同步：纳入9月22日至10月1日的 Room/RGB 收束与 review 修复，当前串口插件 **1.2.1**、真实 Room/RGB 模块 **0.2.1**，产品仍为 `0.14.0-local`、Host API `1.0`。仅更新 GitHub main 的源码、测试、构建工具和文档；不包含固件、EXE、运行配置、日志或截图，不新建标签或 Release。下方“未上传”描述均为对应日期的历史状态；软件构建与回归通过不代表真实桌面或三板验收通过。

10月5日同步目录完整Release构建通过（Windows PowerShell 5）：Core136、SerialLifecycle65、HardwareRoom47、Modules53、Desktop30、内置选择22项，七插件加载、UI渲染和运行目录检查通过；同时修正UTF-8中文清单与快捷方式脚本的编码处理。仅软件/夹具验证，未操作真实串口或固件。

2026-10-01 整体review修复：串口 **1.2.1**、真实模块 **0.2.1**、配套本地固件 **0.10.2**。修复重加入旧快照、同COM能力缓存不刷新和暂停/释放后旧扫描继续执行；内置构建验收固定安全模式，不被导入版本覆盖。客户端全量/串口定向及固件全新构建通过；实际桌面和三板验收待完成。本轮未上传、未烧录、未操作GPIO。详细证据、停止边界与下一步见 [本次修复记录](docs/REVIEW_FIXES_20261001.md)。

2026-10-01 前一轮本地收束：真实模块 **0.2.1** + 当时本地固件 **0.10.1**。RMT 提交不再等待队列空位，每个在途事务等待限 100 ms；驱动故障锁存后停用 RGB，客户端显示故障。退出的 leader 保留 10 秒旧 Room 公告，期间暂缓创建或加入。历史证据见 [10月1日故障收束](docs/CLOSURE_20261001.md)，当前版本以首段为准；当日记录的GitHub客户端提交为 [4bb5eca](https://github.com/AR121-a1/TriLink/commit/4bb5eca84d8e1fb80366c5903c8545af825fce37)。

2026-09-22 历史本地收束：当日真实模块 **0.2.0** + 配套本地固件 **0.10.0**，修复过期申请迟到批准，增加入群结果未知与手动确认重试。**三板需统一升级新版Room协议**；当日未烧录、真机验收或上传。历史清单和限制见 [阶段收束](docs/CLOSURE_20260922.md)，当前版本以首段为准。下述9月20日信息为上次已上传快照。

2026-09-20 客户端源码同步：真实 Room/RGB 模块 **0.1.1**，配套本地固件 **0.9.1**。客户端补上失效状态清除、保留选择与暂停取消批次；固件侧旧版本操作拒绝、RGB会话内防重放和终态保护的开发记录见 [加固记录](docs/HARDENING_20260920.md)。本仓库不包含固件源码或镜像。

面向三块 ESP32-S3 / 三台笔记本的 Windows 客户端骨架：插件平台、三节点房间演示与 USB CDC 发现接口。

本仓库仅包含客户端源码，不包含 ESP32 固件。已接入独立的真实 Room / RGB 模块，当前本地开发版配套固件 v0.10.2；软件检查通过，尚未烧录该版和完成三板实测。文件传输与远程唤醒仍待开发，不能把模拟/软件测试通过等同于真机联网通过。

| 文档 | 内容 |
| --- | --- |
| [功能说明](docs/FEATURES.md) | 当前功能、验证状态与限制 |
| [后续开发计划](docs/ROADMAP.md) | 下一阶段顺序、接口边界与验收条件 |
| [系统架构](docs/ARCHITECTURE.md) | 宿主、插件、服务、数据流和生命周期 |
| [真实 Room / RGB](docs/REAL_ROOM_RGB.md) | 新模块入口、回执含义、三板验收与限制 |
| [当前修复与验收](docs/REVIEW_FIXES_20261001.md) | review修复、停止边界、内置更新验证和剩余门 |
| [版本记录](CHANGELOG.md) | 已发布与本地开发版的范围、版本规则 |

2026-10-05 源码快照：产品版本保持 `0.14.0-local`，模拟 Room 插件为 `1.1.0`；Git 正式版本标签仍为 `v0.13`。本次更新 main 源码，不新建正式 Release 或上传预编译 EXE。Host API `1.0` 与插件版本独立管理。

最新本地源码变化：`trilink.serial` 1.2.1增加手动身份刷新、取消轮次与内部I/O测试边界；`trilink.hardware-room` 0.2.1保留本机RGB故障/退出公告提示及确认状态；桌面1.1.1和公共Host API不变。配套本地固件实现最多 **6 人（含本机）**的 S3 内存成员副本、审批/邀请/正常继任与 RGB 结果链路，真机验收待进行；仅克隆本仓库不能构建该固件。历史模拟规则见 [Room 1.1.0](docs/ROOMS_1_1_0.md)。

新增可见入口：主窗口右侧 **扩展模块…**。支持详情、启停、导入更新、待重启状态、恢复内置，以及独立文本检查工具。见 [扩展模块管理](docs/MODULE_MANAGEMENT.md)。

本地修复（2026-09-14）：`trilink.serial`、`trilink.desktop` 更新至 `1.0.1`，
修复串口“无效查询”和普通模式误选模拟节点，宿主不变；尚未发布新 Git 标签。
原因、验证与接线要求见 [USB 扫描修复记录](docs/USB_SCAN_FIX.md)。

架构状态：**微内核 + 首方插件，可扩展。** 设计细节和新增插件流程见 [插件平台说明](docs/PLUGIN_PLATFORM.md)。

日常使用只需 `artifacts/Release/`。源码、测试、编译暂存与历史材料已分开，完整目录及归类规则见 [目录说明](docs/DIRECTORY_LAYOUT.md)，回归入口见 [测试说明](tests/README.md)。

## 已完成

- Windows 单实例托盘客户端；按 X 隐藏到托盘，托盘右键“退出”才真正关闭，再次启动会恢复已有窗口；
- `WM_DEVICECHANGE` 触发扫描，加 3 秒低频兜底；串口扫描连续失败 5 次后自动暂停，可手动恢复；
- 使用 SetupAPI 只读枚举当前在线端口，不依赖 WMI；只探测 TriLink 描述、Espressif VID `303A` 或 `TRILINK_PORTS` 显式指定且在线的端口；
- nonce 回显握手，识别成功后托盘通知并恢复主窗口；
- 搜索附近节点、创建 Room、申请加入和成员邀请；
- leader 准入/拒绝/踢出，正常退出后按加入顺序继任并增加 `term`；
- 模拟插件保留独立快照/日志；真实模块读取各 S3 的成员内存副本，当前不含持久化日志和自动故障选举；
- 一人新 Room 可被发现，正式 Room 降为一人后解散；
- 插件 profile、清单、Host API 版本、服务注入、依赖拓扑和反向生命周期；
- 插件服务/effect 自动回滚、重复提供者和未声明服务访问保护；
- 每个插件 DLL 的 SHA-256 部署校验；
- 客户端本轮完整构建通过核心136、串口生命周期65、模块53、桌面30、HardwareRoom47项，以及内置更新选择22项；profile启动、UI渲染和运行目录门通过；这些不替代真实桌面与硬件验收；
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
| `trilink.hardware-room` | 真实成员管理、GPIO48 RGB 显式启用、执行结果 | 功能贡献：真实 Room / RGB；消费共享 `trilink.hardware-commands` |

EXE 只保留单实例、profile 选择和启动/停止。Room、串口、模拟器和 GUI 都不是宿主内置功能。

## 构建与运行

不下载 NuGet 包。构建脚本使用本机 Visual Studio Roslyn 和 .NET Framework 4.8 参考程序集：

请在 **Windows PowerShell 5.1（powershell.exe）** 中执行；内置选择回归使用 .NET Framework，不使用 PowerShell 7（pwsh）。

```powershell
# 在克隆仓库的根目录执行（需 Visual Studio 2022 Community 与 .NET Framework 4.8 开发工具）
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

右侧“模块概览”显示运行模块状态；主窗口右侧“扩展模块…”打开七模块的显式管理窗口，可启停、导入更新或打开具体功能。

## USB 识别协议

当前配套的独立本地 v0.10.2 固件沿用 S3 原生 USB Serial/JTAG 固定 CDC 数据接口，通过 Espressif
`VID_303A` 筛选，不要求自定义产品名。CH340/CH343 是烧录与日志口，不承载本版客户端协议。
开发阶段可以显式指定已经实现该协议的数据端口（不能借此把烧录口变为数据口）：

```powershell
$env:TRILINK_PORTS = 'COM7'
.\artifacts\Release\TriLink.MinClient.exe
```

握手和搜索文本见 [USB 与 Room 契约](docs/USB_AND_ROOM_CONTRACT.md)。该文本只用于 PC 与本机 S3 的最小控制面，三块 S3 之间继续使用现有定长二进制 `APP_DATA` 帧。

## 当前硬件边界

串口枚举、握手解析、通知与轮询保护已有客户端实现和无硬件回归；Windows 在线端口枚举已在本机验证。
本地独立固件 v0.10.2 已实现 `HELLO/SEARCH`、真实 Room 管理、成员副本和 RGB 应用回执；客户端共享命令服务与真实模块已接入。固件源码、镜像和构建receipt不随本仓库分发。原生 USB 真机握手、多板联通和物理 LED 仍待验证。因此：

- 模拟三节点 Room 闭环现在可直接运行；
- 真实 S3 插入识别应连接原生 USB 数据口后验证，至少两块 S3 同时上电才可能发现邻居；
- 真实 Room 软件后端已接入 S3/ESP-NOW，操作入口为“扩展模块 → 真实 Room / RGB”；下一步按当前验收门进行三板联调；
- 退出的 leader 正在重播旧公告时，请等待退出后10秒并刷新，再创建或加入；RGB驱动故障后先检查接线，协调退出Room再重启设备；
- 本仓库不包含或自动烧录固件；本轮未改写开发板或执行GPIO。

不建议退回 CH343 承载业务数据，否则调试日志、烧录和二进制业务会再次耦合。
