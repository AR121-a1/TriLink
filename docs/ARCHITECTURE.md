# TriLink v0.13 架构

扩展状态：可扩展。当前是一个 Windows 进程、一个插件宿主和四个首方 DLL。界面与业务通过服务契约通信，没有额外本机 HTTP 后端或独立托盘进程。当前实际运行范围为单机模拟与串口控制入口，ESP32 固件不包含在此仓库。

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
```

数据不经过公司服务器。此处的“无服务器”描述单机演示没有网络服务依赖，不代表已实现真实三机无线传输或公网直连。

## 代码所有权

| 模块 | 源码目录 | 职责 |
| --- | --- | --- |
| 应用入口 | `src/TriLink.MinClient/` | 启动、单实例、消息循环和产品版本 |
| 宿主 | `src/TriLink.PluginHost/` | profile、manifest、服务、依赖与资源回收 |
| 契约 | `src/TriLink.Plugin.Abstractions/` | Host API、DTO、插件接口和窗口恢复消息 |
| 房间插件 | `src/Plugins/TriLink.Plugin.Rooms/` | 房间命令、权限、模拟网络与副本对象 |
| 串口插件 | `src/Plugins/TriLink.Plugin.Serial/` | 端口发现、USB 文本解析、握手与轮询策略 |
| 演示插件 | `src/Plugins/TriLink.Plugin.Simulation/` | 注入模拟节点、控制模拟在线状态 |
| 桌面插件 | `src/Plugins/TriLink.Plugin.Desktop/` | 展示节点与房间、提交命令、托盘通知 |

文件已按所属组件归位；部分命名空间保留历史名称 `TriLink.Core` / `TriLink.MinClient`，不等于仍在跨目录编译。契约程序集与插件有各自版本；产品 `0.13.0` 不改变当前 Host API `1.0`。

## 服务与依赖

| 服务 | 提供者 | 消费者 |
| --- | --- | --- |
| `IPluginCatalog` | 宿主 | 桌面 |
| `IRoomNetwork` | 房间 | 演示、桌面 |
| `IDeviceDiscoveryService` | 串口 | 桌面 |
| `ISimulationControl` | 演示 | 桌面 |
| `IDesktopShell` | 桌面 | 应用入口 |

消费者在 manifest 中声明 `requiresServices`，提供者声明 `providesServices`。宿主拒绝重复提供者、缺失服务、循环依赖和未声明访问。`context.Defer()` 登记清理动作；配置失败或退出时逆序回收，消费者先于提供者停止。

桌面依赖模拟控制接口，默认 profile 始终包含模拟插件；因此单纯去掉该插件并不能得到独立的真实硬件 profile。这是后续真实节点身份拆分的一部分。

## 当前数据流

**模拟房间：** UI 操作 -> `IRoomNetwork` -> `RoomSession` 校验与提交 -> 更新进程内各 `RoomReplica` -> Changed/Notice -> UI 线程刷新。快照副本存在内存中，未序列化到无线链路或磁盘。

**设备发现：** 启动或设备到达事件 -> 受控扫描 -> 筛选候选端口 -> HELLO/DEVICE nonce 校验 -> DeviceArrived -> 加入 UI 的节点列表并通知。

**邻居搜索：** 用户点击搜索 -> 串口发送 SEARCH -> 解析 PEER/END -> 更新邻居列表。来自远端的房间元数据目前记录日志；房间按钮仍调用本地房间服务，没有发送远端 JOIN/INVITE 消息。

后台扫描通过单飞保护避免重入；连续失败触发暂停并关闭定时源，外部设备事件不能绕过暂停。显式手动搜索有独立操作入口；串口扫描与搜索的完整访问协调、取消和异常注入仍需增强。

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

每个部署插件目录包含 DLL 与 `plugin.json`。构建生成 SHA-256，宿主启动时检查 DLL/清单一致性；哈希不提供签名或恶意代码隔离。所有插件与主程序同权限，只加载项目自己的插件。

`tools/build.ps1 -PluginId ...` 只编译指定组件，再验证完整 profile 与 UI 加载；桌面更新还执行生命周期回归。更新前退出客户端，更新后重启，不在进程内热替换已经加载的程序集。

运行文件位于 `artifacts/Release/`；测试、编译暂存、归档分别位于 `artifacts/tests/`、`artifacts/build/`、`artifacts/archive/`。发布目录检查只接受运行文件。源码仓库不提交这些生成目录。

## 后续扩展入口

先增加传输服务与真实 Room 实现，保持 UI 依赖 `IRoomNetwork` 等契约；文本、资源与小游戏再成为该传输服务的消费者。不要让 UI 直接写 ESP-NOW 帧或承担文件分片缓存。详细阶段与验收条件见 [后续开发计划](ROADMAP.md)，插件清单约束见 [插件平台](PLUGIN_PLATFORM.md)。
