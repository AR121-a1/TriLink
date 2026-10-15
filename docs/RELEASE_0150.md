# 0.15.0 节点模块与共享会话

日期：2026-10-11（Asia/Shanghai）。本版本落实 [职责方案](P2P_PLATFORM_RESPONSIBILITIES.md) 的阶段 A：公共底座。客户端版本 `0.15.0-local`，固件版本 `0.15.0`；后续文本、连续文件流、资源对齐与实时子模式仍按 B–E 推进。

## 固件模块边界

新增 `trilink_app` 固定4槽注册表，统一模块标识、版本、长度校验和 receive/tick 分发。首个模块为已有 RGB，本机与远端路径均使用分发器；Room 权限、成员 boot、应用去重、显式许可和执行回执保持。RGB 状态暂留 bridge，后续可继续拆分内部实现；本版没有引入动态代码加载或每模块任务栈。

模块化指 C 模块的注册/调用边界，新增原生执行器仍要整包编译和烧录固件；传输数据包不会自动安装 C 代码。通用分发 tick 不依赖 PC 轮询，USB 回复队列满也不会停止已受理的 RGB 状态机。

增加 `CAPS` 只读描述，发现 bit7 表示支持该契约。契约版本1返回固件版本、boot、route body 132 B、6邻居、6成员和模块 mask。当前仅声明 `01`（RGB），不声明文本/文件/实时/OTA。

```text
请求：TRILINK/3 CAPS <nonce>
回复：TRILINK/3 CAPS <nonce> 1 0.15.0 <BOOT8HEX> 84 06 06 01
```

固件原有前导 LF 和末尾 LF 保持。旧 Room/RGB 空口与 USB 命令兼容；PC 0.15.0 遇到旧固件时不发送 CAPS，只显示已识别的旧能力，版本/boot/预算保持未知，不猜测。

## 上位机扩展入口

本版增加两个独立服务契约，Host API 1.0 的既有接口不删改：

- `trilink.node-capabilities.v1` / `INodeCapabilityService`：由串口插件提供，复用唯一 owner；新设备先做 HELLO 身份/能力复核再读取 CAPS。严格校验长度、编码、nonce、版本和预算，原始串口空白不会被 Trim 后伪装为合法描述。
- `trilink.node-session.v1` / `INodeSessionService`：由真实 Room 插件提供，持有共享队列、快照、轮询和回执跟踪；功能页按身份绑定，提交类型化意图并订阅状态。新插件在清单中声明服务依赖后复用，不自行打开 COM。

服务提供者：serial `1.3.0`、hardware-room `0.5.0`；桌面壳 `1.3.0`。仍为一个进程、九个首方插件，没有新常驻后台进程。

生产首页和独立 Room 窗口共享一个节点会话。阶段 A 只允许一个物理节点绑定；另一窗口不能偷偷切换已绑定页面的端口。FIFO 上限8待项、在途另计；同目标意图跨页去重，出队重新检查设备/Room/boot/角色/期限。页面关闭只取消其独占未提交意图，已经提交的结果由共享服务处理。停止轮询、连续五次失败和设备替换保持暂停状态，重新打开窗口不自动解锁。

PC 插件调用会话服务须使用创建服务的桌面同步上下文。DTO 是快照副本，修改副本不修改节点或共享服务。显示“已排队/已受理/结果未知”遵循原结果语义；没有新增固件 requestId 幂等查询命令，不能把 PC 队列标识当成新板端事务标识。

订阅硬上限16；重复刷新共享一个 Task，暂停/换板/关闭时取消，错误时失败完成，不留下无界等待者。CAPS 本机 boot 与本机成员条目核对，不使用 Room 的 source.boot；未入 Room 时不臆造成员身份。已知 boot 的前台意图执行前重读 CAPS，检测同 MAC 重启后清队、失效并暂停。此检查不是固件原子授权，重启仍可能发生在检查与写入之间；旧固件没有额外启动编号承诺。

新增 PC 功能页的最短接入路径：清单声明 `trilink.node-session.v1` 依赖，在 Configure 获取服务；页面用 MAC/端口 Bind，订阅 Changed 并显示 Snapshot，操作使用当前 Generation/Basis 提交 NodeOperationIntent，关窗 Dispose 订阅。不要自行创建 Timer、打开 COM 或实例化另一个生产协调器。新增固件原生业务先扩展模块/协议与能力声明，再增加 PC 对应类型化操作；不能只加 UI 按钮或下载包就宣称支持。

## 构建、交付与更新

完整运行包需包含 EXE、共享 Abstractions、宿主、九插件及 profile。新增服务契约使本轮必须整套更新；仅复制新版 DLL 到旧 Host API 1.0 目录不足以满足依赖。先从旧实例托盘右键退出，X 仍为隐藏，再启动新目录 EXE。

复现客户端构建入口：

```powershell
.\tools\build-isolated.ps1 -Configuration Both
```

脚本建立唯一短路径临时快照，核对 `src/tests/tools/VERSION` 哈希，运行全量 Release/Debug；产物与日志保留在输出的 `ISOLATED_STAGE`。新交付目录由 `tools/package-local.ps1` 拒绝覆盖式创建，便携 ZIP 不含测试、用户配置或绝对路径快捷方式。测试启动使用 fake 或 `--safe-mode --demo`。

固件独立本地构建：`../trilink_firmware/tools/build_local.ps1`；产物保持在固件工程，PC 便携包不包含固件，也不自动烧录。

## 验证状态

固件全新 ESP-IDF 5.5.5 构建、15组 C 主机测试、旧 USB 跨语言兼容和8个 chain/mesh 探针单测通过。构建 `20261011-103721-87a83230`：App 807584 B，静态 DIRAM 149191 B，注册表84 B；较保留的0.10.2包增加688 B/88 B。分区表 SHA-256 完全相同，原 App/内存/栈门限保留。

PC 串口/能力定向检查173项通过；增加真实固件 bridge 生成的 CAPS 样本验证后174项通过。覆盖新旧能力、排队期间同端口 MAC/PnP/能力替换拒绝、旧 nonce、畸形描述、协议版本和串口层空白/超长拒绝。此处“真实 bridge”指编译固件 C 实现的 host fixture，不是实板读取。

客户端全量 Release/Debug 均退出0，每套 **1153项**：Core136、Serial173、HardwareRoom264、Modules64、Thunder444、Desktop34、Smoke/Guard38。HardwareRoom 保留旧225项并新增共享会话39项；包含双页所有权、去重、1000次刷新合并、暂停/失败、真实 Room 字节布局、idle/leader/member 的 boot 处理和同 MAC 重启拒绝旧意图。当前未重跑 OpenSpeedy 或长逻辑时间探针，不挪用旧版加速结论。

独立运行目录：`artifacts/NodeConsole-0.15.0/`。便携包：`artifacts/packages/TriLink-NodeConsole-0.15.0-Release.zip`，22项、167519 B，SHA-256 `BAA92BCD9ADB4DCBC8F9A5F7125ABD1DD7BE898A6091EDF9EAC242F01FB53DFB`。98个源码/构建输入与冻结副本一致，22运行文件及3本机定位快捷方式、九插件清单、ZIP逐项 SHA 校验通过；源码快照 `<TEMP>/tl0150-a715086b` 保留，非日常入口。

正式证据根 NodeConsole-0150-StageA (historical local evidence, not included: `../artifacts/tests/NodeConsole-0150-StageA/`)：包含完整日志、测试程序、离屏渲染、输入哈希及 交付收据 (historical local evidence, not included: `../artifacts/tests/NodeConsole-0150-StageA/delivery-receipt.json`)。`cross-caps.log` 是冻结 Release 测试程序读取本次固件 C fixture 的174项结果，不加到普通1153项里重复计数。共享页和最小主窗口截图已人工查看，无新布局遮挡；离屏渲染不代表用户交互桌面验收。

旧0.14.4 ZIP SHA仍为 `7519880EC0DDEE593E4D7B228FA50BAC53250683C59EF373561A535BA83DCC3F`；旧运行目录/进程未覆盖或结束。真实桌面交互、无线/灯光和新固件硬件运行尚未验收；本轮未烧录、未上传 GitHub。
