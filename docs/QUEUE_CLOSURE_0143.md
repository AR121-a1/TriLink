# 0.14.3：前台操作队列与后台读取让路

后续说明（2026-10-10）：扩大到 v2.1 时序场景后，正常速度与 20× 均复现第五失败残留刷新、跨 CREATE 的 RGB 去重两项缺陷，已由 0.14.4 修复。本文保留 0.14.3 当时的验收范围与证据，不代表新扩大场景通过；前后对照见 [0.14.4 时序补丁报告](TIMING_PATCH_0144.md)。

状态：0.14.3 软件构建/回归与原生 USB 只读响应验证完成；新前台写 FIFO 的真实无线执行、默认 GUI 人工审批时序未验收。后加的 OpenSpeedy 时间加速检查单独记录，不并入软件门禁。

## 本轮范围

沿用 Windows / .NET Framework 4.8 / WinForms 插件架构，产品 `0.14.3-local`、文件版本 `0.14.3.0`，desktop `1.2.2`、hardware-room `0.4.0`、serial `1.2.2`、Host API `1.0`。没有增加公共调度框架、线程、USB 所有者、固件能力、资源包、板端任务或 OTA。

页面用同一 UI 线程上的异步 pump 串行调度：前台业务意图优先，后台读在已经开始的一笔 I/O 返回后让路。不能中断正在执行的 USB 调用，也不是并行打开串口。刷新请求合并，不无界堆积完整扫描。

## 操作语义

- 每个真实 Room 页面最多 8 个等待项，当前执行项另计；界面显示当前阶段、队列数量/动作，提供“清空待执行”。两个页面仍共用原 USB 服务，并不是跨页面全局业务事务锁。
- 保留合法性门控：有可用快照的合法操作在轮询或其他前台动作期间可排队；无设备、无角色权限、缺少选择、未获 RGB 许可等按钮仍不可执行。
- 捕获点击时的名称、目标 MAC、设备 COM/MAC/PNP/generation、Room/incarnation 和所选票据；适用时绑定成员 boot、leader/term/角色。之后改变 UI 选择不会改变旧意图的目标。
- 出队时真实快读/复核。相同 incarnation 下可以采用最新 revision 构造命令，但旧票据、重启的目标、不同 Room 或改变退出角色的意图会取消。
- 审批/拒绝等待期限为 6 秒，其他为 15 秒；等待或前台核验中已过期则不提交。重复动作去重，队满或取消有明确事件说明。
- 暂停、换板、页面销毁清除待执行项与合并刷新；已经调用 USB 命令服务即可能在途，必须等服务收尾，不能声称撤销，也不重放写命令。
- RGB 固定原应用 ID 和会话跟踪，最多 12 秒。驱动失败/目标拒绝不等于成功；phase 5 是结果未知，停止相关队列续写、使操作状态失效，保留诊断而不当作成功继续执行。应用未知不计作 USB 传输失败。

## 读取节奏与限制

active leader 为 2 秒轮询意图，其他角色为 5 秒；同一 pump 合并请求，计时不是实时保证。leader 轻量路径优先读取状态和 6 个申请槽，约 10 笔读；普通活动成员轻量路径为 3 笔读。活动 Room 的邻居 SEARCH 至少间隔 15 秒，再在可用轮询边界更新 MAC/RSSI，不无期限沿用首次扫描。

手动完整刷新和未入 Room 的发现仍读取完整相关页面，约 22 笔 I/O（含 SEARCH 和 RGBRESULT）。审批区可以先发布经状态锚点确认的批次，后台未完成的旧批次不能覆盖新的前台权威状态。前台写后先读 `ROOMGET 00`，较重合并更新交给后台。

当前固件申请窗口约 8 秒。2 秒 leader 节奏和可排队减少旧整批忙锁，但用户操作时间、无线变化和在途 RGB 跟踪仍可能让票据过期；不保证每次点击立即执行或每个申请都获及时审批。固件审批命令仍只带 target MAC，并不携带期望票据，PC 复核不能消除板端 TOCTOU。公开 `RefreshAll` 拒绝绕过 pump 的并行批次，生产按钮统一走调度入口。

## 可见异常的根因修复

用户提供的 .NET 弹窗来自开发测试 `TriLink.HardwareRoom.Tests.exe`，不是已交付的 0.14.2 客户端。暂停调用取消令牌，异步代码可能同步恢复、完成 pump 并清空 `_executingIntent`；原回调随后访问该字段产生 NullReference。

修复先保存当前动作引用、退休旧 generation 并清除两类合并刷新标志，再取消。否则仅修空引用仍可能让旧 post-refresh 在暂停后复活。异常报告及原堆栈保留；正式程序没有关闭 .NET 错误提示或更改系统 JIT。已处理的通信/执行异常显示完整 `ex.ToString()` 诊断；fake 测试未处理异常完整输出堆栈并非零退出，不允许“继续”后误判通过。

## 验证与交付

隔离短路径构建 `tlq-0143-7fb365c1`：Release、Debug 均正常退出，警告按错误处理，各 **1034** 项软件检查：Core 136、Serial 126、HardwareRoom 194、Modules 64、Thunder 444、Desktop 34、Smoke/Guard 36。硬件页 194 项使用 fake USB，保留旧测试意义并补队列顺序/容量/去重、改选目标、身份/票据/角色失效、自然取消重入、phase 5 清尾、迟到结果、两类刷新标志、节奏和低频发现等检查。6/15 秒 TTL 门禁用确定性过期夹具；后加的自然到期与 20× 隔离时钟检验另见本文末节，不并入 1034 项。

已检查 1000×860 队列等待图和 600×760 窄成员图，按钮、队列计数与状态可见。这是离屏/自动窗体检查，不等于用户交互桌面验收。

原生只读探针使用实际主表单功能工厂与同一个 USB 服务，识别 `COM_PORT / Board1`。命令包装器只允许 `ROOMGET`、`RGBRESULT` 原样转发；SEARCH 仍走真实发现服务，不构造假回复、不创建新 USB owner。后台读取中双击刷新并更新端口，三次同步提交合计 **2 ms**（本机单次观测，不是性能保证），随后暂停/手动刷新恢复；包装器实际读 **31/31** 完成，退出码 0，stderr 为空。

前后均为 `Room=16 / incarnation=44360405 / revision=2 / term=1`，成员顺序 `Board1,Board2`，本机 RGB 许可 false。没有 Room/RGB 写入，不烧写固件。最近 RGB `00000001` 驱动终态 2 是旧单板测试记录，不是本轮新执行。这里只验证真实读取下的刷新/更新端口/暂停/恢复响应与状态不变，不证明新前台写 FIFO 的真实无线执行。

探针准备曾发现两处工具脚本问题：Windows PowerShell 5 对无 BOM 脚本中的中文路径字面量解码错误（尚未访问 COM），以及 `Start-Process` 异步等待未取得退出码。均修正为从 `$PSScriptRoot` 推导路径、`-Wait` 获取并保存退出码、UTF-8 输出；前次目录保留，最终通过日志单独归档，不把脚本错误当成产品或物理设备失败。

实际交付：新版 EXE (historical local evidence, not included: `../artifacts/NodeConsole-0.14.3/TriLink.MinClient.exe`)、整套便携 ZIP (historical local evidence, not included: `../artifacts/packages/TriLink-NodeConsole-0.14.3-Release.zip`)、机器收据 (historical local evidence, not included: `../artifacts/tests/NodeConsole-Queue-0143/delivery-receipt.json`)。91 个源码/构建输入与冻结快照逐 SHA 一致；22 个便携运行文件、3 个已重新定位的本机快捷方式；ZIP 22 项 / 154881 B，逐项 hash 与 Release 一致。

ZIP SHA-256：`663FE3241114AA142C9D5A5D1A418D3A3626E24FAD325E3A3B805B36EBAF1EA9`。证据目录 `artifacts/tests/NodeConsole-Queue-0143/{Release,Debug,NativeQueue,CancellationRegression}` 与运行文件分开。整目录更新 PC2，不仅复制 EXE 或某个 DLL；先从旧实例托盘“退出”，X 只隐藏。本轮由用户退出旧实例，没有自动终止用户进程或覆盖旧运行目录。

此前 [两电脑 Room 验证](TWO_PC_ROOM_VALIDATION_20261009.md) 是 0.14.2 的一次实际审批与 PC2 用户一致确认；不当作 0.14.3 默认 GUI 审批时序通过。此前 [一次授权单板 RGB](SINGLE_NODE_CLOSURE_20261009.md) 不重复，本轮不点灯、不创建/退出/踢人、不刷写固件。

## 20× 隔离时间加速补录（2026-10-10）

用户给 `TriLink.SpeedBoundaryProbe.exe / PID 59436` 设置 OpenSpeedy 20×，并在隔离窗口运行“全部本轮测试”。仅加载冻结 fake fixture、HardwareRoom 和 Abstractions，没有 Serial 插件、真实串口、实板或测试器内置注入/倍率修改。此前只读确认该 PID 加速许可为 1，加载安装目录的 `speedpatch64.dll`；实际倍率以校准与自然到期数据确认，不单靠 UI 显示。

用户粘贴记录与本机 `probe.log` 字节完全一致，SHA-256 `FDE083F86F1047FEAD4A035A6D508EC3C31D6A15174B1ADDB779CC55800F2A34`。本轮真实参考采用该版本未 Hook 的 `QueryUnbiasedInterruptTime`，排除系统挂起时间；没有同步外部父进程秒表证据。UTC、Stopwatch/QPC 都受加速影响，不能把日志里的虚拟 UTC 当作实际墙钟或长时间稳定运行证明。

| 检查 | 20× 下真实参考耗时 | 进程内时间 / 观察 |
| --- | ---: | --- |
| 审批自然 TTL 6000 ms | 308.2 ms | 虚拟 6163.1 ms，19.997× |
| 普通动作自然 TTL 15000 ms | 755.7 ms | 虚拟 15117.3 ms，20.004× |
| `Task.Delay(1000)` | 1024.4 ms | QPC 20480.8 ms；等待没有等比缩短 |
| `Thread.Sleep(1000)` | 62.2 ms | QPC 1255.6 ms；名义等待 / 真实耗时约 16.08× |
| 独立 WinForms Timer 500 ms | 平均周期 31.5 ms | 2212.5 ms 内 70 ticks，名义周期倍率约 15.87× |

两种 TTL 实际等待捕获的 `ExpiresAt` 到达，未将其强行改成 0；到期后写入 0、队列 0、传输失败计数 0，界面明确提示过期取消。冻结 Queue/Pause **49 项**通过，包括同步取消、迟到结果和 phase 5 未知结果清尾；日志中没有 FAIL/CANCELLED。总耗时 `29758835−29749354=9481 ms`，与汇总 **9.481 秒**一致。53 条 PASS 文本含两种 TTL 和汇总，不当作 53 个独立测试。

单独未注入 1× 基线自然 TTL 约 6.01 / 15.02 秒，Queue/Pause 同样 49 项通过。20× 下本次未发现声明范围内的队列异常或假 PASS 迹象，但时间 API 没有统一缩放；不能将 9.481 秒乘 20 当作所有行为运行了同等长时间。

范围未覆盖：RGB 回执/上下文挂起的 12 秒截止、实际 HardwareRoom 页面 2/5 秒自动 Tick、五次通信失败熔断、194/1034 全套加速回归或真实无线/LED。既有 1034 正常速度门禁仍独立有效，本次结果不据此扩大。夹具自身 `WaitIdle` 的 15 秒 QPC 上限在 20× 下约为 0.75 秒真实预算，后续若遇此夹具超时，应先区分夹具时钟与产品失败，不能直接改产品期限。

证据单独归档于 `artifacts/tests/NodeConsole-Queue-0143/Speed20x-20261010/`：原始日志 (historical local evidence, not included: `../artifacts/tests/NodeConsole-Queue-0143/Speed20x-20261010/probe.log`)、机器收据 (historical local evidence, not included: `../artifacts/tests/NodeConsole-Queue-0143/Speed20x-20261010/acceleration-receipt.json`)、1× 基线 (historical local evidence, not included: `../artifacts/tests/NodeConsole-Queue-0143/Speed20x-20261010/baseline-1x.log`)，以及 7 个逐 SHA 验证的探针代码/依赖/日志文件。没有修改冻结产品源码、正式运行文件或 ZIP；原发布包 SHA 保持 `663FE3241114AA142C9D5A5D1A418D3A3626E24FAD325E3A3B805B36EBAF1EA9`。
