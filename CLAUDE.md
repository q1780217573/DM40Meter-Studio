# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## 项目现状（先读这里）

**已完整实现、真机打通、UI 复刻原型完成**。WPF 图形界面（1:1 还原高保真原型）+ CLI 无头模式双形态，91 个单测全过。**模拟源已全部移除**，生产代码默认连真机。

- 目标产物：**DM40Meter Studio** —— Windows 桌面端 DM40 万用表上位机（单机免费工具）+ AI 接入 CLI。
- 技术栈：**.NET Framework 4.8 / WPF + MVVM / ECharts 5.5（WebView2 实时波形）+ ScottPlot 4.1（兜底）/ C# 8.0**，简体中文 UI。
- 解决方案：`DM40MeterStudio.sln`（7 个项目）。
- 入口：`src/Dm40.App/bin/Debug/net48/DM40MeterStudio.exe`（无参数→WPF，默认即真机 BLE；`--cli`→无头 CLI）。

**已实现的项目：**

| 项目 | 职责 | 依赖 Win SDK |
|---|---|---|
| `src/Dm40.Protocol` | BLE 帧解析/命令构造（纯函数，移植自 maj113/DM40GUI） | 否 |
| `src/Dm40.Core` | 状态机/统计/报警/录制/回放/`MeterSession`/`WaveformBuffer`/`FakeMeterSource`（测试用内存替身）/`RealFrames`（真实帧常量） | 否 |
| `src/Dm40.Cli` | 无头 CLI（scan/stream/send/record，JSON Lines/CSV） | 否 |
| `src/Dm40.App` | WPF UI（1:1 复刻原型：三栏布局 + 三页签 + 中栏 **WebView2/ECharts 波形** + 回放）+ 双模式入口（`App.xaml.cs` 分发 `--cli`，默认真机 BLE）；波形页 `Assets/waveform.html` + 本地嵌入 `Assets/echarts.min.js`，C# 仅 `setSeries/setMode/setWindow/setUnit` 喂数据，呈现/交互全用 ECharts 默认控件。**报文页**：查找(Ctrl+F,输入即搜)/暂停滚动(VM `PacketPaused` 暂停时停止向 `PacketLog` 插入)/清空；**WPF 坑**：绝不在 `CollectionChanged` 处理器里同步 `ScrollIntoView`（会 BringItemIntoView→强制 UpdateLayout→ItemContainerGenerator 不一致崩溃），一切滚动/选中须 `BeginInvoke` 延迟 | 否 |
| `src/Dm40.Transport` | 真实 BLE（WinRT），经 NuGet `Microsoft.Windows.SDK.Contracts` 投影，**无需装 Windows SDK，真机已通** | 否（NuGet 投影） |

**测试（93 用例全过）：** `Protocol.Tests`(15) + `Core.Tests`(53→64) + `Cli.Tests`(14)。
测试 seam：时间 `IClock`(`FakeClock`)、文件 `ICsvSink`(内存)、设备 `IMeterDataSource`(`SimulatedMeterSource`)、等待 `IWaitHandle`(`FakeWaitHandle`)、周期 `ITicker`(`ManualTicker`) 全部可注入，故所有非 BLE 逻辑可离线单测。

**真机 BLE 已端到端打通（DM40B）**：`--cli scan/stream/record`（默认真机 BLE，`--source` 已随模拟源移除）全部可用，读数与表笔实测逐位一致。关键实现要点（排障/改动必读）：
1. **CLI 模式必须清同步上下文**：`App.RunCli` 开头 `SynchronizationContext.SetSynchronizationContext(null)`，否则 WPF Dispatcher 无消息泵导致 `BleMeterSource` 里的 `await` 延续永不执行（连接挂起）。
2. **设备解析**：未配对 BLE 设备离线时 `FromBluetoothAddressAsync` 返回 null；解析期间跑 advertisement 保活 watcher + Public/Random 地址类型轮换重试（DM40 是 **Public**），无需配对。
3. **写特征 fff3 仅 `WriteWithoutResponse`**（用 WriteWithResponse 报 0x80650003）。
4. **CMD_READ 是"读一次"**：每下发推一帧，需 200ms 轮询定时器持续重发拉流（约 5fps）。

**协议帧为 17 字节**（非早期假设的 16）：`DF 05 03 09 | f4=0B f5=flag f6=status | f7,f8,f9=M3/M2/M1 比例槽 | f10-11=M3 f12-13=M2 f14-15=M1计数(小端) | f16=CRC(全帧和&0xFF==0)`。`MeasurementParser` 数据字节**相对帧尾定位**（对齐 maj113 参考实现的 Python 负索引），帧长自适应；模拟源同样生成 17 字节真实布局帧。数值/CSV 输出用 `G10` 格式消除二进制浮点尾噪。

## 常用命令（已实测可用）

```bash
# VS 2022 Community 安装根目录
VS="D:/Program Files/Microsoft Visual Studio/2022/Community"

# 还原 NuGet + 构建整个解决方案（注意用 -p: 前缀，/p: 会被 MSBuild.rsp 误解析）
"$VS/MSBuild/Current/Bin/MSBuild.exe" DM40MeterStudio.sln -p:Configuration=Debug -v:m -t:Restore,Build

# 仅构建（已还原过）
"$VS/MSBuild/Current/Bin/MSBuild.exe" DM40MeterStudio.sln -p:Configuration=Debug -v:m -t:Build

# 跑全部测试（3 个测试项目）
"$VS/Common7/IDE/CommonExtensions/Microsoft/TestWindow/vstest.console.exe" \
  tests/Dm40.Protocol.Tests/bin/Debug/net48/Dm40.Protocol.Tests.dll \
  tests/Dm40.Core.Tests/bin/Debug/net48/Dm40.Core.Tests.dll \
  tests/Dm40.Cli.Tests/bin/Debug/net48/Dm40.Cli.Tests.dll

# 运行应用（WPF 图形界面，默认模拟设备）
src/Dm40.App/bin/Debug/net48/DM40MeterStudio.exe

# CLI 无头模式（数据走 stdout，供 AI/自动化消费）
src/Dm40.App/bin/Debug/net48/DM40MeterStudio.exe --cli stream --source sim --duration 5
```

- MSBuild 在 `MSBuild/Current/Bin/`（不在 IDE 目录）；`devenv.exe`/`vstest.console.exe` 在 IDE 目录。
- 系统里的 `dotnet.exe` 在 git-bash 下缺 `api-ms-win-crt-*.dll` 跑不起来 → **一律用上面的 MSBuild.exe**，别用 dotnet CLI。
- net48 引用程序集在 `C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8`（已就位）。
- **CLI 双模式**：`App.xaml.cs` 的 `OnStartup` 检测 `--cli` 分流到 `CliProgram.Run`，用 `AttachConsole` 附加父控制台输出。`CliProgram` 的 stdout/stderr/数据源/时钟/等待全部注入，故可离线单测。

**BLE 传输层**：`Dm40.Transport` 用 `Microsoft.Windows.SDK.Contracts`(10.0.19041.1) + `System.Runtime.WindowsRuntime`(4.7.0) NuGet 包获得 WinRT 投影与互操作，**无需安装 Windows SDK**即可编译并连接真机。`Dm40.App.csproj` 的 `CopyWinRtInterop` target 把互操作 dll 拷到输出目录供运行期加载。

## 核心文档（实施前必读）

| 文件 | 作用 |
|---|---|
| `docs/PRD_DM40Multimeter.md` | **唯一权威需求来源**。15 章 PRD（V0.5，含 §6.7 FR-014 CLI 模块）：FR-001~014、§9 数据契约、**DM40 BLE 协议帧布局（§11.2）**、AC-01~12、技术验证（§12.2）。 |
| `docs/CLI_Contract.md` | CLI 对外契约：四命令、选项、JSON/CSV schema、退出码、AI 使用示例。改 CLI 行为前必读。 |
| `README.md` | 构建/运行/测试/接入真机的快速上手。 |
| `prototypes/DM40Multimeter_prototype.html` | 高保真 UI 原型（单文件，双击浏览器打开）。采用**无限画布模式**：4 个界面状态平铺，左侧导航栏和右侧批注卡片是"预览工具"，**不属于软件设计**。真实软件 = 单主窗口 + 应用内三页签（实时波形/原始报文/数据回放）+ 模态设置对话框。 |
| `skill/PRD_Generation_Skill.md`、`skill/Prototype_Specification_Skill.md` | 本项目文档自身的生成规范（仅当需要再生成 PRD/原型时参考）。 |

## 分层架构（来自 PRD §15.1，已实现）

```
Dm40.Protocol   ✅ BLE 帧解析/命令构造。纯函数 byte[]→Measurement。移植自 maj113/DM40GUI。
Dm40.Core       ✅ 状态机/统计/报警(滞回)/录制/回放 + MeterSession(主链路聚合) + SimulatedMeterSource。
Dm40.Cli        ✅ 无头 CLI：scan/stream/send/record。IMeterDataSource 注入（sim/ble）。
Dm40.Transport  ✅ 真实 BLE（WinRT GATT），真机已通（无需装 Windows SDK）。
Dm40.App        ✅ WPF UI（MVVM，MainViewModel 聚合 MeterSession，Dispatcher+30fps 节流）+ 双模式入口。
```

**数据流（§5.1 主链路，`MeterSession.OnFrame` 编排）：**
原始帧 → `MeasurementParser.Parse`（CRC+解析）→ 有效则 `Statistics.Add` + `Alarm.Feed` + `WaveformBuffer.Add` + `Recorder.Write`；无效帧仅留痕报文查看器。

**关键架构约束**（PRD 要求，已落实）：
- **UI 线程与数据源回调线程严格隔离**：`MainViewModel` 用 `Dispatcher` marshal，`DispatcherTimer`(33ms≈30fps) 合并刷新读数，禁止回调直接触 UI。
- **录制落盘**用 `CsvRecorder` 内存队列 + 每秒 flush；写失败立即停止录制并保留已录数据（`HasError`）。
- **比例映射表（scale map）** 移植自参考实现，注意小端序与符号槽位；所有未知键走降级分支（显示 `---`，原始帧留痕，绝不抛异常到 UI）。
- **数据源抽象 `IMeterDataSource`** 统一模拟/真机，使 UI 与 CLI 不感知来源——这是 QA "模拟设备"开关和 AI 自动化接入的根基。

## DM40 BLE 协议速查（实施时必须逐字节对齐 PRD §11.2）

- **UUID**：Service `fff0` / Notify（设备→上位机）`fff1` / Write（上位机→设备）`fff3`。
- **命令帧**（6 字节）：`AF 05 03 <cmd> <arg> <checksum>`，校验和 = `(-sum(前5字节)) & 0xFF`。读型号 `...08 00`、读测量 `...09 00`、Hold `...04 01 xx`、自动量程 `...03 01 xx`、REL `...05 01 01`、档位切换 `...06 01 xx`。
- **通知帧**：型号应答前缀 `DF 05 03 08 14`；测量帧前缀 `DF 05 03 09`。
  - `data[5]` 模式/量程标志；`data[6]` 状态字节（`&0x07` 电量、`&0x08` 充电、`&0x40` 锁定、`&0x80` Hold）。
  - `data[14:16]`/`[12:14]`/`[10:12]` 主/副/第三计数（**小端**）；`data[-8]`/`[-9]`/`[-10]` 三个槽位比例与符号。
  - **CRC**：全帧字节和 `& 0xFF == 0`，失败帧丢弃（仅入报文查看器）。
- **工程值换算**：显示值 = `原始计数 × 比例系数 × 符号`；比例系数由比例槽位字节查 scale map，查表失败按未知帧降级处理。

## 测试与验收基线（来自 PRD §12）

- 协议解析层单测覆盖率 **≥90%**；核心层（统计/报警）**≥80%**。（均已达标）
- 模糊注入 **1000 条随机畸形帧，进程 0 崩溃**（`Parse_RandomGarbage_NeverThrows`）。
- 三方对账专项：同一监测会话 **"统计面板值 = CSV 核算值 = 回放统计值"** 必须一致（`MeterSessionTests.Statistics_Waveform_AndRecording_Agree`）。
- CLI 契约一致性：四命令 stdout schema 与 `CLI_Contract.md` 逐字段一致，退出码与契约一致（`CliProgramTests`）。
- P0 缺陷定义：进程崩溃、数据文件损坏、与万用表实体读数不一致。
- **"模拟设备"开关**已实现：`SimulatedMeterSource` 生成合法帧驱动完整链路，AC-04/05/07 可无真机验证。

## 数据落点

- 配置文件：`%AppData%/DM40MeterStudio/config.json`（theme/lastDeviceMac/windowSeconds/alarm/csvDir）。
- 运行日志：`logs/app-yyyyMMdd.log`（单文件 10MB 滚动，最多 10 个）。
- CSV 文件：UTF-8 **带 BOM**（保证 Excel 直接打开不乱码），默认文件名 `DM40_yyyyMMdd_HHmmss.csv`。

## 工作流约定

本项目遵循用户全局 CLAUDE.md 的 **Spec 双轨工作流**：任何功能开发前需在 `docs/superpowers/specs/` 下产出 brainstorming 版（设计参考）+ to-spec 版（实施/验收基线）两份文档。实施时以 to-spec 版为主指令，以本 PRD 为需求与协议事实来源。
