using System;

namespace Dm40.Core
{
    /// <summary>
    /// 手动推进的假时钟（测试工具）。供各层测试精确控制时间相关逻辑（滞回/时长/录制 flush）。
    /// 生产代码用 <see cref="SystemClock"/>。
    /// </summary>
    public sealed class FakeClock : IClock
    {
        public FakeClock() => UtcNow = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        public FakeClock(DateTime start) => UtcNow = start;

        public DateTime UtcNow { get; private set; }

        public void Advance(TimeSpan dt) => UtcNow += dt;
        public void AdvanceMs(int ms) => UtcNow += TimeSpan.FromMilliseconds(ms);
        public void AdvanceSeconds(int s) => UtcNow += TimeSpan.FromSeconds(s);
    }
}
