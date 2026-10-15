# TriLink validation record

> Public copy, redacted for publication on 2026-10-15. Board1/Board2/Board3 are stable public labels, not transmitted MAC addresses. Physical MAC/USB serial/PnP instance identifiers, local COM assignments and backup hashes are withheld. Original private records remain unchanged. Historical local evidence paths are not public downloads.

## 2026-10-14: third 0.16.0 board complete; native download-mode recovery

MAC `Board3`, CH340 [port-redacted], Boya JEDEC68:4018 16MiB/AP8MiB. Backup, unchanged partition, three writes/independent digests and35-second startup passed. Native USB initially exposed ROM download mode; read-only ROM evidence and corrected standard reset retained, then BOOT release/physical RESET restored application CDC [port-redacted]. Four DTR=false read-only transactions passed. NCM Code10 reproduced. [Board3 record](HARDWARE_0160_BOARD3_20261014.md); [three-board round overview](HARDWARE_0160_ROUND_20261014.md). No multi-board business or RGB acceptance.

## 2026-10-14: second 0.16.0 board flashing/startup/CDC passed

Target MAC `Board2`, CH343 [port-redacted], GD16MiB/AP8MiB. Initial security-read transport interruption preserved; retry succeeded before writing. Backup1114112B independently verified, unchanged partition, three writes and independent verification passed. One boot/35-second idle observation, native CDC [port-redacted] HELLO/CAPS/ROOMGET/SEARCH passed with DTR=false. NCM Code10 reproduced; no Room/RGB mutation or multi-board acceptance. [Board2 record](HARDWARE_0160_BOARD2_20261014.md) carries the independent receipt and limits.

## 2026-10-14: 0.16.0 flashing and native CDC passed; NCM failed

Sole attached target MAC `Board1`, CH343 [port-redacted], ESP32-S3 rev0.2, XMC16MiB/PSRAM8MiB. Fresh local backup `[0,0x110000)` independently verified; existing partition table matches. Three image writes and independent Flash digests passed. No full erase, partition/eFuse/source change or upload.

35-second UART observation: one boot, 0.16.0 / IDF5.5.5, TinyUSB CDC+NCM and ESP-NOW initialization, CORE_READY, stable idle heap, no panic/WDT/brownout. After cable change, native CDC [port-redacted] passed four read-only transactions with DTR=false. Microsoft NCM driver selected but Code10/0xC0000483 prevents network startup; no DHCP/web acceptance. Full evidence and precise limits: [single-board record](HARDWARE_0160_BOARD1_20261014.md). No Room mutation, RGB action, multi-board or long-load acceptance.

## 2026-10-09：唯一连接设备第2台烧写v0.10.2，启动与单侧发现观察通过

- 用户按同一时间只连一块ESP32的规则选择本轮第2台：CH343 [port-redacted]、MAC `Board1`，历史称一号机；本轮第1台MAC `Board2`历史称二号机，历史板号不改。
- 沿用验证包`20261001-025538-49e98315`，108输入及三BIN长度/SHA256一致；原分区表与目标相同。覆盖区`[0,0x110000)`1114112B备份，SHA256 `[backup-hash-redacted]`；三段写入及独立Flash校验通过，不改分区/SDK/eFuse、不全片擦除、不在烧录命令中写NVS/PHY。
- XMC `0x207018`保持已有窄补丁；工具恢复成功，UART已有SDK startup fail之后准确`20 70 18`接受并正常加载。SDK启动自动保存RF校准记录，不宣称运行数据逐字节未变。
- 35秒仅一次ROM启动，v0.10.2 / IDF5.5.5、16MiB Flash / 8MiB PSRAM自测OK、USB后端/ESP-NOW信道6/CORE_READY。6条core采样5292→30292ms；free heap224419→224431B后持平，min192148B、largest147456B，PSRAM free/min8386080B稳定；最低栈余量USB892、node1804、TX1924、控制器1380B。
- 本板单侧发现第一台MAC `Board2`，首次RSSI=-48dBm，持续peers=1/6；RX/TX成功15→90，RX acquired/released平衡，fail/timeout/drop/ownership error均0。最低MAC发现诊断不是Room leader；不把单侧发现当作双侧Room或业务ACK验收。
- USB命令数0，未做原生USB/客户端、Room、RGB、六邻居、长期压力或资源包/OTA验收；没有上传。见 [本轮第2台详细记录](HARDWARE_SINGLE2_20261009.md)。原构建收据保持历史状态，实际烧写另由机器收据记录。

## 2026-10-09：唯一连接设备第1台烧写v0.10.2，单板启动通过

- 按用户新规则：电脑同时只连接一块ESP32，按本轮连接顺序选择目标并记录MAC。此次CH343 [port-redacted]、MAC `Board2`；它在9月14日记录中称二号机，本轮称第1台，历史编号不改。
- 使用既有验证包`20261001-025538-49e98315`；108输入与三BIN哈希复核通过，原分区表与目标相同。覆盖区1114112B备份完成，三镜像写入与独立Flash校验均通过；不全片擦除、不写NVS/PHY区域、不改eFuse或分区布局。
- 35秒UART确认版本0.10.2/IDF5.5.5、16MiB Flash/8MiB PSRAM、内存测试OK、USB后端/ESP-NOW信道6/CORE_READY。内部heap稳定224323B，min192048B；栈最低USB892、node1808、TX1928、控制器1380B。TX成功至60、fail/timeout/drop/ownership error为0，未见异常或重复启动。
- 只通过单板烧写/启动/短时空闲稳态；USB命令数0、邻居0，客户端/Room/多板/物理RGB/长期压力未验收；任务包、复制、压缩与OTA未实现。未上传。本次证据见 [本轮第1台](HARDWARE_SINGLE1_20261009.md)。
- 下方“未烧录”和构建收据的Flashed=false均为对应日期的历史事实，不改原收据；本次硬件结果由单独flash-verification.json记录。

## 2026-10-01：v0.10.2 review 重加入修复

- FreshBuild `artifacts/builds/20261001-025538-49e98315`，原 IDF5.5.5 全新构建、镜像/分区/容量门 PASS。HardwareVerified、RoomIntegrationVerified、Flashed、Uploaded 均 false。
- App **806896 B**（较 0.10.1 +96 B），1 MiB 应用分区余 **241680 B**；静态 DIRAM149103 B、Room4472 B、route3560 B、RX3360 B、最大项目函数栈帧416 B均不变。新增本地 floor 未扩大对齐后的 Room 结构；运行时高水位未测。
- App SHA256 `4C93DD75640E94FC28C63992667A78D770BAA03439165F86DA1F71579DB3952C`，bootloader20944 B，SHA256 `83FBAB2DDF0C7008A36C03A9D5815BDE68C4DC20A626F6A055C4B06002BB4446`；BSP/bootloader 源码与配置未改变。
- 108 项构建/测试输入与工程 SHA256 对照 0 不一致；C14组94232项检查、0失败，Room565。旧实现新增乱序回归9失败，修复后恢复正常准入、ACK与后续成员变更。旧USB契约与Python3+5均通过。
- 空口 opcode2 / 132 B Room体不变；测试为主机 fake，不是无线、物理LED或并发压力证明。详细设计和保留边界见 [V0102_REVIEW_FIXES.md](V0102_REVIEW_FIXES.md)。

## 2026-10-01：v0.10.1 RGB 故障与退出交接收束

- FreshBuild `artifacts/builds/20261001-015520-156cebc6`，IDF5.5.5完整构建、镜像/分区/容量门禁PASS。receipt记录 `HardwareVerified=false`、`RoomIntegrationVerified=false`、`Flashed=false`、`Uploaded=false`。
- App **806800 B**，较0.10.0增加 **272 B**；真实1 MiB应用分区余 **241776 B**。静态DIRAM **149103 B**、Room **4472 B**、route **3560 B**、RX池 **3360 B**均不增加。最大项目单函数栈帧 **416 B**（bridge），Wi-Fi RX回调48 B、node task64 B；运行时堆/栈/调度时延未测。
- App SHA256 `160204A5224F9AEE4DEA9F9C882F0450BB5E11A6B7E1B50C8EB4AD20DFCDB333`；bootloader20944 B，SHA256 `1CD539BB8CBDF2D646E8D9FFBF8D9CC71467369604552A3A0537402212F17B2E`。bootloader来源和兼容补丁不变，新构建哈希本身不代表补丁改变。
- `source-manifest.csv` **108个输入文件**与当前工程逐一SHA256比对，**0处不一致**。构建后文档更新位于未作为编译输入的顶层README/docs，不改变编译源快照；本地LED驱动的补丁出处包含在源清单中。
- C **14组/94150项检查/0失败**，严格C11警告编译；当前完整日志 `artifacts/builds/20261001-015520-156cebc6/host-tests.log`。Room483、真实RMT驱动226、真实RGB worker622（11场景）、Room/RGB集成93；数量包括报文与循环断言，不表示九万种独立场景。
- Room交接丢包+立即CREATE/JOIN旧行为得到9项失败，修复后通过；RMT旧行为临时负例得到37项失败，但旧完整stage不存在，不称为精确旧镜像复现。当前驱动/worker测试使用fake SDK/任务边界，不验证物理无线、ISR或光学输出。
- 旧USB C→实际C#契约DEVICE1/PEER6/END1和Python3+5均通过。Room空口opcode2/RGB格式未改；本机USB status新增64故障、128退出公告，命令中不回传这两个健康位。
- 客户端完整构建Core136/Modules53/Desktop30/HardwareRoom45通过；最终增加退出公告显示后定向模块 **47项**通过，七清单/profile/部署哈希/干净运行目录门PASS。已查看故障与退出公告fixture图；fixture渲染不等于交互桌面验收。
- 本轮端口只读检查仍只有蓝牙[port-redacted]–[port-redacted]，无S3数据/烧录口。没有打开串口、烧录或操作GPIO；RGB默认关闭，第三板灯引脚未确认。
- 设计、S3专用有界停止依据与剩余门槛见 [V0101_FAULT_CLOSURE.md](V0101_FAULT_CLOSURE.md)。本轮未加文件/游戏业务、持久化、认证或故障选举；先做既定真机验收。

## 2026-09-22：v0.10.0 入群确认与阶段收束

- FreshBuild `artifacts/builds/20260922-084842-bc955f5b`，完整IDF5.5.5构建与镜像/容量门禁PASS；未烧录、未上传、未验证真实硬件。
- App **806528 B**（较0.9.1 +944 B），1 MiB应用分区余242048 B；静态DIRAM **149103 B**（+8 B）。Room **4472 B**（+8 B），route3560 B、RX3360 B不变。最大项目单函数栈帧416 B；运行时堆/栈高水位待测。
- App SHA256 `1AB5C6B15ECEF823B5872B6DC353D040605A74B32935D6B2A6C72A68558AFD6C`；bootloader SHA256 `A1042C2E0BF8169CFB70E854FA21C10D60576F00377B1F7E75BCF0919A7ED88C`。
- C12组93248断言：Room429、RGB集成93。旧实现迟到批准回归3项失败，修复后全部通过。另有旧USB C→C#契约及Python3+5通过。
- 客户端完整构建与136/53/30/42项回归通过，检查正常/空/暂停/未知四状态fixture图；未将自动渲染当作用户交互桌面验收。
- 协议改为Room opcode2；完整设计与失败边界见 [V010_ADMISSION.md](V010_ADMISSION.md)。新旧固件不可混合组Room，固件仍仅本地保存。

## 2026-09-20：v0.9.1 版本与重放边界加固

- 全新构建：`artifacts/builds/20260920-085546-21a06627`，`verification.json`各构建/容量门禁PASS，硬件/烧录/上传均为false。
- App805584 B，较0.9.0增加352 B，1 MiB应用分区余242992 B；静态DIRAM149095 B，仅增加120 B（六项固定RGB序号水位）。Room4464 B、route3560 B、RX3360 B不变，最大项目栈帧仍512 B。
- App SHA256 `69ACD0DCF9994A46F43175EF7EDDB1F9D75AC537754E358C2E3CA9D81F242E20`。
- C12组93138断言：Room319、RGB集成93，先复现旧Room命令误执行和旧RGB请求重放，再修复通过；旧USB契约及Python3+5探针测试通过。
- 客户端全构建、核心136/模块53/桌面30回归通过；hardware-room0.1.1专属37项通过，含第4次继续/第5次暂停、选择保留、失效清除、输入错误无写入、暂停取消批次，检查正常/空/暂停渲染图。
- 软件测试不替代真实无线、LED、USB插拔和交互桌面验收。入群申请超时/迟到批准协调等剩余限制已明确保留；未引入自动选举/持久化/新业务。

## 2026-09-17：v0.9 Room / RGB，软件集成与完整构建通过

- 12 组 C 主机测试共 **93101 项断言、0 失败**：原路由覆盖保留，Room 299、Room/route/bridge/RGB 集成76、扩展USB parser1092。计数包含逐包/循环断言，不是93101种独立场景。
- Room：六人、全连接与六节点链、审批/普通成员邀请、正常leader继任、两人降一解散、断链冻结/恢复、丢失DENY重试、旧Room审批隔离。
- 集成：真实C协议/桥接/路由、fake无线与LED；默认禁用、受理/完成/驱动失败、重复应用ID、不同内容冲突、仅传输ACK最终结果未知。不是RTOS/物理无线/光学测试。
- 旧USB C→实际C#契约通过（DEVICE1/PEER6/END1），Python chain probe3 / mesh probe5通过。
- 固件最终全新构建目录 `artifacts/builds/20260917-233743-5485439d`。ESP-IDF5.5.5，N16R8、DIO80MHz、分区、bootloader兼容补丁未改变。
- App **805232 B**，1 MiB分区余243344 B；静态DIRAM **148975 B**（工程门限196608），物理DIRAM静态余192785 B不等于运行时空闲堆。
- Room静态状态 **4464 B**，route3560 B，RX池3360 B；项目最大单函数栈帧512 B（room input），Wi-Fi回调48 B、node task64 B。RGB worker配置3072 B栈，实际高水位/堆峰值待测。
- App SHA256 `E84BD9B34ED3C601C41E0093972895DFC61757622762DC395979AEF5C38EBF03`；完整receipt为同目录 `verification.json`，C日志为 `host-tests.log`。
- 客户端新增独立模块与fake服务窗口回归；发布物/测试分开。具体客户端检查以 `trilink_min_client/artifacts/tests/Release/logs/` 为准。
- **未烧录、未上传、未进行三板 Room/RGB 实测**。尤其三号板灯引脚未知，所有设备RGB默认关闭。
- 规格、驱动出处和限制见 [V09_ROOM_RGB.md](V09_ROOM_RGB.md)。既有 v0.8 记录保留如下。

## 2026-09-16：v0.8 六成员自动路由，软件构建验收通过

按后续确认将固定链替换为最多六成员（含本机）的自动中继；规格与未完成边界见 [V08_AUTO_ROUTING.md](V08_AUTO_ROUTING.md)。未烧录、未上传、未对真实 COM 运行探针。BSP、Flash/PSRAM 参数、分区与 ESP-IDF 版本未变。

### 软件验证

- 10 组 C 主机测试，共 **92697 项断言、0 失败**；编译选项 `-std=c11 -Wall -Wextra -Werror -pedantic`。
- 路由组 90667 项：任意位置中继、六节点五跳双向、菱形跨路径去重、改路重试、DATA/ACK 丢失、relay 关闭/恢复、非对称链路、拓扑过期/重复不续期、重启旧拓扑、成员指纹不一致、缓存/接收队列背压、身份/序号错误等。
- 128 个确定性六节点图，对所有有序源/目标组合比较路由结果和独立 BFS 的跳数，并验证可达端对传输。断言总数包含报文级检查，不表示执行了九万种独立网络场景。
- USB v3 parser 1063 项，实际 bridge 源码配合单线程队列适配器 108 项；后者不是 RTOS 并发/无线驱动测试。
- 旧真实 C# USB 契约：DEVICE=1、PEER=6、END=1。Python：旧 probe 3 个测试、新 mesh probe 5 个测试全部通过；新工具 `--help` 已验证，未使用真实串口。
- 客户端全量构建通过，随后 Room 插件 1.1.0 定向构建通过：Core 136 项；既有模块 53 项、桌面模式/生命周期 30 项通过。新增满员申请/邀请/批准、待审批不超额、空位重试、六副本一致性和顺序继承检查。UI 冒烟图已检查，但未当成真实交互桌面验收。

### 最终构建证据

- 路径：`../artifacts/builds/20260916-183648-00afbe9a/`，含 verification.json、source-manifest.csv、build.log 和三个 BIN。
- ESP-IDF v5.5.5 / ESP32-S3 构建、镜像、分区、链接符号、静态容量检查均 PASS。
- 本轮先完成全新构建；最后增加容量检查时发现 map 中短符号单行、长符号换行，修正检测表达式后在明确的本轮临时目录增量校验。最终 receipt **FreshBuild=false**，不宣称最终包为全新构建。
- 最终源清单 **70 个文件**逐个对照工作区 SHA-256，**0 处不一致**；最终 BIN 哈希也已重核。
- 应用：**773024 B**，实际 factory 分区 1048576 B，剩余 **275552 B（约 26.3%）**；较 v0.7 增加 3488 B。容量脚本默认 app 预算已收紧到真实 1 MiB，而非原先 2000000 B 策略值。
- 静态 DIRAM：**139575 B**，较 v0.7 增加 **2088 B**；物理 DIRAM 扣静态后 202185 B，**不是运行时堆实测**。
- S3 链接后的路由状态：**3560 B**；Windows 主机 sizeof=3576 B，ABI 对齐不同。编译期及 map 均检查不超过 4096 B。
- 最大项目静态栈帧 272 B、RX 回调 48 B、节点任务入口 64 B，RX 池仍 3360 B。单函数静态栈帧不是最坏调用链或运行时栈高水位。
- 应用 SHA-256：`263D345EAA1A2D7377A3CAD46D6EAC3FA0EB100030A097D2C1BCAA38D79ACBE0`。
- `HardwareVerified=false`、`RoomIntegrationVerified=false`、`Flashed=false`、`Uploaded=false`。

### 接续验收门

真实 Room 申请/审批/成员提交、USB 唯一会话调度及 RGB 执行尚未实现。现有 CONFIG 只是测试 roster，不能代替 Room 准入；最终目标仍未完成。三板须分别轮换中继，验证真实不可直达时经第三板双向传送、断链有界失败/改路、恢复及至少 30 分钟栈/堆/队列压力。六节点五跳目前只由主机图测试覆盖。

当前 32 条/30 秒目标去重缓存偏低频控制，不构成文件或高频游戏吞吐验收；后续需独立流控/滑窗设计，不得只扩大缓存并声称容量足够。

## 2026-09-16：v0.7 两跳传输底座，软件验证通过，硬件待验收

本轮在主工程实现固定三节点传输，不修改旧双板工程、不烧录、不上传。
范围和限制见 [V07_CHAIN_TRANSPORT.md](V07_CHAIN_TRANSPORT.md)。真实 Room 后端和 RGB 执行仍未实现，不能将本轮结果标为首版目标完成。

- 10 组 C 主机测试：2983 项断言、0 失败；均以 `-std=c11 -Wall -Wextra -Werror -pedantic` 编译。
- 路由测试 978 项：两跳双向、最大 200 B 完整空口帧、CRC、回执丢失重传、重复去重、错误 room/epoch/跳数/回执会话、中继关闭/恢复、队列满、去重缓存满和序号耗尽。
- USB v2 解析 1057 项；真实 bridge 源码配合单线程队列适配器 93 项；适配器不证明 RTOS 调度正确。
- 真实旧 C# USB 解析器契约通过：DEVICE=1、PEER=6、END=1。三板探针 Python 单元测试 3 项通过；探针未对实板运行。
- ESP-IDF v5.5.5 / esp32s3 编译、镜像、分区与容量检查 PASS。初次为新建构建目录；最终版本复用该明确目录做增量构建，记录 `FreshBuild=false`，不伪称最终版本全新构建。
- 最终证据目录：`../artifacts/builds/20260916-173843-fea25ac3/`；包含 `verification.json`、日志、源清单和三个烧录 BIN。
- 最终源码清单 68 个文件与工作区逐个 SHA-256 比对，0 处不一致。
- 应用 769536 B；实际 factory 分区 1048576 B，剩余 279040 B。这里的 27% 是 factory 分区余量，不是整块 16 MiB Flash/8 MiB PSRAM 的余量。
- 静态 DIRAM 137487 B，物理 DIRAM 扣静态后 204273 B；这不是运行时可用堆实测值。
- 最大项目静态栈帧 272 B，RX 回调 48 B，节点任务入口 64 B；RX 池仍 3360 B。已增加 TX worker 的运行时栈高水位日志，实测待板子连接。
- 应用 SHA-256：`50F09D321136A6575EE417FFAB6213C8631C0672E1264A33BBAD84632E1D315E`。

硬件只读检查：WMI 与实际客户端 SetupAPI 枚举均仅见 [port-redacted]–[port-redacted] 蓝牙串口，没有 S3 数据口或烧录桥；后者 11 ms 完成，未打开任何 COM。故本轮 `HardwareVerified=false`、`RoomIntegrationVerified=false`、`Flashed=false`。

下一阶段仍需实现真实 Room 申请/审批及成员快照、接入设备双闪执行，再做三板烧录、真实两跳/跨范围/断电恢复和至少 30 分钟栈堆压力验收。配置夹具和主机测试不能替代这些门槛。

## 既有硬件与历史记录

最新硬件进展（2026-09-14）：[一号机烧录与 35 秒启动检查](HARDWARE_BOARD1.md)通过；
MAC `Board1`、CH343 [port-redacted]。
[二号机烧录与 35 秒启动检查](HARDWARE_BOARD2.md)也通过；
MAC `Board2`、CH343 [port-redacted]，使用与一号机相同的 v0.6.0 镜像。
[三号机烧录与 35 秒启动检查](HARDWARE_BOARD3.md)通过；
MAC `Board3`、CH340 [port-redacted]，三块板使用相同的 v0.6.0 镜像。
原生 USB 握手及多板验收尚未完成。

> 以下为历史结果，不代表 v0.6 硬件验证。当前更新范围见
> [V06_USB_DISCOVERY.md](V06_USB_DISCOVERY.md)，新构建证据在 `artifacts/builds/`。

v0.6 当前软件验收（2026-09-14）：855 checks / 0 failures，实际 C# USB 解析器兼容 PASS，
ESP-IDF 编译/镜像/容量 PASS。应用 762960 B，静态 DIRAM 126843 B；该软件验收时尚未烧录，
后续一、二、三号机烧录结果见上方独立硬件记录。
精确过程、增量构建标记和产物位置见上述 v0.6 文档。

Last software-only verification: 2026-09-13.

## v0.5.0 host regression

```powershell
.\tools\test_host.ps1
```

Result:

- `wakenic_state`: 185 checks, 0 failures;
- `trilink_protocol`: 113 checks, 0 failures;
- `trilink_service`: 63 checks, 0 failures;
- `trilink_content`: 142 checks, 0 failures;
- `trilink_file_wire`: 142 checks, 0 failures;
- `trilink_cluster`: 132 checks, 0 failures;
- total: 777 checks, 0 failures.

Every suite is compiled as a fresh Windows C11 executable with `-Wall`,
`-Wextra`, `-Werror` and `-pedantic`, run, then removed. v0.5 coverage includes:

- strict UTF-8 boundaries: valid 1/2/3/4-byte sequences, truncation, overlong
  encodings, surrogate code points, values above U+10FFFF and embedded NUL;
- all-zero and non-zero 32-byte resource IDs;
- exact little-endian CONTENT layouts and borrowed-pointer positions;
- zero/max text, summary, name/detail, range overflow and malformed service
  envelope cases;
- exact FILE OPEN/CHUNK/ACK/CLOSE/CANCEL layouts;
- 1 B and maximum 158 B chunks, offset overflow, invalid transfer/window/status
  values, cumulative ACK plus bitmap and zero bitmap;
- CONTENT/FILE -> APP_DATA -> 200 B TriLink wire -> borrowed view composition.

## v0.5.0 fresh ESP-IDF build

The evidence directory did not exist before the build. It was created with
`-NoCcache -RequireFresh` and a separately generated sdkconfig:

```text
<private-work-root>\verify_trilink_v050_final_20260913b
```

Verified result:

- project `esp32s3_wakenic_p2p` v0.5.0;
- ESP32-S3, ESP-IDF v5.5.5, minimal build enabled;
- 52 non-empty application components;
- required `esp_wifi`, `esp_netif`, `nvs_flash` and all four `trilink_*`
  components present;
- no TinyUSB/NCM component prematurely linked;
- application 753,040 B in a 1,048,576 B factory partition;
- bootloader 20,944 B;
- image header 16 MiB, DIO, 80 MHz; config has 80 MHz Octal PSRAM;
- valid checksum and validation hash for application and bootloader;
- bootloader contains `xmc_7018_compat` and
  `--wrap=bootloader_flash_xmc_startup`;
- application SHA-256
  `24BAE8748E9F217EB291E250D8BA22186F9C20507287FDB0DCEE7F60A07CBE90`;
- bootloader SHA-256
  `D132F00DDD7CF9E2C2B279D3167211FAE36DADC4BA7FB13317640C5CAF5ABC1A`.

The Git safe-directory warnings in the build log concern the sandbox account
reading ESP-IDF installation metadata. The pinned project version is set in
CMake; compilation, linking, image generation and both independent verifiers
completed successfully.

## v0.5.0 capacity and dormant-code gate

`tools/verify_capacity.ps1` passed under PowerShell 7 and Windows PowerShell
5.1. Result:

- app: 753,040 / 2,000,000 B, headroom 1,246,960 B;
- static DIRAM: 122,543 / 196,608 B, headroom 74,065 B;
- physical DIRAM left after static sections: 219,217 B;
- largest project static function frame: 272 / 1,024 B,
  `log_diagnostics`;
- linked RX pool: exactly 3,360 B;
- dedicated `radio_rx_task`: absent;
- `radio_receive_cb`: 48 / 64 B static-frame gate;
- `trilink_node_task`: 64 / 96 B static-frame gate;
- zero-copy frame view: linked;
- service/content/file codec objects: each compiled once with project
  warnings-as-errors;
- service/content/file codec symbols in current application map: all absent,
  as required before APP_DATA dispatch exists.

Object-level `xtensa-esp32s3-elf-size` evidence:

| object | text | data | bss |
|---|---:|---:|---:|
| `trilink_service.c.obj` | 312 B | 0 B | 0 B |
| `trilink_content.c.obj` | 2,120 B | 0 B | 0 B |
| `trilink_file_wire.c.obj` | 1,673 B | 0 B | 0 B |
| total | 4,105 B | 0 B | 0 B |

This object total is a conservative all-functions reference for later
activation, not current linked cost. The clean v0.5 image is only 16 B larger
than v0.4 because of changed version/phase metadata; static DIRAM is identical.
The largest codec function static frame is 64 B.

## Retained v0.4.0 baseline

The final v0.4 evidence remains at:

```text
<private-work-root>\verify_trilink_v040_final_20260913
```

It passed 492 host checks and produced a 753,024 B application with 122,543 B
static DIRAM. It introduced the 16-slot fixed RX pool, removed the project radio
RX task and second frame queue, and added the generic service envelope. v0.5
preserves that runtime path.

## Retained v0.3.0 baseline

The final v0.3 evidence remains at:

```text
<private-work-root>\verify_trilink_v030_final_20260910
```

It passed 408 host checks and built an application of 752,512 B with 118,951 B
static DIRAM. Its RX path used two 16-deep 210 B queues, a 4,096 B radio RX task
and owning frame decode. It remains the measured pre-optimization point.

## Hardware gate: pending

No ESP32 board was connected and no `flash` command was executed in this run.
v0.5 is software-qualified only; its new codecs are not reachable at runtime.

For each board, one at a time:

1. bind the CH343 instance and record port, location path and ContainerId;
2. capture cold boot and confirm v0.5.0, MAC, JEDEC, 16,777,216 B Flash and
   8,388,608 B PSRAM;
3. confirm `BOOT -> INIT -> CORE_READY` without panic, WDT or reset;
4. retain heap, largest-block and stack diagnostics.

For all three boards together:

1. require two peers and the same Leader MAC on every node within 3 s;
2. confirm broadcast HELLO plus bidirectional unicast HEARTBEAT counters;
3. detach each non-Leader and then the Leader; verify 2 s expiry and re-election;
4. run 30 minutes with `radio_rx_drop=0`, `rx_owner_err=0`, equal
   acquired/released counts, `rx_in_use` returning to zero and no monotonic heap
   loss;
5. record RSSI and TX completion/failure/timeout counters.

Only after this gate may the three-board ESP-NOW runtime path be called hardware
PASS. USB CDC ACM, APP_DATA dispatch, text exchange, file reliability,
encryption and wake remain separate later gates.
