# USB 扫描失败与模拟模式混淆修复

日期：2026-09-14。仅本地客户端修复；固件未修改、未重新烧录，未上传仓库。

## 原因

1. 旧串口插件查询 `SELECT DeviceID, Name, PNPDeviceID, Manufacturer FROM Win32_SerialPort`。
   `Manufacturer` 不属于该类，用户日志中的“无效查询”与此错误查询相符。
   [Microsoft 类定义](https://learn.microsoft.com/en-us/windows/win32/cimwin32prov/win32-serialport)
   列出了完整继承属性。本执行环境另有 WMI 拒绝访问，不能把这里的拒绝访问说成用户原错误的实测复现。
2. 模拟插件即便不是 `--demo` 也创建在线的虚拟 A；旧界面默认选中它，没有真实串口时搜索走模拟分支。
   这解释了“模拟搜索完成：发现 0 个附近设备”，不是 ESP-NOW 回包。
3. 最初实测只有 CH340 COM13；v0.6.0 的 `TRILINK/1` 服务在 S3 原生 USB Serial/JTAG，
   CH340/CH343 的 UART0 仅输出日志和用于烧录。换正确数据口是独立的物理前置条件。

## 修复范围

- `trilink.serial 1.0.1`：只读 SetupAPI 枚举当前存在的 Ports 类设备，不依赖 WMI，
  不打开普通蓝牙或 CH340/CH343 端口；设备身份仍以 nonce HELLO 握手为准。
- 保留 3 秒兜底、单飞、连续失败 5 次暂停、手动恢复；设备状态提示按变化去重。
- 手动指定端口必须有效且当前在线，不再凭过期配置制造候选设备。
- 搜索只有收到匹配 nonce 的 END 才能成功返回，包括“0 台”；部分回复/超时明确失败。
- `trilink.desktop 1.0.1`：真实/模拟模式明确隔离，未连接设备时等待且禁用搜索，模拟需显式启用。
- 真实邻居单独按本机节点保存视图快照，成功搜索整体替换，断开/失败清除；不会变成“当前电脑”。
- 真实 Room 传输未接入，相关操作禁用；模拟房间逻辑与托盘退出语义不变。
- 轮询按钮初始状态读取真实服务状态；提示放到独立工具栏行，不挤掉按钮。

SetupAPI 参考：
[当前设备筛选与句柄释放](https://learn.microsoft.com/en-us/windows/win32/api/setupapi/nf-setupapi-setupdigetclassdevsw)、
[只读设备注册表句柄](https://learn.microsoft.com/en-us/windows/win32/api/setupapi/nf-setupapi-setupdiopendevregkey)。
新枚举器是串口插件内部适配器，没有更改宿主、服务 ABI 或模拟/房间插件。

## 验证

```powershell
.\tools\build.ps1 -PluginId trilink.serial
.\tools\build.ps1 -PluginId trilink.desktop
.\artifacts\tests\Release\bin\TriLink.Core.Tests.exe --serial-inventory
```

- 101 项核心回归，0 失败。
- 两个插件独立编译、部署哈希、完整 profile 加载与干净发布目录检查通过。
- 普通模式空设备、显式模拟、真实列表、断开、超时和暂停/恢复自动界面检查通过。
- X 隐藏、托盘打开、托盘退出、Windows 关机放行的自动生命周期检查通过。
- 已实际查看空设备、显式模拟、轮询暂停三种状态截图；工具栏长文本遮挡已修正。
- 本机 SetupAPI 只读枚举通过；该时刻枚举到 6 个蓝牙 COM，没有 S3 原生 USB 数据口，未打开串口。
- 原生 USB HELLO/SEARCH、三板无线互发现、当前用户交互桌面点击仍未验收。

证据：`artifacts/tests/Release/logs/core-tests.log`、`desktop-lifecycle.log`、`serial-inventory.log`；
图片在 `artifacts/tests/Release/ui/ui-hardware-empty.png`、`ui-explicit-simulation.png`、`ui-polling-paused.png`。
UI 的 TEST LOCAL/COM99 是测试夹具，不是真机识别记录。

旧两个插件可从 `artifacts/archive/usb-scan-before-20260914-205129/` 恢复，先退出客户端再更换 DLL 和对应清单。

## 用户操作

1. 从托盘“退出”旧客户端；双击发布目录中的“启动 TriLink（真实硬件）”。
2. 本机 S3 连接原生 USB 数据口，等待握手识别出真实 MAC/COM，不点击“启用模拟”。
3. 至少另一块已刷同版固件的 S3 同时上电，再点击“搜索设备”。只有一块上电时返回 0 邻居属正常。
4. 如仍没有出现 `VID_303A` 数据口，先核对原生 USB 接口/数据线/枚举状态，不再次盲目烧录。

## 扩展性

扩展状态：可扩展。稳定契约仍为 `IDeviceDiscoveryService` 和 `TRILINK/1`；
SetupAPI 枚举封装在 `WindowsSerialPortCatalog`，串口开关和协议由串口插件负责。
后续传输可经 Composition Root 的 `SerialPlugin.Configure` 替换，不改变 UI 或 Room 核心。
视图保存的真实搜索快照不是房间副本；跨电脑 Room、分片数据及文件功能仍需独立传输实现。
