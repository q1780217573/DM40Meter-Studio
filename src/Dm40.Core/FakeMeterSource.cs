using System;

namespace Dm40.Core
{
    /// <summary>
    /// 最小内存数据源替身：替代已移除的 SimulatedMeterSource，供核心逻辑（MeterSession/统计/报警/录制/CLI）离线单测。
    /// 与模拟源的区别：不合成帧，而是由测试显式 <see cref="PushFrame"/> 注入<b>真实抓取的 DM40 帧字节</b>（见 <see cref="RealFrames"/>）。
    /// 这样测试跑的是真实数据，而非合成数据——符合"全部使用实际数据"的要求。
    ///
    /// 2026-11-25 增强：替身现在会记住"设备当前档位/状态字节"，下发命令后改写状态并回推一帧，
    /// 使 CLI 的 applied 生效判据（档位/ HOLD / REL）能被离线验证——原来 SendCommand 只记录帧、不回帧，
    /// 任何"命令是否真的生效"的测试都只能靠肉眼。
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

        private byte[] _lastFrame;      // 最近一帧
        private bool _hasState;         // 是否已从首帧学到设备状态
        private bool _mirrorState;      // 下发过命令后，后续注入帧套用设备状态（模拟真机持续回报新状态）
        private byte _stateFlag;        // 设备当前档位/量程标志（data[5]）
        private byte _stateStatus;      // 设备当前状态字节（data[6]）

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

            // 模拟真机：按命令帧改状态（档位旗标 / HOLD bit7 / REL bit5），随后回推一帧
            if (!_hasState || command.Length < 6) return;
            if (command[3] == 0x06 && command[4] == 0x01)
                _stateFlag = command[5];
            else if (command[3] == 0x04)
                _stateStatus = (byte) (command[5] != 0 ? (_stateStatus | 0x80) : (_stateStatus & ~0x80));
            else if (command[3] == 0x05)
                _stateStatus = (byte) (command[5] != 0 ? (_stateStatus | 0x20) : (_stateStatus & ~0x20));
            else
                return; // 其余命令不改状态（与真机一致）

            // 命令生效后，真机会在后续每一帧里持续回报新状态
            _mirrorState = true;
            if (_lastFrame != null) FrameReceived?.Invoke(ApplyState(_lastFrame));
        }

        /// <summary>
        /// 注入一帧（测试驱动统计/报警/录制/波形）。
        /// 未下发过命令时原样注入（保持 CRC 损坏等测试语义）；下发过命令后套用当前设备状态。
        /// </summary>
        public void PushFrame(byte[] frame)
        {
            if (frame == null) return;
            _lastFrame = frame;
            if (frame.Length < 17)
            {
                FrameReceived?.Invoke(frame);
                return;
            }

            if (!_hasState)
            {
                _stateFlag = frame[5];
                _stateStatus = frame[6];
                _hasState = true;
                FrameReceived?.Invoke(frame);
                return;
            }

            FrameReceived?.Invoke(_mirrorState ? ApplyState(frame) : frame);
        }

        // 把当前设备状态写回帧的 data[5]/data[6]，并重算 CRC（全帧和 & 0xFF == 0）
        private byte[] ApplyState(byte[] frame)
        {
            var f = (byte[]) frame.Clone();
            f[5] = _stateFlag;
            f[6] = _stateStatus;
            int sum = 0;
            for (int i = 0; i < f.Length - 1; i++) sum += f[i];
            f[f.Length - 1] = (byte) ((-sum) & 0xFF);
            return f;
        }

        private void SetState(ConnectionState s)
        {
            State = s;
            StateChanged?.Invoke(s);
        }
    }
}