# v0.10.1：RGB 故障与退出交接收束

> Public copy, redacted for publication on 2026-10-15. Board1/Board2/Board3 are stable public labels, not transmitted MAC addresses. Physical MAC/USB serial/PnP instance identifiers, local COM assignments and backup hashes are withheld. Original private records remain unchanged. Historical local evidence paths are not public downloads.

2026-10-01，本地更新，不上传固件、不自动烧录、不操作 GPIO。配套客户端硬件模块为 0.2.1；产品 0.14.0-local / Host API1.0 / 七插件组合不变。

## 本轮范围

沿用 ESP32-S3 N16R8、ESP-IDF5.5.5、HW-678A BSP、GPIO48、WS2812 GRB 单灯、无 DMA。Room service5/opcode2、132 B Room体、200 B无线帧与 RGB编码均不变；0.10.0协议可互通，但完整修复需每个节点使用0.10.1，不能混用0.9.x。

没有新增任务、Room缓存、文件缓存、自动硬件重试或其他业务。原厂 LED 驱动仅本工程 vendored 副本做局部补丁，SDK和参考工程未修改；出处与上游校验保留，补丁另记于 [LOCAL_PATCHES.md](../components/led_strip/LOCAL_PATCHES.md)。上游 CHECKSUMS 不代表本地补丁后的哈希。

## RGB：有界等待与保留资源

原驱动的提交入队和 `wait_all_done(-1)` 都可能无限等待。当前驱动设置 `queue_nonblocking=1`，完成等待改为100 ms；实际 RGB worker 顺序独占单灯，最多一个在途事务，因此不把“每事务100 ms”误写成任意队列的总时限。

启用通道成功后，提交或等待失败仍尝试停止；保留原始失败结果。停止失败时保留 channel-enabled 状态、通道和像素缓冲。故障句柄拒绝 set/clear/refresh，避免在途缓冲变更或新增发送。成功删除的通道/encoder句柄置空；应用清理仅尝试一次，任何删除失败都隔离保留资源至重启，不能假设SDK部分失败后可以安全重删。

worker 出错后锁存故障、关闭 RGB使能，发布失败结果；消费结果仍释放 busy，但不能通过“重新启用”恢复。此后不再 clear/refresh/delete 或分配替换驱动。停止失败不保证物理灯熄灭，错误回执也不是光学测量。

上述停止有界性仅对应当前 S3：SDK的 `SOC_RMT_SUPPORT_ASYNC_STOP=1`、无DMA路径使用异步停止与零等待队列操作。不能泛化到其他芯片/SDK/DMA设置。worker 无任务时的队列阻塞是空闲等待，未把它改成无意义轮询。真实任务调度、ISR、故障时延和接线仍待实测。

USB `ROOMGET page0` 本机 status 数值位64（bit6）表示故障锁存。客户端显示检查接线、协调退出Room后重启设备；不自动重启设备或重试GPIO操作。RGB仍默认关闭，第三板GPIO48未知，禁止未经确认启用。

## Room：退出后保留旧公告

原 leader 退出后会重播旧Room的交接/解散快照10秒；但立即 CREATE/JOIN 会覆盖唯一的旧快照。首个交接包丢失时，剩余成员可能永远保留旧leader。

现在用统一 `trilink_room_retiring(room, now)` 判断原 leader 的退出公告窗：同时控制重播、CREATE/JOIN拒绝和本机status数值位128（bit7）。条件是 `now < retired_until`；截止前1ms仍拒绝，精确到期放行。无需新增结构字段或扩大缓存。

客户端窗口显示“正在重播退出公告；退出后10秒内暂不能创建或加入，请稍后刷新”。不自动创建/加入；普通成员退出不触发原leader的限制。

这是有限公告保护，不是可靠共识、故障选举或掉电恢复。若整个10秒窗内通信都失败、原leader断电或永久分区，仍不承诺自动恢复。入群已确认但结果未知等原边界见 [准入协议](V010_ADMISSION.md)。

## 验证层次

- C14组 **94150项检查、0失败**，严格C11警告编译。计数包括循环/报文断言，不是94150个独立网络场景。
- Room483项：首交接快照丢失后立即CREATE/JOIN、恢复链路后的继任与同步、精确公告截止。旧行为483项中9项失败，修复后全通过。
- 实际RMT驱动226项：非阻塞标志、100ms等待、各API故障、缓冲冻结、停止/删除路径。fake RMT边界不是硬件计时；分步删除故障mock不证明SDK部分失败可重试。
- 实际RGB worker622项/11场景：默认零初始化、正常双闪、结果消费与busy释放、队列失败、首轮/第二轮驱动失败、故障锁存和资源隔离。fake任务单线程执行，不是RTOS/ISR压力测试。
- Room/route/bridge/RGB软件集成93项；无线与LED仍为fake。旧USB C→实际C#契约及Python3+5测试通过。
- 客户端Core136、Modules53、Desktop30通过；硬件模块最终定向47项、部署哈希/profile/七清单/UI/干净运行目录门通过。正常/空/暂停/未知/故障/退出公告fixture不代表用户交互桌面验收。
- 全新IDF构建、镜像/容量门通过，具体源清单与占用见 [VALIDATION.md](VALIDATION.md)。未烧录、未上传、未验证三板Room或灯光。

当前只读端口清单只有蓝牙[port-redacted]–[port-redacted]，无可验收的S3数据口。下一步按既有计划先确认真实桌面入口，再逐板确认身份/原生USB/引脚及烧录授权，验证正常准入、退出继任、断链恢复、RGB结果与至少30分钟堆/栈/队列压力；不先堆文件或游戏功能。
