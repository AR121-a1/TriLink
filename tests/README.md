# 测试目录

测试源码与系统源码分离；测试二进制、日志和截图不进入客户端发布目录。

| 目录 | 覆盖范围 |
| --- | --- |
| `TriLink.Core.Tests/` | 房间、成员与 leader 规则、协议、轮询熔断、插件服务与生命周期约束 |
| `TriLink.Desktop.Tests/` | X 隐藏、托盘打开、托盘退出、关机事件放行 |
| `TriLink.Modules.Tests/` | 包导入、启停持久化、恢复、依赖、容量、完整性、功能与管理窗口 |
| `TriLink.ModuleProbe/` | 全新进程验证有效启用集合与实际程序集加载位置 |
| `TriLink.HardwareRoom.Tests/` | Room wire/UTF-8、能力筛选、受控查询、5次熔断、关闭取消批次、fake USB UI 渲染 |

2026-09-17：新增真实 Room 模块软件测试（服务为 fake，不打开真实 COM）；完整构建与 `-PluginId trilink.hardware-room` 均会运行。对应截图为 `ui-hardware-room-fixture.png`，不得标注为真实设备截图。

2026-09-16：核心共 136 项，包含六成员上限、排队批准防超额、空位重试、副本/继承，以及在线候选端口筛选、手动端口边界、搜索 END/超时判定；
桌面测试增加普通模式空设备、模拟显式启停、真实列表隔离/清空、断开、暂停/恢复及工具栏布局。
这些桌面状态使用模拟发现适配器，不冒充真实 USB 验收。

完整构建会运行核心、插件加载、UI 渲染及桌面生命周期检查：

```powershell
.\tools\build.ps1
```

只更新桌面插件也会执行 UI 和生命周期回归：

```powershell
.\tools\build.ps1 -PluginId trilink.desktop
```

构建后可以从工程根目录单独复跑：

```powershell
.\artifacts\tests\Release\bin\TriLink.Core.Tests.exe
.\artifacts\tests\Release\bin\TriLink.Desktop.Tests.exe .\artifacts\Release
.\artifacts\tests\Release\bin\TriLink.Modules.Tests.exe .\artifacts\Release
.\tools\check-release-layout.ps1

# 可选本机只读检查：调用真实 SetupAPI，但不打开任何 COM，也不发握手
.\artifacts\tests\Release\bin\TriLink.Core.Tests.exe --serial-inventory
```

结果位于 `artifacts/tests/Release/` 下的 `bin/`、`ui/`、`logs/`，发布目录检查等验证样本放在 `fixtures/`。旧测试副本和篡改验证材料位于 `artifacts/archive/`，不参与构建或插件发现。

单独更新 `trilink.serial` 或 `trilink.rooms` 也会重新编译并执行全部核心回归。

单独更新 `trilink.desktop`、`trilink.modules`、`trilink.text-tools` 会运行模块及桌面测试。
模块夹具位于 `artifacts/tests/Release/fixtures/modules-<GUID>/`，保持原始 Release 配置不变；
不执行不可信 DLL，不打开测试之外的应用或创建自启动。截图为自动窗体渲染，不能代替用户交互桌面验收。

2026-09-15 最终记录：核心 101 项、模块 53 项、桌面 30 项自动检查通过。
桌面回归额外覆盖主窗口入口、管理窗口独立关闭、最小宽度，以及托盘隐藏 / 恢复 / 退出时的嵌套功能窗口。

上述属于自动化检查，不证明用户的交互桌面可见，也不代表真实 ESP32 已通过验收。真实桌面启动应从用户的资源管理器双击运行目录里的快捷方式；不要把自动化沙箱桌面内的窗口句柄作为交互桌面证据。
