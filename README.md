# TriLink v0.14（开发源码快照）

面向三块 ESP32-S3 / 三台笔记本的 Windows 客户端骨架：插件平台、三节点房间演示与 USB CDC 发现接口。

本仓库仅包含客户端源码，不包含 ESP32 固件。v0.13 已实现单机三节点模拟闭环；真实三电脑的无线 Room 同步、文件传输与远程唤醒仍待开发，不能把模拟通过等同于真机联网通过。

| 文档 | 内容 |
| --- | --- |
| [功能说明](docs/FEATURES.md) | 当前功能、验证状态与限制 |
| [后续开发计划](docs/ROADMAP.md) | 下一阶段顺序、接口边界与验收条件 |
| [系统架构](docs/ARCHITECTURE.md) | 宿主、插件、服务、数据流和生命周期 |
| [版本记录](CHANGELOG.md) | 已发布与本地开发版的范围、版本规则 |

2026-09-16 源码快照：产品版本保持 `0.14.0-local`，Room 插件为 `1.1.0`；Git 正式版本标签仍为 `v0.13`。本次更新 main 源码，不新建正式 Release 或上传预编译 EXE。Host API `1.0` 与插件版本独立管理。

最新变化：显式扩展模块管理、串口 SetupAPI 扫描修复，以及最多 **6 人（含本机）**的 Room 容量检查。六人规则已实现于模拟后端，真实 Room 联网仍未接通。见 [Room 1.1.0](docs/ROOMS_1_1_0.md)。

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
- leader 准入/拒绝/踢出，leader 退出后按不可变 `join_order` 继承并增加 `term`；
- 单机模拟中各成员持有独立的快照和事件日志副本，界面显示副本一致性；真实跨电脑复制尚未接入；
- 一人新 Room 可被发现，正式 Room 降为一人后解散；
- 插件 profile、清单、Host API 版本、服务注入、依赖拓扑和反向生命周期；
- 插件服务/effect 自动回滚、重复提供者和未声明服务访问保护；
- 每个插件 DLL 的 SHA-256 部署校验；
- 136 项无硬件核心检查、53 项模块检查、30 项桌面模式/生命周期检查，以及 profile 启动和 UI 渲染检查；这些不替代真实桌面与硬件验收；
- 已验证只重建 `trilink.desktop` 时不重编宿主及其他插件，仍能通过完整加载和 UI 门。

## 插件组成

| 插件 | 职责 | 提供服务 |
| --- | --- | --- |
| `trilink.rooms` | Room、leader、成员关系、对等副本 | `trilink.room-network` |
| `trilink.serial` | USB CDC 发现、握手、搜索和受控轮询 | `trilink.device-discovery` |
| `trilink.simulation` | A/B/C 三节点数据源 | `trilink.simulation-control` |
| `trilink.desktop` | WinForms、托盘、模块管理 UI | `trilink.desktop-shell` |
| `trilink.modules` | 启停、导入、恢复、功能注册 | `trilink.module-management`、`trilink.module-features` |
| `trilink.text-tools` | 可选本地文本检查，按需创建 UI | 功能贡献：文本数据检查 |

EXE 只保留单实例、profile 选择和启动/停止。Room、串口、模拟器和 GUI 都不是宿主内置功能。

## 构建与运行

不下载 NuGet 包。构建脚本使用本机 Visual Studio Roslyn 和 .NET Framework 4.8 参考程序集：

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

右侧“模块概览”显示运行模块状态；主窗口右侧“扩展模块…”打开六模块的显式管理窗口，可启停、导入更新或打开具体功能。

## USB 识别协议

本地 v0.6.0 固件使用 S3 原生 USB Serial/JTAG 固定 CDC 数据接口，通过 Espressif
`VID_303A` 筛选，不要求自定义产品名。CH340/CH343 是烧录与日志口，不承载本版客户端协议。
开发阶段可以显式指定已经实现该协议的数据端口（不能借此把烧录口变为数据口）：

```powershell
$env:TRILINK_PORTS = 'COM7'
.\artifacts\Release\TriLink.MinClient.exe
```

握手和搜索文本见 [USB 与 Room 契约](docs/USB_AND_ROOM_CONTRACT.md)。该文本只用于 PC 与本机 S3 的最小控制面，三块 S3 之间继续使用现有定长二进制 `APP_DATA` 帧。

## 当前硬件边界

串口枚举、握手解析、通知与轮询保护已有客户端实现和无硬件回归；Windows 在线端口枚举已在本机验证。
本地独立固件 v0.6.0 已实现 `HELLO/SEARCH`，但原生 USB 真机握手与多板联通仍待验证。因此：

- 模拟三节点 Room 闭环现在可直接运行；
- 真实 S3 插入识别应连接原生 USB 数据口后验证，至少两块 S3 同时上电才可能发现邻居；
- 真实三电脑 Room 同步应新增传输服务插件，接入现有 ESP-NOW `APP_DATA` dispatcher；
- 本仓库不包含或自动烧录固件；本次客户端修复未改写开发板。

不建议退回 CH343 承载业务数据，否则调试日志、烧录和二进制业务会再次耦合。
