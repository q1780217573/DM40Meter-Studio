using System;

namespace Dm40.Core
{
    /// <summary>越限方向。</summary>
    public enum AlarmDirection
    {
        /// <summary>高于上限。</summary>
        High,
        /// <summary>低于下限。</summary>
        Low,
    }

    /// <summary>一次报警事件（PRD FR-007：记录时间、方向、值）。</summary>
    public sealed class AlarmEvent
    {
        public AlarmEvent(DateTime timestamp, AlarmDirection direction, double value)
        {
            Timestamp = timestamp;
            Direction = direction;
            Value = value;
        }

        public DateTime Timestamp { get; }
        public AlarmDirection Direction { get; }
        public double Value { get; }
    }
}
