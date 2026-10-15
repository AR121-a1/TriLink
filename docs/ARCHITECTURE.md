# TriLink v0.16.0 本地版架构

Current detailed reference (2026-10-14): [Architecture 0.16.0](ARCHITECTURE_0160.md), with the companion [Development Guide](DEVELOPMENT_GUIDE_0160.md). These source-backed guides separate implemented contracts from roadmap proposals. The dated sections below preserve historical decisions and evidence.

2026-10-12：USB 适配层升级为 CDC+NCM，共享业务层不分叉。Windows 10 保留 CDC；Windows 11 可选本地 USB 网卡/网页；桌面仍优先 CDC。固件只有 USB worker 消费 bridge 回执，HTTP 通过单槽 mailbox 进入同一命令入口。NCM 固定地址、接口验证、有限请求、未知写入不重放；具体职责、扩展预算与验收边界见 [0.16.0 架构与交付](RELEASE_0160.md)。以下 0.15.0 是继承的阶段 A 设计和历史证据。

## Historical 0.15.0 baseline and earlier records

2026-10-11，当前实现为 **0.15.0 阶段 A：共享节点会话与固件业务分发骨干**，详见 [本轮交付与验证](RELEASE_0150.md)。职责仍以 [固件与上位机职责分配](P2P_PLATFORM_RESPONSIBILITIES.md) 为准：S3 持有无线/Room/执行状态，PC 共享节点服务统一页面队列和订阅，唯一 USB owner 保持。小文本、连续文件流、扩展数据对齐和 realtime 策略属于阶段 B–E，尚未实现；传输方向不建立板端持久化库。

当前版本为产品 `0.15.0-local`、desktop `1.3.0`、hardware-room `0.5.0`、serial `1.3.0`、固件 `0.15.0`，Host API 保持 `1.0`。HardwareRoom 提供 `INodeSessionService`，首页和独立窗口订阅同一物理节点的共享快照、FIFO、轮询和 RGB 回执；阶段 A 同时只允许一个节点绑定，不支持多目标并行会话。Serial 提供 `INodeCapabilityService`，通过 fresh HELLO 与 CAPS 读取版本、boot、容量和内建模块描述。固件阶段 A 增加模块分发/CAPS，保留原 Room/RGB 行为。当前入口为 `artifacts/NodeConsole-0.15.0/TriLink.MinClient.exe`，证据根为 `artifacts/tests/NodeConsole-0150-StageA/`；最终测试与收据以 [0.15.0 记录](RELEASE_0150.md) 为准。

历史0.14.4-local、desktop1.2.3、hardware-room0.4.1，serial1.2.2/Host API1.0/固件0.10.2保持。同一UI线程pump执行每页FIFO8待项＋在途和合并后台读，非全局业务锁/额外线程/并行串口。补丁清除第五自动失败后的light/full残留，并将RGB去重身份独立于CREATE前后Room转换；出队身份/Room/boot/权限复核不变。Release/Debug各1067项普通软件检查及v21 fake正常/20×各12场景通过；当轮未访问真实USB/无线/GPIO或烧写固件。0.14.3真实只读31/31、0.14.2有限单板/双PC证据均保持历史，新前台写FIFO真实无线、默认人工审批及用户桌面仍未验收。见 [时序补丁](TIMING_PATCH_0144.md)与 [历史队列收束](QUEUE_CLOSURE_0143.md)。

2026-10-09 历史版本：产品 **0.14.2-local**，desktop **1.2.1**、serial **1.2.2**、hardware-room **0.3.1**，Host API **1.0**。首页/独立页共用功能工厂与单USB所有者；本机双闪、原ID/会话有界跟踪、可见页默认只读初刷新/5秒更新，不改固件0.10.2。Release/Debug各985项通过，COM_PORT实际主表单工厂在未选成员时提交一次双闪并自动读回原ID驱动终态2，恢复原许可/保持原Room；不是光学或用户桌面验收。见 [当轮单板收束](SINGLE_NODE_CLOSURE_20261009.md)。

同日早轮0.14.1/desktop1.2.0/hardware-room0.3.0实现真实页面嵌入与模拟隔离，Release/Debug各934项及COM_PORT主表单刷新/创建/退出通过；当时邻居0、没有RGB执行。历史见 [早轮收束](CLIENT_CLOSURE_20261009.md)，不作为0.14.2新增路径的验收。

2026-10-05 历史目标为ESP32自治节点、PC可选控制台；范围元数据区分PC工具、S3控制界面与PC示例。当前已完成的阶段 A 不等于有限任务数据互传、压缩或 OTA 完成；旧持久化包方案与当前边界见 [节点控制台定义](NODE_CONSOLE.md)。

2026-10-01 历史review修复：serial1.2.1内部以ISerialIo/ISerialConnection封装OS枚举与端口，公开服务保持不变。每次恢复创建取消轮次并复核缓存，周期扫描复用未变化身份；共享USB所有者仍为单通道。内置构建验证固定desktop+safe-mode，真实用户选择独立保留。S3 v0.10.2在原准入状态机保存本地成员版本下限，不改变空口体/任务/缓存。历史证据见 [review修复](REVIEW_FIXES_20261001.md)。

2026-10-01 前一轮：hardware-room0.2.1读取本机RGB故障和退出公告状态；固件0.10.1保留10秒退出公告，并收束RMT等待/故障资源生命周期。Room空口opcode2、Host API1.0、产品0.14.0-local及七插件不变。历史证据见 [故障收束](CLOSURE_20261001.md)。

2026-09-22：hardware-room0.2.0只增加状态展示和人工确认重试；OFFER/CONFIRM、票据、截止时间由S3执行，PC不承担新的一致性状态机。Room空口opcode2与旧版隔离；协议边界、未知结果和验收状态见 [阶段收束](CLOSURE_20260922.md)。

扩展状态：可扩展。当前是一个 Windows 进程、一个插件宿主和九个首方 DLL。界面与业务通过服务契约通信，没有额外本机 HTTP 后端或独立托盘进程。真实 Room / RGB 的首页与独立窗口共享节点应用服务，模拟仍隔离保留；ESP32 固件是旁边独立工程，不进入 PC ZIP。早轮单板 USB/Room 证据保留，本轮不扩为新版多板验收。雷霆战机当前仍由 PC 执行、局域网 UDP 承载；文本 P2P、连续数据流、有限任务数据互传、S3 游戏和 OTA 均未完成。

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
        +-- trilink.serial      发现/旧命令/CAPS（唯一 Windows 串口 owner）
        +-- trilink.simulation  ISimulationControl（A/B/C 演示数据）
        +-- trilink.desktop     IDesktopShell（WinForms 与 NotifyIcon，真实页/演示页分域）
        +-- trilink.modules     IModuleManagementService + IModuleFeatureRegistry
        +-- trilink.text-tools  按需文本功能窗口（可选模块）
        +-- trilink.hardware-room INodeSessionService + 真实 Room / RGB 订阅视图
        +-- trilink.game-link    IGameLinkFactory（按需有界 UDP，无后台工作）
        +-- trilink.thunder      按需像素射击、2–6人合作与确定性输入同步
```

无商业服务器依赖。真实页面通过 `INodeSessionService` 提交意图和订阅快照，协调器通过保留的 `IHardwareCommandService` 访问 S3；发现、搜索、CAPS 与命令共用串口 owner。旧命令接口是底层兼容入口，不提供新的全局业务事务保证。Room 的易失副本由 S3 持有，RGB 经成员路由。没有公网直连、持久化共识或自动故障选举；原链路见 [真实 Room / RGB](REAL_ROOM_RGB.md)，阶段 A 接缝见 [本轮记录](RELEASE_0150.md)。

## 代码所有权

| 模块 | 源码目录 | 职责 |
| --- | --- | --- |
| 应用入口 | `src/TriLink.MinClient/` | 启动、单实例、消息循环和产品版本 |
| 宿主 | `src/TriLink.PluginHost/` | profile、manifest、服务、依赖与资源回收 |
| 契约 | `src/TriLink.Plugin.Abstractions/` | Host API、DTO、插件接口和窗口恢复消息 |
| 房间插件 | `src/Plugins/TriLink.Plugin.Rooms/` | 模拟域的房间命令、权限与副本对象；不替代真实 S3 Room |
| 串口插件 | `src/Plugins/TriLink.Plugin.Serial/` | 唯一 USB owner、发现/握手、旧业务命令和 CAPS 描述读取 |
| 演示插件 | `src/Plugins/TriLink.Plugin.Simulation/` | 注入模拟节点、控制模拟在线状态 |
| 桌面插件 | `src/Plugins/TriLink.Plugin.Desktop/` | 识别/连接指导、真实功能页嵌入、演示模式隔离、托盘通知 |
| 扩展管理插件 | `src/Plugins/TriLink.Plugin.Modules/` | 模块管理服务、按需功能注册表 |
| 可选文本插件 | `src/Plugins/TriLink.Plugin.TextTools/` | 本地文本检查，独立功能窗口 |
| 真实Room插件 | `src/Plugins/TriLink.Plugin.HardwareRoom/` | 共享 NodeSessionService 的快照、意图队列、轮询与回执；页面保留选择、确认与显示 |
| 游戏传输插件 | `src/Plugins/TriLink.Plugin.GameLink/` | 按需打开的有界UDP传输与可替换传输工厂 |
| 雷霆战机插件 | `src/Plugins/TriLink.Plugin.Thunder/` | 像素视图、固定六席/实体池、确定性引擎和逐玩家输入同步 |

文件按组件归位，历史命名空间`TriLink.Core`/`TriLink.MinClient`不代表跨目录编译。0.15.0 增量契约为 `trilink.node-session.v1` 和 `trilink.node-capabilities.v1`，Host API1.0 及唯一 USB owner 保持。共享队列与去重已移出设备控制视图，当前仍限定 Room/RGB 单节点骨干。历史1067/1034/985/934/822项分别保留，不作本轮软件数量。

## 服务与依赖

| 服务 | 提供者 | 消费者 |
| --- | --- | --- |
| `IPluginCatalog` | 宿主 | 桌面 |
| `IRoomNetwork` | 房间 | 演示、桌面 |
| `IDeviceDiscoveryService` | 串口 | 桌面、真实Room |
| `IHardwareCommandService` | 串口 | NodeSessionService；旧兼容入口保留 |
| `INodeCapabilityService` | 串口 | NodeSessionService |
| `INodeSessionService` | 真实Room | 首页和独立 Room/RGB 订阅视图 |
| `ISimulationControl` | 演示 | 桌面 |
| `IDesktopShell` | 桌面 | 应用入口 |
| `IModuleManagementService` | 扩展管理 | 桌面 |
| `IModuleFeatureRegistry` | 扩展管理 | 桌面、文本工具、真实Room、雷霆战机 |
| `IGameLinkFactory` | 游戏传输 | 雷霆战机 |

消费者在 manifest 中声明 `requiresServices`，提供者声明 `providesServices`。宿主拒绝重复提供者、缺失服务、循环依赖和未声明访问。`context.Defer()` 登记清理动作；配置失败或退出时逆序回收，消费者先于提供者停止。

桌面依赖模拟控制接口，默认 profile 始终包含模拟插件；因此单纯去掉该插件并不能得到独立的真实硬件 profile。这是后续真实节点身份拆分的一部分。

## 当前数据流

**雷霆战机0.2.0：** 按需视图 → GameSession → IGameLink → 最多5个对端。房主等所选2–6人Ready才开局，每100ms提交3tick完整输入向量，逐peer维护ACK/超时/序号并补发；所有PC独立运行同一整数引擎。固定六席和720B输入环不扩容，v2报文最高120B。游戏房主与Room leader分开，不修改S3；ESP32将来替换传输提供者，当前没有游戏USB桥接。稳定接口、容量与限制见 [雷霆战机](THUNDER.md)，实施约定见 [多人扩展](THUNDER_MULTIPLAYER_PLAN.md)。

**模拟房间：** UI 操作 -> `IRoomNetwork` -> `RoomSession` 校验与提交 -> 更新进程内各 `RoomReplica` -> Changed/Notice -> UI 线程刷新。快照副本存在内存中，未序列化到无线链路或磁盘。

**设备发现：** 启动或设备到达事件 -> 受控扫描 -> 筛选候选端口 -> HELLO/DEVICE nonce 校验 -> DeviceArrived -> 加入 UI 的节点列表并通知。

**邻居搜索：** 页面请求→共享会话合并刷新→同一 owner 的 SEARCH→共享附近MAC/RSSI快照。leader约2秒/其他约5秒轻量读，active SEARCH至少15秒追加单笔，手动完整刷新/未入Room读较重。固件500ms HELLO、1秒邻居心跳、6邻居/2秒过期与MAC peer注册不变；不触发全信道扫描/自动入Room。0.14.2本端RSSI=-51是发现历史，后续一次双PC准入有独立证据，二者不混为认证连接。业务仍经原生USB，不用CH343日志口。

**真实Room：** 首页嵌入页/独立窗口 -> 同一功能工厂 -> `INodeSessionService.Bind` 的订阅 -> 共享协调器 -> `IHardwareCommandService` -> 本机S3控制面 -> ESP-NOW -> 对端S3；OFFER/CONFIRM、票据、成员快照和ACK由S3执行。PC只读取状态和提交意图，固件校验revision/term；退出leader保留旧快照重播10秒，暂缓本机CREATE/JOIN。各页面拿到快照副本，后台读与在途状态由服务统一持有，不新增串口所有者或PC一致性副本。发现层lowest-mac不是Room leader；准入/审批仍只由当前leader进行。

**能力描述：** 共享服务通过 `INodeCapabilityService.DescribeAsync` 读取 CAPS。serial1.3.0 先依据 HELLO 的 bit7 判断描述能力，支持时在唯一 owner 内 fresh HELLO 复核 MAC/所需能力并匹配回复 nonce，随后严格解析版本、boot、route body 上限、邻居/成员上限和模块位。旧固件返回明确的 legacy 观察，未知版本/boot/预算留空或零；不发送不支持的 CAPS、不推测新能力。

**RGB：** 模块显式提交 -> 成员路由 -> 目标单任务worker -> 单灯RMT -> 应用结果回执。当前S3无DMA且一次最多一个事务在途，提交不等待队列空位、完成等待每事务100 ms。驱动故障锁存后不再发送新灯数据；清理失败保留资源、不重复删除，物理LED状态未知。

**本机RGB用例：** 明确硬件许可（不点灯） -> 专用本机双闪（不依赖成员选择） -> inactive时明确确认创建/复核一人Room -> 一次RGB提交 -> 绑定原QUEUED应用ID与MAC/room/incarnation/revision/term/目标boot -> 12秒预算只读跟踪RGBRESULT -> ROOMGET00快速会话核验 -> 显示终态或未知。新建Room保留，不自动LEAVE；已有Room不改成员。追踪失败/超时/取消不重发写命令，取消读操作不保证板端受理动作已撤销；驱动完成、无线ACK与光学观察分层记录。

在途RGB的去重键为generation/local MAC/physical target，不把Room/incarnation/boot或名称变化当作新的同目标点击；因此单机CREATE后的第二次明确点击仍不会排入第二个同目标灯效。该键只用于重复意图识别：出队时仍复核Room/目标boot/角色/许可，不能沿旧上下文发送；不同物理目标与许可禁用并不被合并。

本机ROOMGET的状态字节含RGB故障位bit6（64）和退出公告位bit7（128），只用于本机显示；它们不是设备能力位，也不是空口协议升级。故障提示建议协调退出Room后重启；退出公告提示等待10秒期限后刷新。

后台扫描通过单飞保护避免重入；连续失败触发暂停并关闭定时源，外部设备事件不能绕过暂停。扫描、搜索和业务命令共用串口访问锁，业务前进行身份复核和nonce校验；真实模块关闭、手动暂停或切换端口中止后续查询批次，不重放结果未知的写命令。

页面仍有无设备/不兼容/需刷新/就绪/忙/失效状态，但不再用全批次完成或后台忙统一禁止合法前台动作。有效身份与操作所需快照可产生意图，捕获点击时名称、目标、COM/MAC/PNP/generation、Room/incarnation与票据，适用时绑定boot/角色。同一绑定节点共享FIFO8待项＋在途另计，跨页去重/队满可见；阶段 A 拒绝同时绑定另一目标。出队真实快读，允许同incarnation采用最新revision，但旧票据/重启目标/换Room或退出角色变化取消。审批/拒绝等待6秒、其他15秒；权限/容量/未知/许可仍校验，PC无法消除仅MAC审批命令的板端TOCTOU。

共享设备事件回到服务创建时的桌面同步上下文，首次recognized+visible只读刷新；同一服务pump前台优先、后台合并且在一笔已开始I/O返回后让路。leader轻量约10笔、其他活动成员约3笔，active SEARCH≥15秒追加；重刷新约22笔，申请区可经状态锚点先发布，写后ROOMGET00权威状态不被旧批次覆盖。各订阅的刷新请求合并，兼容 RefreshAll 不得绕过pump启动并行批次；跨页共享的是单节点应用协调，不是多节点分布式事务锁。

共享暂停/换板/服务销毁/未知清待项与light/full刷新标志，第五次自动失败先清两类合并读意图再停timer。显式恢复才清失败计数并立即读；清空待项不撤销在途。关闭某页释放订阅与其独占未提交意图，其他页面拥有的共享意图保留，已提交 RGB 回执跟踪由服务继续。RGB仍12秒原ID跟踪，phase5是应用未知，清相关队尾并失效，不计USB失败或重放。完整ex.ToString诊断保留。全部订阅隐藏时停止周期读；新开窗口或设备事件不能复活手动/失败暂停，不承诺强行中断OS调用或8秒期限内每次审批成功。

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

0.15.0 使用独立入口 `artifacts/NodeConsole-0.15.0/TriLink.MinClient.exe`、ZIP `artifacts/packages/TriLink-NodeConsole-0.15.0-Release.zip` 和证据根 `artifacts/tests/NodeConsole-0150-StageA/`。隔离构建冻结 `src/tests/tools/VERSION` 全部输入，打包器复核输入、运行文件与 ZIP 每项 SHA；具体测试数量和交付收据见 [本轮记录](RELEASE_0150.md)。PC 包只含客户端运行文件，固件构建通过独立收据关联，未刷写设备。PC2 先从托盘退出旧实例，再整套解压使用，不混旧 DLL。

历史0.14.4独立入口`artifacts/NodeConsole-0.14.4/TriLink.MinClient.exe`、ZIP`artifacts/packages/TriLink-NodeConsole-0.14.4-Release.zip`已校验：91输入、22便携＋3定位快捷方式、九插件清单，ZIP22项/158299B；SHA与细目见历史交付收据 (historical local evidence, not included: `../artifacts/tests/NodeConsole-Timing-0144/delivery-receipt.json`)。历史证据根`artifacts/tests/NodeConsole-Timing-0144/`包含构建、回归、同场景负例/复测和长测，冻结副本原样保留，不是当前部署入口。

历史0.14.4普通软件1067项/配置：Core136、Serial126、HardwareRoom225、Modules64、Thunder444、Desktop34、Smoke/Guard38；硬件页使用fake服务。构建UI smoke固定desktop+safe-mode+demo防止自动访问真实串口，正常启动行为不改。另用同一v21 fake探针对照，旧0.14.3正常/20×都在两项失败，补丁版分别12场景/132断言与12场景/130断言零失败，QPC约1.0008/19.9970。长测26项零失败、30.0379逻辑分钟/90.113真实秒，三次暂停恢复、最大I/O并发1/零队尾/零写，倍率恢复1×；资源仅观测、不证明零泄漏。这些不计入1067或宣称全部时间API均匀加速/真实无线耐久，见 [历史时序记录](TIMING_PATCH_0144.md)。

历史0.14.3实际入口及包原样保留：91输入匹配、22便携＋3定位快捷方式、ZIP22项/154881B逐SHA通过；收据/哈希见 [队列收束](QUEUE_CLOSURE_0143.md)。COM_PORT/Board1共享owner只读31/31、忙时刷新/端口更新合计2ms和原Room/许可保留是该轮历史单次观察，不证明0.14.4前台写队列、默认人工审批时效或性能。早期V1有限20×检查也不替代后续v21扩展覆盖。

历史0.14.2每配置985项：Core136、Serial126、HardwareRoom145、Modules64、Thunder444、Desktop34、Smoke/Guard36。真实COM_PORT/Board1/CAP99在已有Room16/incarnation44360405、许可false的基线上，实际首页工厂+共享owner不选成员提交一次`00000001`；phase1→2自动回显，恢复许可false，原Room保持，未CREATE/LEAVE或固件变更。inactive确认创建分支当时仅fake覆盖；后续两PC一次准入由本机仪器+远端用户确认，仍不证明新版默认审批时序、继任、光学或用户桌面。旧包22项/151286B保持，与后来0.14.4或当前0.15.0源码不混用；0.14.1/934项继续保留[历史记录](CLIENT_CLOSURE_20261009.md)。

## 后续扩展入口

阶段 A 的共享会话、CAPS 和内建 RGB 分发已接入；雷霆战机仍是 PC/UDP 示例。阶段 B–E 再实现小文本 P2P、连续数据通道、内建描述/有限 RAM 任务数据对齐以及低延迟状态策略。新页面复用会话服务，S3 处理无线分片、流控与内建执行器；大文件源/接收文件在 PC，板端仅有界易失暂存，不新增 Flash 资源库。真实桌面、多板无线与压力验收独立记录，OTA 仍另行设计。详细边界见 [节点控制台](NODE_CONSOLE.md)、[后续开发计划](ROADMAP.md)和 [插件平台](PLUGIN_PLATFORM.md)。
