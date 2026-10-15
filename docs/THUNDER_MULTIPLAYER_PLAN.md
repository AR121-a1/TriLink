# 多人扩展实施约定

基线：官方 thunder-game 分支 3cb3fda30cb6295e841af68eee1e1f70e54290e8（父提交04e0013），2026-10-05下载。主干客户端及本地固件不改；优化仅在独立游戏源码目录。

## 范围

- 单人练习与2–6人合作，游戏房主仍独立于TriLink Room leader。
- 房主选择实际参与人数（2–6），所有玩家完成握手后自动开始；已开局不接纳中途加入。
- 一个玩家离开、失联或超出输入恢复窗口，整局终止并释放所有端口；不做自动重连、迁移或忽略未确认tick。
- 原UDP适配器/IGameLinkFactory不改，最多128B，单线程驱动，无新增后台线程/固件协议。

## 共享实现接口

GameEngine.MaxPlayers=6；固定Players池长度6，PlayerCount记录本局1–6人。新增GameEngine(uint seed, int playerCount)，保留bool构造函数（false映射1，true映射2）。新增Step(byte[] inputs, int offset=0)，消费PlayerCount个连续字节、不分配；原Step(byte,byte)保留并为其余席位提供中立输入。Cooperative表示PlayerCount>1。保持敌机/子弹/特效池上限；GameOver检查本局所有玩家。两人旧规则位置保持，三至六人均匀排列。Seed/PlayerCount必须纳入世界hash。

最终实现：3人及以上采用(Tick/6)%PlayerCount轮换玩家更新/发射起点，输入仍按玩家席位读取，普通6tick与加强3tick射击均在完整轮转窗口获得相等的饱和池机会。单/双人保留旧顺序；不新增轮换状态或逐tick分配。

GameSession新增Host(int localPort,uint seed,int playerCount)，原Host映射2。属性PlayerCount、LocalPlayerIndex、ReadyPlayerCount；单人LocalPlayerIndex=0，Host=0，Join由Start分配1–N-1。房主最多5个固定PeerState，分别保存endpoint/token/Ready/ACK/输入sequence/中立超时/通信时间。固定history为120*6字节，逐peer独立确认/补发，最慢peer限制可恢复窗口。不增长无界集合。

Start是分配席位/种子/人数消息；Join收到后Connected=true但Running=false，等第一条连续Frames后运行。Host只有所有N-1席Ready后创建引擎并Connected/Running=true。等待期持续重发Start/Ready，显示到齐进度，不因等待第三位而把已Ready玩家判离线。每席5秒无有效通信终止，未获Start的Join仍12秒连接截止。终局仍补发最后tick并确认；重复Start/Ready不能重置世界。

## GameWire v2

整数仍little-endian，24B头：magic(2),version(1),message(1),session u32,token u32,tick u32,sequence u32,count u8,input u8,playerCount u8,playerIndex u8。

- Hello：session/tick/sequence/count/input/playerCount/playerIndex均0，token非0。
- Start：session非0，tick为seed，playerCount=2–6，playerIndex=分配席位1–N-1，其余字段0。
- Ready：session非0，playerCount=2–6，playerIndex=已分配席位，其余字段0。
- Input：session非0，tick为已应用最高tick，sequence为输入序号，input低5bit，playerCount=本局人数，playerIndex=发送者席位1–N-1，count=0。
- Frames：session非0，tick>=1，count=1–16，playerCount=2–6，playerIndex=0，sequence/input=0；body=count*playerCount字节，按tick再玩家顺序排列，输入低5bit。
- Bye：session非0，playerCount=2–6，playerIndex=发送者（Host=0），其它0。
- 最大帧24+16*6=120B，常规每3tick为24+3*N B。旧v1拒绝，所有PC需统一客户端；没有修改引擎时钟或现有ESP32链路。

## UI与验证

增加2–6参与人数选择；Join不要求手填席位，禁用会话运行中的地址/端口/人数编辑，明确本机玩家/到齐进度。六种玩家/友弹配色，紧凑六席HUD；隐藏在线窗口终止并重绘停止态，单人隐藏暂停。

回归至少覆盖3/4/6人确定性、各席不同输入、到齐开局、重复握手、不合法人数/席位、分玩家ACK与丢包/重排、一个玩家离开/超时、终局、报文容量和各席位生命周期。至少真实loopback三进程与六进程世界hash相同；保留两人基线。正常/空闲/三人/六人/停止UI截图检查。测试加载内置profile固定--demo+--safe-mode，用户overlay不执行。

现场桌面、跨实体电脑和ESP32仍是独立验收门；不修改防火墙，不推送或合并main。
