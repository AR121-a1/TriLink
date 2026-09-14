# 目录与文件归类

整理范围为当前 `trilink_min_client` 客户端。旁边的 ESP32 固件工程以及工作区根目录的资料、临时研究材料分别属于其他工作，未纳入本次迁移。

## 日常使用与分发

`artifacts/Release/` 是完整的运行目录。使用其中的两个快捷方式启动演示或硬件模式；分发时复制整个目录，不要只复制 EXE。EXE 路径保持原样，已有指向它的快捷方式仍然有效。快捷方式包含本机绝对路径；复制到其他电脑或位置后，应直接启动 EXE，或重新创建指向新位置的快捷方式。

```text
trilink_min_client/
├─ README.md                         使用入口与项目状态
├─ TriLink.MinClient.sln             开发解决方案
├─ src/                             系统源码
│  ├─ TriLink.MinClient/             启动入口与单实例
│  ├─ TriLink.PluginHost/            加载、依赖、服务与生命周期
│  ├─ TriLink.Plugin.Abstractions/   接口、共享模型和消息契约
│  ├─ Plugins/
│  │  ├─ TriLink.Plugin.Desktop/     窗体、托盘与桌面入口
│  │  ├─ TriLink.Plugin.Serial/      串口适配、USB 协议和轮询策略
│  │  ├─ TriLink.Plugin.Rooms/       房间、副本和节点状态
│  │  └─ TriLink.Plugin.Simulation/  可选择的三节点演示功能
│  └─ profiles/                     插件组合的源配置
├─ tests/                           测试源码和运行说明
├─ tools/                           构建、插件校验、发布检查与快捷方式生成
├─ docs/                            技术与使用文档
└─ artifacts/                       自动生成的文件（不入 Git）
   ├─ Release/                      干净的客户端运行目录
   │  ├─ TriLink.MinClient.exe
   │  ├─ TriLink.Plugin.Abstractions.dll
   │  ├─ TriLink.PluginHost.dll
   │  ├─ 启动 TriLink（三节点演示）.lnk
   │  ├─ 启动 TriLink（真实硬件）.lnk
   │  ├─ profiles/
   │  └─ plugins/<plugin-id>/        每个插件独立 DLL 与清单
   ├─ tests/Release/
   │  ├─ bin/                       测试 EXE 与测试使用的依赖副本
   │  ├─ ui/                        自动 UI 截图及其失败报告
   │  ├─ logs/                      核心与桌面生命周期回归日志
   │  └─ fixtures/                  发布目录检查等验证使用的样本
   ├─ build/Release/plugins/        插件编译暂存文件
   └─ archive/                     旧截图、日志、测试副本及迁移清单
```

Debug 构建使用对应的 `Debug` 目录，与 Release 分开。

## 归类约束

- 插件实现保存在所属插件的源码目录，宿主目录不再放窗体或串口代码；项目文件不再跨目录链接这些实现。
- 既有 C# 命名空间保持不变，避免文件整理引入服务契约、类型名或插件入口的兼容性变化。
- `trilink.simulation` 是用户可用的演示功能插件，不是测试执行器，故仍属于系统。自动断言、测试 EXE 和篡改样本只进入测试或归档目录。
- 测试依赖运行目录时显式传入路径，不依赖“测试恰好放在 EXE 旁边”。
- 历史材料迁移到带时间戳的归档目录，`migration.csv` 保存原位置、新位置与 SHA-256；如需取回，应先核对目标是否存在，不覆盖现有文件。
- 完整构建和单插件构建都运行 `tools/check-release-layout.ps1`；运行目录混入截图、测试程序、暂存文件等会导致构建验收失败，不会静默删除未知文件。

## 扩展性

扩展状态：可扩展。稳定边界为 `TriLink.Plugin.Abstractions` 中的服务、DTO 和 Host API；插件实现可独立编译，`src/profiles/desktop.profile.json` 是当前组合入口。新增功能在 `src/Plugins/` 中建立插件，在 `tests/` 中增加独立验证，构建暂存和验证结果分别进入 `artifacts/build/`、`artifacts/tests/`。具体服务约束见 [插件平台说明](PLUGIN_PLATFORM.md)。
