using System;

namespace Dm40.Core
{
    /// <summary>时间源抽象（测试 seam）：核心层所有时间相关逻辑经它取时，便于注入假时钟。</summary>
    public interface IClock
    {
        /// <summary>当前时刻（单调递增即可，用于滞回/时长计算）。</summary>
        DateTime UtcNow { get; }
    }

    /// <summary>系统时钟（生产用）。</summary>
    public sealed class SystemClock : IClock
    {
        public static readonly SystemClock Instance = new SystemClock();
        private SystemClock() { }
        public DateTime UtcNow => DateTime.UtcNow;
    }
}
