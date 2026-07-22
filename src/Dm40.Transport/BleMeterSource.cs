using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Dm40.Core;
using Dm40.Protocol;
using Windows.Devices.Bluetooth;
using Windows.Devices.Bluetooth.Advertisement;
using Windows.Devices.Bluetooth.GenericAttributeProfile;

namespace Dm40.Transport
{
    /// <summary>
    /// 真实 DM40 BLE 数据源（PRD §11.2、FR-001/002/004）。
    /// 实现 <see cref="IMeterDataSource"/>，用 WinRT Windows.Devices.Bluetooth 完成：
    ///  - 扫描（BluetoothLEAdvertisementWatcher，过滤 DM40 前缀）
    ///  - 连接（BluetoothLEDevice.FromBluetoothAddressAsync + 型号校验 CMD_ID）
    ///  - 订阅 Notify 特征 fff1（ValueChanged 推送测量帧）
    ///  - 写特征 fff3 下发 6 字节命令帧
    ///
    /// ⚠️ 本文件仅在 BLE_ENABLED=true（装好 Windows SDK）时编译进项目。
    /// 启用步骤见 Dm40.Transport.csproj 顶部注释与 CLAUDE.md 的 R-06。
    /// 回调在 WinRT 线程触发，订阅方负责 marshal 到 UI 线程。
    /// </summary>
    public sealed class BleMeterSource : IMeterDataSource
    {
        // GATT 端点（PRD §11.2）
        private static readonly Guid ServiceUuid = Guid.Parse("0000fff0-0000-1000-8000-00805f9b34fb");
        private static readonly Guid NotifyUuid = Guid.Parse("0000fff1-0000-1000-8000-00805f9b34fb");
        private static readonly Guid WriteUuid = Guid.Parse("0000fff3-0000-1000-8000-00805f9b34fb");

        private const string DeviceNamePrefix = "DM40";
        private const int DefaultDeviceCounts = 60000; // 默认 DM40C，型号校验后可细化

        private BluetoothLEAdvertisementWatcher _watcher;
        private BluetoothLEDevice _device;
        private GattCharacteristic _notifyChar;
        private GattCharacteristic _writeChar;
        private readonly Dictionary<string, string> _nameByMac = new Dictionary<string, string>(); // 扫描期按 MAC 记名字
        private readonly Dictionary<string, BluetoothAddressType> _addrTypeByMac = new Dictionary<string, BluetoothAddressType>(); // 扫描期按 MAC 记地址类型

        public event Action<DiscoveredDevice> DeviceFound;
        public event Action<byte[]> FrameReceived;
        public event Action<ConnectionState> StateChanged;

        public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
        public int DeviceCounts { get; private set; } = DefaultDeviceCounts;

        /// <summary>调试开关：true 时报告所有 BLE 广播设备（FR-001"显示全部 BLE 设备"）；false 仅 DM40 前缀。</summary>
        public bool ShowAllDevices { get; set; }

        /// <summary>每收到一个 BLE 广播包（含被过滤的）触发，用于诊断。参数：原始名/信号/MAC。</summary>
        public event Action<string> AdvertisementSeen;

        /// <summary>连接/测量各阶段诊断（连接成功、订阅、下发 CMD_READ、收到帧、失败原因）。</summary>
        public event Action<string> Diagnostic;
        private static readonly string _diagPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "diag.log");
        private void Diag(string msg)
        {
            Diagnostic?.Invoke(msg);
            // 落盘带时间戳（真机时序排障：帧到达节奏、命令写出时机、写状态）
            try { System.IO.File.AppendAllText(_diagPath, DateTime.Now.ToString("HH:mm:ss.fff") + " " + msg + System.Environment.NewLine, System.Text.Encoding.UTF8); } catch { }
        }

        public void StartScan()
        {
            if (State != ConnectionState.Disconnected) return;
            _nameByMac.Clear();
            SetState(ConnectionState.Scanning);

            _watcher = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
            _watcher.Received += OnAdvertisementReceived;
            _watcher.Start();
        }

        public void StopScan()
        {
            if (_watcher != null)
            {
                _watcher.Received -= OnAdvertisementReceived;
                _watcher.Stop();
                _watcher = null;
            }
            if (State == ConnectionState.Scanning) SetState(ConnectionState.Disconnected);
        }

        private void OnAdvertisementReceived(BluetoothLEAdvertisementWatcher sender, BluetoothLEAdvertisementReceivedEventArgs args)
        {
            string name = args.Advertisement.LocalName;
            string mac = FormatMac(args.BluetoothAddress);
            int rssi = args.RawSignalStrengthInDBm;
            _addrTypeByMac[mac] = args.BluetoothAddressType; // 记录真实地址类型，连接时用

            // 诊断：报告所有广播（含被过滤的）
            AdvertisementSeen?.Invoke($"{mac} rssi={rssi} addrType={args.BluetoothAddressType} name=\"{name}\"");

            // 设备名可能在主广播或扫描响应里，同一 MAC 会先后收到带名/不带名的包。
            // 按 MAC 记住已见过的名字，一旦见过带名包就用它；避免把无名包当成独立设备。
            if (!string.IsNullOrEmpty(name))
                _nameByMac[mac] = name;
            string effectiveName = _nameByMac.TryGetValue(mac, out var known) ? known : name;

            bool matches = !string.IsNullOrEmpty(effectiveName) && effectiveName.StartsWith(DeviceNamePrefix, StringComparison.OrdinalIgnoreCase);
            if (!matches && !ShowAllDevices)
                return; // 默认仅显示 DM40 前缀设备（FR-001）

            // 型号识别：DM40A/B/C → 40000/50000/60000 counts（影响工程值换算，PRD §11.2）
            if (ProtocolConstants.ModelTable.TryGetValue(effectiveName, out var counts))
                DeviceCounts = counts;

            DeviceFound?.Invoke(new DiscoveredDevice(string.IsNullOrEmpty(effectiveName) ? "(未知)" : effectiveName, mac, rssi));
        }

        public async void Connect(string macAddress)
        {
            try
            {
                SetState(ConnectionState.Connecting);
                ulong addr = ParseMac(macAddress);
                Diag($"连接 {macAddress} ...");

                // 用扫描期记录的真实地址类型（public/random）；没扫描到则两种都试。
                var typesToTry = new List<BluetoothAddressType>();
                if (_addrTypeByMac.TryGetValue(macAddress, out var knownType)) typesToTry.Add(knownType);
                if (!typesToTry.Contains(BluetoothAddressType.Public)) typesToTry.Add(BluetoothAddressType.Public);
                if (!typesToTry.Contains(BluetoothAddressType.Random)) typesToTry.Add(BluetoothAddressType.Random);

                // 解析期间保持一个 advertisement watcher 运行：收到该设备广播的瞬间它在系统缓存里"在线"，
                // FromBluetoothAddressAsync 此时最易成功（未配对 BLE 设备离线时返回 null）。
                using (var keepAlive = StartKeepAliveWatcher(addr))
                {
                    const int maxAttempts = 20;
                    for (int attempt = 1; attempt <= maxAttempts && _device == null; attempt++)
                    {
                        var addrType = typesToTry[(attempt - 1) % typesToTry.Count];
                        Task<BluetoothLEDevice> resolve;
                        try { resolve = BluetoothLEDevice.FromBluetoothAddressAsync(addr, addrType).AsTask(); }
                        catch (Exception rex) { Diag($"第 {attempt} 次启动解析即异常: {rex.HResult:X8} {rex.Message}"); await Task.Delay(300); continue; }

                        var completed = await Task.WhenAny(resolve, Task.Delay(1500));
                        if (completed != resolve) continue; // 静默重试，等下一次广播
                        if (resolve.IsFaulted)
                        {
                            var e = resolve.Exception?.InnerException;
                            Diag($"第 {attempt} 次解析失败({addrType}): {(e != null ? e.HResult.ToString("X8") + " " + e.Message : "?")}");
                            await Task.Delay(300); continue;
                        }
                        _device = resolve.Result;
                        if (_device == null) { await Task.Delay(300); continue; }
                        Diag($"第 {attempt} 次解析成功（{addrType}）");
                    }
                }
                if (_device == null) { Diag("多次解析均返回 null：设备不可达或未在广播"); ConnectFailed(); return; }
                Diag($"已拿到设备句柄 name=\"{_device.Name}\" status={_device.ConnectionStatus}");
                _device.ConnectionStatusChanged += (s, a) =>
                    Diag($"连接状态变更 → {s.ConnectionStatus}");

                // 获取 GATT 服务
                var services = await _device.GetGattServicesForUuidAsync(ServiceUuid);
                Diag($"GetGattServices status={services.Status} count={services.Services.Count}");
                if (services.Status != GattCommunicationStatus.Success || services.Services.Count == 0)
                { ConnectFailed(); return; }

                var service = services.Services[0];
                var notifyChars = await service.GetCharacteristicsForUuidAsync(NotifyUuid);
                var writeChars = await service.GetCharacteristicsForUuidAsync(WriteUuid);
                Diag($"notify={notifyChars.Characteristics.Count} write={writeChars.Characteristics.Count}");
                if (notifyChars.Characteristics.Count == 0) { ConnectFailed(); return; }

                _notifyChar = notifyChars.Characteristics[0];
                _writeChar = writeChars.Characteristics.Count > 0 ? writeChars.Characteristics[0] : null;

                // 型号校验（CMD_ID）：写后等待应答前缀 DF 05 03 08 14
                // 简化：订阅成功即视为 DM40（完整校验可在 ValueChanged 中匹配前缀）
                SetState(ConnectionState.Connected);

                // 订阅 Notify fff1 → 测量中
                _notifyChar.ValueChanged += OnValueChanged;
                var status = await _notifyChar.WriteClientCharacteristicConfigurationDescriptorAsync(
                    GattClientCharacteristicConfigurationDescriptorValue.Notify);
                Diag($"订阅 Notify status={status}");
                if (status != GattCommunicationStatus.Success) { ConnectFailed(); return; }

                // 启动数据推送。fff3 写特征仅支持 WriteWithoutResponse（WriteWithResponse 报 0x80650003）。
                // 拉流采用"请求-响应"（复刻 maj113/DM40GUI BleWorker）：设备是严格请求-响应状态机，
                // 推一帧后期望收一条命令；收到帧 → 发下一条（控制命令优先，否则 CMD_READ）。
                // 200ms 盲轮询会把控制命令淹没在写队列里、夹在 READ 之间被固件忽略
                // （WriteWithoutResponse 的 Success 只代表控制器收下，不代表设备处理 → "写 Success 但表无反应"）。
                // 连接后先发 CMD_ID 握手（型号应答帧 DF 05 03 08 被 MeasurementParser 头校验安全丢弃）。
                if (_writeChar != null)
                {
                    await SendInitAsync();
                    StartWatchdog();
                    TryPump();   // 发首条 CMD_READ 引爆泵；之后由 OnValueChanged 自驱动
                    Diag("已进入请求-响应拉流（CMD_ID 握手 + 收帧驱动 + 500ms 看门狗兜底）");
                }
                else Diag("警告：无 write 特征，无法下发命令");

                SetState(ConnectionState.Measuring);
            }
            catch (Exception ex)
            {
                Diag($"连接异常: {ex.GetType().Name}: {ex.Message}");
                ConnectFailed();
            }
        }

        private int _frameCount;
        private void OnValueChanged(GattCharacteristic sender, GattValueChangedEventArgs args)
        {
            // WinRT IBuffer → byte[]，注意空缓冲
            var reader = Windows.Storage.Streams.DataReader.FromBuffer(args.CharacteristicValue);
            byte[] data = new byte[args.CharacteristicValue.Length];
            reader.ReadBytes(data);
            _frameCount++;
            _lastFrameUtc = DateTime.UtcNow;
            // 帧诊断：前 30 帧 + 控制命令后 20 帧（_verboseUntilFrame）+ 之后每 200 帧，带时间戳落盘
            if (_frameCount <= 30 || _frameCount <= _verboseUntilFrame || _frameCount % 200 == 0)
                Diag($"帧#{_frameCount} {BitConverter.ToString(data)}");
            FrameReceived?.Invoke(data);
            // 记录设备状态 + 命令确认：data[5]/data[6] 相对发命令前有变化即视为命令生效，停止重发
            if (data.Length > 6)
            {
                _lastFlag = data[5];
                _lastStatus = data[6];
                ConfirmIfChanged(data[5], data[6]);
            }
            // 请求-响应：收到一帧 → 驱动下一次发送（命令未确认且到重发间隔则重发，否则 CMD_READ）
            TryPump();
        }

        // ===== 请求-响应拉流（复刻 maj113/DM40GUI BleWorker）=====
        // 串行化所有 fff3 写入（WriteWithoutResponse 并发/重叠会静默丢包）。
        private readonly System.Threading.SemaphoreSlim _writeLock = new System.Threading.SemaphoreSlim(1, 1);
        private readonly object _pendingLock = new object();
        private byte[] _pendingCommand;              // 待确认的控制命令：发出后保留，直到收帧确认生效或重试用尽
        private int _confirmRetry;                   // 待确认命令的剩余重试次数
        private DateTime _lastCommandSendUtc = DateTime.MinValue;  // 上次发命令时刻（控重发间隔，防 toggle 抖动）
        private byte _lastFlag;                      // 最近帧 data[5]（模式/量程）
        private byte _lastStatus;                    // 最近帧 data[6]（电量/充电/HOLD/REL/锁定位）
        private byte _preFlag, _preStatus;           // 发命令前的设备状态快照，供收帧比对确认命令生效
        private int _pumpRunning;                    // Interlocked：保证"一帧一写"，避免 READ 与控制命令交错
        private int _verboseUntilFrame;              // 写命令时延长帧诊断窗口（记接下来 20 帧，看设备是否响应）
        private DateTime _lastFrameUtc = DateTime.MinValue;
        private System.Threading.Timer _watchdog;    // 帧流停滞时兜底补发，防止泵卡死
        private const int MaxConfirmRetry = 5;       // 命令待确认时最多重发次数（防 WriteWithoutResponse 丢包）
        private const int ResendIntervalMs = 300;    // 命令重发最小间隔：给设备处理命令+回帧时间，避免 toggle 抖动

        // 连接握手：发 CMD_ID（型号查询）。型号应答帧 DF 05 03 08 被 MeasurementParser 头校验安全丢弃。
        private async Task SendInitAsync()
        {
            if (_writeChar == null) return;
            try
            {
                await _writeLock.WaitAsync();
                var writer = new Windows.Storage.Streams.DataWriter();
                writer.WriteBytes(ProtocolConstants.CmdId);
                await _writeChar.WriteValueAsync(writer.DetachBuffer(), GattWriteOption.WriteWithoutResponse);
            }
            catch (Exception ex) { Diag($"CMD_ID 写失败: {ex.HResult:X8}"); }
            finally { _writeLock.Release(); }
        }

        // 取一条命令写出：控制命令优先（排队中），否则 CMD_READ。_pumpRunning 保证同时仅一个写。
        private async void TryPump()
        {
            if (_writeChar == null) return;
            if (System.Threading.Interlocked.CompareExchange(ref _pumpRunning, 1, 0) != 0) return;
            try
            {
                byte[] payload = ProtocolConstants.CmdRead;
                bool isCommand = false;
                lock (_pendingLock)
                {
                    if (_pendingCommand != null)
                    {
                        // 命令待确认：按 ResendIntervalMs 间隔重发（给设备处理命令+回帧的时间，避免 toggle 抖动）；
                        // 未到间隔则本轮发 CMD_READ 维持帧流。重试用尽则放弃。
                        bool due = (DateTime.UtcNow - _lastCommandSendUtc).TotalMilliseconds >= ResendIntervalMs;
                        if (due)
                        {
                            isCommand = true;
                            payload = _pendingCommand;
                            _lastCommandSendUtc = DateTime.UtcNow;
                            if (_confirmRetry <= 0) _pendingCommand = null;
                            else _confirmRetry--;
                        }
                    }
                }
                await _writeLock.WaitAsync();
                try
                {
                    // 功能命令追加校验和第7字节（见 WithChecksum）；CMD_READ 前6字节自洽（和=0）原样发
                    byte[] frame = isCommand ? WithChecksum(payload) : payload;
                    var writer = new Windows.Storage.Streams.DataWriter();
                    writer.WriteBytes(frame);
                    var status = await _writeChar.WriteValueAsync(writer.DetachBuffer(), GattWriteOption.WriteWithoutResponse);
                    if (isCommand) { _verboseUntilFrame = _frameCount + 20; Diag($"控制命令写出 {BitConverter.ToString(frame)} → {status}"); }
                }
                catch (Exception ex) { Diag($"{(isCommand ? "控制命令" : "CMD_READ")}写异常: {ex.HResult:X8}"); }
                finally { _writeLock.Release(); }
            }
            finally { System.Threading.Interlocked.Exchange(ref _pumpRunning, 0); }
        }

        private void StartWatchdog()
        {
            StopWatchdog();
            // 每 500ms：若距上一帧 > 500ms（泵可能漏触发/卡死），补发一次拉流重启帧流
            _watchdog = new System.Threading.Timer(
                _ => { if ((DateTime.UtcNow - _lastFrameUtc).TotalMilliseconds > 500) TryPump(); },
                null, TimeSpan.FromMilliseconds(500), TimeSpan.FromMilliseconds(500));
        }

        private void StopWatchdog()
        {
            var t = _watchdog; _watchdog = null;
            t?.Dispose();
        }

        // 追加校验和字节：(-sum(命令))&0xFF，使全帧和 &0xFF==0。复刻 maj113 base_app.send_command（行248）。
        // 真机实测（diag.log）：DM40B 校验下行命令全帧和——CMD_ID/CMD_READ 前6字节自洽(和=0)直接通过；
        // 功能命令(HOLD/DIODE/CAP/CONT/HZ/REL)前6字节和≠0，必须追加此校验和，否则被固件静默丢弃
        // （写返回 Success 但 flag/status 永不变，2026-07-21 真机日志铁证）。
        private static byte[] WithChecksum(byte[] cmd)
        {
            var frame = new byte[cmd.Length + 1];
            Array.Copy(cmd, frame, cmd.Length);
            int sum = 0;
            for (int i = 0; i < cmd.Length; i++) sum += cmd[i];
            frame[cmd.Length] = (byte)((-sum) & 0xFF);
            return frame;
        }

        public void SendCommand(byte[] command)
        {
            if (command == null) return;
            // 命令入队 + 快照当前设备状态（收帧时比对确认生效）+ 设重试计数 + 立即可发首条。
            // WriteWithoutResponse 不保证送达，故未确认时按 ResendIntervalMs 间隔重发，直到设备 flag/status
            // 变化（ConfirmIfChanged）或重试用尽——避免单次丢包导致"按好几次才生效"。
            lock (_pendingLock)
            {
                _pendingCommand = command;
                _preFlag = _lastFlag;
                _preStatus = _lastStatus;
                _confirmRetry = MaxConfirmRetry;
                _lastCommandSendUtc = DateTime.MinValue;
            }
            TryPump();
        }

        // 命令生效确认：发命令后设备回帧的 data[5](模式)/data[6](HOLD·REL位)相对快照变化，即认为命令已被
        // 固件执行，清待确认命令（停止重发）。测量值波动不会改这两个字节，故误检率极低。
        private void ConfirmIfChanged(byte flag, byte status)
        {
            lock (_pendingLock)
            {
                if (_pendingCommand == null) return;
                if (flag != _preFlag || status != _preStatus)
                {
                    Diag($"命令确认生效 flag {_preFlag:X2}→{flag:X2} status {_preStatus:X2}→{status:X2}");
                    _pendingCommand = null;
                }
            }
        }

        public void Disconnect()
        {
            StopWatchdog();
            if (_notifyChar != null)
            {
                try { _notifyChar.ValueChanged -= OnValueChanged; } catch { }
                _notifyChar = null;
            }
            _writeChar = null;
            _device?.Dispose();
            _device = null;
            SetState(ConnectionState.Disconnected);
        }

        private void ConnectFailed()
        {
            Disconnect();
            SetState(ConnectionState.Disconnected);
        }

        /// <summary>
        /// 连接解析期间运行的保活广播监听器：收到目标地址的广播包时，设备进入系统蓝牙缓存，
        /// 此刻 FromBluetoothAddressAsync 最易成功。返回可 Dispose 的 watcher 句柄。
        /// </summary>
        private IDisposable StartKeepAliveWatcher(ulong addr)
        {
            var w = new BluetoothLEAdvertisementWatcher { ScanningMode = BluetoothLEScanningMode.Active };
            w.Received += (s, a) =>
            {
                if (a.BluetoothAddress == addr)
                    _addrTypeByMac[FormatMac(a.BluetoothAddress)] = a.BluetoothAddressType;
            };
            w.Start();
            return new DisposableAction(() => { try { w.Stop(); } catch { } });
        }

        private sealed class DisposableAction : IDisposable
        {
            private readonly Action _a;
            public DisposableAction(Action a) { _a = a; }
            public void Dispose() => _a();
        }

        private void SetState(ConnectionState s)
        {
            State = s;
            StateChanged?.Invoke(s);
        }

        private static string FormatMac(ulong addr) =>
            string.Format("{0:X2}:{1:X2}:{2:X2}:{3:X2}:{4:X2}:{5:X2}",
                (addr >> 40) & 0xFF, (addr >> 32) & 0xFF, (addr >> 24) & 0xFF,
                (addr >> 16) & 0xFF, (addr >> 8) & 0xFF, addr & 0xFF);

        private static ulong ParseMac(string mac)
        {
            ulong v = 0;
            foreach (var part in mac.Split(':'))
                v = (v << 8) | ulong.Parse(part, System.Globalization.NumberStyles.HexNumber);
            return v;
        }
    }
}
