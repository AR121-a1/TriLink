# 测试目录

## Current 0.16.0 test baseline and entry points

Publication documentation update: 2026-10-15. The original 0.16.0 client build passed **1244 checks in Release and 1244 in Debug**: Core 136, Serial 259, HardwareRoom 269, Modules 64, Thunder 444, Desktop 34, and SmokeAndGuard 38 per configuration. The matching C-to-C# CAPS fixture run passed **260 checks separately**; it is not added twice to the ordinary suite totals. See the [public validation summary (derived)](../releases/v0.16.0/validation-summary.json), [release baseline](../docs/RELEASE_0160.md), and [development guide](../docs/DEVELOPMENT_GUIDE_0160.md).

Run a new isolated client build from the repository root when validation is required:

```powershell
.\tools\build-isolated.ps1 -Configuration Both
```

Keep the printed `ISOLATED_STAGE` and use its matching test binaries and evidence. For a cross-language check, replace both placeholders below with the freshly built test directory and the matching firmware build's fixture; do not reuse an unrelated historical fixture:

```powershell
& '<TEST_BIN>\TriLink.SerialLifecycle.Tests.exe' `
  --caps-fixture '<FIRMWARE_BUILD>\node-caps-fixture.txt' `
  --caps-version '0.16.0'
```

These counts cover software/fake services, automated rendering and PC loopback, not interactive-desktop or physical USB/RF/GPIO acceptance. Three boards later passed individual flash/startup/native CDC read checks; NCM Code 10 remained unresolved on the tested host. See the [hardware round summary](../firmware/docs/HARDWARE_0160_ROUND_20261014.md) for that separate scope. No new test run, binary rebuild or hardware action was performed for this publication-only README update. Original input hashes describe the original build snapshot, before this documentation adaptation.

The public package contains selected portable records, not the complete local test EXEs/logs/screenshots archive. Physical board identities are replaced with Board1/Board2/Board3 and the historical local port with COM_PORT. The records below remain historical; their counts and timing claims do not describe 0.16.0.

## Historical 0.14.4 and earlier records

历史0.14.4：desktop1.2.3、hardware-room0.4.1，serial1.2.2/Host API1.0/固件0.10.2保持。完整Release/Debug均exit0，各1067项普通速度软件检查：Core136、Serial126、HardwareRoom225、Modules64、Thunder444、Desktop34、Smoke/Guard38。HardwareRoom225为旧194+31，新增第五失败时清light/full残留与CREATE前后同物理RGB目标去重等回归；执行前Room/boot/权限复核保留。构建UI smoke要求desktop+safe-mode+demo，防止自动扫描真实串口，正常启动不改；38项含新增demo守卫两项。

独立v21 fake探针观察实际Timer.Tick与pump：旧0.14.3正常速度及20×都在“第五失败后残留刷新”和“单机CREATE后同目标RGB去重”两项失败；换入0.14.4依赖、同一源/EXE/场景后，正常速度12场景/132断言及20×12场景/130断言均零失败。QPC约1.0008/19.9970，两种模式差2为正常时钟专有时限断言；OpenSpeedy对Task.Delay/FormsTimer等不统一缩放，不能把所有12秒期限视为0.6秒，更不能称全1067项加速。另长测26项零失败，30.0379逻辑分钟/90.113真实秒、三次暂停恢复、零写；最大I/O并发1、队尾/失败/合法按钮异常采样均0，倍率恢复1×。资源样本含测试仪表，只作观测，不证明零泄漏或真实30分钟无线耐久。正式证据在`artifacts/tests/NodeConsole-Timing-0144/`，冻结副本保留，详见 [时序补丁](../docs/TIMING_PATCH_0144.md)。本轮仅fake、不访问实板或改/烧固件，不作无线/GPIO/交互桌面验收。

0.14.4独立运行目录`artifacts/NodeConsole-0.14.4/`、ZIP`artifacts/packages/TriLink-NodeConsole-0.14.4-Release.zip`已校验91输入、22便携＋3定位快捷方式及九插件清单；ZIP22项/158299B，SHA见交付收据 (historical local evidence, not included: `../artifacts/tests/NodeConsole-Timing-0144/delivery-receipt.json`)。冻结副本的build-stage不是部署入口；测试二进制/日志/截图/负例不进入便携包，0.14.3既有运行目录、ZIP和证据保留。

## 0.14.3历史队列与有限时间检查

历史0.14.3完整Release/Debug各1034项，HardwareRoom194项覆盖FIFO8待项/在途、捕获/复核/去重/队满/期限、逐笔后台让路、取消同步重入/未知清尾/refresh flags、并行RefreshAll拒绝、leader2秒/其他5秒与SEARCH15秒。早期V1 OpenSpeedy20×确认两类自然过期零写入与49项Queue/Pause通过，真实总耗时9.481秒；这不覆盖后来v21实际周期重叠和CREATE去重场景。该次有限通过与本轮旧版负例均保留，不倒改历史，也不扩大V1覆盖范围。详见 [历史队列及20×补录](../docs/QUEUE_CLOSURE_0143.md)。

历史运行目录`artifacts/NodeConsole-0.14.3/`、ZIP`artifacts/packages/TriLink-NodeConsole-0.14.3-Release.zip`已校验91冻结输入、22便携＋3本机快捷方式，ZIP22项/154881B；SHA见delivery-receipt及队列记录。证据根`artifacts/tests/NodeConsole-Queue-0143/{Release,Debug,NativeQueue,CancellationRegression}`分离测试/截图/收据，不混发布包。

历史NativeQueue以实际主表单功能工厂与共享USB owner，只允许ROOMGET/RGBRESULT读转发、SEARCH走真发现；31/31完成、exit0/stderr空。后台忙中双refresh＋Reload同步提交合计2ms为本机单观测，随后暂停/手动刷新恢复。前后Room16/inc44360405/rev2/term1、Board1 -> Board2顺序和permitfalse不变，无新RGB/CREATE/LEAVE/KICK/固件写；仅只读响应，不物理验证前台写FIFO。runner中文路径/退出码捕获前次失败已修并保留记录，不当产品bug或虚假通过；0.14.2快读审批与0.14.3只读都不证明新版默认人工8秒审批时序。

2026-10-09 历史0.14.2：desktop1.2.1、serial1.2.2、hardware-room0.3.1，固件仍0.10.2。完整Release/Debug各985项通过：Core136、SerialLifecycle126、HardwareRoom145、Modules64、Thunder444、Desktop34、Smoke/Guard36。新增fake回归覆盖许可不执行、本机无成员选择、确认取消/旧身份、创建一人Room保留、原ID终态+ROOMGET00复核、未知/晚到/取消不重发；以及recognized+visible只读初刷新/5秒单飞、暂停/失败5次/换设备后不偷偷恢复。详见 [当轮单板RGB](../docs/SINGLE_NODE_CLOSURE_20261009.md)。

本轮独立证据根`artifacts/tests/NodeConsole-Single-20261009/{Release,Debug,NativeSingle,PeerDiscovery}/`，运行目录`artifacts/NodeConsole-0.14.2/`；90源码输入与隔离构建一致，ZIP22项与Release逐SHA匹配，151286B，SHA见delivery-receipt.json。普通fake回归不打开真实设备，测试二进制/日志/截图/收据不混入便携包。

真实NativeSingle PASS仅在用户授权的COM_PORT、Board1/CAP99执行一次：已有Room16/incarnation44360405、许可false；实际主表单工厂+共享owner、没有成员选择，许可读回true但不点灯，再QUEUED一次`00000001`，phase1→2于22:46:35自动回显；独立读回终态2，最后恢复许可false并保留原Room，没有CREATE/LEAVE或固件变更。inactive创建/保留路径本次仅fake覆盖，不能泛称全部单板分支都已实板通过。

PeerDiscovery只读SEARCH看到Board2/RSSI=-51，本机执行现场另一采样=-55；这是单侧发现，不等于多人JOIN/审批。驱动终态2通过，人工光学及用户交互桌面仍未确认，不重复RGB补证据；远端更新新版客户端后的跨电脑审批/继任另行验收。

2026-10-09 早轮0.14.1历史：隔离Release/Debug各934项通过（Core136、SerialLifecycle126、HardwareRoom94、Modules64、Thunder444、Desktop34、内置选择/输出保护36）。新回归覆盖首页真实功能嵌入/模拟按钮隔离、设备事件与同端口身份、握手诊断、完整按钮角色/选择/容量门控、ACK失效、暂停/销毁及未知状态。测试/截图/日志保存于 `artifacts/tests/NodeConsole-20261009/{Release,Debug}/`，与运行目录 `artifacts/NodeConsole-0.14.1/` 分开；这些数量不作为0.14.2通过依据。

早轮单独真机证据 `artifacts/tests/NodeConsole-20261009/NativeClosure/`：COM_PORT、Board1，实际主表单+嵌入页面共用真实服务；按钮刷新、创建一人Room、退出清成员通过，当时附近节点0，没有RGB执行或固件变更；自动化Form不是用户桌面验收。其他审批/邀请/踢出/RGB路径当时为fake软件回归，不当作多板实测。详见 [早轮范围与证据](../docs/CLIENT_CLOSURE_20261009.md)。以下带日期的822/794等数量保留为对应历史版本。

2026-10-05 节点控制台定义：完整Debug与独立短路径Release构建各822项通过（Core136、SerialLifecycle65、HardwareRoom47、Modules64、Thunder444、Desktop30、内置选择/输出保护36）。新增范围默认/复制/无效枚举拒绝、PC/板端文案与惰性打开检查，以及构建占用保护13项；保留X/托盘生命周期。Release构建使用91个源码/测试/工具文件一致的隔离快照，测试README仅在构建后刷新文档、不影响编译代码。新Release软件证据保存在`artifacts/tests/NodeConsole-Release/`；新版运行目录为`artifacts/NodeConsole/`，不是原`artifacts/Release/`。详见 [控制台定义与验证](../docs/NODE_CONSOLE.md)。这些检查未实现或验证资源包运行、P2P复制、压缩或固件OTA，亦不替代三板和交互桌面验收。

下方794项记录是定义改动前的多人版本历史；其原始Release/Debug证据保存在`artifacts/tests/archive/20261005-multiplayer-pre-node-console-{Release,Debug}/`，不混用作新板端功能证据。

2026-10-05 本地雷霆0.2.0多人扩展：2–6席输入/握手/逐peer恢复，测试固定内置safe-mode并完善world hash和I/O失败释放。完整Release/Debug各794项通过（Thunder440，内含Engine131）；2/3/6独立UDP进程世界一致，单插件重建通过。完整证据见 [核对记录](../docs/THUNDER_REVIEW_20261005.md)。跨实体PC/ESP32与用户交互桌面仍需单独验收。

2026-10-05 原始雷霆0.1.0分支历史：完整Debug构建通过Core136、SerialLifecycle65、HardwareRoom47、Modules53、Desktop30、Thunder136及内置选择22，共489项。下载后完整Release也通过；原双进程loopback一致，不替代多人新版本验证。

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
| 0.14.4页面queue/时序回归 | 同一硬件页suite共225项，含FIFO容量/顺序/去重/上下文/期限、后台让路/轻重读/SEARCH节奏、第五失败清light/full、跨CREATE同目标RGB去重、取消/未知；fake写入不当真实无线验收 |
| v21隔离时序场景 | 正常/20×各12场景（132/130断言），真实Timer.Tick/重叠/队列让路/暂停恢复/按钮矩阵/RGB超时与去重；依赖fake服务，不计入1067项或真实固件期限验收 |
| LongLogicalTime隔离长测 | 30.0379逻辑分钟、26项零失败；实际回调/15秒发现守卫、三次暂停恢复/按钮与资源观测，零写；fake命令历史由仪表裁剪，不证明零泄漏或真实无线耐久 |
| `TriLink.Thunder.Tests/` | 多人确定性引擎、固定池/争抢公平性、报文边界/独立ACK故障恢复、2/3/6进程UDP、游戏窗口生命周期与渲染 |

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
