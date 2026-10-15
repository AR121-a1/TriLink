# 0.14.4：时序补丁与长逻辑时间检验

2026-10-10，延续 0.14.3 的 WinForms/插件栈；不改固件，不操作真实串口、Room、无线或 RGB。用户要求推进进程内逻辑时间以观察长等待/周期边界，类似仿真，而非仅测运行速度。

## 两项已复现并修复的缺陷

| 缺陷 | 旧版 0.14.3 | 补丁版 0.14.4 |
| --- | --- | --- |
| 第五次后台失败时仍有重叠刷新意图 | timer 停了，但残留第六批读取成功，把状态恢复为有效、失败计数清零 | 停轮询前清除轻量/完整合并刷新标志；保持 failures=5、状态无效，须明确手动恢复 |
| 单机 CREATE 后第一次 RGB 尚未完成，再次点击同一本机目标 | Room 变化改变去重键，提交 CREATE 与两次 RGB 共 3 次写 | generation、本机 MAC、物理目标组成稳定去重键，仅 CREATE 与一次 RGB 共 2 次写 |

去重不替代执行校验：仍复核设备、Room/incarnation、目标 boot、角色和 RGB 许可；不同物理目标及禁用 RGB 不被误合并。已提交写不能撤销，不自动重放写命令。异常诊断与完整堆栈保留，没有关闭错误弹窗或改变 JIT 设置。

## 同一测试器的前后对照

冻结 v2.1 源码 SHA-256 `0A40A0D331B2BEF3B3BEABB75A81F1018DF9A0E9360980AD2377415726269209`；EXE `EC7484C40846D9F11DC5E31801BEB45D23631F204164282C9D5CAD2D0EB05DE7`。前后使用同字节测试器；Fake 实现未变，替换的是产品 DLL 和扩展后的测试程序集。

| 产品与模式 | 场景 | 断言 | 失败场景 | 实际 QPC/独立参考比率 |
| --- | ---: | ---: | ---: | ---: |
| 0.14.3 / 正常速度完整重跑 | 12 | 132 | 2 | 1.0003 |
| 0.14.3 / OpenSpeedy 20× | 12 | 130 | 2 | 20.0249 |
| 0.14.4 / 正常速度 | 12 | 132 | 0 | 1.0008 |
| 0.14.4 / OpenSpeedy 20× | 12 | 130 | 0 | 19.9970 |

12 场景覆盖校准、第五失败重叠请求、真实页面 leader 2 秒/成员 5 秒 Timer 回调、single-flight/合并/前台优先、暂停与迟到读、失败后手动即时恢复、角色/权限按钮矩阵、RGB 回执及上下文挂起期限、错误应用 ID 清尾、跨 CREATE 去重。132/130 的差异来自仅正常速度启用的两条 RGB 耗时范围断言，不是漏跑两个场景。

RGB 的 12 秒等待涉及 `Task.Delay`，在该 OpenSpeedy 版本下并未等比缩短；加速模式验证清尾、结果未知、迟到结果不假成功与不重放，不能宣称所有期限都精确 20×。真实参考采用未被本版本 Hook 的 `QueryUnbiasedInterruptTime`，并由未加速父进程记录预算/退出码。

旧版一次 180 秒预算截止的 baseline 没有结果收据，保留为不完整记录；后续 300 秒预算完整重跑才作为负例。更早 v2 的 Ready/message-pump 夹具失败不当作产品缺陷；V1 的 49 项有限加速检查继续独立保留，不覆盖本次扩大场景找到的问题。

## 长逻辑时间补充

专用长测使用实际硬件页 Timer 与冻结的 Fake 服务，执行 **30.0379 分钟逻辑时间 / 90.113 秒真实参考时间**，26 项断言零失败，父进程退出码 0。QPC 初始校准 20.0385×、整段 20.0000×；计时从初始化完成后开始，父进程连同校准/退出约 94.045 秒。

- 实际页面回调 777 次，ROOMGET/RGBRESULT 读取 7858 次，SEARCH 112 次；最大同时 I/O=1，业务队列最大长度=0，连续失败=0，合法按钮异常采样=0。
- 在逻辑 5、15、25 分钟分别暂停 30 逻辑秒；设备到达/隐藏再显示事件没有复活轮询，明确手动恢复都立即执行完整只读刷新。
- 普通 SEARCH 的 107 个间隔观测为 15.032–17.506 逻辑秒，无 15 秒守卫违例；首次及三次手动恢复的完整发现单独排除。
- 只读包装器禁止任何 Room/RGB 写，写次数 0；没有加载 Serial 插件。测试仪表会清理 Fake 自身的命令历史，防止仪表日志产生线性增长，并在结果中披露。

每逻辑分钟记录资源；前后均完整 GC：托管内存 477088→1460928 B，private bytes 31354880→37367808 B，句柄 413→427，线程 14→13。中途托管内存呈 GC 锯齿，private bytes 后段约 37.4–38.0 MB，句柄/线程后段为 427/13；这只是短真实时长、含测试仪表的资源观测，不证明零泄漏或真实 30 分钟无线耐久。

本次仅注入新建隔离测试 PID 4336。恢复后 OpenSpeedy 原倍率为 1×，确认无其他 enabled PID/无 GUI 控制后临时设为 20×，最终观察恢复 1×，恢复收据与 runner 均通过；未改持久化自动进程名单或系统时间。共有倍率并发保护是重复审计的尽力保护，不是原子锁。不同时间 API 未统一缩放，固件/真实无线时钟也不在该进程内；不能把本次等同真实环境全部跑了 30 分钟。

## 软件门禁与交付边界

Release、Debug 各 **1067** 项普通速度软件检查：Core 136、Serial 126、HardwareRoom 225、Modules 64、Thunder 444、Desktop 34、Smoke/Guard 38。HardwareRoom 较 0.14.3 增加 31 项确定性回归。UI smoke 采用 `--safe-mode --demo`，避免构建期间真实串口枚举/打开；此参数不改变正常客户端启动方式。

91 个当前源码/构建输入与冻结 build-stage SHA 一致。队列等待和单节点完成截图已人工查看，但仍只是自动窗体/离屏证据，不声称交互桌面、实板无线执行或光学验收。0.14.3 的原生 USB 只读及 0.14.2 的两电脑 Room 实验保留为历史，不当作 0.14.4 新真机验证。

已生成独立 新版 EXE (historical local evidence, not included: `../artifacts/NodeConsole-0.14.4/TriLink.MinClient.exe`) 和 整套便携 ZIP (historical local evidence, not included: `../artifacts/packages/TriLink-NodeConsole-0.14.4-Release.zip`)。91 个构建输入逐 SHA 一致，9 个插件清单通过，22 个便携文件与 Release/ZIP 逐项匹配；3 个本机快捷方式已重新定位并核对参数。ZIP 22 项 / **158299 B**，SHA-256 `7519880EC0DDEE593E4D7B228FA50BAC53250683C59EF373561A535BA83DCC3F`。

证据与运行文件分开：机器收据 (historical local evidence, not included: `../artifacts/tests/NodeConsole-Timing-0144/delivery-receipt.json`)，`NodeConsole-Timing-0144/{Release,Debug,TimingScenarios,Regression,LongLogicalTime}` 保留普通构建、同测试器负例/复测、补丁回归、长测源码/EXE/依赖/资源及倍率恢复记录。更早夹具 Ready 失败另存 HarnessDiagnostics-v2，不混入通过场景。

不覆盖 0.14.3 运行目录与 ZIP，不自动结束用户客户端。旧 ZIP SHA 保持 `663FE3241114AA142C9D5A5D1A418D3A3626E24FAD325E3A3B805B36EBAF1EA9`。Windows/.NET Framework 4.8；PC2 更新须先从托盘“退出”，再解压整套运行文件使用其中 EXE，不能只换 EXE 或单个插件 DLL。本轮未自动启动真实客户端或上传 GitHub。

永久冻结证据：现场与续作记录 (historical local evidence, not included: `../../designs/2026-10-10_timing_patch_freeze_handoff.md`)，`artifacts/freezes/20261010-timing-0144/` 下的 build-stage、v2.1 新旧结果与 regression-agent。旧发布历史见 [0.14.3 队列报告](QUEUE_CLOSURE_0143.md)。
