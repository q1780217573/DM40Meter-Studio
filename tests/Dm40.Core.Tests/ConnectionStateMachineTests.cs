using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dm40.Core.Tests
{
    /// <summary>
    /// 连接状态机单元测试（对应 PRD §5.3 连接状态机、FR-002、AC-11）。
    /// 状态：未连接/扫描中/设备列表/连接中/已连接/测量中/重连中。
    /// 意外断开自动重连 ≤3 次（2s/5s/10s 退避），均失败回未连接。
    /// </summary>
    [TestClass]
    public class ConnectionStateMachineTests
    {
        private static ConnectionStateMachine Make(out List<ConnectionState> transitions)
        {
            var sm = new ConnectionStateMachine();
            var list = new List<ConnectionState>();
            sm.StateChanged += s => list.Add(s);
            transitions = list;
            return sm;
        }

        [TestMethod]
        public void Initial_IsDisconnected()
        {
            var sm = new ConnectionStateMachine();
            Assert.AreEqual(ConnectionState.Disconnected, sm.State);
        }

        [TestMethod]
        public void HappyPath_DisconnectedToMeasuring()
        {
            var sm = Make(out var t);
            sm.StartScan();
            sm.DeviceFound();
            sm.SelectDevice();
            sm.ConnectSucceeded();
            sm.NotifySubscribed();
            Assert.AreEqual(ConnectionState.Measuring, sm.State);
            // 验证完整迁移序列
            CollectionAssert.AreEqual(new[]
            {
                ConnectionState.Scanning, ConnectionState.DeviceList, ConnectionState.Connecting,
                ConnectionState.Connected, ConnectionState.Measuring,
            }, t);
        }

        [TestMethod]
        public void ScanTimeout_ReturnsToDisconnected()
        {
            var sm = Make(out _);
            sm.StartScan();
            sm.ScanTimeout();
            Assert.AreEqual(ConnectionState.Disconnected, sm.State);
        }

        [TestMethod]
        public void ConnectFailed_ReturnsToDisconnected()
        {
            var sm = Make(out _);
            sm.StartScan(); sm.DeviceFound(); sm.SelectDevice();
            sm.ConnectFailed();
            Assert.AreEqual(ConnectionState.Disconnected, sm.State);
        }

        [TestMethod]
        public void UnexpectedDisconnect_EntersReconnecting()
        {
            var sm = Make(out _);
            sm.StartScan(); sm.DeviceFound(); sm.SelectDevice(); sm.ConnectSucceeded(); sm.NotifySubscribed();
            sm.ConnectionLost();
            Assert.AreEqual(ConnectionState.Reconnecting, sm.State);
            Assert.AreEqual(1, sm.ReconnectAttempt);
        }

        [TestMethod]
        public void Reconnect_BackoffDelays_Are2s5s10s()
        {
            var sm = new ConnectionStateMachine();
            CollectionAssert.AreEqual(
                new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10) },
                ConnectionStateMachine.BackoffDelays);
        }

        [TestMethod]
        public void ReconnectSuccess_ReturnsToConnected()
        {
            var sm = Make(out _);
            sm.StartScan(); sm.DeviceFound(); sm.SelectDevice(); sm.ConnectSucceeded(); sm.NotifySubscribed();
            sm.ConnectionLost();
            sm.ReconnectSucceeded();
            Assert.AreEqual(ConnectionState.Connected, sm.State);
            Assert.AreEqual(0, sm.ReconnectAttempt); // 复位
        }

        [TestMethod]
        public void ReconnectFailsThreeTimes_GivesUp()
        {
            var sm = Make(out _);
            sm.StartScan(); sm.DeviceFound(); sm.SelectDevice(); sm.ConnectSucceeded(); sm.NotifySubscribed();
            sm.ConnectionLost();            // 重连中，attempt=1
            sm.ReconnectFailed();           // attempt=2
            Assert.AreEqual(ConnectionState.Reconnecting, sm.State);
            sm.ReconnectFailed();           // attempt=3
            Assert.AreEqual(ConnectionState.Reconnecting, sm.State);
            sm.ReconnectFailed();           // 超过 3 次 → 放弃
            Assert.AreEqual(ConnectionState.Disconnected, sm.State);
        }

        [TestMethod]
        public void UserDisconnect_FromMeasuring_GoesDisconnected()
        {
            var sm = Make(out _);
            sm.StartScan(); sm.DeviceFound(); sm.SelectDevice(); sm.ConnectSucceeded(); sm.NotifySubscribed();
            sm.UserDisconnect();
            Assert.AreEqual(ConnectionState.Disconnected, sm.State);
        }

        [TestMethod]
        public void NotifyInterrupted_ReturnsToConnected_Recoverable()
        {
            var sm = Make(out _);
            sm.StartScan(); sm.DeviceFound(); sm.SelectDevice(); sm.ConnectSucceeded(); sm.NotifySubscribed();
            sm.NotifyInterrupted();
            Assert.AreEqual(ConnectionState.Connected, sm.State); // 测量中→已连接（可恢复）
        }

        [TestMethod]
        public void InvalidTransition_IsRejected()
        {
            var sm = new ConnectionStateMachine();
            // 未连接状态下不能直接 NotifySubscribed
            Assert.ThrowsException<InvalidOperationException>(() => sm.NotifySubscribed());
        }

        [TestMethod]
        public void StartScan_WhenMeasuring_IsRejected()
        {
            var sm = Make(out _);
            sm.StartScan(); sm.DeviceFound(); sm.SelectDevice(); sm.ConnectSucceeded(); sm.NotifySubscribed();
            Assert.ThrowsException<InvalidOperationException>(() => sm.StartScan());
        }
    }
}
