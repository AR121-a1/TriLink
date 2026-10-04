# 测试目录

2026-10-05 雷霆战机分支：在最新主干修复基线上完整Debug构建通过，Core136、SerialLifecycle65、HardwareRoom47、Modules53、Desktop30、Thunder136及内置选择22项，共489项。九插件/profile加载、UI渲染与运行目录检查通过；游戏双进程loopback状态一致，不替代实体PC或ESP32验收。

2026-10-01 review修复：串口1.2.1，全量Core136/SerialLifecycle65/Modules53/Desktop30/HardwareRoom47通过，内置构建选择22项通过。新增回归只用fake串口或TEMP元数据；UI实际渲染与自动托盘测试固定内置安全组合，演示启动不自动扫描真实COM。定向串口构建其余21运行文件哈希不变；详细证据见 [修复记录](../docs/REVIEW_FIXES_20261001.md)。

2026-10-01：硬件模块0.2.1。本轮完整客户端检查为核心136、模块53、桌面30、HardwareRoom45项；新增退出公告提示后，定向HardwareRoom47项通过，包含RGB故障状态与退休状态解析/提示，manifest7/profile/UI和运行目录检查通过。截图与服务均为fake USB夹具，不打开真实COM或执行GPIO，也不作为交互桌面验收。

2026-09-22：HardwareRoom42项，包含入群结果未知展示、无有效成员操作、手动重试只发送kind20；新增`.uncertain.png`夹具图。核心136、模块53、桌面30项保持通过，全部不替代真机验收。

测试源码与系统源码分离；测试二进制、日志和截图不进入客户端发布目录。

| 目录 | 覆盖范围 |
| --- | --- |
| `TriLink.Core.Tests/` | 房间、成员与 leader 规则、协议、轮询熔断、插件服务与生命周期约束 |
| `TriLink.SerialLifecycle.Tests/` | 实际watcher+fake I/O：缓存刷新、身份失效、暂停/释放/快速恢复、排队/握手/打开中取消、熔断和demo不自动扫描 |
| `TriLink.Desktop.Tests/` | X 隐藏、托盘打开、托盘退出、关机事件放行 |
| `TriLink.Modules.Tests/` | 包导入、启停持久化、恢复、依赖、容量、完整性、功能与管理窗口 |
| `TriLink.ModuleProbe/` | 全新进程验证有效启用集合与实际程序集加载位置 |
| `TriLink.HardwareRoom.Tests/` | Room wire/UTF-8、能力筛选、受控查询、5次熔断、关闭取消批次、入群未知/人工重试、RGB故障与退出公告提示、fake USB UI 渲染 |
| `TriLink.Thunder.Tests/` | 确定性引擎、碰撞/固定池、报文边界、故障恢复、双进程UDP、游戏窗口生命周期与渲染 |

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
.\tests\TriLink.SerialLifecycle.Tests\run.ps1
# 此选择回归使用.NET Framework，在Windows PowerShell执行；不启动GUI或真实串口
powershell.exe -NoProfile -File .\tools\test-built-in-smoke.ps1
.\artifacts\tests\Release\bin\TriLink.Thunder.Tests.exe .\artifacts\Release .\artifacts\tests\Release\ui
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
