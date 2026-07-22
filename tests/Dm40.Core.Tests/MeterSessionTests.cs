using System.Collections.Generic;
using Dm40.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dm40.Core.Tests
{
    /// <summary>
    /// 测量会话测试：验证 §5.1 主链路编排——帧 → 解析 → 统计/报警/波形/录制，
    /// 及无效帧不参与统计（§5.2 规则1）。这是"三方对账"一致性的核心验证点。
    /// 帧数据为真实抓取的 DM40B 帧（<see cref="RealFrames"/>），经 <see cref="FakeMeterSource"/> 注入。
    /// </summary>
    [TestClass]
    public class MeterSessionTests
    {
        private static (MeterSession, FakeMeterSource, FakeClock) MakeSession()
        {
            var clock = new FakeClock();
            var src = new FakeMeterSource();
            var session = new MeterSession(src, clock);
            return (session, src, clock);
        }

        // 连接并注入 n 帧真实 5V 帧（循环取波动序列），每帧推进 250ms（供报警滞回判定）
        private static void ConnectAndFeed(FakeMeterSource src, FakeClock clock, int n, byte[] frame = null)
        {
            src.StartScan();
            src.Connect("86:5C:1C:06:A1:97");
            for (int i = 0; i < n; i++)
            {
                src.PushFrame(frame ?? RealFrames.Dcv5VSequence[i % RealFrames.Dcv5VSequence.Length]);
                clock.AdvanceMs(250);
            }
        }

        [TestMethod]
        public void ValidFrames_AccumulateStatistics()
        {
            var (session, src, clock) = MakeSession();
            ConnectAndFeed(src, clock, 10);
            Assert.AreEqual(10, session.Statistics.Count);
            Assert.IsTrue(session.Statistics.Min.HasValue);
        }

        [TestMethod]
        public void ValidFrames_AppendToWaveform()
        {
            var (session, src, clock) = MakeSession();
            ConnectAndFeed(src, clock, 10);
            Assert.AreEqual(10, session.Waveform.Count);
        }

        [TestMethod]
        public void ValidFrames_ProduceExpectedReading()
        {
            // 真实帧解析值应为 ~5.002V（5V 基准源），验证工程值换算（60000 counts）
            var (session, src, clock) = MakeSession();
            var readings = new List<Measurement>();
            session.ReadingAvailable += m => readings.Add(m);
            ConnectAndFeed(src, clock, 3);
            Assert.AreEqual(3, readings.Count);
            foreach (var r in readings)
            {
                Assert.IsTrue(r.NormValue.HasValue);
                Assert.AreEqual(5.002, r.NormValue.Value, 0.001, "真实 5V 帧应解析出 ~5.002V");
                Assert.AreEqual("VDC", r.Kind);
                Assert.AreEqual("V", r.DisplayUnit);
            }
        }

        [TestMethod]
        public void Statistics_Waveform_AndRecording_Agree()
        {
            // 三方对账：统计样本数 = 波形点数 = 录制行数
            var (session, src, clock) = MakeSession();
            var sink = new InMemoryCsvSink();
            session.Recorder = new CsvRecorder(sink, clock);
            session.Recorder.Start();

            ConnectAndFeed(src, clock, 10);
            session.Recorder.Stop();

            Assert.AreEqual(10, session.Statistics.Count);
            Assert.AreEqual(10, session.Waveform.Count);
            Assert.AreEqual(1 + 10, sink.Lines.Count); // 表头 + 10 数据行
        }

        [TestMethod]
        public void AlarmFires_OnSustainedOverLimit()
        {
            // 真实帧 ~5.002V，上限设 3.0 → 持续越上限 0.5s 后应触发
            var (session, src, clock) = MakeSession();
            session.Alarm.UpperLimit = 3.0;
            session.Alarm.LowerLimit = 1.0;
            session.Alarm.IsEnabled = true;
            var events = new List<AlarmEvent>();
            session.AlarmTriggered += e => events.Add(e);

            ConnectAndFeed(src, clock, 10); // 10 帧 × 250ms = 2.5s 越限
            Assert.IsTrue(events.Count >= 1);
            Assert.AreEqual(AlarmDirection.High, events[0].Direction);
        }

        [TestMethod]
        public void CrcCorruptedFrame_NotCountedInStatistics()
        {
            var (session, src, clock) = MakeSession();
            src.StartScan(); src.Connect("86:5C:1C:06:A1:97");
            src.PushFrame(RealFrames.Dcv5V_A); // 1 个有效帧
            int countAfterValid = session.Statistics.Count;

            src.PushFrame(RealFrames.BadCrc); // CRC 损坏帧 → 不计入统计

            Assert.AreEqual(countAfterValid, session.Statistics.Count);
        }

        [TestMethod]
        public void OverloadFrame_NotCountedInStatisticsButReadingFires()
        {
            // OL 帧（M1=0xFFFF）：NormValue=null 不参与统计，但 ReadingAvailable 仍触发（波形断点）
            var (session, src, clock) = MakeSession();
            var readings = new List<Measurement>();
            session.ReadingAvailable += m => readings.Add(m);
            ConnectAndFeed(src, clock, 3, RealFrames.Overload);

            Assert.AreEqual(0, session.Statistics.Count, "OL 帧不参与统计");
            Assert.AreEqual(3, readings.Count, "OL 帧仍触发 ReadingAvailable");
            Assert.IsTrue(readings.TrueForAll(r => r.Overload && !r.NormValue.HasValue));
        }

        [TestMethod]
        public void ReadingAvailable_FiresForValidFrame()
        {
            var (session, src, clock) = MakeSession();
            var readings = new List<Measurement>();
            session.ReadingAvailable += m => readings.Add(m);
            ConnectAndFeed(src, clock, 5);
            Assert.AreEqual(5, readings.Count);
            Assert.IsTrue(readings.TrueForAll(r => r.NormValue.HasValue));
        }
    }
}
