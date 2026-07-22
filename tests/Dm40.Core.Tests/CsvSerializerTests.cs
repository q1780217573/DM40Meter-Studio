using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dm40.Core.Tests
{
    /// <summary>
    /// CSV 序列化/解析单元测试（对应 PRD FR-008/009/010、§9.1）。
    /// 关键不变量：序列化→解析往返一致；含 BOM；畸形行给出明确错误不崩溃。
    /// </summary>
    [TestClass]
    public class CsvSerializerTests
    {
        private static CsvRecord Sample() => new CsvRecord
        {
            Timestamp = new DateTime(2026, 7, 17, 14, 30, 25, 123),
            Value = 3.3012,
            Unit = "V",
            Mode = "DCV",
            Range = "6V",
            Aux1 = 0.5,
            Aux2 = null,
            Alarm = null,
        };

        [TestMethod]
        public void Header_MatchesPrdColumnOrder()
        {
            Assert.AreEqual("Timestamp,Value,Unit,Mode,Range,Aux1,Aux2,Alarm", CsvSerializer.Header);
        }

        [TestMethod]
        public void Serialize_ProducesCorrectColumns()
        {
            var line = CsvSerializer.Serialize(Sample());
            Assert.AreEqual("2026-07-17 14:30:25.123,3.3012,V,DCV,6V,0.5,,", line);
        }

        [TestMethod]
        public void RoundTrip_PreservesAllFields()
        {
            var original = Sample();
            original.Alarm = "HIGH";
            var line = CsvSerializer.Serialize(original);
            var parsed = CsvSerializer.Parse(line);

            Assert.AreEqual(original.Timestamp, parsed.Timestamp);
            Assert.AreEqual(original.Value, parsed.Value, 1e-12);
            Assert.AreEqual(original.Unit, parsed.Unit);
            Assert.AreEqual(original.Mode, parsed.Mode);
            Assert.AreEqual(original.Range, parsed.Range);
            Assert.AreEqual(original.Aux1.Value, parsed.Aux1.Value, 1e-12);
            Assert.IsNull(parsed.Aux2);
            Assert.AreEqual("HIGH", parsed.Alarm);
        }

        [TestMethod]
        public void RoundTrip_NullOptionals_StayNull()
        {
            var original = Sample(); // Aux1=0.5, Aux2=null, Alarm=null
            var line = CsvSerializer.Serialize(original);
            var parsed = CsvSerializer.Parse(line);
            Assert.AreEqual(0.5, parsed.Aux1.Value, 1e-12);
            Assert.IsNull(parsed.Aux2);
            Assert.IsNull(parsed.Alarm);
        }

        [TestMethod]
        public void Parse_TooFewColumns_ThrowsFormatException()
        {
            Assert.ThrowsException<CsvFormatException>(() => CsvSerializer.Parse("2026-07-17 14:30:25.123,3.3,V"));
        }

        [TestMethod]
        public void Parse_BadTimestamp_ThrowsFormatException()
        {
            Assert.ThrowsException<CsvFormatException>(() =>
                CsvSerializer.Parse("not-a-time,3.3,V,DCV,6V,,,"));
        }

        [TestMethod]
        public void Parse_BadValue_ThrowsFormatException()
        {
            Assert.ThrowsException<CsvFormatException>(() =>
                CsvSerializer.Parse("2026-07-17 14:30:25.123,abc,V,DCV,6V,,,"));
        }

        [TestMethod]
        public void Parse_NegativeValue_Preserved()
        {
            var r = CsvSerializer.Parse("2026-07-17 14:30:25.123,-1.5,V,DCV,6V,,,");
            Assert.AreEqual(-1.5, r.Value, 1e-12);
        }

        [TestMethod]
        public void TryParse_InvalidLine_ReturnsFalse()
        {
            // 回放加载用 TryParse：单行损坏不应中断整个文件
            Assert.IsFalse(CsvSerializer.TryParse("garbage", out _));
            Assert.IsTrue(CsvSerializer.TryParse(CsvSerializer.Serialize(Sample()), out var ok));
            Assert.AreEqual("DCV", ok.Mode);
        }

        [TestMethod]
        public void Header_IsSkippedByTryParse()
        {
            // 文件首行表头不应被当作数据行解析
            Assert.IsFalse(CsvSerializer.TryParse(CsvSerializer.Header, out _));
        }
    }
}
