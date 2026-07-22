using System;
using System.Collections.Generic;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dm40.Core.Tests
{
    /// <summary>
    /// 阈值报警单元测试（对应 PRD FR-007、§5.2 规则4）。
    /// 越限带 0.5s 滞回；同方向越限恢复前不重复触发；上限必须大于下限；留空方向不判定。
    /// 假时钟用 Dm40.Core.FakeClock（共享测试工具）。
    /// </summary>
    [TestClass]
    public class ThresholdAlarmTests
    {
        private static ThresholdAlarm MakeAlarm(FakeClock clock, double? upper, double? lower)
        {
            var a = new ThresholdAlarm(clock) { UpperLimit = upper, LowerLimit = lower, IsEnabled = true };
            return a;
        }

        [TestMethod]
        public void Disabled_NeverTriggers()
        {
            var clock = new FakeClock();
            var a = MakeAlarm(clock, 5.0, 1.0);
            a.IsEnabled = false;
            var events = new List<AlarmEvent>();
            a.Triggered += e => events.Add(e);
            for (int i = 0; i < 10; i++) { a.Feed(99.0); clock.AdvanceMs(100); }
            Assert.AreEqual(0, events.Count);
        }

        [TestMethod]
        public void WithinLimits_NoTrigger()
        {
            var clock = new FakeClock();
            var a = MakeAlarm(clock, 5.0, 1.0);
            var events = new List<AlarmEvent>();
            a.Triggered += e => events.Add(e);
            for (int i = 0; i < 20; i++) { a.Feed(3.0); clock.AdvanceMs(100); }
            Assert.AreEqual(0, events.Count);
        }

        [TestMethod]
        public void BriefSpike_Under500ms_DoesNotTrigger()
        {
            // 滞回：越限持续不足 0.5s 不触发
            var clock = new FakeClock();
            var a = MakeAlarm(clock, 5.0, 1.0);
            var events = new List<AlarmEvent>();
            a.Triggered += e => events.Add(e);
            a.Feed(6.0); clock.AdvanceMs(100);
            a.Feed(6.0); clock.AdvanceMs(100);
            a.Feed(6.0); clock.AdvanceMs(100);   // 仅 300ms
            a.Feed(3.0); clock.AdvanceMs(100);   // 回落
            Assert.AreEqual(0, events.Count);
        }

        [TestMethod]
        public void SustainedOver500ms_TriggersHigh()
        {
            var clock = new FakeClock();
            var a = MakeAlarm(clock, 5.0, 1.0);
            var events = new List<AlarmEvent>();
            a.Triggered += e => events.Add(e);
            for (int i = 0; i < 7; i++) { a.Feed(6.0); clock.AdvanceMs(100); } // 700ms 越上限
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(AlarmDirection.High, events[0].Direction);
            Assert.AreEqual(6.0, events[0].Value, 1e-12);
        }

        [TestMethod]
        public void SustainedBelowLower_TriggersLow()
        {
            var clock = new FakeClock();
            var a = MakeAlarm(clock, 5.0, 1.0);
            var events = new List<AlarmEvent>();
            a.Triggered += e => events.Add(e);
            for (int i = 0; i < 7; i++) { a.Feed(0.5); clock.AdvanceMs(100); }
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(AlarmDirection.Low, events[0].Direction);
        }

        [TestMethod]
        public void SameDirection_DoesNotRetriggerUntilRecovered()
        {
            var clock = new FakeClock();
            var a = MakeAlarm(clock, 5.0, 1.0);
            var events = new List<AlarmEvent>();
            a.Triggered += e => events.Add(e);
            for (int i = 0; i < 20; i++) { a.Feed(6.0); clock.AdvanceMs(100); } // 持续越限
            Assert.AreEqual(1, events.Count); // 只触发一次
        }

        [TestMethod]
        public void AfterRecovery_CanTriggerAgain()
        {
            var clock = new FakeClock();
            var a = MakeAlarm(clock, 5.0, 1.0);
            var events = new List<AlarmEvent>();
            a.Triggered += e => events.Add(e);
            for (int i = 0; i < 7; i++) { a.Feed(6.0); clock.AdvanceMs(100); }  // 触发 HIGH
            a.Feed(3.0); clock.AdvanceMs(100);                                   // 恢复
            for (int i = 0; i < 7; i++) { a.Feed(6.0); clock.AdvanceMs(100); }  // 再次越限
            Assert.AreEqual(2, events.Count);
        }

        [TestMethod]
        public void NullUpper_OnlyLowDirectionJudged()
        {
            var clock = new FakeClock();
            var a = MakeAlarm(clock, null, 1.0); // 上限留空
            var events = new List<AlarmEvent>();
            a.Triggered += e => events.Add(e);
            for (int i = 0; i < 10; i++) { a.Feed(999.0); clock.AdvanceMs(100); } // 远超上限，但不判定
            Assert.AreEqual(0, events.Count);
            for (int i = 0; i < 7; i++) { a.Feed(0.0); clock.AdvanceMs(100); }    // 越下限
            Assert.AreEqual(1, events.Count);
            Assert.AreEqual(AlarmDirection.Low, events[0].Direction);
        }

        [TestMethod]
        public void UpperNotGreaterThanLower_Throws()
        {
            var clock = new FakeClock();
            var a = new ThresholdAlarm(clock);
            Assert.ThrowsException<InvalidOperationException>(() =>
            {
                a.UpperLimit = 1.0;
                a.LowerLimit = 5.0; // 下限 > 上限
                a.Validate();
            });
        }

        [TestMethod]
        public void BoundaryValue_ExactlyAtLimit_NotOver()
        {
            // 判定为严格 > 上限 / < 下限
            var clock = new FakeClock();
            var a = MakeAlarm(clock, 5.0, 1.0);
            var events = new List<AlarmEvent>();
            a.Triggered += e => events.Add(e);
            for (int i = 0; i < 10; i++) { a.Feed(5.0); clock.AdvanceMs(100); } // 恰好等于上限
            Assert.AreEqual(0, events.Count);
        }

        [TestMethod]
        public void NullValueFeed_IsIgnored()
        {
            // 过载/无效帧不参与报警判定，也不打断滞回计时之外的状态
            var clock = new FakeClock();
            var a = MakeAlarm(clock, 5.0, 1.0);
            var events = new List<AlarmEvent>();
            a.Triggered += e => events.Add(e);
            a.Feed(null); clock.AdvanceMs(100);
            a.Feed(null); clock.AdvanceMs(100);
            Assert.AreEqual(0, events.Count);
        }
    }
}
