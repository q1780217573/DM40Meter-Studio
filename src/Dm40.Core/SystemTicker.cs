using System;
using System.Threading;

namespace Dm40.Core
{
    /// <summary>生产用周期触发器：基于 System.Threading.Timer。</summary>
    public sealed class SystemTicker : ITicker
    {
        private Timer _timer;

        public event Action Elapsed;

        public void Start(TimeSpan interval)
        {
            Stop();
            _timer = new Timer(_ => Elapsed?.Invoke(), null, interval, interval);
        }

        public void Stop()
        {
            _timer?.Dispose();
            _timer = null;
        }

        public void Dispose() => Stop();
    }

    /// <summary>测试用手动触发器：调用 <see cref="Tick"/> 才触发一次。</summary>
    public sealed class ManualTicker : ITicker
    {
        public event Action Elapsed;
        public bool IsRunning { get; private set; }
        public TimeSpan? Interval { get; private set; }

        public void Start(TimeSpan interval)
        {
            IsRunning = true;
            Interval = interval;
        }

        public void Stop() => IsRunning = false;

        /// <summary>手动触发一次（测试用）。</summary>
        public void Tick()
        {
            if (IsRunning) Elapsed?.Invoke();
        }

        public void Dispose() => Stop();
    }
}
