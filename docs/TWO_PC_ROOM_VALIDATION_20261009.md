# 0.14.2 两电脑Room准入验证与队列体验边界

日期：2026-10-09。本次使用既有客户端0.14.2-local、desktop1.2.1、serial1.2.2、hardware-room0.3.1、Host API1.0；配套S3固件仍0.10.2。该版本的软件基线仍为Release/Debug各985项，本次增加真实Room证据，不修改它的源码发布快照、运行目录或便携包。**0.14.3的queue优化正在开发，尚未完成或验收**，不能借用本次985项或两电脑通过记录作为新版本证明。

## 结论与证据层级

本机真实原生USB读取申请、经真实首页`HardwareApprove`按钮批准一次，S3提交第二成员并达到`synchronized=True`；用户明确确认电脑2显示同样的2/6、leader、revision与成员顺序。可记录为**两电脑一次JOIN/审批/确认/提交的功能闭环通过：本机仪器证据＋远端用户确认**。

没有电脑2截图或其原始ROOMGET帧，所以不是双端完整、可重放的仪器验收；助手为赶固件8秒准入窗口，使用快读请求/状态注入实际视图，再触发同一产品按钮，绕过0.14.2默认整页刷新时序。此次不证明默认人工操作在8秒窗口内稳定可用，也不是三板、踢人、邀请、掉电或leader继任通过。

## 参与者与原状态

| 参与者 | 已知身份 | 本次角色与证据 |
| --- | --- | --- |
| 电脑1 / 本机S3 | COM_PORT，`Board1` | 既有Room的leader；本机真实USB日志与自动窗体图 |
| 电脑2 / 对端S3 | `Board2` | 发起新JOIN后成为普通成员；本机记录其票据/boot，用户确认远端界面 |

原Room未创建或替换：room=16、incarnation=44360405、revision=1、term=1、count=1、synchronized=True；成员为Board1、boot44360405、order0，Room名`TriLink Room`。

## 一次准入链路

1. 本机真实首页使用共享命令owner识别COM_PORT，读取既有leader状态。
2. 电脑2发起新的JOIN；本机记录Board2、ticket=19、boot=4247252633，使用当前票据而非重放旧申请。
3. 临时助手快读真实本机状态及申请，转为视图实际所属程序集的实例，注入当前视图后触发`HardwareApprove`一次；不另造模拟成员，也不绕过S3权限/版本校验。
4. 本机读回COMMITTED：room/incarnation不变、revision=2、term=1、count=2、synchronized=True。日志确认leader已提交对端并收到成员同步ACK。
5. 用户确认电脑2为普通成员，显示2/6、leader Board1、revision2与相同成员顺序；尚无远端截图/raw证据。

| 提交后join_order | MAC | 成员boot |
| ---: | --- | ---: |
| 0 | `Board1` | 44360405 |
| 1 | `Board2` | 4247252633 |

本机原始状态帧及前后字段保留在日志，不从仅发现MAC/RSSI推导Room成员。这里的Room leader来自真实成员状态，不是发现层`lowest-mac`诊断。

## 测试助手与失败记录

第一次助手运行在只读刷新后出现`InvalidCastException`：相同RoomWire类型分别从产品`LoadFrom`上下文和助手`Default`上下文加载，不能直接强转。异常发生在本轮Room写入前；修复的是临时助手加载/转换方式，不是产品源码、固件或协议。

成功助手复用实际主表单工厂、视图和共享USB owner；快读后的对象转换使用视图所属程序集，实际执行仍由真实按钮提交。`pc1-request.png`和`pc1-committed.png`是自动窗体渲染图，可展示申请及2人成员状态；不是用户桌面人工点击验收。首次失败日志也保留，不隐藏成全部尝试均成功。

## 关闭后的只读核对与保护边界

本轮测试助手/自动窗体退出后，再次独立只读读取本机：Room仍count2、revision2、term1、同步标志成立，成员顺序保持。**这只证明该助手退出不自动执行LEAVE，且当时本机S3仍持有RAM副本**；不证明电脑2客户端已退出、远端掉电、跨重启持久化或长期分区恢复。

此次没有CREATE、LEAVE、KICK、RGB许可/执行或固件写入，不自动拆用户当前Room。后续日志读取的RGB `00000001 / 2`及界面RGB完成文字，是22:46那次本机双闪的历史结果，不是本次新点灯。

当前两人Room已成正式状态，任一成员正常退出后人数降为一会解散；验证leader继任且Room继续保留至少要三成员。不能为证明关闭/继任而擅自让用户任一成员退房或重启。

## queue仍待优化：不能扩大本次通过范围

用户反馈0.14.2默认整页约20项读取、`_busy`期间管理按钮整体禁用，遇到8秒准入窗口时难以人工及时审批。此次助手有意绕过这个时序瓶颈，**协议能通过不等于默认队列体验已经解决**。

0.14.3 queue优化仍在开发，后续应独立验证轻量状态/审批刷新、忙时管理意图与新票据/版本核验、未知写结果不自动重放、取消/切换/失败熔断不偷偷恢复等。这里只记录待验收方向，不宣布具体调度实现、完成数量或新交付已通过；不通过放宽leader权限、伪造申请或延长未更改的固件期限解决。

## 本地证据

归档根：`artifacts/tests/TwoPC-20261009-4f631ff5/`；原始临时目录为`<TEMP>/tl-two-4f631ff5/`。日志、源码与图片属于本地测试材料，不混入0.14.2便携包、不上传固件/日志。

- 成功准入日志 (historical local evidence, not included: `../artifacts/tests/TwoPC-20261009-4f631ff5/two-pc-room-ready.log`)
- 初次助手加载失败日志 (historical local evidence, not included: `../artifacts/tests/TwoPC-20261009-4f631ff5/two-pc-room.log`)
- 本机申请图 (historical local evidence, not included: `../artifacts/tests/TwoPC-20261009-4f631ff5/pc1-request.png`)、本机提交后图 (historical local evidence, not included: `../artifacts/tests/TwoPC-20261009-4f631ff5/pc1-committed.png`)
- 助手退出后的只读日志 (historical local evidence, not included: `../artifacts/tests/TwoPC-20261009-4f631ff5/post-client-close-read.log`)
- 临时测试助手源码 (historical local evidence, not included: `../artifacts/tests/TwoPC-20261009-4f631ff5/TriLink.TwoPcRoomProbe.cs`)、本次独立机器收据 (historical local evidence, not included: `../artifacts/tests/TwoPC-20261009-4f631ff5/verification-receipt.json`)

既有软件/单板RGB交付见 [0.14.2单板记录](SINGLE_NODE_CLOSURE_20261009.md)，早轮历史见 [0.14.1上位机记录](CLIENT_CLOSURE_20261009.md)。本记录遵循设备证据与自动窗体/用户确认分层，文档收束不操作设备、不修改固件或旧收据。
