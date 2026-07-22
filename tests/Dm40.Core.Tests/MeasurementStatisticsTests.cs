using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dm40.Core.Tests
{
    /// <summary>
    /// 测量统计单元测试（对应 PRD FR-006、§5.4）。
    /// 统计对象为有效主读数工程值；无效帧（Overload/NormValue=null）不计入；清零后自下一帧重计。
    /// </summary>
    [TestClass]
    public class MeasurementStatisticsTests
    {
        [TestMethod]
        public void Empty_ShowsNoSamples()
        {
            var s = new MeasurementStatistics();
            Assert.AreEqual(0, s.Count);
            Assert.IsNull(s.Min);
            Assert.IsNull(s.Max);
            Assert.IsNull(s.Avg);
            Assert.IsNull(s.Delta);
        }

        [TestMethod]
        public void SingleSample_MinMaxAvgEqual()
        {
            var s = new MeasurementStatistics();
            s.Add(3.3);
            Assert.AreEqual(1, s.Count);
            Assert.AreEqual(3.3, s.Min.Value, 1e-12);
            Assert.AreEqual(3.3, s.Max.Value, 1e-12);
            Assert.AreEqual(3.3, s.Avg.Value, 1e-12);
            Assert.AreEqual(0.0, s.Delta.Value, 1e-12);
        }

        [TestMethod]
        public void MultipleSamples_ComputesMinMaxAvgDelta()
        {
            var s = new MeasurementStatistics();
            s.Add(3.0);
            s.Add(3.3);
            s.Add(3.6);
            s.Add(3.3);
            Assert.AreEqual(4, s.Count);
            Assert.AreEqual(3.0, s.Min.Value, 1e-12);
            Assert.AreEqual(3.6, s.Max.Value, 1e-12);
            Assert.AreEqual(3.3, s.Avg.Value, 1e-9);
            Assert.AreEqual(0.6, s.Delta.Value, 1e-12); // Δ = Max − Min
        }

        [TestMethod]
        public void NegativeValues_Handled()
        {
            var s = new MeasurementStatistics();
            s.Add(-5.0);
            s.Add(5.0);
            Assert.AreEqual(-5.0, s.Min.Value, 1e-12);
            Assert.AreEqual(5.0, s.Max.Value, 1e-12);
            Assert.AreEqual(0.0, s.Avg.Value, 1e-12);
            Assert.AreEqual(10.0, s.Delta.Value, 1e-12);
        }

        [TestMethod]
        public void Reset_ClearsAndRecountsFromNext()
        {
            var s = new MeasurementStatistics();
            s.Add(1.0);
            s.Add(2.0);
            s.Reset();
            Assert.AreEqual(0, s.Count);
            Assert.IsNull(s.Min);
            s.Add(10.0);
            Assert.AreEqual(1, s.Count);
            Assert.AreEqual(10.0, s.Min.Value, 1e-12);
        }

        [TestMethod]
        public void Avg_UsesRunningSum_NoOverflowOnLongRun()
        {
            // 模拟长时间监测：10 万个样本，均值应稳定正确
            var s = new MeasurementStatistics();
            for (int i = 0; i < 100_000; i++) s.Add(3.3);
            Assert.AreEqual(100_000, s.Count);
            Assert.AreEqual(3.3, s.Avg.Value, 1e-9);
        }
    }
}
