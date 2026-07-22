using System;

namespace Dm40.Core
{
    /// <summary>
    /// 连接状态机（PRD §5.3、FR-002、AC-11）。
    /// 管理 未连接→扫描中→设备列表→连接中→已连接→测量中 的主链路，
    /// 以及意外断开后的自动重连（≤3 次，2s/5s/10s 退避）。
    /// 非法迁移抛 <see cref="InvalidOperationException"/>，杜绝状态残留（QA 高危跃迁点）。
    /// 本类只做状态迁移与重连计数；实际的扫描/连接动作由 Transport 层触发并回报结果。
    /// </summary>
    public sealed class ConnectionStateMachine
    {
        /// <summary>自动重连退避序列（2s/5s/10s，PRD §5.3）。</summary>
        public static readonly TimeSpan[] BackoffDelays =
        {
            TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10),
        };

        /// <summary>最大重连次数。</summary>
        public const int MaxReconnectAttempts = 3;

        private ConnectionState _state = ConnectionState.Disconnected;

        /// <summary>当前状态。</summary>
        public ConnectionState State => _state;

        /// <summary>当前重连尝试序号（1..3；非重连态为 0）。</summary>
        public int ReconnectAttempt { get; private set; }

        /// <summary>状态变化通知（UI 订阅以刷新连接指示）。</summary>
        public event Action<ConnectionState> StateChanged;

        // ---- 主链路迁移 ----

        /// <summary>开始扫描：未连接 → 扫描中。</summary>
        public void StartScan()
        {
            Require(ConnectionState.Disconnected);
            Transition(ConnectionState.Scanning);
        }

        /// <summary>发现设备：扫描中 → 设备列表。</summary>
        public void DeviceFound()
        {
            Require(ConnectionState.Scanning);
            Transition(ConnectionState.DeviceList);
        }

        /// <summary>扫描超时/停止：扫描中 → 未连接。</summary>
        public void ScanTimeout()
        {
            Require(ConnectionState.Scanning);
            Transition(ConnectionState.Disconnected);
        }

        /// <summary>选择设备并连接：设备列表 → 连接中。</summary>
        public void SelectDevice()
        {
            Require(ConnectionState.DeviceList);
            Transition(ConnectionState.Connecting);
        }

        /// <summary>GATT 连接成功 + 型号校验通过：连接中 → 已连接。</summary>
        public void ConnectSucceeded()
        {
            Require(ConnectionState.Connecting);
            Transition(ConnectionState.Connected);
        }

        /// <summary>连接失败/超时：连接中 → 未连接。</summary>
        public void ConnectFailed()
        {
            Require(ConnectionState.Connecting);
            Transition(ConnectionState.Disconnected);
        }

        /// <summary>订阅通知成功：已连接 → 测量中。</summary>
        public void NotifySubscribed()
        {
            Require(ConnectionState.Connected);
            Transition(ConnectionState.Measuring);
        }

        /// <summary>通知中断（可恢复）：测量中 → 已连接。</summary>
        public void NotifyInterrupted()
        {
            Require(ConnectionState.Measuring);
            Transition(ConnectionState.Connected);
        }

        // ---- 重连 ----

        /// <summary>意外断开：已连接/测量中 → 重连中（attempt=1）。</summary>
        public void ConnectionLost()
        {
            if (_state != ConnectionState.Connected && _state != ConnectionState.Measuring)
                throw new InvalidOperationException($"当前状态 {_state} 不能进入重连。");
            ReconnectAttempt = 1;
            Transition(ConnectionState.Reconnecting);
        }

        /// <summary>重连成功：重连中 → 已连接，并重置计数。</summary>
        public void ReconnectSucceeded()
        {
            Require(ConnectionState.Reconnecting);
            ReconnectAttempt = 0;
            Transition(ConnectionState.Connected);
        }

        /// <summary>重连失败：递增计数；超过 3 次放弃 → 未连接。</summary>
        public void ReconnectFailed()
        {
            Require(ConnectionState.Reconnecting);
            if (ReconnectAttempt >= MaxReconnectAttempts)
            {
                ReconnectAttempt = 0;
                Transition(ConnectionState.Disconnected);
            }
            else
            {
                ReconnectAttempt++;
                // 停留在重连中，等待下一次退避后重试
            }
        }

        /// <summary>用户主动断开：任意已建立状态 → 未连接。</summary>
        public void UserDisconnect()
        {
            if (_state == ConnectionState.Disconnected || _state == ConnectionState.Scanning)
                throw new InvalidOperationException($"当前状态 {_state} 无需断开。");
            ReconnectAttempt = 0;
            Transition(ConnectionState.Disconnected);
        }

        /// <summary>取当前重连尝试对应的退避时长（0 起索引）。</summary>
        public TimeSpan CurrentBackoff()
        {
            int idx = Math.Min(Math.Max(ReconnectAttempt - 1, 0), BackoffDelays.Length - 1);
            return BackoffDelays[idx];
        }

        private void Require(ConnectionState expected)
        {
            if (_state != expected)
                throw new InvalidOperationException($"非法状态迁移：当前 {_state}，期望 {expected}。");
        }

        private void Transition(ConnectionState next)
        {
            _state = next;
            StateChanged?.Invoke(next);
        }
    }
}
