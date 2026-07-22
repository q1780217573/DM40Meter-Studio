# 收尾复盘：ECharts 波形迁移 + 报文页工具 + 设备控制反馈 + 一键截图/自动连接

> 日期：2026-07-21 ~ 2026-07-22 ｜ 形态：功能迭代 + 多轮真机联调 ｜ 测试：91/91 全过

## 一、本轮交付清单

| # | 功能 | 落点 | 状态 |
|---|------|------|------|
| 1 | 波形由 ScottPlot 迁为 **ECharts 5.5 / WebView2**，本地嵌入 `echarts.min.js`（离线单文件 `NavigateToString`） | `Assets/waveform.html` + `MainWindow.xaml.cs` 桥接 | ✅ |
| 2 | 波形交互全用 **ECharts 默认控件**：tooltip、dataZoom inside(滚轮)+slider(缩略条)、框选缩放(像素百分比)、双击还原 | `waveform.html` | ✅ |
| 3 | 实时 X 轴固定 `[-win,0]` 防抖动；上下限 markLine 标注联动报警 | `waveform.html` / `MainViewModel` | ✅ |
| 4 | 时间窗口自定义（快捷 30/60/300/600s + 自定义 ≥5s）；波形缓冲固定 100 万点 | `SettingsWindow` / `WaveformBuffer` | ✅ |
| 5 | 上下限支持小数输入（字符串字段缓存，防 round-trip 覆盖中间态） | `MainViewModel.AlarmUpper/Lower` | ✅ |
| 6 | 主读数/单位字体 52/26（单位≈读数一半，负数不超边界）；y 轴疏朗青色刻度参考设备 UI | `MainWindow.xaml` / `waveform.html` | ✅ |
| 7 | **报文页**：查找(Ctrl+F,输入即搜,Enter/Shift+Enter/Esc)、暂停滚动、清空、CRC 失败计数、底部状态 | `MainWindow.xaml(.cs)` / `MainViewModel` | ✅ |
| 8 | **设备控制**：保持按钮失同步修复（双向跟随 `m.Hold` 回读）；锁定项按用户要求删除（协议无锁定命令） | `MainViewModel.FlushLatest` / `MainWindow.xaml` | ✅ |
| 9 | **一键截图**：工具条按钮 + Ctrl+P，渲染窗口 PNG 自动保存到可配置目录，提示含"打开位置"链接 | `MainWindow.xaml(.cs)` / `MainViewModel` | ✅ |
| 10 | **扫描后自动连接第一个设备**（1.2s 发现窗口），设置可关 | `MainViewModel.StartScan` / `SettingsWindow` | ✅ |

## 二、关键技术决策

1. **波形渲染迁移**：ScottPlot 4.1 在深色高保真样式（渐变填充/平滑/缩略条）上 API 受限，迁 ECharts。`echarts.min.js` 读出后**内联**进 `waveform.html` 占位符 `/*ECHARTS_INLINE*/`，`NavigateToString` 一次性注入——避免 file:// 相对路径/跨域坑，离线可靠。C# 仅 `setSeries/setMode/setWindow/setUnit/setLimits/clear` 喂数据，呈现与交互全部交给 ECharts 默认控件，不在 JS 里自绘 overlay。

2. **实时滚动下的框选缩放**：X 轴相对"现在"滚动，绝对 `startValue/endValue` 会漂移失效 → 改用**绘图区像素百分比** `dataZoom start/end`（经 `grid.coordinateSystem.getRect()` 换算），滚动中稳定。

3. **ECharts graphic 合并坑**：`setOption({graphic:[]})` 空数组在 merge 模式被忽略，框选矩形残留 → 必须 `setOption({graphic:[]},{replaceMerge:['graphic']})`。

4. **报文页暂停滚动的真正实现**：不是"插入后再对抗滚动"，而是暂停时 **VM 停止向 `PacketLog` 插入新帧**（`PacketPaused`），从源头消除 `Insert(0)` 顶部重排；CRC 失败仍计数。

5. **截图**：`RenderTargetBitmap` 渲染 `MainRoot` 可视树；透明窗口需**先铺根 Border 实际背景色**再叠加内容，否则 PNG 透明区被查看器显成白边。截图目录默认 `图片\DM40MeterStudio`，设置可改。

6. **设备控制状态对齐**：HOLD 有帧状态位（0x80）回读 → 按钮态**双向跟随设备**（`if (m.Hold != HoldActive) HoldActive = m.Hold`），修复"点关闭后按钮仍亮"；AUTO/REL 无回读 → 本地镜像。锁定无协议命令 → 只读，按用户要求从控制区移除。

## 三、踩坑记录（复盘重点，防再犯）

| 坑 | 现象 | 根因 | 解法 |
|----|------|------|------|
| **CollectionChanged 重入崩溃** | 打开报文页闪退，`ItemsControl 与它的项源不一致` | 在 `CollectionChanged` 处理器里**同步** `ScrollIntoView` → BringItemIntoView → 强制 UpdateLayout，ItemContainerGenerator 正处理上一个变更即判不一致 | 一切滚动/选中 `BeginInvoke` 延迟到布局稳定后执行。**已写入 CLAUDE.md 防再犯** |
| 关闭虚拟化放大问题 | 设 `IsVirtualizing=False` 后更易崩 | 每次插入全量布局，重入概率更高 | 恢复虚拟化（默认开），靠延迟+暂停停止插入解决 |
| X 轴乱跳 | 实时波形横轴抖动 | 每帧按数据首末点设 `startValue/endValue`，BLE 到达不均 | 实时固定 `[-win,0]`，只换数据不碰轴 |
| 上下限无法输小数 | 输 `3.` 被改成 `3` | getter 用 `UpperLimit?.ToString()` round-trip 覆盖中间态 | 字符串字段缓存原样回显 |
| 截图白边 | PNG 四周白、圆角变直角 | 透明窗口渲染无底色，查看器把透明显白 | 先 `DrawRectangle(Background)` 铺底再叠加 |
| WebView2 空域 | 波形区截不到 | WebView2 独立 HWND，RenderTargetBitmap 抓不到（airspace） | 本轮可接受（用户确认"可以了"）；如需波形可 `CapturePreviewAsync` 合成 |

## 四、未决 / 后续可选

- 截图不含 WebView2 波形画面（空域限制）。如需，用 `CoreWebView2.CapturePreviewAsync` 抓波形再与 WPF 底图合成。
- 配置持久化（`%AppData%/DM40MeterStudio/config.json`）目前**未实现**：窗口秒数/截图目录/自动连接等设置均为运行期生效，重启还原默认。CLAUDE.md 数据落点一节描述的是目标态。
- 协议无锁定命令字节，锁定仅设备侧可触发（帧 0x40 回读），上位机只能读不能控。

## 五、验证

- 构建：MSBuild `-t:Build` 全绿（7 项目）。
- 单测：91/91 通过（Protocol 15 + Core + Cli，本轮仅动 UI 层，核心/协议/CLI 未受影响）。
- 真机 DM40B：波形平滑渲染、框选/滚轮/缩略条交互、上下限标注、报文查找/暂停/清空、设备控制状态同步、一键截图、扫描自动连接——均逐项实机确认可用。
