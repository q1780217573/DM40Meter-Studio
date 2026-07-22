using System;

namespace Dm40.Core
{
    /// <summary>设备通告（扫描发现的一台设备）。</summary>
    public sealed class DiscoveredDevice
    {
        public DiscoveredDevice(string name, string macAddress, int rssi)
        {
            Name = name;
            MacAddress = macAddress;
            Rssi = rssi;
        }

        /// <summary>设备名（DM40 前缀）。</summary>
        public string Name { get; }
        /// <summary>MAC 地址。</summary>
        public string MacAddress { get; }
        /// <summary>信号强度 dBm。</summary>
        public int Rssi { get; }

        /// <summary>设备下拉显示名（"DM40B (86:5C:…) · -52dBm"）。</summary>
        public string DisplayName => $"{Name} ({MacAddress}) · {Rssi}dBm";
    }

    /// <summary>
    /// 测量数据源抽象（FR-001/002、§5.1）。统一真实 BLE 设备的接入，
    /// 使上层（App/Core）不感知底层传输细节。生产实现为 <c>Dm40.Transport.BleMeterSource</c>；
    /// 离线单测用内存替身注入真实抓取的帧字节。
    /// 实现线程：回调可能来自任意线程，订阅方负责 marshal 到 UI 线程（UI 节流）。
    /// </summary>
    public interface IMeterDataSource
    {
        /// <summary>扫描发现一台设备。</summary>
        event Action<DiscoveredDevice> DeviceFound;

        /// <summary>收到一帧原始测量数据（未解析，byte[]）。</summary>
        event Action<byte[]> FrameReceived;

        /// <summary>连接状态变化。</summary>
        event Action<ConnectionState> StateChanged;

        /// <summary>诊断信息（连接各阶段、命令写出状态、失败原因等），供 UI/日志展示。</summary>
        event Action<string> Diagnostic;

        /// <summary>当前连接状态。</summary>
        ConnectionState State { get; }

        /// <summary>设备满量程计数（DM40A/B/C = 40000/50000/60000），连接后经型号校验确定。</summary>
        int DeviceCounts { get; }

        /// <summary>开始扫描 BLE 设备。</summary>
        void StartScan();

        /// <summary>停止扫描。</summary>
        void StopScan();

        /// <summary>连接指定 MAC 的设备（含型号校验 + 订阅 Notify）。</summary>
        void Connect(string macAddress);

        /// <summary>断开当前连接。</summary>
        void Disconnect();

        /// <summary>下发一条命令帧（6 字节）。写特征失败应抛异常。</summary>
        void SendCommand(byte[] command);
    }
}
