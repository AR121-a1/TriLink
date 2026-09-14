# TriLink 插件平台

状态：**可扩展；宿主 API 1.0；插件更新边界为重启加载。**

## 设计依据与取舍

本平台借鉴 DeepSeek Harness/Cordis 的架构约束，而不是照搬其 Node.js 实现：

- [DeepSeek Harness 架构](https://github.com/deepseek-ai/deepseek-harness/blob/master/docs/architecture.md)：能力进入插件树，宿主只负责组合和生命周期；
- [Services and dependencies](https://github.com/deepseek-ai/deepseek-harness/blob/master/docs/user/develop/framework/service.md)：消费者依赖稳定服务契约，不依赖具体提供者；
- [Lifecycle and effects](https://github.com/deepseek-ai/deepseek-harness/blob/master/docs/cordis-tutorial/02-lifecycle-and-effects.md)：插件注册的服务和资源都是可逆 effect，卸载时逆序撤销；
- [Three-role capability design](https://github.com/deepseek-ai/deepseek-harness/blob/master/docs/user/develop/practice/index.md)：服务定义、提供者、消费者分离；
- [CLI/plugin management](https://github.com/deepseek-ai/deepseek-harness/blob/master/apps/cli/reference/README.md)：profile 负责组合，插件包更新后以重启作为安全边界。

TriLink 是小型 Windows/ESP32 项目，故不引入 Cordis、Node.js、包管理器或 Web 服务。实现保留同样的结构原则，但使用约 60 KB 的 .NET Framework 微内核和独立 DLL。

## 运行时结构

```text
TriLink.MinClient.exe                 只做单实例、profile 选择、启动和退出
  ├─ TriLink.Plugin.Abstractions.dll 稳定 ABI、服务接口、共享 DTO
  ├─ TriLink.PluginHost.dll          清单、依赖图、服务容器、effect 回滚
  ├─ profiles/desktop.profile.json   本次运行选择的插件组合
  └─ plugins/
      ├─ trilink.rooms/              Room、leader、对等副本状态机
      ├─ trilink.serial/             USB CDC 识别、握手、受控轮询
      ├─ trilink.simulation/         三节点数据源和模拟控制
      └─ trilink.desktop/            WinForms、托盘、插件状态页
```

宿主不会创建 Room、串口 watcher、模拟节点或窗体。它只加载 profile，验证每个清单和 DLL 哈希，按服务依赖拓扑配置插件，再按同一顺序启动；停止时反向执行。

## 服务契约

稳定服务 ID 定义在 `TriLink.Plugin.Abstractions`，当前包括：

| 服务 ID | 契约 | 当前提供者 | 消费者 |
| --- | --- | --- | --- |
| `trilink.plugin-catalog` | `IPluginCatalog` | 宿主 | 桌面插件 |
| `trilink.room-network` | `IRoomNetwork` | Room 插件 | 模拟、桌面插件 |
| `trilink.device-discovery` | `IDeviceDiscoveryService` | 串口插件 | 桌面插件 |
| `trilink.simulation-control` | `ISimulationControl` | 模拟插件 | 桌面插件 |
| `trilink.desktop-shell` | `IDesktopShell` | 桌面插件 | 宿主 |

插件通过 `requiresServices` 声明注入项，通过 `providesServices` 声明输出。加载器保证：

1. 一个服务至多有一个已启用提供者；
2. 缺少服务、重复提供者和依赖环均阻止启动；
3. 插件访问未声明的服务或未提供清单承诺的服务时，启动失败并标出插件；
4. 消费者只引用 ABI 接口，因此提供者实现可独立替换。

`dependencies` 仍保留给“必须依赖某个具体插件包”的少数场景；普通功能依赖应优先声明服务。

## 生命周期与可逆 effect

插件在 `Configure` 中使用：

```csharp
context.Provide<IMyService>(service);
context.Defer(() => resource.Dispose());
```

服务注册和 `Defer` 清理统一归属当前插件 scope。启动失败、正常退出或配置回滚时，scope 按注册的逆序撤销；依赖插件先停，提供者后停，避免容器中留下已释放的对象。

当前生命周期是：

```text
Discovered → Configured → Active → Stopped
                    └────→ Failed（随后回滚 effect）
```

## 清单与完整性

每个插件目录只有一个 DLL 和一个 `plugin.json`。构建时会把 DLL 的 SHA-256 写入部署清单；启动和 `pluginctl` 都重新计算并核对。哈希用于发现损坏、错拷和 DLL/清单版本不配套，不等同于发布者签名。

关键清单字段：

```json
{
  "schemaVersion": 1,
  "id": "trilink.serial",
  "version": "1.0.0",
  "hostApi": "1.0",
  "enabled": true,
  "entryAssembly": "TriLink.Plugin.Serial.dll",
  "entryType": "TriLink.Plugins.Serial.SerialPlugin",
  "dependencies": [],
  "requiresServices": [],
  "providesServices": ["trilink.device-discovery"],
  "capabilities": ["usb-cdc", "serial-search"],
  "sha256": "构建时生成"
}
```

## 构建和只更新一个功能

完整构建、测试清单/依赖、运行 88 项逻辑检查并渲染两个 UI 门：

```powershell
.\tools\build.ps1
```

以后只修改既有插件时，可只重建该插件：

```powershell
.\tools\build.ps1 -PluginId trilink.rooms
.\tools\build.ps1 -PluginId trilink.serial
.\tools\build.ps1 -PluginId trilink.simulation
.\tools\build.ps1 -PluginId trilink.desktop
```

单插件构建流程为：在 staging 中编译同名程序集、替换该插件 DLL、重写其 SHA-256、验证完整 profile 的依赖图，再用独立截图进程实际加载全部插件。它不会重新编译宿主或其他插件。

查看和验证当前部署：

```powershell
.\tools\pluginctl.ps1 -Action List
.\tools\pluginctl.ps1 -Action Validate
```

客户端运行中更新 DLL 可能被系统拒绝；应先从托盘退出，更新后重新启动。第一版不承诺热卸载：.NET Framework 4.8 不能单独卸载默认 `AppDomain` 中的程序集，强行覆盖会留下旧类型和事件订阅。若未来要运行不受信任的社区插件，应新增“插件进程 + 命名管道 RPC”隔离层，而不是扩大当前进程内 ABI。

## 新增或替换插件

新增可替换能力时按以下顺序：

1. 在 `TriLink.Plugin.Abstractions` 定义小而稳定的请求/结果 DTO 与服务接口，并赋予唯一 `PluginService` ID；
2. 在独立目录实现 `ITriLinkPlugin`，通过 `context.GetRequired<T>()` 消费服务；
3. 通过 `context.Provide<T>()` 提供服务，通过 `context.Defer(...)` 登记外部资源清理；
4. 在 `plugin.json` 准确声明输入/输出服务，在 profile 中选择插件；
5. 为构建脚本增加该插件的独立编译函数，并加入缺失服务、冲突、回滚和功能测试。

下一批自然扩展点是 `trilink.transport`、`trilink.text-messaging`、`trilink.resource-transfer`、`trilink.wake-provider` 和 `trilink.virtual-nic`。其中传输层可分别实现 ESP-NOW、Wi-Fi Direct、USB CDC 或未来的 USB ECM/NCM；Room 和桌面插件无需知道具体无线实现。

## 安全边界

当前插件与主程序同权限运行，SHA-256 只能校验一致性，不能判断代码是否可信。因此：

- 只加载项目自行构建或明确审计过的插件；
- 不自动扫描用户下载目录，不在线下载安装代码；
- 清单拒绝路径穿越，只接受插件目录内的单一 DLL 文件名；
- 任一插件失败时整棵树失败并回滚，不降级为未知的半工作状态。
