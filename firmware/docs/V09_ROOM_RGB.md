# v0.9.0 — 易失 Room 副本与 RGB 执行闭环

历史文档：0.10.0 起改用 Room opcode2 与 OFFER/CONFIRM，覆盖下述 opcode1、直接批准和超时说明。当前 0.10.1 另补上 RGB 故障有界处理与退出交接保护；以 [当前准入协议](V010_ADMISSION.md) 和 [当前收束记录](V0101_FAULT_CLOSURE.md) 为准。下面版本段保留历史证据。

## v0.9.1 增补（2026-09-20）

- 本地Room命令除room/incarnation外必须匹配revision/term；无线LEAVE同样拒绝旧版本。PC应刷新后重新提交，不自动重放失败写命令。
- RGB增加六项固定的当前成员应用序号水位；当前执行/结果槽之外的过旧请求不再触发执行。离开/重启清除对应水位，不是持久化认证或永久exactly-once。
- 已完成/失败/拒绝终态不接受矛盾覆盖；迟到受理不复活未知请求，迟到终态允许解决未知。Room变化令未决发送显示未知。
- 12组C测试93138断言（Room319、集成93），无线和驱动仍为fake。完整构建结果以最新receipt为准，未进行烧录或硬件验收。
- 原有申请超时/迟到批准协调、RMT故障等待、掉电恢复等限制仍待后续单独处理，不宣称此次已解决。

以下为0.9.0基础设计；上述增补优先。

2026-09-17。本地固件，不上传。沿用 ESP-IDF 5.5.5、N16R8 BSP、DIO/80 MHz、分区和 XMC bootloader 补丁；不自动烧录。

## 层次与所有权

- USB worker：有界 ASCII 解析和应答，命令深度4、应答深度4；只投递，不修改 Room。
- node task：唯一 Room / route / 应用回执状态所有者。固定 RX 池16槽/3360 B 保持不变。
- `trilink_room.c`：无动态分配，成员6、附近房间6、申请6、邀请6、去重32，状态编译期上限6144 B。
- `room_changed`：把当前 Room 成员映射为 route roster，revision 映射为 route epoch；成员变更使旧路由事务终止。已有待执行 RGB 会完成自身低亮双闪，但跨旧 revision 的应用回执不会混入新 Room。
- `trilink_rgb.c`：静态深度1任务队列、独立3072 B worker 栈，默认禁用；首次显式启用且有任务时才创建 RMT 灯驱动。node/Wi-Fi 任务不执行灯效延时。
- `components/led_strip`：复用本机已用工程的 Espressif 3.0.3 驱动，保留 LICENSE 和 [来源](../components/led_strip/PROVENANCE.md)，不修改原工程。

## Room 控制面

外层仍为200 B TriLink帧，APP_DATA中承载 service=5/opcode=1，4 B service头 + 132 B固定Room体。
源广播 TTL=5，最多5次中继转发；这与数据面最多5跳的 route TTL 是不同计数语义。未入群节点也可转发控制面，但 RGB 数据中继必须在已配置成员内。
广播通过来源MAC/boot/operation/规范化内容CRC去重，32项、2秒；同ID内容冲突拒收；仅接收已发现物理邻居送来的Room帧。

| 字节偏移 | 字段 |
| --- | --- |
| 0 / 1 / 2 / 3 | kind / TTL / member_count / formed |
| 4 / 10 / 14 | source MAC6 / source boot u32 / operation u32 |
| 18 / 22 / 26 / 30 | room / incarnation / revision / term，均u32小端 |
| 34 | authority MAC6：当前快照的变更者 |
| 40 | 六个成员槽，每槽 MAC6 + boot u32；数组顺序就是加入顺序 |
| 100 | name24：UTF-8，至多23字节 + NUL |
| 124 / 130 / 131 | target MAC6 / status / reserved=0 |

线上kind：SNAPSHOT=1、ACK=2、JOIN=3、INVITE=4、LEAVE=5、DENY=6。
USB本地kind：CREATE=16、APPROVE=17、REJECT=18、KICK=19，不允许从无线直接执行这些管理入口。

leader每1秒发布全快照；成员确认相同room/incarnation/revision/term/名单，确认有效期3500ms。只有当前leader且所有成员近期确认时才能继续变更。
正常leader退出，成员0被移除、term与revision增加，剩余首成员接任；两人房间退到一人则发空名单解散。全新一人房间不解散。
JOIN/非leader LEAVE最多8次、间隔1秒；审批/邀请15秒过期；拒绝决定保留15秒以处理丢失DENY后的重试；换Room清除旧审批记录。
退休leader最多重播最终快照10秒。没有多数派共识、自动故障选举、掉电持久化或认证加密；分区/成员重启冻结与恢复边界见客户端使用说明。

## USB 命令

旧 `TRILINK/1 HELLO/SEARCH` 保持兼容；能力增加 bit6（0x40）。原始 route v3测试接口在无实际Room时继续可用。

```text
TRILINK/3 ROOM <nonce> <132字节大写/小写HEX>
TRILINK/3 ROOMGET <nonce> <两位HEX页码>
TRILINK/3 RGBENABLE <nonce> 0|1
TRILINK/3 RGB <nonce> <目标MAC12HEX> <RRGGBB>
TRILINK/3 RGBRESULT <nonce>
```

ROOMGET：00本机、01–06附近Room、07–0C待审批、0D–12邀请；回复 `ROOMSTATE nonce page HEX132` 或 `EMPTY nonce`。
本机页status位：1已入Room、2本机leader已收成员确认、4等待请求处理、8本机RGB启用。无线快照不把PC页状态当权限。
`OK ... ROOM`仅表示本机接受命令，不是三机同步完成；`RGB QUEUED`也不是LED完成。ERROR不自动重放。

## RGB 数据面

经现有自动路由传输19 B应用包：magic=A6、version=1、kind(command=1/result=2)、status，接incarnation/app_operation/revision三个u32，最后R/G/B三字节。
验收源MAC与boot属于当前Room，incarnation/revision匹配；分量≤32。独立应用ID与传输ID，不把route ACK提升为执行成功。
命令重复且内容一致返回最近结果，不再次执行；同应用ID不同颜色/Room范围拒收。仅缓存最近一项，加上传输去重窗口，不承诺无限期exactly-once。
结果：0等待、1执行器受理、2驱动完成、3驱动失败、4禁用/无法接收、5发送端10秒结果未知。驱动完成仅证明API调用，不是光学反馈。
执行为80ms亮、80ms灭、80ms亮后灭；GPIO48 / WS2812 GRB单灯；默认禁用，每次启动重新人工确认。驱动日志含返回值和worker栈高水位，实测前不写虚构栈/堆数据。

## 扩展边界

Room控制与应用数据分离；后续文本/文件应新增应用服务分发器，不往广播快照里塞文件。
当前桥接只分发RGB；文件codec仍只编译、不进入链接运行路径。不要把当前原始SEND测试接口称作文件功能。
GPIO不兼容板应替换/新增明确BSP映射，不通过远端请求任意写引脚。

## 验证

`tools/build_local.ps1`依次运行C主机测试、旧USB C→C#契约、Python探针测试、全量IDF构建、镜像/容量/栈帧门禁。
新增Room正常/链路/六人/继任/断链恢复/丢DENY/换Room隔离测试，以及真实bridge+Room+route、fake radio/LED的执行回执集成测试。
Windows模块另有132 B编解码、UTF-8、页面、能力筛选、关闭停止批次与自动UI渲染检查。
最终精确容量与HASH见最新 `artifacts/builds/*/verification.json`。本轮 `HardwareVerified=false`、`Flashed=false`、`Uploaded=false`；软件集成通过不等于三板实测。
