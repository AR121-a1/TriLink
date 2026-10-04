# TriLink v0.14 本地版架构

2026-10-01 review修复：serial1.2.1内部以ISerialIo/ISerialConnection封装OS枚举与端口，公开服务保持不变。每次恢复创建取消轮次并复核缓存，周期扫描复用未变化身份；共享USB所有者仍为单通道。内置构建验证固定desktop+safe-mode，真实用户选择独立保留。S3 v0.10.2在原准入状态机保存本地成员版本下限，不改变空口体/任务/缓存。当前证据见 [review修复](REVIEW_FIXES_20261001.md)。

2026-10-01 前一轮：hardware-room0.2.1读取本机RGB故障和退出公告状态；固件0.10.1保留10秒退出公告，并收束RMT等待/故障资源生命周期。Room空口opcode2、Host API1.0、产品0.14.0-local及七插件不变。历史证据见 [故障收束](CLOSURE_20261001.md)。

2026-09-22：hardware-room0.2.0只增加状态展示和人工确认重试；OFFER/CONFIRM、票据、截止时间由S3执行，PC不承担新的一致性状态机。Room空口opcode2与旧版隔离；协议边界、未知结果和验收状态见 [阶段收束](CLOSURE_20260922.md)。

扩展状态：可扩展。当前是一个 Windows 进程、一个插件宿主和九个首方 DLL。界面与业务通过服务契约通信，没有额外本机 HTTP 后端或独立托盘进程。真实 Room / RGB 已通过独立模块接入 S3，模拟仍隔离保留；ESP32 固件不包含在此仓库，真机联调待验收。雷霆战机新增独立游戏传输服务，当前为局域网 UDP，ESP32 适配待实现。

## 实际组成

```text
TriLink.MinClient.exe
  单实例锁、恢复消息、profile 选择、STA 消息循环、最终退出
        |
        v
TriLink.PluginHost + TriLink.Plugin.Abstractions
  清单/哈希 -> 依赖排序 -> 服务注册 -> Start -> 逆序 Stop/Dispose
        |
        +-- trilink.rooms       IRoomNetwork（当前为 SimulatedNetwork）
        +-- trilink.serial      IDeviceDiscoveryService（Windows 串口）
        +-- trilink.simulation  ISimulationControl（A/B/C 演示数据）
        +-- trilink.desktop     IDesktopShell（WinForms 与 NotifyIcon）
        +-- trilink.modules     IModuleManagementService + IModuleFeatureRegistry
        +-- trilink.text-tools  按需文本功能窗口（可选模块）
        +-- trilink.hardware-room 按需真实 Room / RGB（消费共享串口命令服务）
        +-- trilink.game-link    IGameLinkFactory（按需有界 UDP，无后台工作）
        +-- trilink.thunder      按需像素射击、两人合作与确定性输入同步
```

无商业服务器依赖。真实模块通过 `IHardwareCommandService` 访问本机 S3，发现/搜索/命令共用串口锁；Room 的易失副本由 S3 持有，RGB 经成员自动路由。没有公网直连、持久化共识或自动故障选举；详见 [新模块链路与边界](REAL_ROOM_RGB.md)。

## 代码所有权

| 模块 | 源码目录 | 职责 |
| --- | --- | --- |
| 应用入口 | `src/TriLink.MinClient/` | 启动、单实例、消息循环和产品版本 |
| 宿主 | `src/TriLink.PluginHost/` | profile、manifest、服务、依赖与资源回收 |
| 契约 | `src/TriLink.Plugin.Abstractions/` | Host API、DTO、插件接口和窗口恢复消息 |
| 房间插件 | `src/Plugins/TriLink.Plugin.Rooms/` | 房间命令、权限、模拟网络与副本对象 |
| 串口插件 | `src/Plugins/TriLink.Plugin.Serial/` | 端口发现、USB文本解析、握手、轮询策略与共享业务命令 |
| 演示插件 | `src/Plugins/TriLink.Plugin.Simulation/` | 注入模拟节点、控制模拟在线状态 |
| 桌面插件 | `src/Plugins/TriLink.Plugin.Desktop/` | 展示节点与房间、提交命令、托盘通知 |
| 扩展管理插件 | `src/Plugins/TriLink.Plugin.Modules/` | 模块管理服务、按需功能注册表 |
| 可选文本插件 | `src/Plugins/TriLink.Plugin.TextTools/` | 本地文本检查，独立功能窗口 |
| 真实Room插件 | `src/Plugins/TriLink.Plugin.HardwareRoom/` | 本机S3状态显示、真实管理命令、RGB回执与故障/退出公告提示 |
| 游戏传输插件 | `src/Plugins/TriLink.Plugin.GameLink/` | 按需打开的有界UDP传输与可替换传输工厂 |
| 雷霆战机插件 | `src/Plugins/TriLink.Plugin.Thunder/` | 像素视图、固定实体池、确定性引擎和双人输入同步 |

文件已按所属组件归位；部分命名空间保留历史名称 `TriLink.Core` / `TriLink.MinClient`，不等于仍在跨目录编译。契约程序集与插件有各自版本；本地产品`0.14.0-local`保持Host API `1.0`的旧接口，加法扩展以服务声明进行能力约束。

## 服务与依赖

| 服务 | 提供者 | 消费者 |
| --- | --- | --- |
| `IPluginCatalog` | 宿主 | 桌面 |
| `IRoomNetwork` | 房间 | 演示、桌面 |
| `IDeviceDiscoveryService` | 串口 | 桌面、真实Room |
| `IHardwareCommandService` | 串口 | 真实Room |
| `ISimulationControl` | 演示 | 桌面 |
| `IDesktopShell` | 桌面 | 应用入口 |
| `IModuleManagementService` | 扩展管理 | 桌面 |
| `IModuleFeatureRegistry` | 扩展管理 | 桌面、文本工具、真实Room、雷霆战机 |
| `IGameLinkFactory` | 游戏传输 | 雷霆战机 |

消费者在 manifest 中声明 `requiresServices`，提供者声明 `providesServices`。宿主拒绝重复提供者、缺失服务、循环依赖和未声明访问。`context.Defer()` 登记清理动作；配置失败或退出时逆序回收，消费者先于提供者停止。

桌面依赖模拟控制接口，默认 profile 始终包含模拟插件；因此单纯去掉该插件并不能得到独立的真实硬件 profile。这是后续真实节点身份拆分的一部分。

## 当前数据流

**雷霆战机：** 按需功能窗口 → GameSession → IGameLink → 对端会话。游戏房主每100 ms串行提交3 tick输入，两端独立运行整数引擎；连续输入确认与有界补发恢复丢包。游戏房主与Room leader分开，不修改S3成员表。ESP32将来替换传输提供者，当前不存在游戏USB桥接。资源与协议见 [雷霆战机](THUNDER.md)。

**模拟房间：** UI 操作 -> `IRoomNetwork` -> `RoomSession` 校验与提交 -> 更新进程内各 `RoomReplica` -> Changed/Notice -> UI 线程刷新。快照副本存在内存中，未序列化到无线链路或磁盘。

**设备发现：** 启动或设备到达事件 -> 受控扫描 -> 筛选候选端口 -> HELLO/DEVICE nonce 校验 -> DeviceArrived -> 加入 UI 的节点列表并通知。

**邻居搜索：** 用户点击搜索 -> 串口发送SEARCH -> 解析PEER/END -> 更新邻居列表。主窗口房间按钮保留模拟入口；真实模块另经ROOMGET查询附近Room并发送真实JOIN/INVITE管理命令。

**真实Room：** 模块 -> `IHardwareCommandService` -> 本机S3控制面 -> ESP-NOW -> 对端S3；OFFER/CONFIRM、票据、成员快照和ACK由S3执行。PC只读取状态及提交意图，固件校验revision/term；退出leader保留旧快照重播10秒，暂缓本机CREATE/JOIN。

**RGB：** 模块显式提交 -> 成员路由 -> 目标单任务worker -> 单灯RMT -> 应用结果回执。当前S3无DMA且一次最多一个事务在途，提交不等待队列空位、完成等待每事务100 ms。驱动故障锁存后不再发送新灯数据；清理失败保留资源、不重复删除，物理LED状态未知。

本机ROOMGET的状态字节含RGB故障位bit6（64）和退出公告位bit7（128），只用于本机显示；它们不是设备能力位，也不是空口协议升级。故障提示建议协调退出Room后重启；退出公告提示等待10秒期限后刷新。

后台扫描通过单飞保护避免重入；连续失败触发暂停并关闭定时源，外部设备事件不能绕过暂停。扫描、搜索和业务命令共用串口访问锁，业务前进行身份复核和nonce校验；真实模块关闭、手动暂停或切换端口中止后续查询批次，不重放结果未知的写命令。

## 窗口与资源生命周期

```text
启动 -> 主窗口显示 + 托盘图标
           | X / 驻留后台
           v
        主窗口隐藏，进程继续
           | 托盘打开 / 双击 / 同桌面再次启动
           v
        主窗口恢复

托盘退出 / Windows 关机 / 截图完成
  -> 允许真正关闭 -> 隐藏并释放图标 -> 停止插件与后台任务 -> 释放单实例锁
```

注册窗口消息用于同一交互桌面的二次启动恢复。自动化工具可能运行在 `CodexSandboxDesktop`，用户实际桌面可能是 `Default`；会话号相同不代表桌面相同。窗口句柄、Visible 标志和 DrawToBitmap 图像不能证明用户桌面显示，最终启动需在用户的 Explorer 中完成。

## 插件更新与发布边界

主窗口提供“扩展模块”入口。UI → 管理服务 → `ModuleStore` 共用加载校验；功能模块 → 注册表 → 按需创建功能窗口。GUI 导入采用独立包目录和配置原子替换，不覆盖正在加载的 DLL，启停与更新在重启后生效。`--safe-mode` 绕过导入映射，提供恢复内置配置的独立路径。详见 [模块管理](MODULE_MANAGEMENT.md)。

每个部署插件目录包含 DLL 与 `plugin.json`。构建生成 SHA-256，宿主启动时检查 DLL/清单一致性；哈希不提供签名或恶意代码隔离。所有插件与主程序同权限，只加载项目自己的插件。

`tools/build.ps1 -PluginId ...` 只编译指定组件，再验证完整 profile 与 UI 加载；桌面更新还执行生命周期回归。更新前退出客户端，更新后重启，不在进程内热替换已经加载的程序集。

运行文件位于 `artifacts/Release/`；测试、编译暂存、归档分别位于 `artifacts/tests/`、`artifacts/build/`、`artifacts/archive/`。发布目录检查只接受运行文件。源码仓库不提交这些生成目录。

## 后续扩展入口

共享命令服务与真实Room实现已经接入，雷霆战机现通过独立游戏服务实现局域网合作。后续先完成真实桌面、三板Room/RGB和压力验收，再接入ESP32游戏传输及其他资源应用。新应用复用明确服务边界，不让UI直接写ESP-NOW帧或承担文件分片缓存；原始文件主体仍由PC保存。详细阶段与验收条件见 [后续开发计划](ROADMAP.md)，插件清单约束见 [插件平台](PLUGIN_PLATFORM.md)。
