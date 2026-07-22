using System;

namespace Dm40.Core
{
    /// <summary>
    /// 最小内存数据源替身：替代已移除的 SimulatedMeterSource，供核心逻辑（MeterSession/统计/报警/录制/CLI）离线单测。
    /// 与模拟源的区别：不合成帧，而是由测试显式 <see cref="PushFrame"/> 注入<b>真实抓取的 DM40 帧字节</b>（见 <see cref="RealFrames"/>）。
    /// 这样测试跑的是真实数据，而非合成数据——符合"全部使用实际数据"的要求。
    /// </summary>
    public sealed class FakeMeterSource : IMeterDataSource
    {
        public event Action<DiscoveredDevice> DeviceFound;
        public event Action<byte[]> FrameReceived;
        public event Action<ConnectionState> StateChanged;
        public event Action<string> Diagnostic;

        public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
        public int DeviceCounts { get; set; } = RealFrames.DeviceCounts;

        /// <summary>最后下发的命令帧（send 命令测试断言用）。</summary>
        public byte[] LastCommand { get; private set; }

        public void StartScan()
        {
            SetState(ConnectionState.Scanning);
            DeviceFound?.Invoke(new DiscoveredDevice("DM40B", "86:5C:1C:06:A1:97", -52));
        }

        public void StopScan()
        {
            if (State == ConnectionState.Scanning) SetState(ConnectionState.Disconnected);
        }

        public void Connect(string macAddress)
        {
            SetState(ConnectionState.Connecting);
            SetState(ConnectionState.Connected);
            SetState(ConnectionState.Measuring);
        }

        public void Disconnect() => SetState(ConnectionState.Disconnected);

        public void SendCommand(byte[] command)
        {
            if (command == null) throw new ArgumentNullException(nameof(command));
            LastCommand = command;
        }

        /// <summary>注入一帧（测试驱动统计/报警/录制/波形）。</summary>
        public void PushFrame(byte[] frame) => FrameReceived?.Invoke(frame);

        private void SetState(ConnectionState s)
        {
            State = s;
            StateChanged?.Invoke(s);
        }
    }
}
