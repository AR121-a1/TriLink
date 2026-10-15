# 版本记录

## 2026-10-15 - 0.16.0 public source and package distribution

- Include client source, tests, tools and documentation at the root, with firmware source under `firmware/`.
- Publish the selected original PC ZIP and separate firmware image ZIP under [releases/v0.16.0](releases/v0.16.0/README.md), with a portable [validation summary](releases/v0.16.0/validation-summary.json) and [manifest](releases/v0.16.0/manifest.json).
- This explicitly supersedes earlier client-only/no-firmware publication policy. Earlier dated no-upload statements remain historical; package inclusion does not change their build or hardware flags.
- Preserve the 2026-10-14 individual three-board CDC results and unresolved NCM Code 10 limitation. No new build, flash, Room/RGB action or hardware acceptance is implied by publication.
- Exclude user module overlays, full-device backups, machine-specific shortcuts, TEMP worktrees and unrestricted local logs from this distribution.


## 2026-10-12 — 0.16.0-local USB 接口平台（本地，未上传）

- 固件升级 TinyUSB CDC+NCM，保留 Windows 10 CDC 控制路径；NCM 仅是主机到本块板的本地管理链路，不是透明三机 IP 网络。
- 固件单命令 owner 仲裁 CDC 与 HTTP，超时旧回执隔离；板内轻量网页、有限 mailbox、来源/令牌/输入边界。
- 客户端复用既有 I/O owner 与共享节点会话，CDC 优先、NCM 绑定接口；无不确定写入跨通道重放。端点元数据保持真实目标与显示分离。
- desktop/serial `1.4.0`、hardware-room `0.6.0`，Host API `1.0`。整套运行目录升级，旧目录保留，不自动烧录或安装驱动。
- 功能边界、构建与硬件验收状态见 [0.16.0 记录](docs/RELEASE_0160.md)。文本/文件/OTA/HID 等后续能力不作本轮完成声明。

## 2026-10-11 — 0.15.0-local 公共节点底座（本地，未上传）

- desktop/serial 1.3.0、hardware-room 0.5.0，保留 Host API 1.0；整套升级共享契约和插件。
- 新 `INodeSessionService`：跨页统一 FIFO、刷新、快照、去重、回执与生命周期，页面不再各自调度 USB 业务；共享刷新 Task 保持有界，页面关闭不终止已提交跟踪。
- 新 `INodeCapabilityService`：新固件 CAPS 描述、旧固件保守兼容；严格解析，排队绑定原 MAC/PnP/能力，防止同 COM 换板误发；本机启动编号使用本机成员条目而非 Room 来源字段。
- 配套本地固件 0.15.0：4槽静态应用分发器，已有 RGB 接入，能力只声明 RGB。App 807584 B、静态 DIRAM 149191 B，分区不变，无新增任务栈/内容存储/动态代码加载。
- 这是职责方案阶段 A；文本 P2P、连续文件流、扩展对齐和实时子模式继续按 B–E 开发，不虚报可用。本轮未访问实板、未烧录、未上传；软件证据与交付见 [版本记录](docs/RELEASE_0150.md)。

## 2026-10-10 — 0.14.4-local 时序边界补丁（本地，未上传）

- 产品0.14.4-local、desktop1.2.3、hardware-room0.4.1；serial1.2.2/Host API1.0/本地固件0.10.2不变，不新增公共调度框架、板端能力或串口所有者。
- 第五次后台自动刷新失败时先清除light/full合并刷新意图再停timer，避免排队的第六批读把失效状态恢复为就绪；显式手动启动仍清零并立即读。
- RGB在途去重键改为generation/local MAC/physical target，单机CREATE转为活动Room后同一物理目标仍去重；出队Room/boot/角色/许可复核保留，不允许旧上下文写入。不同目标与RGB禁用仍可排队。
- Release/Debug均exit0、各1067项普通速度软件检查：Core136、Serial126、HardwareRoom225（194+31）、Modules64、Thunder444、Desktop34、Smoke/Guard38。构建UI smoke固定desktop+safe-mode+demo，防止真实串口自动扫描；未改变正常客户端启动。
- 同一个v21 fake探针：旧0.14.3正常速度和20×均在上述两项失败；0.14.4正常速度12场景/132断言、20×12场景/130断言均零失败，QPC约1.0008/19.9970。两种模式断言数差2来自正常速度专有时限检查；不宣称所有时间API等比例缩放、1067项全量加速或真机验收。早期V1 49项有限加速历史独立保留，不能覆盖新增v21场景。
- 长测26项零失败：30.0379逻辑分钟/90.113真实秒，实际Timer回调777、读取7858、SEARCH112、最大并发I/O=1，三次暂停恢复、零写入。倍率最终恢复1×、父runner退出0；资源样本仅观测，不证明零泄漏或真实30分钟无线耐久。
- 独立0.14.4运行目录22便携＋3定位快捷方式、九清单及91输入校验通过；ZIP22项/158299B，SHA256 `7519880EC0DDEE593E4D7B228FA50BAC53250683C59EF373561A535BA83DCC3F`，见交付收据 (historical local evidence, not included: `artifacts/tests/NodeConsole-Timing-0144/delivery-receipt.json`)。正式证据在`artifacts/tests/NodeConsole-Timing-0144/`，冻结副本与旧0.14.3包/运行目录/证据保留。本轮未访问真实USB/无线/GPIO，不改/烧固件，不上传或创建Release。详见 [时序补丁记录](docs/TIMING_PATCH_0144.md)。

## 0.14.3-local — 前台意图与后台让路（本地，未上传）

- 产品0.14.3-local、desktop1.2.2、hardware-room0.4.0，serial1.2.2/Host API1.0/本地固件0.10.2保持；不是板端新任务或公共全局调度框架。
- 每页FIFO最多8等待项，在途另计；捕获点击时名称/目标/身份/Room/票据，出队真实复核；同一动作去重，审批/拒绝等待6秒、其他15秒，过期不写。底层USB仍共享串行，跨页面不是全局业务事务锁。
- 前台优先，后台在已开始一笔I/O返回后让路；leader约2秒、其他约5秒合并轻量刷新，active SEARCH至少15秒追加一笔，不每次退回完整22笔。手动完整刷新/未入Room仍较重，固件8秒窗口不变且不保证实时审批。
- 暂停/换板/销毁/未知清队尾与两类refresh flags，先保存当前动作引用再取消，避免同步收尾空引用及旧刷新复活；已提交可能在途，不能撤销或自动重放。phase5按应用未知处理，不计作USB失败。保留完整ex.ToString诊断，不关闭.NET异常提示。
- 隔离Release/Debug均exit0，各1034项：Core136、Serial126、HardwareRoom194、Modules64、Thunder444、Desktop34、Smoke/Guard36。194项fake不替代真实前台写FIFO；OpenSpeedy后加隔离时间测试另记，不计入1034或据此宣称加速通过。
- COM_PORT/Board1实际主表单工厂+共享USB只读探针31/31完成，exit0/stderr空；忙中两次刷新＋端口更新同步提交合计2ms为本机单次观察，不是完整读取耗时或性能承诺。暂停/手动刷新恢复，原Room16/inc44360405/rev2/term1、Board1 -> Board2成员顺序、RGB许可false保持，无新Room/RGB/固件写入；旧RGB ID1仅历史结果读取。
- 91冻结源码/构建输入匹配，独立runtime22便携＋3定位快捷方式；ZIP22项/154881B，SHA256 `663FE3241114AA142C9D5A5D1A418D3A3626E24FAD325E3A3B805B36EBAF1EA9`。原生助手编码/退出码捕获问题修在临时runner，不是产品故障。新版写FIFO真实无线、默认人工审批及用户交互桌面仍未验收；详见 [队列收束](docs/QUEUE_CLOSURE_0143.md)，旧0.14.2有限双PC证据不改写。

## 2026-10-09 — 0.14.2-local 两电脑Room准入增补（本地验证，不更改发布物）

- 既有0.14.2、软件985项/配置和便携包不变；本次只增加真实验证记录，0.14.3 queue优化正在开发，未完成/未验收。
- PC1 COM_PORT/Board1保持原Room16/incarnation44360405，PC2 Board2以fresh ticket19/boot4247252633申请；真实首页HardwareApprove一次后提交revision2/term1/count2/synchronized=True，join_order为Board1 boot44360405→Board2 boot4247252633。
- 用户确认PC2显示一致2/6、leader Board1、revision2与同序成员、自身普通成员；没有远端截图/raw。快读真实状态和请求后转实际view程序集实例再点击产品按钮，绕过默认整页刷新以赶8秒窗口，不宣称默认人工审批稳定。
- 初次临时助手LoadFrom/Default类型强转失败发生在Room写入前，修复助手而非产品；保留失败与成功日志。助手退出后只读本机Room仍2人，不证明PC2退出或掉电持久化。
- 未CREATE/LEAVE/KICK/新RGB/固件写入；读取的RGB00000001终态来自此前22:46双闪。当前用户Room保留，2人任退一人会解散，保Room的继任验收至少3成员。
- 用户反馈0.14.2约20项全刷新和_busy整体禁用影响8秒准入窗口，记录为0.14.3 queue待优化，不篡改此轮有限通过或宣布修复完成。证据与限制见 [双PC验证](docs/TWO_PC_ROOM_VALIDATION_20261009.md)。以下单板/发现阶段“尚无JOIN”等描述为当时历史。

## 2026-10-09 — 0.14.2-local 单板RGB闭环与主动发现（本地，未上传）

- 产品0.14.2-local、desktop1.2.1、serial1.2.2、hardware-room0.3.1，Host API1.0；沿用本地固件0.10.2，不改源码/分区/SDK，不上传。
- RGB硬件许可与执行命令明确分开；专用“本机青色双闪”不依赖成员选中。若未入Room，必须明确确认创建一人真实Room；执行后保留该Room，不自动退出或把单板伪装成多人网络。
- 执行请求只提交一次；跟踪原QUEUED应用ID，并绑定MAC、room/incarnation/revision/term与目标成员boot。结果跟踪预算12秒，读取终态并快速复核ROOMGET00；换身份/会话、取消、故障或超时标注未知，不自动重发。
- 已识别且可见的真实页默认首次只读刷新及5秒单飞更新；手动暂停、连续5次失败、换设备、隐藏/销毁与在途取消保留停止边界，设备事件不绕过暂停。USB发现轮询和业务页刷新是不同调度。
- 固件已有500ms广播发现、1秒邻居单播心跳、6邻居/2秒过期及MAC peer自动注册；附近设备不等于Room成员，leader准入/审批规则不改。原生USB SEARCH本端看到Board2、RSSI=-51dBm，仅单侧发现，尚无多板JOIN闭环。
- Release/Debug各985项通过：Core136、Serial126、HardwareRoom145、Modules64、Thunder444、Desktop34、Smoke/Guard36；本轮构建退出码0，新增路径的fake检查不作为真机证明。
- 真机COM_PORT/Board1/CAP99在已有Room16/incarnation44360405、许可false的基线上，用实际首页功能工厂与共享USB所有者，未选成员直接点击本机双闪一次。22:46:34许可true读回（不点灯）、QUEUED一次、phase1→2于22:46:35自动回显；独立RGBRESULT `00000001 2`，随后恢复许可false且保持原Room，未CREATE/LEAVE，不改固件。自动窗体/真实USB/驱动回执通过，人工光学及用户交互桌面未确认，多板准入/审批/继任仍待验收。
- 独立0.14.2运行目录22便携文件＋3本机快捷方式，ZIP22项/151286B，内容与隔离Release及90源码输入匹配；SHA见本轮记录。没有固件、测试或用户配置混入。统一见 [本轮单板闭环](docs/SINGLE_NODE_CLOSURE_20261009.md)，下方0.14.1/934项和未执行RGB保留早轮历史。

## 2026-10-09 — 0.14.1-local 上位机实际功能闭环（本地，未上传）

- desktop 1.2.0：真实 Room/RGB 页面直接嵌入首页；旧模拟按钮只在演示模式出现，切换保留同一真实控件，不创建第二个 USB 所有者。处理跨线程退出竞态、句柄创建后的设备快照和滚动保持。
- serial 1.2.2：候选口不等于已连接，分别诊断占用/断开/超时/身份或能力不匹配；按端口与PnP身份去重。保留nonce、受控轮询、单I/O所有者与手动恢复。
- hardware-room 0.3.0：无设备/不兼容/需刷新/就绪/忙/失效的状态闭环；创建、加入、邀请、审批、踢出、退出和RGB的权限/选择/容量门控及原因提示；拔插共享事件、回执校验、未知状态不自动重放、暂停/销毁停止后续批次。
- Release/Debug各934项通过：Core136、Serial126、HardwareRoom94、Modules64、Thunder444、Desktop34、内置选择/输出保护36；九插件/profile和UI门通过。
- 真机COM_PORT / Board1，经实际主表单按钮完成HELLO、刷新、创建一人Room、退出清成员。附近节点0，未操作RGB或固件；这是自动化Form+真实设备证据，不代表人工桌面或多板无线/灯光验收。
- 新运行目录 `artifacts/NodeConsole-0.14.1/`，保留旧进程和目录；完整便携包不含固件、测试、用户配置及绝对路径快捷方式。详见 [闭环与交付](docs/CLIENT_CLOSURE_20261009.md)。

## 2026-10-05 — 节点控制台定义重构（本地，未上传）

- 收束为ESP32稳定核心、有限资源/任务包与可选PC控制台；游戏优先小状态轻回合，现雷霆保留PC示例，不宣称移植完成。
- 新增功能范围元数据，区分PC工具、设备控制界面与PC演示；注册表拒绝未知范围并防御复制，管理窗口明确DLL不是板端包。
- 主窗口改称节点控制台；单USB所有者、惰性功能创建、X/托盘/单实例和受控轮询保持。首次需整套升级共享契约与插件；旧插件→新宿主兼容不等于新插件→旧宿主兼容。
- 完整Debug/隔离Release各822项通过；新版Release独立放入`artifacts/NodeConsole/`，未停止旧运行客户端。首次旧输出占用失败后按原hash恢复旧共享DLL，22个原运行文件最终均匹配；新增构建前占用检查与13项非破坏回归。旧794项证据归档，新软件证据独立保存。
- 板端存储/任务、压缩、P2P复制及真正OTA均待实施；未改变固件、SDK/BSP、分区或硬件，未推送。见 [新定义与验收边界](docs/NODE_CONSOLE.md)。

## 2026-10-05 — 本地多人扩展与核对（未上传）

- 从官方thunder-game 3cb3fda下载校验，在独立工作副本扩展 `trilink.thunder` 为0.2.0：2–6人、固定六席和五peer、到齐开局、逐玩家输入/ACK与有界恢复。
- GameWire v2最高120B，不兼容v1；UDP提供者0.1.0、128B契约、实体池及无后台线程边界保持。PC需统一更新；ESP32桥接未实现。
- 多人UI/六色HUD、端点配置锁定、隐藏停止重绘；补游戏验证safe-mode、完整世界hash及I/O失败释放回归。
- 修复多人加入人数显示、隐藏后的停止重绘；3+人按六tick轮换发射优先级，满池不再长期偏向低席位，单/双人顺序不变且不增池/逐tick分配。
- 完整Release/Debug各794项通过，2/3/6独立UDP进程hash一致；单插件重建与其余运行文件不变验证通过。证据见 [核对记录](docs/THUNDER_REVIEW_20261005.md)，历史0.1.0记录不作为本地0.2.0证据；未修改主干、固件、GPIO或防火墙，未推送/发布。

## 2026-10-05 — 雷霆战机开发插件

- 新增 `trilink.thunder` 0.1.0：像素战机、单人练习、两人合作、固定实体池和整数确定性引擎。
- 新增 `trilink.game-link` 0.1.0 与加法服务契约：当前IPv4局域网UDP，按需打开、无后台线程，后续ESP32可替换提供者。
- 30Hz游戏、10Hz批量输入；最高88B游戏报文、有界补发、会话隔离、断线终止；隐藏或关闭联机释放资源。
- 插件profile、独立构建、解决方案、软件回归与资源预算文档已接入；模块基线调整为九项。
- 基于最新主干修复的完整Debug构建通过：核心136、SerialLifecycle65、HardwareRoom47、模块53、桌面30、雷霆战机136及内置选择22项，共489项检查；九插件/profile加载、UI和运行目录门通过，双进程UDP世界hash一致。
- 产品保持 `0.14.0-local`，Host API `1.0`；不改固件、不烧录，ESP32游戏桥接及跨实体PC验收待完成。详见 [雷霆战机](docs/THUNDER.md)。

## 2026-10-05 — 客户端开发源码同步

- 同步9月22日至10月1日的客户端收束与 review 修复：串口1.2.1、真实 Room/RGB 模块0.2.1；产品保持 `0.14.0-local`，Host API `1.0`，七个首方插件。
- 包含入群确认/未知状态与人工重试、RGB故障/退出公告提示、同COM身份复核、扫描轮次取消和内置构建安全模式验收；功能、架构及后续验收计划随源码更新。
- 修正干净克隆在Windows PowerShell 5中的UTF-8读取：构建/清单/目录检查显式解码JSON，含中文的目录/快捷方式脚本保留UTF-8 BOM，避免中文清单解析失败或快捷方式乱码。
- 10月5日同步目录用Windows PowerShell 5完整Release构建通过：Core136、SerialLifecycle65、HardwareRoom47、Modules53、Desktop30、内置选择22项；七插件/profile加载、UI渲染和干净运行目录检查通过。仅软件/夹具验收，没有连接真实串口或执行固件操作。
- 仅上传客户端源码、测试、构建工具和文档。不上传独立固件工程或镜像、EXE/DLL、构建产物、运行配置、测试日志/截图及工作区交接材料；不新建标签或 Release。
- 10月1日的完整软件验证记录见 [review修复](docs/REVIEW_FIXES_20261001.md)。真实桌面、固件烧录和三板联调仍待验收；以下历史“未上传”仅指当时状态。

## 2026-10-01 — 整体review修复（本地，未上传）

- 串口1.2.1：手动恢复复核相同COM的能力/身份，PnP变化与失败复核使缓存更新/失效；扫描取消轮次阻止暂停/释放后的后续I/O、缓存和事件，快速恢复只提交新轮次。
- 演示/截图模式不自动扫描真实串口；内部I/O边界支持确定性回归，不改公共Host API或产品版本。
- 内置构建UI与桌面生命周期固定desktop+safe-mode，用户导入覆盖不影响新内置包验收；用户配置不被覆盖。
- 配套本地固件0.10.2：加入成员版本下限，旧快照不能错误完成重加入；空口opcode2、六成员、SDK/BSP保持。
- 客户端全量136/65/53/30/47及内置选择22项通过；串口定向更新其它21运行文件哈希不变。固件14C组94232与全新镜像/容量门通过。未烧录、操作GPIO或上传，真实桌面/三板待验收。见 [修复记录](docs/REVIEW_FIXES_20261001.md)。

## 2026-10-01 — RMT与Room退出收束（本地，未上传）

- `trilink.hardware-room` 0.2.1：新增本机RGB驱动故障提示与退出公告等待提示，保留入群确认/未知状态及手动重试。产品仍为`0.14.0-local`，Host API `1.0`，七个插件。
- 配套本地固件0.10.1：RMT非阻塞提交，每个在途事务等待限100 ms；驱动故障锁存并停用RGB至重启。清理失败保留资源，不再发送clear/refresh或重复删除，物理LED状态不能保证。
- 原leader退出后保留10秒旧Room公告，期间拒绝CREATE/JOIN，客户端提示稍后刷新；Room空口opcode仍为2，与0.10.0相同。
- 固件14组C测试94150项检查通过，包含Room483、RMT226、实际RGB worker622（11个场景）、RGB集成93。客户端本轮全量136/53/30/45通过；新增退休提示后的HardwareRoom定向47项、7份manifest/profile加载、UI及干净运行目录门通过。夹具不替代真实桌面或硬件验收。
- 完整固件构建、容量、源哈希及USB/Python证据以本地固件VALIDATION和当前构建receipt为准。未烧录、未执行GPIO、未上传GitHub或新建Release；上次客户端源码上传仍为9月20日的`4bb5eca`。详见 [当前收束与验收](docs/CLOSURE_20261001.md)。

## 2026-09-22 — 入群确认与阶段收束（本地，未上传）

- hardware-room0.2.0：区分确认中/结果未知，增加“重试入群确认”，显示已批准等待申请者的条目。
- 配套本地固件0.10.0：OFFER/CONFIRM后才提交成员，票据/挑战关联、8次有界重试、未知状态禁止另建房间。Room空口opcode2与旧版隔离，需三板统一升级。
- 客户端136/53/30/42项检查和四状态渲染通过；没有烧录、真机验收、GitHub上传或新Release。详见 [收束与验收](docs/CLOSURE_20260922.md)。

## 2026-09-20 — 真实 Room / RGB 客户端源码同步与加固

- `trilink.hardware-room` 0.1.1：通信失败清除失效状态、保留稳定选择、手动暂停停止余下查询、RGB结果日志去重，输入错误与通信错误分开处理。
- 配套固件0.9.1：Room命令和无线LEAVE版本校验，六项RGB应用序号水位，终态不回退，Room改变时未决RGB标记未知。
- 不改SDK/BSP、容量上限或默认GPIO策略；未烧录。验证范围与剩余问题见 [加固记录](docs/HARDENING_20260920.md)。
- 同步客户端源码、测试和文档到 main，包含下述9月17日的模块接入；不上传固件、EXE、构建产物和本机日志，不创建新标签或 Release。
- 客户端检查：核心136、模块53、桌面30、HardwareRoom37项通过；真实三板与用户交互桌面验收待完成。

## 2026-09-17 — 真实 Room / RGB（当日开发记录，客户端现纳入9月20日源码同步）

- 新增 `trilink.hardware-room` 0.1.0，独立的设备、Room、成员、审批、邀请与 RGB 结果窗口。
- `trilink.serial` 1.2.0：发现/搜索/命令共用 USB 所有者，业务前身份复核、nonce、有界读、150 ms 间隔、超时未知不重放。
- `trilink.desktop` 1.1.1：真实模式指向新模块；模拟入口与真实状态保持隔离。
- 新模块默认不轮询，连续5次失败暂停，关闭/切换设备中止剩余批次；RGB 默认禁用，逐板确认后启用。
- 本地配套固件 v0.9.0：六成员易失副本、审批/邀请/正常继任、断链冻结、RGB 路由/驱动结果。未烧录、未进行真实三机或灯光验收。
- 初次升级需整体更新运行目录（新增抽象契约）；后续模块保持独立更新。产品仍 `0.14.0-local`，正式 Git 标签不变。
- 详见 [使用与验收](docs/REAL_ROOM_RGB.md)。

## 2026-09-16 — v0.14 开发源码同步

- 产品版本保留 `0.14.0-local`，Host API `1.0`；更新 main 源码，不创建新的正式版本标签或二进制 Release。
- 包含此前的显式模块平台、文本工具、SetupAPI 扫描修复、真实/模拟模式隔离和托盘生命周期改进。
- `trilink.rooms` 更新为 **1.1.0**：最多六成员，包含创建者/本机；申请、邀请、批准均检查容量，满员时操作入口禁用，待审批不超额且保留可重试。
- 正常 leader 退出按加入顺序继承，六成员副本一致性与空位后批准均有回归；Room 仍为模拟后端，不宣称已完成跨电脑 Room。
- Core 136、模块 53、桌面模式/生命周期 30 项自动检查通过；构建、插件清单和运行目录检查通过。
- 仅同步客户端源码、测试源码和文档。固件源码/BIN、构建产物、测试截图/日志、个人路径下的归档及工作区交接文件不上传。
- 细节见 [Room 更新](docs/ROOMS_1_1_0.md)、[模块管理](docs/MODULE_MANAGEMENT.md)、[后续计划](docs/ROADMAP.md)。

## 0.14.0-local — 2026-09-15（当日开发记录，现包含于源码快照）

- 显式“扩展模块”管理窗口：详情、启停、导入更新、依赖校验、待重启、撤销与恢复。
- 新增 `trilink.modules` 1.0.0 和 `trilink.text-tools` 1.0.0；桌面 1.1.0，串口保持 1.0.1。
- 独立包目录和原子配置保存，不覆盖正在运行的 DLL；安全模式恢复内置选择。
- 文本工具按需提供 UTF-8 长度、SHA-256、Base64，不联网、不增加轮询。
- API 1.0 加法服务扩展，无热卸载、在线市场或恶意代码隔离。
- 自动模块用例、独立进程加载探针和真实窗体渲染；不改固件、不推送 Git。
- 操作和边界见 [模块管理](docs/MODULE_MANAGEMENT.md)。

## 本地 USB 扫描修复 — 2026-09-14（未发布新 Git 标签）

- `trilink.serial` / `trilink.desktop`：`1.0.1`；产品 `0.13.0`、Host API `1.0` 不变。
- 用 Windows SetupAPI 在线设备枚举替换包含无效属性的 WMI 查询；保留 5 次连续失败熔断。
- 普通模式不默认选模拟 A；模拟显式启停，物理邻居不混入本机电脑选择器。
- 未识别设备时禁用搜索；未收到匹配 END 的搜索报超时，不伪装为成功返回 0 台。
- 未接入的真实 Room 操作禁用；真实邻居列表每次成功搜索替换，断开或失败后清除。
- 修正轮询按钮初始状态与工具栏长提示布局，保留 X 隐藏和托盘独立退出语义。
- 核心回归 101 项；新增模式/断开/错误状态自动界面回归及三种状态截图检查。
- 仅更新上述两个插件及部署哈希；详情见 [USB 扫描修复记录](docs/USB_SCAN_FIX.md)。

## v0.13 — 2026-09-14

产品版本 `0.13.0`，发布范围为 Windows 客户端源码。ESP32 固件、构建产物、测试截图、日志、历史归档和本机快捷方式不纳入源码提交。

### 已实现

- 微内核与四个首方插件：Room、USB 串口、模拟数据源、WinForms 桌面。
- profile 组合、稳定服务接口、依赖拓扑、反向清理、SHA-256 清单校验。
- 三节点单机房间演示：创建、加入、邀请、leader 审批与踢出、正常退出顺序继任、一人正式房间解散。
- 按 X 隐藏到托盘，托盘打开恢复窗口，托盘退出结束客户端。
- USB CDC 候选端口筛选、nonce 握手与邻居搜索协议；连续扫描失败 5 次暂停，手动恢复。
- 源码按宿主、共享接口和插件归类；发布、测试、编译暂存、历史材料分别存放。
- 只重建指定插件；发布目录检查拒绝测试材料混入。

### 验证与限制

- 核心回归 88 项；桌面生命周期自动回归；插件加载、UI 渲染与发布目录检查。
- 单插件更新校验宿主和其他插件文件不变。
- 三电脑无线同步、分区选举、跨重启持久化、文本/文件传输、远程唤醒和虚拟网卡尚未实现。
- UI 自动截图和窗口句柄不构成用户交互桌面或硬件验收证据。

### 版本规则

`VERSION` 与宿主程序集记录产品版本；Git 标签 `v0.13` 对应产品 `0.13.0`。Host API 当前仍为 `1.0`，插件版本独立于产品版本，不能为了发布整包就假称发生了 ABI 升级。
