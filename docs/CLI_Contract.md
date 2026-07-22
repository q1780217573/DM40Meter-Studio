# DM40Meter Studio CLI 契约（供 AI / 自动化消费）

> 版本：v1.1（2026-07-20）
> 用途：无头命令行模式，让 AI / 脚本完成 **发现 → 读数 → 控制 → 录制** 全流程，用于自动化测试。
> 数据流走 **stdout**（结构化数据），日志/诊断走 **stderr**（不污染数据流）。
> 数据源恒为**真实 BLE 设备**（模拟源已于 v1.1 移除，软件"真实可用"）。

## 调用方式

```
DM40MeterStudio.exe --cli <command> [options]
```

不带 `--cli` 时启动 WPF 图形界面；带 `--cli` 时进入无头 CLI 模式。

## 全局选项

| 选项 | 取值 | 默认 | 说明 |
|---|---|---|---|
| `--format` | `json` / `csv` | `json` | 输出格式（`stream`/`scan`/`record` 有效） |
| `--mac` | `AA:BB:...` | 自动选第一台 | 目标设备 MAC；不指定则连接扫描到的第一台 DM40 |
| `--duration` | 秒（整数） | 0 = 持续 | `stream`/`record` 持续时间；0 表示直到 Ctrl+C |

> 已移除：`--source sim|ble`（v1.0）。数据源恒为真实 BLE，传入 `--source` 将报参数错误（退出码 1）。

## 命令

### 1. scan —— 扫描设备

```
DM40MeterStudio.exe --cli scan
```

发现周边 DM40 设备并逐行输出。**JSON 每行一台设备：**

```json
{"name":"DM40B","mac":"86:5C:1C:06:A1:97","rssi":-52}
```

**CSV 格式：**

```
name,mac,rssi
DM40B,86:5C:1C:06:A1:97,-52
```

### 2. stream —— 实时数据流

```
DM40MeterStudio.exe --cli stream --duration 10
```

连接设备并持续输出测量帧（约 5Hz，CMD_READ 轮询拉流）。**JSON Lines，每行一条测量：**

```json
{"ts":"2026-07-20T10:35:31.539","value":5.0029,"unit":"V","mode":"VDC","range":"AUTO","overload":false,"alarm":null}
```

| 字段 | 类型 | 说明 |
|---|---|---|
| `ts` | string | ISO8601 本地时区毫秒精度 |
| `value` | number | 主读数工程值（已乘比例）；过载时为 `null` |
| `unit` | string | 单位（V/A/Ω/F/Hz/℃ 等） |
| `mode` | string | 测量模式（VDC/VAC/VDC+AC/ADC/AAC/ADC+AC/RES/RES_ONLINE/CAP/CONT/DIODE/FREQ/TEMP，同协议 Kind） |
| `range` | string | 量程（6V / AUTO 等） |
| `overload` | bool | 是否过载（OL） |
| `alarm` | string/null | `HIGH`/`LOW`/null（需配 `--upper`/`--lower`） |

**CSV 格式**（首行表头，列序同 PRD §9.1）：

```
Timestamp,Value,Unit,Mode,Range,Aux1,Aux2,Alarm
2026-07-20 10:35:31.539,5.0029,V,VDC,AUTO,,,
```

报警阈值可选参数：`--upper <值>` `--lower <值>`（带 0.5s 滞回，越限时 `alarm` 字段标记）。

### 3. send —— 下发命令

```
DM40MeterStudio.exe --cli send --cmd hold-on
```

向设备下发一条命令后退出。`--cmd` 取值：

| 值 | 命令帧 |
|---|---|
| `hold-on` / `hold-off` | Hold 开/关 |
| `auto-on` / `auto-off` | 自动量程开/关 |
| `rel` | 相对值 REL |
| `cap` / `diode` / `cont` / `hz` / `temp` | 档位切换 |

成功时 stdout 输出确认：

```json
{"sent":"hold-on","frame":"AF 05 03 04 01 01"}
```

### 4. record —— 录制到文件

```
DM40MeterStudio.exe --cli record --duration 60 --out run.csv
```

录制测量数据到 CSV 文件（UTF-8 带 BOM，列序同 §9.1）。完成后 stdout 输出摘要：

```json
{"recorded":240,"path":"run.csv"}
```

## 退出码

| 码 | 含义 |
|---|---|
| `0` | 成功 |
| `1` | 参数错误（未知命令/非法选项，含已废弃的 `--source`） |
| `2` | 未发现设备 |
| `3` | 连接失败 |
| `4` | 命令发送失败 |
| `5` | 录制/写入失败 |

## AI 使用示例

```bash
# 发现真机
DM40MeterStudio.exe --cli scan

# 监测 60s，带越限标记（默认连扫描到的第一台 DM40）
DM40MeterStudio.exe --cli stream --duration 60 --upper 3.465 --lower 3.135

# 指定设备持续流式输出（供管道消费）
DM40MeterStudio.exe --cli stream --mac 86:5C:1C:06:A1:97
```
