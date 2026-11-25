using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dm40.Core.Tests
{
    /// <summary>峰值保持：极值、时刻、区间清零、非有限值忽略。</summary>
    [TestClass]
    public class PeakHoldTests
    {
        private static readonly DateTime T0 = new DateTime(2026, 11, 25, 9, 0, 0, DateTimeKind.Utc);

        [TestMethod]
        public void Update_TracksMaxAndMinWithTimestamps()
        {
            var p = new PeakHold();
            p.Update(3.30, "V", "VDC", T0);
            p.Update(3.45, "V", "VDC", T0.AddSeconds(1));
            p.Update(3.10, "V", "VDC", T0.AddSeconds(2));

            Assert.AreEqual(3.45, p.Max.Value, 1e-9);
            Assert.AreEqual(T0.AddSeconds(1), p.MaxAt);
            Assert.AreEqual(3.10, p.Min.Value, 1e-9);
            Assert.AreEqual(T0.AddSeconds(2), p.MinAt);
            Assert.AreEqual(3, p.Samples);
            Assert.AreEqual("V", p.Unit);
            Assert.AreEqual("VDC", p.Kind);
            Assert.IsTrue(p.HasValue);
        }

        [TestMethod]
        public void Update_EqualValue_DoesNotMoveTimestamp()
        {
            var p = new PeakHold();
            p.Update(5.0, "V", "VDC", T0);
            p.Update(5.0, "V", "VDC", T0.AddSeconds(3));

            Assert.AreEqual(T0, p.MaxAt, "相等值不应把最大值时刻往后推");
            Assert.AreEqual(T0, p.MinAt, "相等值不应把最小值时刻往后推");
        }

        [TestMethod]
        public void Update_IgnoresNaNAndInfinity()
        {
            var p = new PeakHold();
            Assert.IsFalse(p.Update(double.NaN, "V", "VDC", T0));
            Assert.IsFalse(p.Update(double.PositiveInfinity, "V", "VDC", T0));
            Assert.IsFalse(p.HasValue);
            Assert.AreEqual(0, p.Samples);
        }

        [TestMethod]
        public void Reset_ClearsEverything()
        {
            var p = new PeakHold();
            p.Update(1.0, "mV", "VDC", T0);
            p.Reset();

            Assert.IsFalse(p.HasValue);
            Assert.IsNull(p.Max);
            Assert.IsNull(p.MaxAt);
            Assert.IsNull(p.Min);
            Assert.IsNull(p.MinAt);
            Assert.AreEqual(0, p.Samples);
            Assert.AreEqual("", p.Unit);
        }

        [TestMethod]
        public void Update_ReportsChangeOnlyWhenExtremumMoves()
        {
            var p = new PeakHold();
            Assert.IsTrue(p.Update(1.0, "V", "VDC", T0), "首个样本应同时建立最大值与最小值");
            Assert.IsTrue(p.Update(3.0, "V", "VDC", T0.AddSeconds(1)), "刷新最大值应返回 true");
            Assert.IsFalse(p.Update(2.0, "V", "VDC", T0.AddSeconds(2)), "落在区间内不应返回 true");
            Assert.IsTrue(p.Update(0.5, "V", "VDC", T0.AddSeconds(3)), "刷新最小值应返回 true");
        }
    }
}