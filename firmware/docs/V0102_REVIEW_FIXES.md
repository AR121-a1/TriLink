# v0.10.2：重加入的成员版本下限

2026-10-01，本地 review 修复。沿用 ESP32-S3 N16R8 / ESP-IDF5.5.5 / HW-678A BSP、引脚、分区、六成员上限；不烧录、不操作 GPIO、不上传固件。

## 故障与修复

同一 boot 的成员被踢后重新申请，在 OFFER 已接收、CONFIRM 延迟期间，超过两秒去重窗口的旧成员快照可能提前清除 waiting。随后当前移除快照令申请者变为 inactive，但 leader 又收到合法 CONFIRM 并提交成员。双方状态不一致，缺少申请者 ACK，成员变更冻结。

现在申请者在覆盖 request.revision 为票据之前保存本地 `join_revision_floor`：取有效发现快照和同 Room 已知本机状态中的较大成员版本。等待期间，同一 leader 的更新若不含申请者，只提高 floor，不取消等待。仅在已 CONFIRM、快照含本机 MAC/boot、成员版本严格高于 floor 时完成加入。

有效发现槽拒绝更低 revision 的广告；已含本机同 boot 的发现基线不能开启新申请。暂停于 UNKNOWN 的原申请仍使用 ROOM_RETRY，不以新申请替代。floor 不是空口字段；132 B Room 体、service5/opcode2 和 200 B 无线帧不变。

这不是认证、防无限重放或永久历史存储。leader 切换/掉电期间未决确认、永久分区、跨重启恢复等原限制仍存在。0.10.0/0.10.1 空口可互通，但不具备本次防护；三板验收应统一 0.10.2，不能混入 0.9.x。

## 回归与容量

- 先将重加入乱序回归加入旧实现，Room 539 项中 9 项失败；修复并补齐发现/并发准入回归后 Room 565 项、0 失败。
- 覆盖旧快照越过去重窗口、迟到 CONFIRM、当前移除公告不取消等待、恢复后 ACK 收敛和继续踢出；另覆盖等待中其它成员先加入，以及重申前发现广告不倒退。
- 14 组 C 测试共 94232 项检查、0 失败；数字包含逐包/循环断言，不是独立场景数。旧 USB C→C# 契约、Python 3+5 测试通过。无线/SDK/LED 为 fake，不是三板或 RTOS 时序实测。
- 全新构建 `20261001-025538-49e98315`，镜像、分区、容量门通过。App 806896 B，较 0.10.1 增加 96 B，真实 1 MiB 应用分区余 241680 B。
- 本地 floor 利用结构对齐后的空间；S3 链接后的 Room 仍 4472 B、静态 DIRAM 149103 B、route 3560 B、RX 池 3360 B。无新增任务或快照缓存；最大项目单函数栈帧仍 416 B。
- App SHA256 `4C93DD75640E94FC28C63992667A78D770BAA03439165F86DA1F71579DB3952C`。receipt 中 FreshBuild=true；HardwareVerified、RoomIntegrationVerified、Flashed、Uploaded 均 false。

具体源码清单、构建日志和镜像位于 `artifacts/builds/20261001-025538-49e98315/`。旧构建与验证记录保留。后续先验收原生 USB、真实桌面和三板 Room，再逐板确认 RGB 引脚和长稳；不先扩展文件/游戏业务。
