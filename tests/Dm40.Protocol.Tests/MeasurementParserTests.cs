using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dm40.Protocol.Tests
{
    /// <summary>
    /// 协议解析器单元测试（PRD §12.2 要求覆盖率 ≥90%）。
    /// 全部使用构造帧（已知答案）验证：CRC、状态字节、工程值换算、OL 过载、
    /// 未知比例降级、畸形帧容错、命令帧逐字节一致性。
    /// </summary>
    [TestClass]
    public class MeasurementParserTests
    {
        // 帧布局（17 字节真实设备帧，数据字节相对帧尾定位，对应参考实现的负索引）：
        //   f[0..3]  帧头 DF 05 03 09
        //   f[4]     固定 0x0B（真实帧如此）
        //   f[5]     模式/量程标志
        //   f[6]     状态字节（电量/充电/锁定/Hold）
        //   f[7]     data[-10] → M3 比例/符号
        //   f[8]     data[-9]  → M2 比例/符号
        //   f[9]     data[-8]  → M1 比例/符号
        //   f[10..11] M3 计数（小端，data[-7],[-6]）
        //   f[12..13] M2 计数（小端，data[-5],[-4]）
        //   f[14..15] M1 计数（小端，data[-3],[-2]）
        //   f[16]    CRC（全帧字节和 & 0xFF == 0）
        //
        // 测试仅精确控制 M1 比例槽位（f[9]）做工程值断言；第三读数槽位多为内部值，不做数值断言。

        /// <summary>构造一帧 17 字节测量帧并补末尾 CRC（真实设备布局）。</summary>
        private static byte[] BuildFrame(byte flag, byte status, int m1, int m2, int m3, byte scaleM1)
        {
            var f = new byte[17];
            f[0] = 0xDF; f[1] = 0x05; f[2] = 0x03; f[3] = 0x09;
            f[4] = 0x0B;
            f[5] = flag;
            f[6] = status;
            f[9] = scaleM1;
            f[10] = (byte)(m3 & 0xFF); f[11] = (byte)((m3 >> 8) & 0xFF);
            f[12] = (byte)(m2 & 0xFF); f[13] = (byte)((m2 >> 8) & 0xFF);
            f[14] = (byte)(m1 & 0xFF); f[15] = (byte)((m1 >> 8) & 0xFF);
            int sum = 0; for (int i = 0; i < 16; i++) sum += f[i];
            f[16] = (byte)((256 - (sum & 0xFF)) & 0xFF);
            return f;
        }

        // ---------------- CRC ----------------

        [TestMethod]
        public void Crc_ValidFrame_Passes()
        {
            var frame = BuildFrame(0x08, 0x00, 30000, 0, 0, 0x08);
            var m = MeasurementParser.Parse(frame, deviceCounts: 60000);
            Assert.IsTrue(m.CrcOk);
        }

        [TestMethod]
        public void Crc_CorruptedFrame_Fails()
        {
            var frame = BuildFrame(0x08, 0x00, 30000, 0, 0, 0x08);
            frame[10] ^= 0xFF; // 篡改一个字节，破坏 CRC
            var m = MeasurementParser.Parse(frame, deviceCounts: 60000);
            Assert.IsFalse(m.CrcOk);
        }

        // ---------------- 主值换算（核心工程值公式） ----------------

        [TestMethod]
        public void Parse_Vdc6vRange_HalfCounts_Gives3Volts()
        {
            // VDC 6V 档：flag=0x08 → (VDC, 6V)；scale 槽位 0x08 → ScaleInfo(6.0V, mul=1, dec=4)
            // counts=30000，60000 counts 机型：value = 30000 * (6.0/60000) = 3.0
            var frame = BuildFrame(0x08, 0x00, 30000, 0, 0, 0x08);
            var m = MeasurementParser.Parse(frame, deviceCounts: 60000);

            Assert.IsTrue(m.IsValid);
            Assert.AreEqual("VDC", m.Kind);
            Assert.AreEqual("V", m.DisplayUnit);
            Assert.AreEqual(3.0, m.NormValue.Value, 1e-9);
            Assert.AreEqual("3.0000", m.ValueText);
            Assert.IsFalse(m.Overload);
        }

        [TestMethod]
        public void Parse_NegativeSign_AppliesMinus()
        {
            // scale 槽位置符号位（&1 == 1 → 负）。0x08|0x01 = 0x09 → 查表按 0x09&0xFE=0x08。
            var frame = BuildFrame(0x08, 0x00, 30000, 0, 0, 0x09);
            var m = MeasurementParser.Parse(frame, deviceCounts: 60000);
            Assert.AreEqual(-3.0, m.NormValue.Value, 1e-9);
            StringAssert.StartsWith(m.ValueText, "-3.0000");
        }

        [TestMethod]
        public void Parse_DeviceCountsFactor_Scales()
        {
            // DM40A = 40000 counts。参考实现：fullScale 乘因子 40000/60000=2/3 → 6V*2/3=4V；
            // norm = 20000 * (4.0/40000) = 2.0V。因子与 deviceCounts 抵消，结果随档位线性变化。
            var frame = BuildFrame(0x08, 0x00, 20000, 0, 0, 0x08);
            var m = MeasurementParser.Parse(frame, deviceCounts: 40000);
            Assert.AreEqual(2.0, m.NormValue.Value, 1e-9);
        }

        [TestMethod]
        public void Parse_Resistance_Ohms()
        {
            // RES 6kΩ 档：flag=0x0A → (RES, 6kΩ)；scale 槽位 0x02 → ScaleInfo(6000Ω, "Ω", mul=1, dec=1)
            // counts=30000 → 30000*(6000/60000)=3000 Ω
            var frame = BuildFrame(0x0A, 0x00, 30000, 0, 0, 0x02);
            var m = MeasurementParser.Parse(frame, deviceCounts: 60000);
            Assert.AreEqual("RES", m.Kind);
            Assert.AreEqual("Ω", m.DisplayUnit);
            Assert.AreEqual(3000.0, m.NormValue.Value, 1e-6);
            Assert.AreEqual("3000.0", m.ValueText);
        }

        // ---------------- 过载 ----------------

        [TestMethod]
        public void Parse_Overload_ShowsOL()
        {
            var frame = BuildFrame(0x08, 0x00, 0xFFFF, 0, 0, 0x08);
            var m = MeasurementParser.Parse(frame, deviceCounts: 60000);
            Assert.IsTrue(m.Overload);
            Assert.AreEqual("OL", m.ValueText);
            Assert.IsNull(m.NormValue);
        }

        // ---------------- 状态字节 ----------------

        [TestMethod]
        public void Parse_StatusByte_Decodes()
        {
            // 电量3 + 充电 + 锁定 + Hold
            byte status = 0x03 | 0x08 | 0x40 | 0x80;
            var frame = BuildFrame(0x08, status, 30000, 0, 0, 0x08);
            var m = MeasurementParser.Parse(frame, deviceCounts: 60000);
            Assert.AreEqual(3, m.BatteryLevel);
            Assert.IsTrue(m.Charging);
            Assert.IsTrue(m.Locked);
            Assert.IsTrue(m.Hold);
        }

        // ---------------- 未知比例 / 降级 ----------------

        [TestMethod]
        public void Parse_UnknownScaleFlag_DegradesToUnknown()
        {
            // scale 槽位 0x7E 在电压表里不存在 → 未知比例，显示 ---，不抛异常，保留原始帧。
            var frame = BuildFrame(0x08, 0x00, 30000, 0, 0, 0x7E);
            var m = MeasurementParser.Parse(frame, deviceCounts: 60000);
            Assert.IsFalse(m.IsValid);
            Assert.AreEqual("---", m.ValueText);
            Assert.IsNull(m.NormValue);
            Assert.IsFalse(string.IsNullOrEmpty(m.Raw)); // 原始帧留痕
        }

        // ---------------- 畸形帧容错（PRD：解析任意畸形帧 0 崩溃） ----------------

        [TestMethod]
        public void Parse_ShortFrame_DoesNotThrow()
        {
            var m = MeasurementParser.Parse(new byte[] { 0xDF, 0x05, 0x03 }, deviceCounts: 60000);
            Assert.IsFalse(m.IsValid);
            Assert.AreEqual("---", m.ValueText);
        }

        [TestMethod]
        public void Parse_WrongHeader_DoesNotThrow()
        {
            var frame = BuildFrame(0x08, 0x00, 30000, 0, 0, 0x08);
            frame[0] = 0x00; // 破坏帧头
            var m = MeasurementParser.Parse(frame, deviceCounts: 60000);
            Assert.IsFalse(m.IsValid);
        }

        [TestMethod]
        public void Parse_EmptyFrame_DoesNotThrow()
        {
            var m = MeasurementParser.Parse(new byte[0], deviceCounts: 60000);
            Assert.IsFalse(m.IsValid);
        }

        [TestMethod]
        public void Parse_RandomGarbage_NeverThrows()
        {
            // 模糊注入：任意字节序列不得抛异常（PRD §12.2）。
            var rng = new System.Random(12345);
            for (int i = 0; i < 1000; i++)
            {
                var buf = new byte[rng.Next(0, 40)];
                rng.NextBytes(buf);
                var m = MeasurementParser.Parse(buf, deviceCounts: 60000); // 不应抛
                Assert.IsNotNull(m);
            }
        }

        // ---------------- 命令帧逐字节一致性（PRD §11.2） ----------------

        [TestMethod]
        public void CommandFrames_MatchContractByteForByte()
        {
            CollectionAssert.AreEqual(new byte[] { 0xAF, 0x05, 0x03, 0x08, 0x00, 0x41 }, ProtocolConstants.CmdId);
            CollectionAssert.AreEqual(new byte[] { 0xAF, 0x05, 0x03, 0x09, 0x00, 0x40 }, ProtocolConstants.CmdRead);
            CollectionAssert.AreEqual(new byte[] { 0xAF, 0x05, 0x03, 0x04, 0x01, 0x01 }, ProtocolConstants.CmdHoldOn);
            CollectionAssert.AreEqual(new byte[] { 0xAF, 0x05, 0x03, 0x06, 0x01, 0x45 }, ProtocolConstants.CmdTemp);
        }

        [TestMethod]
        public void Checksum_Formula_IsNegatedSumOfFirst5()
        {
            // 校验和 = (-sum(前5字节)) & 0xFF。以 CmdId 为例：AF+05+03+08+00 = 0xBF，-0xBF & 0xFF = 0x41。
            byte[] cmd = ProtocolConstants.CmdId;
            int sum = cmd[0] + cmd[1] + cmd[2] + cmd[3] + cmd[4];
            byte expect = (byte)((-sum) & 0xFF);
            Assert.AreEqual(expect, cmd[5]);
        }
    }
}
