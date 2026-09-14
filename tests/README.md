# 测试目录

测试源码与系统源码分离；测试二进制、日志和截图不进入客户端发布目录。

| 目录 | 覆盖范围 |
| --- | --- |
| `TriLink.Core.Tests/` | 房间、成员与 leader 规则、协议、轮询熔断、插件服务与生命周期约束 |
| `TriLink.Desktop.Tests/` | X 隐藏、托盘打开、托盘退出、关机事件放行 |

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
.\tools\check-release-layout.ps1
```

结果位于 `artifacts/tests/Release/` 下的 `bin/`、`ui/`、`logs/`，发布目录检查等验证样本放在 `fixtures/`。旧测试副本和篡改验证材料位于 `artifacts/archive/`，不参与构建或插件发现。

上述属于自动化检查，不证明用户的交互桌面可见，也不代表真实 ESP32 已通过验收。真实桌面启动应从用户的资源管理器双击运行目录里的快捷方式；不要把自动化沙箱桌面内的窗口句柄作为交互桌面证据。
