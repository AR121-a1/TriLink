# Windows 使用与开发环境

本工程目标为 .NET Framework 4.8。日常运行使用完整的 `artifacts/Release/`；开发使用解决方案和 `tools/build.ps1`。构建脚本会编译、部署九个插件、生成哈希、执行回归并创建指向本机目录的快捷方式。

## 必要环境

| 用途 | 依赖 |
| --- | --- |
| 运行客户端 | Windows 与 .NET Framework 4.8 或 4.8.1 运行时 |
| 构建部署目录 | Visual Studio 2022 / Build Tools 的 Roslyn、.NET Framework **4.8 Developer Pack**、Windows PowerShell 5.1（`powershell.exe`） |
| 打开 SDK 风格解决方案 | Visual Studio 2022 和兼容的 .NET SDK；本机已验证 SDK 8.0.303 |
| 可视化开发 | Visual Studio Installer 的“.NET 桌面开发”工作负载，可按需添加 |

运行时和 Developer Pack 各有用途：运行时执行程序，Developer Pack 提供构建时使用的 4.8 参考程序集。已经有 4.8.1 运行时的电脑仍可以为 `net48` 安装对应开发包。下载入口：[微软 .NET Framework 4.8](https://dotnet.microsoft.com/en-us/download/dotnet-framework/net48)。

构建脚本通过 `vswhere.exe` 发现 Visual Studio，支持 Community、Professional、Enterprise、Build Tools 及自定义安装位置。不需要把 MSBuild 或 csc 加到系统 PATH。

特殊目录可以显式指定：

```powershell
.\tools\build.ps1 -CscPath 'D:\visual studio\MSBuild\Current\Bin\Roslyn\csc.exe' `
    -FrameworkPath 'C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8'
```

构建用的参考程序集不能用运行时目录 `C:\Windows\Microsoft.NET\Framework*` 替代。四个工具脚本采用 UTF-8 BOM，读取 JSON 时指定 UTF-8，兼容 Windows PowerShell 5.1 的中文目录、清单和快捷方式名称。

## 构建与使用

在工程根目录打开 PowerShell，执行：

```powershell
.\tools\build.ps1
```

如当前终端限制脚本执行，可对这一次进程使用：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\tools\build.ps1
```

在资源管理器中打开 `artifacts\Release`，双击以下快捷方式：

- `启动 TriLink（三节点演示）.lnk`：无需硬件，演示 A/B/C 房间流程。
- `启动 TriLink（真实硬件）.lnk`：等待原生 USB 数据口连接的 S3。
- `启动 TriLink（安全恢复）.lnk`：模块配置损坏时忽略自定义配置并恢复内置模块。

关闭窗口会驻留托盘。重新编译前从托盘菜单“退出”，释放正在使用的 DLL。

真实 Room 操作入口是“扩展模块…”中的“真实 Room / RGB”。主窗口的房间按钮用于模拟演示。客户端仓库不包含配套固件；当前真实模块对应本地固件 0.10.2，真实三板验收仍待完成。连接、能力位和 RGB 启用要求见 [真实 Room / RGB](REAL_ROOM_RGB.md)。无需硬件即可开发客户端并运行软件回归。

## 编辑与调试

1. 用 Visual Studio 打开根目录的 `TriLink.MinClient.sln`，将 `TriLink.MinClient` 设为启动项目。
2. 修改插件源码后，先从托盘退出旧客户端，再用构建脚本部署修改。
3. 需要断点调试时，执行 Debug 构建，然后启动 `artifacts\Debug\TriLink.MinClient.exe` 并在 Visual Studio 中附加到该进程。

```powershell
# 首次 Debug 构建
.\tools\build.ps1 -Configuration Debug

# 后续仅更新一个插件；PluginId 也支持其余内置插件
.\tools\build.ps1 -Configuration Debug -PluginId trilink.hardware-room
```

Debug 构建关闭优化，并把 portable PDB 保存到 `artifacts\build\Debug\symbols\`。调试器可以按程序集中的 PDB 路径加载符号；如果未自动加载，在 Visual Studio“工具 → 选项 → 调试 → 符号”中添加该目录。符号不会混入运行目录。

本机 `src/TriLink.MinClient/TriLink.MinClient.csproj.user` 已配置 F5 启动对应 `artifacts/Debug` 或 `artifacts/Release` 的完整客户端，参数为 `--demo`。这个文件被 Git 忽略，只影响本机。新电脑可以在启动项目的调试属性中设置同样的外部程序路径，或使用“附加到进程”。需要硬件模式时将启动参数留空。

**每次修改后都先执行构建脚本，再 F5。** Visual Studio/.NET SDK 的普通生成只把程序集放入各项目的 `bin/`，不会部署运行所需的 profile、插件清单和哈希。

检查 IDE 的项目编译是否正常：

```powershell
dotnet build .\TriLink.MinClient.sln --configuration Debug
```

初次执行会生成 SDK 项目的 NuGet 还原元数据；构建并部署客户端的入口仍为 `tools/build.ps1`。本工程没有业务 NuGet 包依赖。修改共享接口或宿主后应执行完整构建，确保所有插件与共享 DLL 相配套。

## 回归与更新

完整构建自动执行核心、SerialLifecycle、HardwareRoom、模块、雷霆战机、桌面生命周期、内置安全选择检查、profile 加载和 UI 渲染。结果位于 `artifacts/tests/<Configuration>/`，串口与硬件模块测试使用 fake USB。内置安全选择回归由 Windows PowerShell 运行以加载 .NET Framework 程序集。具体回归入口见 [测试说明](../tests/README.md)。

本机 GitHub SSH 的 22 端口连接失败时，可以只对一次拉取转换为 HTTPS，不改全局 Git 配置：

```powershell
git -c 'url.https://github.com/.insteadOf=git@github.com:' pull --ff-only origin main
```

拉取前检查 `git status`。已有本地修改时先保留这些修改；`--ff-only` 不会创建合并提交，也不会覆盖未提交的冲突文件。
