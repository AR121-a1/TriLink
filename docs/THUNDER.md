# 雷霆战机 0.1.0

2026-10-05。本地开发插件，支持单人练习与两人合作像素射击。当前联机通过 IPv4 局域网 UDP；ESP32 游戏桥接尚未实现，不把 loopback 测试标为三板验收。

## 使用

完整构建后，从主窗口“扩展模块…”选择“雷霆战机”，点击“打开功能”。WASD / 方向键移动，默认自动射击，空格加强射击。每人三条命，阵亡后旁观；两人共享得分，敌机波次逐步加快。

两台电脑连接同一个局域网：

1. A 选择本地端口（默认 `47830`），点击“创建双人房”。
2. B 填写 A 的局域网 IPv4 地址与端口，点击“加入双人房”。需要允许客户端收发该 UDP 端口。
3. 蓝色战机属于创建者，橙色战机属于加入者。游戏房主独立于 TriLink Room leader，当前游戏会话不修改 S3 Room 成员。
4. 同机可打开两个游戏窗口：房主本地端口 `47830`；加入者本地端口 `47831`，对端地址 `127.0.0.1`、对端端口 `47830`。
5. “停止”、关闭或隐藏联机窗口会离开游戏并释放端口；重新联机需创建者重新建房、队友重新加入。单人隐藏暂停，恢复可继续。只有当前游戏画布有焦点时接收操作。

默认打开功能只绘制静态预览，不创建 socket、定时任务或联网；启动游戏后才创建会话。没有网络发现广播、互联网中继、UPnP、音效文件或图像资源包。

## 插件边界

| 插件 | 服务 / 依赖 | 职责 |
| --- | --- | --- |
| `trilink.thunder` 0.1.0 | 消费 `trilink.module-features`、`trilink.game-link` | 按需视图、确定性引擎、游戏协议和会话 |
| `trilink.game-link` 0.1.0 | 提供 `IGameLinkFactory` | 当前为无后台线程的 IPv4 UDP 适配器 |

Host API 保持 `1.0`，新增契约属于加法扩展。新功能需首次完整构建使共享 DLL、profile 与插件配套；以后可分别重建两个插件。游戏只引用 Abstractions，不引用桌面、串口或 Room 插件实现。

```powershell
.\tools\build.ps1
.\tools\build.ps1 -PluginId trilink.thunder
.\tools\build.ps1 -PluginId trilink.game-link
# 当前客户端仍占用 Release 时，可先构建独立 Debug 目录进行验证。
.\tools\build.ps1 -Configuration Debug
```

游戏更新与其他模块一样，托盘退出后重启生效。导入和模块启停沿用 ModuleStore 的校验、独立包目录与原子保存规则；不能禁用仍被游戏依赖的传输提供者。

## 资源预算

| 项目 | 固定上限 / 行为 |
| --- | --- |
| 逻辑更新 | 30 Hz、整数坐标，同 seed + 同逐 tick 输入产生同一状态 |
| 网络更新 | 10 Hz，每次提交最多 3 tick；渲染不驱动网络帧率 |
| 实体池 | 2 玩家、24 敌机、64 友弹、48 敌弹、16 爆炸；满池丢弃新效果 / 实体 |
| 引擎动态数组 | 实体字段约 5 KiB，另有对象头；Step 无逐 tick 分配 |
| 像素画布 | 固定 320×400×4 B，约 500 KiB；复用 Bitmap、Graphics、画笔和字体 |
| 补发缓存 | 120 tick × 2 B = 240 B；约 4 秒输入历史，超出窗口终止会话 |
| 应用消息 | 常规房主帧 30 B、队友输入 24 B，最大补发 88 B；适配器上限 128 B |
| 常规应用载荷 | 双向合计约 540 B/s，另加 UDP/IP、握手与丢包补发开销 |
| 接收工作 | 每次 Update 最多 16 数据报；适配器单次最多丢弃 8 个无效报文 |
| UDP 缓冲 | 应用接收 129 B，OS 收发缓冲各请求 8192 B（实际值由 OS 调整） |
| 空闲 / 关闭 | 不运行游戏 timer；关闭释放 socket、timer 与 GDI 资源 |

这些是代码上限和载荷预算，不是整进程内存、CPU、无线吞吐或端到端延迟的真机测量。运行时、WinForms 双缓冲及 socket 内核资源另计。

## 协议与同步

`GameWire` 是版本 1 的二进制协议，所有整数 little-endian。24 B 头为：magic `54 46`、version、type、session u32、token u32、tick u32、input sequence u32、count u8、input u8、reserved u16=0。Frames 后跟 `count × [player0 input u8, player1 input u8]`，count 为 1–32。输入位 left=1、right=2、up=4、down=8、fire=16。

Hello 携带随机加入 token；Start 携带房主 session、同 token 和种子（tick 字段）；Ready 确认握手。Start/Ready 重发不会重置已开始的游戏。固定对端后按端点、session、token 分别过滤。Input 的 tick 是队友已应用的最高 tick，sequence 用于拒绝过期操作状态。

房主每 100 ms 采样双方最新输入，推进 3 tick，记录有界历史，再发送从已确认 tick 的下一帧开始的连续批次。队友只应用覆盖“下一个 tick”的输入，重复部分跳过，未来缺口包不推进；房主继续补发。队友输入超过 300 ms 未更新时，房主采用中立输入。5 秒无有效通信终止，补发历史耗尽也终止，不跳过已提交 tick 或另选房主。

GameOver 后网络会话继续接收确认与补发最后一帧，直到停止 / 离开。Bye 为尽力通知，丢失后由超时释放另一端会话。没有中途加入、自动重连、主机迁移、状态快照重同步或跨进程存档。首版通过 100 ms 输入批次换取小载荷，尚未实现本地预测；不是竞技低延迟模式。

引擎规则和输入批次定义是同步协议的一部分；改变敌机生成、碰撞、伤害或 tick 规则时必须同步升级 `GameWire.Version`，让不同规则的客户端在握手前明确拒绝报文。

## 后续 ESP32 接入

保持游戏引擎和 `GameWire` 不变，新增替代 `trilink.game-link` 的服务提供者，实现 `IGameLinkFactory / IGameLink`：

1. 适配器规范化自身目标地址（如 MAC），接收时使用相同标识；`Open` 的数字由适配器解释为本地端点 / 通道。当前 UDP 地址规则不进入游戏协议。
2. 与现有共享 USB 访问所有者协调，定义专用、有界的游戏发送 / 接收桥接及能力位。当前固件与 `IHardwareCommandService` 只允许 Room/RGB 命令，不能把游戏数据塞进这些命令。
3. S3 仅路由最高 88 B 的游戏报文；画面、实体模拟、补发历史留在 PC，不向 S3 下发资源文件。不能另建独占 COM 连接与原扫描器争用。
4. 原生 CDC / ESP-NOW 上测量双向 10 Hz 的吞吐、延迟、丢包和队列峰值。若实际链路预算更低，应调整双方约定并升级协议版本，不静默改变确定性 tick。
5. 原服务提供者和 ESP32 提供者只能启用一个，沿用宿主的重复服务保护。游戏房主由游戏会话选择，Room leader 仍仅负责成员关系。

当前 token 用于区分会话和误投报文，不提供认证或加密；首版面向实验局域网。

## 验证

`tests/TriLink.Thunder.Tests` 覆盖确定性、碰撞与池容量、协议拒绝、丢包 / 重复 / 重排恢复、握手重试、终局补发、会话隔离、断线 / 历史耗尽、socket 边界与释放，以及视图渲染和隐藏 / 关闭生命周期。测试使用内存故障适配器及 IPv4 loopback，不接真实 COM。

构建将截图输出到 `artifacts/tests/<Configuration>/ui/ui-thunder-*.png`，日志写入 `logs/thunder-tests.log`。独立进程 UDP 测试验证两个客户端世界一致；跨两台实体电脑、ESP32 无线和用户桌面操作仍须单独验收。

2026-10-05 上传前验证：游戏分支以最新主干串口1.2.1、真实Room/RGB0.2.1修复为基线，独立源码快照的完整Debug构建通过。核心136、SerialLifecycle65、HardwareRoom47、模块53、桌面30、雷霆战机136及内置选择22项，共489项检查；九插件加载、哈希、运行目录与UI渲染通过。SDK/Visual Studio解决方案构建为0警告、0错误。双进程loopback完成90 tick并获得相同完整世界hash。验证未覆盖正在运行的客户端DLL；在源码目录重新构建前，请先从托盘退出旧实例，再启动生成的 `artifacts/Debug/TriLink.MinClient.exe`。
