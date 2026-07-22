using System;

namespace Dm40.Core
{
    /// <summary>
    /// 周期性触发器抽象（测试 seam）：模拟设备/录制 flush 用它驱动。
    /// 生产用 SystemTicker（Timer），测试用手动 ManualTicker 逐步触发。
    /// </summary>
    public interface ITicker : IDisposable
    {
        /// <summary>每次到期间隔触发。</summary>
        event Action Elapsed;

        /// <summary>以指定间隔启动。</summary>
        void Start(TimeSpan interval);

        /// <summary>停止。</summary>
        void Stop();
    }
}
