# 游戏分支核对与多人扩展（本地）

历史范围说明：本文记录节点控制台定义改动前的794项多人版本。原始证据已归档到`artifacts/tests/archive/20261005-multiplayer-pre-node-console-{Release,Debug}/`；本轮范围元数据/UI调整及构建保护后的822项软件验证另见 [节点控制台定义](NODE_CONSOLE.md)。雷霆仍是PC示例，任一测试数量都不代表板端任务或资源同步已实现。

基线为官方thunder-game的 [3cb3fda](https://github.com/AR121-a1/TriLink/commit/3cb3fda30cb6295e841af68eee1e1f70e54290e8)，父提交04e0013。普通Git HTTPS连接失败后改用官方API归档下载，没有从第三方镜像或旧目录拼接代码。112个归档文件按仓库.gitattributes换行规范化，全部匹配GitHub tree `39c7d0cf79c1a4a14728a0234bb6174657b63e92` 的blob哈希。此目录是独立源码工作副本，不是已完成Git fetch的跟踪checkout；原始归档与校验清单在工作区tmp，主干客户端及本地固件未改。

## 核对结论与修复

- 原0.1.0基线完整Release构建通过489项，九插件加载、UI与目录门通过；IPv4双进程loopback世界一致。UDP的非阻塞/128B上限、超长丢弃、资源释放已有边界测试；不替换稳定传输实现。
- 新游戏集成测试遗漏safe-mode，可能加载用户导入旧包或被禁用配置影响。已统一desktop+demo+safe-mode，并加入构建元数据回归门；普通用户选择保持。
- 原“完整世界hash”漏Seed/Cooperative，可能把未来会分歧的初始世界判为相同；补齐Seed/PlayerCount/Cooperative并增加负例。
- 原在线视图隐藏会停止会话，但恢复时可能残留运行画面；可见性停止立即重绘，并加入恢复/停止态图回归。
- 多人加入后人数控件同步实际分配人数，不残留默认2；双方仍保留独立创建/加入和锁定参数的生命周期。
- 多人满友弹池时固定低席位顺序会长期抢到稀缺槽位；3人及以上采用确定性轮换优先级，不增实体池、状态或逐tick分配，单/双人顺序不变。
- 原双人故障恢复测试把房主发送调度冻结为0ms，最后丢失3tick没有机会重发；实测Host/Guest仍活跃但tick=417/414。恢复段改用正常100ms调度，并加强双方活跃与完整世界一致性断言，不放宽协议恢复要求。

## 多人扩展

本地游戏插件0.2.0，游戏传输插件仍0.1.0，产品0.14.0-local/Host API1.0不变；没有增加插件、长期线程、依赖服务器或固件内容。

- 单人练习与2–6人合作；人数在创建房间前明确选择，全部Ready再开局，自动席位分配。
- 固定6玩家、最多5peer、720B输入环；敌机/子弹/效果池不扩大。每peer独立端点/token/ACK/输入序号/时间，不把全局ACK误用于多人。
- GameWire v2输入向量；24B头、每帧最多16tick、最大120B，保持IGameLink的128B预算。v1不兼容，必须统一所有PC客户端。
- 六色玩家/友弹、紧凑HUD、状态/人数显示，联机期间锁定端点/人数；空闲无socket/timer，单人隐藏暂停，联机隐藏结束。
- 任一玩家退出、失联或超出历史窗口时整局有界结束。没有中途加入、自动重连、状态快照或主机迁移；只用于可信实验LAN，不开公网端口。

稳定契约、资源预算和未来ESP32接入见 [雷霆战机](THUNDER.md)，具体约定见 [实施约定](THUNDER_MULTIPLAYER_PLAN.md)。

## 当前验证状态

冻结源码后的完整Release与Debug构建均通过，每个配置794项，不将历史489项或开发中间计数当成当前结果：

| 检查组 | 每个配置通过数 |
| --- | ---: |
| Core | 136 |
| SerialLifecycle | 65 |
| HardwareRoom | 47 |
| Modules | 53 |
| Thunder（含引擎131项） | 440 |
| Desktop | 30 |
| built-in smoke选择 | 23 |
| 合计 | 794 |

执行 `tools/build.ps1 -Configuration Release` 与 `-Configuration Debug`。九插件manifest/hash/profile、干净运行目录、宿主UI渲染及生命周期门通过；新增模块沿用安全模式测试，不加载用户导入包。2/3/6组独立UDP测试进程均达到至少90tick，分配完整且互异的席位，完整世界hash一致，STOP共同收口后全部正常退出。3/4/6人内存故障网络覆盖丢包、重复、乱序和独立ACK；默认/加强射击的3/4/6席满池竞争机会均等。

另执行 `tools/build.ps1 -Configuration Release -PluginId trilink.thunder`，单插件重建及游戏/模块/桌面/元数据/目录门通过；更新前后22个EXE/DLL/JSON运行文件SHA-256逐一相同，证明确定性重建不会旁改其他运行包。最终未残留客户端或测试进程。

人工查看Release渲染图：`ui-thunder-coop-6.png`、`ui-thunder-six-minimum.png`、`ui-thunder-waiting-host.png`、`ui-thunder-waiting-join.png`、`ui-thunder-stopped.png`，六席HUD/配色、等待进度/分配人数和停止重绘可见，最小640×560窗口工具条与完整画布未裁切。截图属于自动化窗体渲染，不等同用户真实桌面验收。日志/截图位于 `artifacts/tests/<Configuration>/`；机器可读结果见 验证清单 (historical local evidence, not included: `../artifacts/tests/Release/verification-20261005.json`)。

交付入口为 `artifacts/Release/TriLink.MinClient.exe`，在“扩展模块…”打开“雷霆战机”。先从托盘退出旧客户端，再启动本目录的新程序；不要只复制EXE，运行需要同级DLL、plugins与profiles完整保留。

另提供 便携运行包 (historical local evidence, not included: `../artifacts/packages/TriLink-Thunder-0.2.0-Release-20261005.zip`)，22个运行文件逐条解压流SHA-256与Release一致；不含绝对路径快捷方式、用户配置、测试文件或固件。各电脑解压到新的独立目录，直接启动其中EXE；不要覆盖已运行的旧客户端目录。压缩包SHA-256和大小记录于验证清单。

真实用户桌面、跨两台及以上实体PC、ESP32无线桥接仍未验收；没有烧录、GPIO或防火墙变更。仅本地修改，不提交、推送或合并main。

## 扩展性

扩展状态：可扩展。传输稳定于IGameLinkFactory/IGameLink；目前UDP，不包含ESP32实现。适配器应单线程驱动、不等待对端，PC保有模拟与恢复，未来提供者不能另开独占COM争用现有串口所有者。玩家与协议容量是显式常量/严格字段，今后超过6人需同时评估输入向量、广播扇出、恢复窗口及UI，不只改一个上限数字。房主负责游戏提交顺序，不承担Room成员治理；多人扩展不提供生产共识或网络认证。

本次按local-exe-app-builder的约束保留接口/生命周期边界，分离源码、运行目录与测试证据，维持按需资源和有界I/O；未为游戏功能改动稳定的桌面壳、串口或Room实现。
