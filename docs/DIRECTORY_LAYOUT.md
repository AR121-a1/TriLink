# 目录与文件归类

Public repository layout (2026-10-15): the client source is at the root, firmware source is in `firmware/`, and selected packages plus derived validation records are in [releases/v0.16.0/](../releases/v0.16.0/README.md). The local `artifacts/` tree shown below documents generated and historical developer output; it is not included wholesale in the public checkout. Earlier sibling-workspace names are historical.


Detailed current guidance: [Architecture 0.16.0](ARCHITECTURE_0160.md) and [Development Guide](DEVELOPMENT_GUIDE_0160.md).

The public client project is the repository root, version `0.16.0-local`; its original local workspace directory was `work/trilink_game_branch/`. hardware-room `0.6.0` provides the shared node session; serial/desktop are `1.4.0`. Firmware `0.16.0` is in `firmware/` and remains separate from the PC runtime/ZIP. See the [release record](RELEASE_0160.md) for the original build baseline and the publication addendum.

## 日常使用与分发

For public use, extract [the complete PC package](../releases/v0.16.0/TriLink-NodeConsole-0.16.0-Release.zip) and run `TriLink.MinClient.exe`. The original local runtime was `artifacts/NodeConsole-0.16.0/`; its three machine-specific shortcuts are not in the ZIP. Test material, user `module-data` and firmware images are also excluded from that PC ZIP. Exit the old tray instance before using the new directory; do not copy only an EXE or one DLL.

`tools/build.ps1` 的常规输出仍为 `artifacts/Release/`／`Debug/`。本轮使用 `tools/build-isolated.ps1` 在短 TEMP 路径冻结 `src/tests/tools/VERSION` 全部输入，生成 `build-inputs.json` 并完整构建；`tools/package-local.ps1` 复核当前源码、快照、日志和运行文件后，生成独立运行目录与交付证据。隔离 stage 和历史冻结 build-stage 是构建证据，不是日常入口。运行后生成的 `module-data/` 是 PC 模块配置与导入包，不是板端资源库。

```text
TriLink/
├─ README.md                         使用入口与项目状态
├─ TriLink.MinClient.sln             开发解决方案
├─ src/                             系统源码
│  ├─ TriLink.MinClient/             启动入口与单实例
│  ├─ TriLink.PluginHost/            加载、依赖、服务与生命周期
│  ├─ TriLink.Plugin.Abstractions/   接口、共享模型和消息契约
│  ├─ Plugins/
│  │  ├─ TriLink.Plugin.Desktop/     窗体、托盘与桌面入口
│  │  ├─ TriLink.Plugin.Serial/      唯一USB owner、发现/旧命令/CAPS
│  │  ├─ TriLink.Plugin.Rooms/       模拟域房间、副本和节点状态
│  │  ├─ TriLink.Plugin.Simulation/  三节点演示功能
│  │  ├─ TriLink.Plugin.Modules/     扩展管理与功能注册服务
│  │  ├─ TriLink.Plugin.TextTools/   可选的文本工具功能
│  │  ├─ TriLink.Plugin.HardwareRoom/ 共享NodeSessionService与Room/RGB订阅视图
│  │  ├─ TriLink.Plugin.GameLink/    按需有界游戏传输
│  │  └─ TriLink.Plugin.Thunder/     像素战机引擎、会话与视图
│  └─ profiles/                     插件组合的源配置
├─ tests/                           测试源码和运行说明
├─ tools/                           常规/隔离构建、打包、插件校验和快捷方式生成
├─ docs/                            技术与使用文档
└─ artifacts/                       自动生成的文件（不入 Git）
   ├─ NodeConsole-0.16.0/            Current complete runtime and relocated local shortcuts
   │  ├─ TriLink.MinClient.exe
   │  ├─ TriLink.Plugin.Abstractions.dll
   │  ├─ TriLink.PluginHost.dll
   │  ├─ 启动 TriLink（三节点演示）.lnk
   │  ├─ 启动 TriLink（真实硬件）.lnk
   │  ├─ 启动 TriLink（安全恢复）.lnk
   │  ├─ module-data/               首次保存配置后创建，包含独立导入包
   │  ├─ profiles/
   │  └─ plugins/<plugin-id>/        每个插件独立 DLL 与清单
   ├─ packages/
   │  ├─ TriLink-NodeConsole-0.16.0-Release.zip  Current portable package
   │  ├─ TriLink-NodeConsole-0.15.0-Release.zip  Preserved historical package
   │  └─ TriLink-NodeConsole-0.14.*-*.zip       历史包保留
   ├─ tests/NodeConsole-0160-UsbPlatform/  Current evidence; counts/hashes in RELEASE_0160.md
   │  ├─ build-inputs.json          冻结构建输入的路径和SHA-256
   │  ├─ delivery-receipt.json      PC交付收据，独立引用可选固件验证收据
   │  ├─ plugin-validation.log      运行目录清单/哈希验证
   │  ├─ Release/
   │  │  ├─ bin/                   测试EXE与测试依赖副本
   │  │  ├─ ui/                    自动UI截图及失败报告
   │  │  ├─ logs/                  本轮各suite回归日志
   │  │  └─ build-Release.log       完整构建日志
   │  └─ Debug/                    对应Debug证据与完整构建日志
   ├─ NodeConsole-0.15.0/           Preserved historical runtime
   ├─ tests/NodeConsole-0150-StageA/  Preserved historical build/evidence
   ├─ NodeConsole-0.14.4/           历史运行目录，原样保留
   ├─ NodeConsole-0.14.3/           历史运行目录，原样保留
   ├─ tests/NodeConsole-Timing-0144/ Historical timing evidence, not 0.16.0 acceptance
   ├─ tests/NodeConsole-Queue-0143/  历史队列/只读证据，原样保留
   ├─ Release/                     常规原位构建输出，不是当前日常入口
   ├─ tests/Release/               常规原位测试bin/ui/logs/fixtures
   ├─ build/Release/plugins/        插件编译暂存文件
   └─ archive/                     旧截图、日志、测试副本及迁移清单
```

Debug 构建使用对应的 `Debug` 目录，与 Release 分开。构建/测试材料和固件独立验证收据不进入 PC ZIP；0.16.0 没有文本/文件收发或板端持久化库。

## 归类约束

- 插件实现保存在所属插件的源码目录，宿主目录不再放窗体或串口代码；项目文件不再跨目录链接这些实现。
- 既有 C# 命名空间保持不变，避免文件整理引入服务契约、类型名或插件入口的兼容性变化。
- `trilink.simulation` 是用户可用的演示功能插件，不是测试执行器，故仍属于系统。自动断言、测试 EXE 和篡改样本只进入测试或归档目录。
- 测试依赖运行目录时显式传入路径，不依赖“测试恰好放在 EXE 旁边”。
- 历史材料迁移到带时间戳的归档目录，`migration.csv` 保存原位置、新位置与 SHA-256；如需取回，应先核对目标是否存在，不覆盖现有文件。
- 完整构建和单插件构建都运行 `tools/check-release-layout.ps1`；运行目录混入截图、测试程序、暂存文件等会导致构建验收失败，不会静默删除未知文件。
- 共享契约及配套插件同时更新时完整隔离构建；单插件构建不会重编 Abstractions，不能用于首次跨契约升级。打包器拒绝覆盖已有运行目录、ZIP或证据根。
- Historical 0.15.0/0.14.x packages, receipts, frozen inputs, counts and time-acceleration results remain unchanged. Current counts and hashes are recorded in [RELEASE_0160.md](RELEASE_0160.md) and its machine receipts.

## 扩展性

扩展状态：可扩展。稳定边界为 `TriLink.Plugin.Abstractions` 中的服务、DTO 和 Host API；阶段 A 已提供 `trilink.node-session.v1` 与 `trilink.node-capabilities.v1`，真实 Room 和模拟 `IRoomNetwork` 保持分离。`src/profiles/desktop.profile.json` 是当前组合入口。实现和验证按组件归位，隔离构建材料与最终运行文件分离；后续 B–E 通过明确的新能力接入，不因存在插件目录就宣称文本/文件/实时功能已完成。服务约束见 [插件平台说明](PLUGIN_PLATFORM.md)和 [当前交付](RELEASE_0160.md)。
