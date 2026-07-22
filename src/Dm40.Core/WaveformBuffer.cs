using System;
using System.Collections.Generic;
using System.Linq;

namespace Dm40.Core
{
    /// <summary>
    /// 波形样本缓冲（FR-005、§5.4）：内存上限 <see cref="Capacity"/> 点，超出丢弃最旧。
    /// 即 CSV 导出的数据源。环形语义，按时间顺序枚举。
    ///
    /// 线程安全：写入（<see cref="Add"/>）来自后台帧线程，读取（<see cref="Points"/>/<see cref="Since"/>）
    /// 来自 UI 线程（波形刷新 30fps）。两者并发枚举同一 Queue 会抛 InvalidOperationException，故所有访问
    /// 经 <see cref="_lock"/> 串行化；<see cref="Points"/>/<see cref="Since"/> 返回**即时快照拷贝**，
    /// 调用方枚举的是拷贝，不会被后台写入改动。
    /// </summary>
    public sealed class WaveformBuffer
    {
        /// <summary>内存上限（默认 100 万点）。</summary>
        public const int Capacity = 1_000_000;

        private readonly object _lock = new object();
        private readonly Queue<WaveformPoint> _points = new Queue<WaveformPoint>();

        /// <summary>当前缓存点数。</summary>
        public int Count { get { lock (_lock) return _points.Count; } }

        /// <summary>是否因超上限丢弃过最旧点（用于提示）。</summary>
        public bool HasOverflowed { get; private set; }

        /// <summary>追加一个样本点。</summary>
        public void Add(DateTime timestamp, double value)
        {
            lock (_lock)
            {
                if (_points.Count >= Capacity)
                {
                    _points.Dequeue();
                    HasOverflowed = true;
                }
                _points.Enqueue(new WaveformPoint(timestamp, value));
            }
        }

        /// <summary>全部点的快照拷贝（锁内 ToArray）。调用方枚举的是拷贝，与后台写入隔离。</summary>
        public WaveformPoint[] Points { get { lock (_lock) return _points.ToArray(); } }

        /// <summary>清空缓冲。</summary>
        public void Clear() { lock (_lock) _points.Clear(); }

        /// <summary>取最近 duration 时间窗内的点快照（波形滚动显示用）。</summary>
        public List<WaveformPoint> Since(DateTime earliest)
        {
            var result = new List<WaveformPoint>();
            lock (_lock)
            {
                foreach (var p in _points)
                    if (p.Timestamp >= earliest)
                        result.Add(p);
            }
            return result;
        }
    }

    /// <summary>一个波形样本点。</summary>
    public readonly struct WaveformPoint
    {
        public WaveformPoint(DateTime timestamp, double value)
        {
            Timestamp = timestamp;
            Value = value;
        }

        public DateTime Timestamp { get; }
        public double Value { get; }
    }
}
