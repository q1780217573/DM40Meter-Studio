using System;
using System.Collections.Generic;
using System.Linq;
using Dm40.Core;
using Dm40.Protocol;

namespace Dm40.Cli
{
    /// <summary>
    /// CLI 程序主体（CLI_Contract.md）。数据走 stdout，诊断走 stderr。
    /// 数据源/输出/时钟全部注入，故可离线单测。返回值为进程退出码。
    /// </summary>
    public sealed class CliProgram
    {
        // 退出码（CLI_Contract.md）
        public const int ExitOk = 0;
        public const int ExitArgError = 1;
        public const int ExitNoDevice = 2;
        public const int ExitConnectFailed = 3;
        public const int ExitSendFailed = 4;
        public const int ExitWriteFailed = 5;

        /// <summary>扫描等待时长（PRD FR-001 默认 10s 扫描）。</summary>
        public static readonly TimeSpan ScanWindow = TimeSpan.FromSeconds(10);

        private readonly Func<IMeterDataSource> _sourceFactory;
        private readonly IClock _clock;
        private readonly IWaitHandle _waiter;

        public CliProgram(Func<IMeterDataSource> sourceFactory, IClock clock, IWaitHandle waiter)
        {
            _sourceFactory = sourceFactory ?? throw new ArgumentNullException(nameof(sourceFactory));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
            _waiter = waiter ?? throw new ArgumentNullException(nameof(waiter));
        }

        /// <summary>运行 CLI，返回进程退出码。</summary>
        public int Run(string[] args, System.IO.TextWriter stdout, System.IO.TextWriter stderr)
        {
            CliOptions opt;
            try
            {
                opt = CliParser.Parse(args);
            }
            catch (CliArgumentException ex)
            {
                stderr.WriteLine("参数错误: " + ex.Message);
                return ExitArgError;
            }

            var source = _sourceFactory();
            try
            {
                switch (opt.Command)
                {
                    case CliCommand.Scan: return RunScan(source, opt, stdout, stderr);
                    case CliCommand.Stream: return RunStream(source, opt, stdout, stderr);
                    case CliCommand.Send: return RunSend(source, opt, stdout, stderr);
                    case CliCommand.Record: return RunRecord(source, opt, stdout, stderr);
                    case CliCommand.Probe: return RunProbe(source, opt, stdout, stderr);
                    default:
                        stderr.WriteLine("未知命令");
                        return ExitArgError;
                }
            }
            catch (Exception ex)
            {
                stderr.WriteLine("错误: " + ex.Message);
                return ExitWriteFailed;
            }
        }

        // ---------------- scan ----------------

        private int RunScan(IMeterDataSource source, CliOptions opt, System.IO.TextWriter stdout, System.IO.TextWriter stderr)
        {
            var byMac = new Dictionary<string, DiscoveredDevice>(StringComparer.OrdinalIgnoreCase);
            source.DeviceFound += d =>
            {
                // 按 MAC 去重：同一设备的无名/有名包合并，优先保留有名字的，其次信号最强的
                if (byMac.TryGetValue(d.MacAddress, out var existing))
                {
                    bool existingKnown = existing.Name != "(未知)";
                    bool newKnown = d.Name != "(未知)";
                    bool better = (!existingKnown && newKnown) || (existingKnown == newKnown && d.Rssi > existing.Rssi);
                    if (better) byMac[d.MacAddress] = d;
                }
                else byMac[d.MacAddress] = d;
            };
            // 诊断：若数据源暴露 AdvertisementSeen 事件（如 BLE 源），把每个广播包打到 stderr 便于排查
            var advEvt = source.GetType().GetEvent("AdvertisementSeen");
            if (advEvt != null)
            {
                var handler = new Action<string>(line => stderr.WriteLine("[ble] " + line));
                advEvt.AddEventHandler(source, handler);
            }
            source.StartScan();
            _waiter.Wait(ScanWindow);
            source.StopScan();

            var found = byMac.Values.OrderByDescending(d => d.Rssi).ToList();
            if (found.Count == 0)
            {
                stderr.WriteLine("未发现设备，请确认 DM40 已开机");
                return ExitNoDevice;
            }

            if (opt.Format == CliFormat.Csv)
                stdout.WriteLine("name,mac,rssi");
            foreach (var d in found)
                stdout.WriteLine(FormatDevice(d, opt.Format));
            return ExitOk;
        }

        internal static string FormatDevice(DiscoveredDevice d, CliFormat format)
        {
            if (format == CliFormat.Csv)
                return d.Name + "," + d.MacAddress + "," + d.Rssi;
            return "{\"name\":" + Json.Str(d.Name) + ",\"mac\":" + Json.Str(d.MacAddress) + ",\"rssi\":" + d.Rssi + "}";
        }

        // ---------------- stream ----------------

        private int RunStream(IMeterDataSource source, CliOptions opt, System.IO.TextWriter stdout, System.IO.TextWriter stderr)
        {
            int code = ConnectAndMeasure(source, opt, stderr);
            if (code != ExitOk) return code;

            var session = new MeterSession(source, _clock);
            session.Alarm.IsEnabled = opt.UpperLimit.HasValue || opt.LowerLimit.HasValue;
            session.Alarm.UpperLimit = opt.UpperLimit;
            session.Alarm.LowerLimit = opt.LowerLimit;

            string lastAlarm = null; // 最近一次越限方向（HIGH/LOW），随帧输出
            session.AlarmTriggered += e => lastAlarm = e.Direction == AlarmDirection.High ? "HIGH" : "LOW";

            if (opt.Format == CliFormat.Csv)
                stdout.WriteLine(CsvSerializer.Header);

            int readingCount = 0;
            session.ReadingAvailable += m =>
            {
                readingCount++;
                stdout.WriteLine(FormatReading(m, opt.Format, lastAlarm));
            };

            WaitForDuration(opt.DurationSeconds);
            source.Disconnect();

            // 契约修正（2026-11-25）：连接失败/GUI 占用链路时实测会拿到 status=Connected 但 0 帧，
            // 原实现仍返回 0，自动化只看退出码会误判成功。无数据帧一律按连接失败返回 3。
            if (readingCount == 0)
            {
                stderr.WriteLine("0 帧数据：链路未真正就绪（GUI 是否占用蓝牙？）");
                return ExitConnectFailed;
            }
            return ExitOk;
        }

        internal static string FormatReading(Measurement m, CliFormat format, string alarm)
        {
            if (format == CliFormat.Csv)
            {
                var rec = new CsvRecord
                {
                    Timestamp = DateTime.Now,
                    Value = m.NormValue ?? 0,
                    Unit = m.DisplayUnit,
                    Mode = m.Kind,
                    Range = m.Range,
                    Alarm = alarm,
                };
                return CsvSerializer.Serialize(rec);
            }
            return "{\"ts\":" + Json.Str(DateTime.Now.ToString("yyyy-MM-dd'T'HH:mm:ss.fff")) +
                   ",\"value\":" + Json.Num(m.NormValue) +
                   ",\"unit\":" + Json.Str(m.DisplayUnit) +
                   ",\"mode\":" + Json.Str(m.Kind) +
                   ",\"range\":" + Json.Str(m.Range) +
                   ",\"overload\":" + Json.Bool(m.Overload) +
                   ",\"alarm\":" + (alarm == null ? "null" : Json.Str(alarm)) + "}";
        }

        // ---------------- send ----------------

        private int RunSend(IMeterDataSource source, CliOptions opt, System.IO.TextWriter stdout, System.IO.TextWriter stderr)
        {
            if (!CommandMap.TryGetValue(opt.SendCmd, out var frame))
            {
                stderr.WriteLine("未知命令名: " + opt.SendCmd);
                return ExitArgError;
            }

            int code = ConnectAndMeasure(source, opt, stderr);
            if (code != ExitOk) return code;

            try
            {
                source.SendCommand(frame);
            }
            catch (Exception ex)
            {
                stderr.WriteLine("命令发送失败: " + ex.Message);
                source.Disconnect();
                return ExitSendFailed;
            }

            stdout.WriteLine("{\"sent\":" + Json.Str(opt.SendCmd) + ",\"frame\":" + Json.Str(ToHex(frame)) + "}");
            source.Disconnect();
            return ExitOk;
        }

        // 命令名 → 命令帧。原有 10 个别名保持 CLI 契约不变；
        // 另把 ModeCatalog 的全部档位/量程（如 vdc-600mv、res-online-auto、temp-auto）并进来，供 send 与 probe 共用。
        internal static readonly IReadOnlyDictionary<string, byte[]> CommandMap = BuildCommandMap();

        private static Dictionary<string, byte[]> BuildCommandMap()
        {
            var map = new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase)
            {
                { "hold-on", ProtocolConstants.CmdHoldOn },
                { "hold-off", ProtocolConstants.CmdHoldOff },
                { "auto-on", ProtocolConstants.CmdAutoOn },
                { "auto-off", ProtocolConstants.CmdAutoOff },
                { "rel", ProtocolConstants.CmdRelative },
                { "cap", ProtocolConstants.CmdCap },
                { "diode", ProtocolConstants.CmdDiode },
                { "cont", ProtocolConstants.CmdCont },
                { "hz", ProtocolConstants.CmdHz },
                { "temp", ProtocolConstants.CmdTemp },
            };
            foreach (var m in ModeCatalog.All)
                if (!map.ContainsKey(m.Key)) map[m.Key] = ModeCatalog.BuildCommand(m.Flag);
            return map;
        }

        // ---------------- probe ----------------

        // FlagInfo 之外、值得真机试一把的候选（判断项：温度 ℉ / 占空比 的 flag 尚未证实）
        private static readonly byte[] ProbeExtraFlags =
        {
            0x06, 0x07, 0x0B, 0x13, 0x1B,
            0x33, 0x3B, 0x43, 0x46, 0x47,
            0x73, 0x9B, 0xBF,
        };

        /// <summary>
        /// 逐条下发档位/量程命令，读回帧的 data[5] 判定该 flag 是否被设备接受。
        /// 只切档，不改任何测量参数；结束后回到 VDC AUTO。
        /// </summary>
        private int RunProbe(IMeterDataSource source, CliOptions opt, System.IO.TextWriter stdout, System.IO.TextWriter stderr)
        {
            int code = ConnectAndMeasure(source, opt, stderr);
            if (code != ExitOk) return code;

            var state = new ProbeState(source.DeviceCounts);
            source.FrameReceived += state.OnFrame;

            var deadline = DateTime.UtcNow.AddSeconds(3);
            while (!state.HasFrame && DateTime.UtcNow < deadline)
                _waiter.Wait(TimeSpan.FromMilliseconds(100));

            if (!state.HasFrame)
            {
                stderr.WriteLine("未收到测量帧，无法探测（GUI 是否占用链路？）");
                source.Disconnect();
                return ExitConnectFailed;
            }

            int settle = Math.Max(200, opt.SettleMs);
            var targets = new List<ModeEntry>(ModeCatalog.All);
            foreach (var f in ProbeExtraFlags)
                targets.Add(new ModeEntry(f, "?", "?", "probe-" + f.ToString("x2"), "候选 0x" + f.ToString("X2")));

            if (opt.Format == CliFormat.Csv)
                stdout.WriteLine("flag,key,kind,range,observed,obs_kind,obs_range,accepted,frames");

            foreach (var t in targets)
            {
                state.Reset();
                source.SendCommand(ModeCatalog.BuildCommand(t.Flag));
                _waiter.Wait(TimeSpan.FromMilliseconds(settle));

                bool got = state.HasFrame;
                byte obs = state.Flag;
                var m = state.Latest;
                bool accepted = got && obs == t.Flag;

                if (opt.Format == CliFormat.Csv)
                {
                    stdout.WriteLine(string.Join(",",
                        "0x" + t.Flag.ToString("X2"), t.Key, t.Kind, t.Range,
                        got ? "0x" + obs.ToString("X2") : "",
                        m?.Kind ?? "", m?.Range ?? "",
                        accepted ? "1" : "0", state.Frames.ToString()));
                }
                else
                {
                    stdout.WriteLine("{\"flag\":" + Json.Str("0x" + t.Flag.ToString("X2")) +
                                     ",\"key\":" + Json.Str(t.Key) +
                                     ",\"kind\":" + Json.Str(t.Kind) +
                                     ",\"range\":" + Json.Str(t.Range) +
                                     ",\"observed\":" + Json.Str(got ? "0x" + obs.ToString("X2") : null) +
                                     ",\"obsKind\":" + Json.Str(m?.Kind) +
                                     ",\"obsRange\":" + Json.Str(m?.Range) +
                                     ",\"accepted\":" + Json.Bool(accepted) +
                                     ",\"frames\":" + state.Frames + "}");
                }
                stdout.Flush();
            }

            // 探测完回到 VDC AUTO，避免把表留在电容/温度之类的档
            source.SendCommand(ModeCatalog.BuildCommand(0x28));
            _waiter.Wait(TimeSpan.FromMilliseconds(settle));
            source.Disconnect();
            return ExitOk;
        }

        /// <summary>探测期间共享的最新一帧状态（回调来自 BLE 线程，加锁读取）。</summary>
        private sealed class ProbeState
        {
            private readonly int _counts;
            private readonly object _lock = new object();
            private byte _flag;
            private Measurement _latest;
            private bool _hasFrame;
            private int _frames;

            public ProbeState(int counts) { _counts = counts > 0 ? counts : 60000; }

            public void OnFrame(byte[] data)
            {
                if (data == null || data.Length < 7) return;
                var m = MeasurementParser.Parse(data, _counts);
                if (!m.IsValid) return;
                lock (_lock)
                {
                    _flag = data[5];
                    _latest = m;
                    _hasFrame = true;
                    _frames++;
                }
            }

            public void Reset()
            {
                lock (_lock) { _hasFrame = false; _latest = null; }
            }

            public bool HasFrame { get { lock (_lock) return _hasFrame; } }
            public byte Flag { get { lock (_lock) return _flag; } }
            public Measurement Latest { get { lock (_lock) return _latest; } }
            public int Frames { get { lock (_lock) return _frames; } }
        }

        // ---------------- record ----------------

        private int RunRecord(IMeterDataSource source, CliOptions opt, System.IO.TextWriter stdout, System.IO.TextWriter stderr)
        {
            int code = ConnectAndMeasure(source, opt, stderr);
            if (code != ExitOk) return code;

            var session = new MeterSession(source, _clock);
            using (var sink = new FileCsvSink(opt.OutFile))
            using (var recorder = new CsvRecorder(sink, _clock))
            {
                session.Recorder = recorder;
                recorder.Start();
                WaitForDuration(opt.DurationSeconds, onSecond: () => recorder.Tick());
                recorder.Stop();
                if (recorder.HasError)
                {
                    stderr.WriteLine("录制失败: " + recorder.ErrorMessage);
                    return ExitWriteFailed;
                }
            }

            stdout.WriteLine("{\"recorded\":" + session.Waveform.Count + ",\"path\":" + Json.Str(opt.OutFile) + "}");
            source.Disconnect();

            // 同 stream：0 帧不算成功（契约 §退出码 3）
            if (session.Waveform.Count == 0)
            {
                stderr.WriteLine("0 帧数据：录制文件为空，链路未真正就绪");
                return ExitConnectFailed;
            }
            return ExitOk;
        }

        // ---------------- 共用 ----------------

        // 连接并进入测量态；返回退出码（0 成功）。
        private int ConnectAndMeasure(IMeterDataSource source, CliOptions opt, System.IO.TextWriter stderr)
        {
            // 诊断：把数据源的 Diagnostic 事件（如 BLE 连接各阶段）打到 stderr
            // 回调在 WinRT 后台线程触发，WriteLine 后立即 Flush，避免进程退出前丢失
            var diagEvt = source.GetType().GetEvent("Diagnostic");
            if (diagEvt != null)
                diagEvt.AddEventHandler(source, new Action<string>(line =>
                {
                    lock (stderr) { stderr.WriteLine("[conn] " + line); stderr.Flush(); }
                }));

            DiscoveredDevice first = null;
            source.DeviceFound += d => { if (first == null) first = d; };
            source.StartScan();
            _waiter.Wait(ScanWindow);
            source.StopScan();

            var target = first;
            if (opt.Mac != null)
            {
                // 指定 MAC 时，若扫描到则用；模拟源始终可连
                target = target ?? new DiscoveredDevice("DM40", opt.Mac, 0);
                if (target.MacAddress != opt.Mac && first == null)
                    target = new DiscoveredDevice("DM40", opt.Mac, 0);
            }

            if (target == null)
            {
                stderr.WriteLine("未发现设备");
                return ExitNoDevice;
            }

            try
            {
                source.Connect(opt.Mac ?? target.MacAddress);
            }
            catch (Exception ex)
            {
                stderr.WriteLine("连接失败: " + ex.Message);
                return ExitConnectFailed;
            }
            return ExitOk;
        }

        private void WaitForDuration(int seconds, Action onSecond = null)
        {
            if (seconds <= 0)
            {
                // 持续模式：等待直到外部中断（Ctrl+C 由宿主处理，此处阻塞）
                _waiter.WaitIndefinitely();
                return;
            }
            for (int i = 0; i < seconds; i++)
            {
                _waiter.Wait(TimeSpan.FromSeconds(1));
                onSecond?.Invoke();
            }
        }

        private static string ToHex(byte[] data)
        {
            var c = new char[data.Length * 3 - 1];
            for (int i = 0; i < data.Length; i++)
            {
                int j = i * 3;
                int b = data[i];
                c[j] = (char)(b >> 4 < 10 ? '0' + (b >> 4) : 'A' + (b >> 4) - 10);
                int lo = b & 0xF;
                c[j + 1] = (char)(lo < 10 ? '0' + lo : 'A' + lo - 10);
                if (j + 2 < c.Length) c[j + 2] = ' ';
            }
            return new string(c);
        }
    }

    /// <summary>等待抽象（测试 seam）：CLI 用它在扫描窗口/录制时长内等待，测试用即时返回。</summary>
    public interface IWaitHandle
    {
        void Wait(TimeSpan duration);
        void WaitIndefinitely();
    }
}
