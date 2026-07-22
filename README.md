# DM40Meter Studio

Windows 桌面端 **DM40 系列蓝牙万用表上位机** + 无头 CLI（供 AI / 自动化测试）。

- 技术栈：**.NET Framework 4.8 / WPF + MVVM / ECharts 5.5（WebView2 实时波形）/ C# 8.0**，简体中文 UI。
- 协议层：逐字节移植自 [maj113/DM40GUI](https://github.com/maj113/DM40GUI)（权威参考实现）。
- 真机 BLE（DM40B）已端到端打通，读数与表笔实测逐位一致。**生产代码默认连真机，无需安装 Windows SDK**（WinRT 经 NuGet 投影）。
- 文档：`docs/PRD_DM40Multimeter.md`（需求）、`docs/CLI_Contract.md`（CLI 契约）。

## 功能一览

### 实时监测（WPF 三栏布局）

- **读数面板**：大字主读数 + 单位 + 模式/量程，双副显（协议副显或采样统计/高精度值），电量 / HOLD / REL / 锁定状态徽章。
- **实时波形**（ECharts / WebView2，本地嵌入离线可用）：
  - 深色主题、平滑曲线 + 渐变填充、疏朗青色 y 轴刻度（参考设备自带 UI）。
  - 交互全用 ECharts 默认控件：**滚轮缩放**、**底部缩略条**平移/选区、**拖拽框选缩放**、**双击还原**、`P` 暂停。
  - **上下限标注**：阈值以虚线 markLine 画在波形上，随报警设置联动。
  - 时间窗口：快捷 30s / 60s / 5min / 10min + **自定义秒数**；波形缓冲 100 万点。
- **设备控制**：保持（HOLD）/ 自动量程（AUTO）/ 相对值（REL）开关 + 10 个测量档位（VDC/VAC/ADC/AAC/RES/CAP/DIODE/CONT/HZ/TEMP）一键切档。按钮态与设备回读**双向同步**（如设备侧按 HOLD 键也会反映）。
- **测量统计**：最小 / 最大 / 平均 / Δ 峰峰，样本数与时长，可清零。
- **阈值报警**：上下限（支持小数）、声音 / 弹窗开关、报警事件日志，右下角 Toast 提醒。

### 原始报文页

- 实时显示 RX/TX/诊断帧（时间、方向、HEX、CRC 校验），底部统计已缓存帧数 + CRC 失败帧数。
- **查找**：`Ctrl+F` 聚焦，输入即搜（不区分大小写、忽略空格/连写），`Enter` 下一个 / `Shift+Enter` 上一个 / `Esc` 清空。
- **暂停滚动**：冻结画面便于阅读；**清空**：清列表 + 失败计数归零。

### 数据回放页

- 打开 CSV 测量文件回放：读数 / 单位同步，播放 / 停止，0.5x / 1x / 2x / 4x / 10x 倍速，与实时模式互斥。

### 工具

- **一键截图**：工具条 `📷 截图` 或 `Ctrl+P`，渲染窗口为 PNG 自动保存到可配置目录（默认 `图片\DM40MeterStudio`），提示含"打开位置"链接直达文件。
- **扫描自动连接**：扫描到设备后约 1.2s 自动连接第一个（设置可关）。
- **录制**：`R` 开始/停止，CSV 落盘（UTF-8 带 BOM，Excel 直接打开不乱码），每秒 flush，写失败自动停止并保留已录数据。
- **导出 CSV**：`Ctrl+S` 导出当前波形缓冲。`Ctrl+C` 复制当前读数。
- **设置**：时间窗口（含自定义）、截图保存目录（浏览选择）、扫描自动连接开关。

### CLI（无头，供 AI / 自动化）

数据走 stdout（JSON Lines / CSV），日志走 stderr，可直接管道：

```bash
DM40MeterStudio.exe --cli scan                          # 扫描设备
DM40MeterStudio.exe --cli stream --duration 5           # 实时数据流（真机 BLE）
DM40MeterStudio.exe --cli send --cmd hold-on            # 下发命令
DM40MeterStudio.exe --cli record --duration 60 --out run.csv  # 录制
```

完整命令 / 选项 / 输出 schema / 退出码见 [`docs/CLI_Contract.md`](docs/CLI_Contract.md)。

## 构建

```bash
VS="D:/Program Files/Microsoft Visual Studio/2022/Community"
"$VS/MSBuild/Current/Bin/MSBuild.exe" DM40MeterStudio.sln -p:Configuration=Release -v:m -t:Restore,Build
```

产物：`src/Dm40.App/bin/Release/net48/DM40MeterStudio.exe`（单 exe + 依赖 dll，含本地 `Assets/echarts.min.js`）。

> 用 **MSBuild.exe**（非 dotnet CLI）；`-p:` 前缀（`/p:` 会被 MSBuild.rsp 误解析）。

## 运行

- **图形界面**：双击 `DM40MeterStudio.exe`（无参数），默认即真机 BLE。运行需 [WebView2 Runtime](https://developer.microsoft.com/microsoft-edge/webview2/)（Win10/11 通常已内置）。
- **CLI**：加 `--cli`，见上文。

## 接入真机

1. 开启 DM40 并处于可被发现状态（无需系统配对）。
2. 点 **🔍 扫描设备** → 自动连接第一个（或从下拉框选择后点 **⚡ 连接**）。
3. 连接后自动持续拉流（约 5fps），读数 / 波形 / 统计 / 报警即实时更新。

## 测试

```bash
"$VS/Common7/IDE/CommonExtensions/Microsoft/TestWindow/vstest.console.exe" \
  tests/Dm40.Protocol.Tests/bin/Debug/net48/Dm40.Protocol.Tests.dll \
  tests/Dm40.Core.Tests/bin/Debug/net48/Dm40.Core.Tests.dll \
  tests/Dm40.Cli.Tests/bin/Debug/net48/Dm40.Cli.Tests.dll
```

**91 个单元测试全过**（协议解析、核心层统计/报警/录制/回放、CLI 契约）。时间 / 文件 / 设备 / 等待 / 周期全部经 seam 注入，非 BLE 逻辑可离线单测。

## 项目分层

| 项目 | 职责 |
|---|---|
| `Dm40.Protocol` | BLE 帧解析 / 命令构造（纯函数 `byte[]→Measurement`） |
| `Dm40.Core` | 状态机 / 统计 / 报警(滞回) / 录制 / 回放 / `MeterSession` / `WaveformBuffer` |
| `Dm40.Cli` | 无头 CLI 四命令 |
| `Dm40.Transport` | 真实 BLE（WinRT GATT，NuGet 投影，无需 Windows SDK） |
| `Dm40.App` | WPF UI（MVVM）+ WebView2/ECharts 波形 + 双模式入口 |

## DM40 BLE 协议速查

- UUID：Service `fff0` / Notify `fff1` / Write `fff3`（fff3 仅 WriteWithoutResponse）。
- 命令帧 6 字节 `AF 05 03 <cmd> <arg> <chk>`，校验和 `(-sum(前5)) & 0xFF`。
- 测量帧 17 字节，前缀 `DF 05 03 09`，CRC 全帧字节和 `& 0xFF == 0`。计数小端，比例槽位查 scale map。

详细帧布局见 `docs/PRD_DM40Multimeter.md` §11.2。
