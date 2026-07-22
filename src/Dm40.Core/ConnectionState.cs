using System;

namespace Dm40.Core
{
    /// <summary>连接状态（PRD §5.3）。</summary>
    public enum ConnectionState
    {
        /// <summary>未连接。</summary>
        Disconnected,
        /// <summary>扫描中。</summary>
        Scanning,
        /// <summary>设备列表（已发现设备，待选择）。</summary>
        DeviceList,
        /// <summary>连接中。</summary>
        Connecting,
        /// <summary>已连接（GATT 成功 + 型号校验通过，未订阅通知）。</summary>
        Connected,
        /// <summary>测量中（已订阅 fff1 Notify）。</summary>
        Measuring,
        /// <summary>重连中（意外断开，自动重连）。</summary>
        Reconnecting,
    }
}
