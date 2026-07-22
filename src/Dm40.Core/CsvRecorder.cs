using System;
using System.Collections.Generic;

namespace Dm40.Core
{
    /// <summary>
    /// CSV 连续录制器（PRD FR-009、§5.2 规则5、录制状态机）。
    /// 内存队列缓冲，由 <see cref="Tick"/>（外部每秒调用一次）flush 落盘；
    /// 写失败立即停止录制（IsRecording=false, HasError=true）并保留已录数据。
    /// 落盘经 <see cref="ICsvSink"/> 抽象，测试可用内存实现。
    /// </summary>
    public sealed class CsvRecorder : IDisposable
    {
        private static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(1);

        private readonly ICsvSink _sink;
        private readonly IClock _clock;
        private readonly List<string> _buffer = new List<string>();
        private DateTime _lastFlush;
        private bool _disposed;

        public CsvRecorder(ICsvSink sink, IClock clock)
        {
            _sink = sink ?? throw new ArgumentNullException(nameof(sink));
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        /// <summary>是否录制中。</summary>
        public bool IsRecording { get; private set; }

        /// <summary>是否发生写入错误（已停止录制）。</summary>
        public bool HasError { get; private set; }

        /// <summary>当前缓冲中待落盘的行数。</summary>
        public int BufferedCount => _buffer.Count;

        /// <summary>错误信息（HasError 时有效）。</summary>
        public string ErrorMessage { get; private set; }

        /// <summary>开始录制：写入表头，进入录制态。重复调用幂等。</summary>
        public void Start()
        {
            if (IsRecording) return;
            _sink.WriteLine(CsvSerializer.Header);
            _sink.Flush();
            _lastFlush = _clock.UtcNow;
            IsRecording = true;
            HasError = false;
            ErrorMessage = null;
        }

        /// <summary>追加一条记录到缓冲。非录制态时忽略。</summary>
        public void Write(CsvRecord record)
        {
            if (!IsRecording || record == null) return;
            _buffer.Add(CsvSerializer.Serialize(record));
        }

        /// <summary>由外部定时器每秒调用：到达 flush 间隔则落盘缓冲。</summary>
        public void Tick()
        {
            if (!IsRecording) return;
            if (_clock.UtcNow - _lastFlush < FlushInterval) return;
            FlushNow();
        }

        /// <summary>停止录制并落盘剩余缓冲。</summary>
        public void Stop()
        {
            if (!IsRecording) return;
            if (!HasError) FlushNow();
            IsRecording = false;
        }

        private void FlushNow()
        {
            try
            {
                foreach (var line in _buffer)
                    _sink.WriteLine(line);
                _buffer.Clear();
                _sink.Flush();
                _lastFlush = _clock.UtcNow;
            }
            catch (Exception ex)
            {
                // PRD：磁盘满/写失败 → 停止录制，保留已录数据，明确提示
                HasError = true;
                ErrorMessage = ex.Message;
                IsRecording = false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try { Stop(); } finally { _sink.Dispose(); }
        }
    }
}
