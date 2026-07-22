using System;
using System.Collections.Generic;
using Dm40.Protocol;

namespace Dm40.Core
{
    /// <summary>
    /// 测量会话协调器：把 IMeterDataSource 的帧流聚合到 Core 各组件
    /// （解析 → 统计 → 报警 → 波形缓冲 → 录制），是 §5.1 主链路的"核心层"实现。
    /// 与 UI 解耦：通过事件暴露增量，订阅方（App）负责 marshal 到 UI 线程并节流。
    /// 可注入 IClock/ICsvSink，故可完全离线单测。
    /// </summary>
    public sealed class MeterSession
    {
        private readonly IMeterDataSource _source;
        private readonly IClock _clock;

        public MeterSession(IMeterDataSource source, IClock clock)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));

            Statistics = new MeasurementStatistics();
            Alarm = new ThresholdAlarm(clock);
            Waveform = new WaveformBuffer();

            _source.FrameReceived += OnFrame;
            Alarm.Triggered += e => AlarmTriggered?.Invoke(e);
        }

        /// <summary>统计面板。</summary>
        public MeasurementStatistics Statistics { get; }

        /// <summary>阈值报警。</summary>
        public ThresholdAlarm Alarm { get; }

        /// <summary>波形缓冲（CSV 导出的数据源，FR-005/§5.4）。</summary>
        public WaveformBuffer Waveform { get; }

        /// <summary>录制器（由 App 在启动录制时创建并赋值）。</summary>
        public CsvRecorder Recorder { get; set; }

        /// <summary>当前连接状态（代理自数据源）。</summary>
        public ConnectionState State => _source.State;

        /// <summary>解析出一条有效读数时触发（UI 刷新读数/波形）。</summary>
        public event Action<Measurement> ReadingAvailable;

        /// <summary>收到任意原始帧（含无效帧，供报文查看器留痕，FR-011）。</summary>
        public event Action<byte[], Measurement> RawFrame;

        /// <summary>报警触发（声音/弹窗/红标）。</summary>
        public event Action<AlarmEvent> AlarmTriggered;

        /// <summary>收到原始帧：CRC/解析 → 有效则统计/报警/波形/录制，无效仅留痕。</summary>
        private void OnFrame(byte[] data)
        {
            var m = MeasurementParser.Parse(data, _source.DeviceCounts);
            RawFrame?.Invoke(data, m);

            // §5.2 规则1：无效帧不参与显示/统计/报警/录制
            if (!m.CrcOk || !m.IsValid)
                return;

            // 过载/无效主值不参与统计与报警，但仍算一条波形断点由 UI 处理
            if (m.NormValue.HasValue)
            {
                var now = _clock.UtcNow;
                Statistics.Add(m.NormValue.Value);
                Alarm.Feed(m.NormValue.Value);
                Waveform.Add(now, m.NormValue.Value);

                Recorder?.Write(new CsvRecord
                {
                    Timestamp = now.ToLocalTime(),
                    Value = m.NormValue.Value,
                    Unit = m.DisplayUnit,
                    Mode = m.Kind,
                    Range = m.Range,
                });
            }

            ReadingAvailable?.Invoke(m);
        }

        /// <summary>录制 tick（App 每秒调用以 flush）。</summary>
        public void TickRecorder() => Recorder?.Tick();
    }
}
