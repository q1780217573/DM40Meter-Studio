using System;
using System.Globalization;

namespace Dm40.Protocol
{
    /// <summary>
    /// DM40 测量帧解析器。纯函数：<see cref="Parse"/> 输入字节帧 → 输出 <see cref="Measurement"/>，
    /// 无副作用、不依赖 UI/BLE/IO。语义逐行对齐参考实现 maj113/DM40GUI 的 dm40/parsing.py。
    /// 任何畸形帧/未知比例都不会抛异常，而是降级为 IsValid=false + "---"（PRD §5.3、§12.2）。
    /// </summary>
    public static class MeasurementParser
    {
        // 帧头固定字节（从帧首数）
        private const int FlagByte = 5;    // 模式/量程标志
        private const int StatusByte = 6;  // 设备状态（量程/Hold/Auto/电池）

        // 数据/比例字节相对帧尾定位（参考实现用 Python 负索引，对帧长自适应）。
        // DM40 测量帧末 1 字节为 CRC；计数与比例槽位相对帧尾偏移恒定，与帧总长无关。
        // 帧尾布局（相对最后一字节 CRC）：
        //   data[-8]/[-9]/[-10] = M1/M2/M3 的比例+符号槽位
        //   data[-4]/[-3] = M1 计数 Lo/Hi；data[-6]/[-5]=M2；data[-8]…（见下）
        // 实测 17 字节帧：DF 05 03 09 0B 28 05 00 00 04 FF FF FF FF 9C 00 3C
        //   idx: 0=DF 1=05 2=03 3=09 4=0B(flag) 5=28(flag2) 6=05(status) 7=00 8=00 9=04(scaleM1)
        //        10=FF 11=FF(M3) 12=FF 13=FF(M2) 14=9C 15=00(M1=0x009C) 16=3C(CRC)
        private static int ScaleM1(int n) => n - 8;   // data[-8]  → M1 比例/符号
        private static int ScaleM2(int n) => n - 9;   // data[-9]  → M2
        private static int ScaleM3(int n) => n - 10;  // data[-10] → M3
        private static int M1Lo(int n) => n - 3;      // data[-3]  → M1 计数低字节
        private static int M1Hi(int n) => n - 2;      // data[-2]  → M1 计数高字节
        private static int M2Lo(int n) => n - 5;      // data[-5]  → M2
        private static int M2Hi(int n) => n - 4;      // data[-4]
        private static int M3Lo(int n) => n - 7;      // data[-7]  → M3
        private static int M3Hi(int n) => n - 6;      // data[-6]

        private const int OverloadCounts = 0xFFFF;

        /// <summary>
        /// 解析一帧数据为 <see cref="Measurement"/>。
        /// </summary>
        /// <param name="data">原始帧（任意长度；非法时返回降级结果）。</param>
        /// <param name="deviceCounts">设备满量程计数（DM40A/B/C = 40000/50000/60000）。</param>
        public static Measurement Parse(byte[] data, int deviceCounts)
        {
            var m = new Measurement();
            if (data == null) return m;

            m.Raw = ToHex(data);
            m.CrcOk = CrcValid(data);

            // 长度与帧头校验 → 不满足则保持降级（IsValid=false, ValueText="---"）
            if (data.Length < ProtocolConstants.MeasurementFrameLength || !StartsWithHeader(data))
                return m;

            // 状态字节恒可解（PRD FR-003）
            DecodeStatus(data[StatusByte], m);

            int n = data.Length;
            int m1 = (data[M1Hi(n)] << 8) | data[M1Lo(n)];
            int m2 = (data[M2Hi(n)] << 8) | data[M2Lo(n)];
            int m3 = (data[M3Hi(n)] << 8) | data[M3Lo(n)];

            // 模式/量程标志查表；未知标志 → 降级
            if (!ProtocolConstants.FlagInfo.TryGetValue(data[FlagByte], out var flagInfo))
                return m;
            m.Kind = flagInfo.Kind;
            m.Range = flagInfo.Range;
            m.Overload = m1 == OverloadCounts;

            if (!ProtocolConstants.ModeSlotMap.TryGetValue(m.Kind, out var slots) || slots.Length == 0)
                return m;

            byte s0 = data[ScaleM1(n)];
            byte s1 = data[ScaleM2(n)];
            byte s2 = data[ScaleM3(n)];

            // 主槽位（slots[0]）比例解析失败 → 整帧降级
            var resolved = ResolveSlotScale(slots[0], m.Kind, s0, deviceCounts);
            if (resolved == null)
                return m;

            var scale = resolved.Value;
            m.DisplayUnit = scale.Unit;
            m.Decimals = scale.Decimals;
            m.IsValid = true;

            // CAP 模式有效计数减半（参考实现 eff_counts = device_counts / 10）
            double effCounts = m.Kind == "CAP" ? deviceCounts / 10.0 : deviceCounts;

            if (m.Overload)
            {
                m.ValueText = "OL";
                m.NormValue = null;
            }
            else
            {
                int sign = (s0 & 0x01) != 0 ? -1 : 1;
                double norm = sign * m1 * (scale.FullScale / effCounts);
                m.NormValue = norm;
                m.DisplayValue = norm * scale.Multiplier;
                m.ValueText = FormatFixed(norm * scale.Multiplier, scale.Decimals);
            }

            // 非 AUTO 档补量程文本（如 "6.000V"）
            if (!m.Range.StartsWith("AUTO", StringComparison.Ordinal))
                m.Range = FormatG(scale.FullScale * scale.Multiplier) + scale.Unit;

            // 次/第三槽位
            if (slots.Length > 1)
                (m.SecondaryText, m.SecondaryUnit) = ProcessSlot(slots[1], m2, s1, m.Kind, deviceCounts);
            if (slots.Length > 2)
                (m.ThirdText, m.ThirdUnit) = ProcessSlot(slots[2], m3, s2, m.Kind, deviceCounts);

            return m;
        }

        /// <summary>解析槽位比例表。返回 null 表示未知比例（调用方决定降级）。</summary>
        private static ProtocolConstants.ScaleInfo? ResolveSlotScale(string slot, string kind, byte signFlag, int deviceCounts)
        {
            byte scaleFlag = (byte)(signFlag & 0xFE);

            ProtocolConstants.ScaleInfo? info = null;

            if (slot == "FREQ")
            {
                if (ProtocolConstants.FreqScaleMap.TryGetValue(scaleFlag, out var v)) info = v;
            }
            else if (slot == "M1" || slot == "DC" || slot == "AC")
            {
                if (kind.StartsWith("V", StringComparison.Ordinal) || kind == "DIODE")
                {
                    if (ProtocolConstants.VoltScaleMap.TryGetValue(scaleFlag, out var v)) info = v;
                }
                else if (kind.StartsWith("A", StringComparison.Ordinal))
                {
                    if (ProtocolConstants.AmpScaleMap.TryGetValue(scaleFlag, out var v)) info = v;
                }
                else if (kind == "RES" || kind == "RES_ONLINE" || kind == "CONT")
                {
                    if (ProtocolConstants.ResScaleMap.TryGetValue(scaleFlag, out var v)) info = v;
                }
                else if (kind == "CAP")
                {
                    if (ProtocolConstants.CapScaleMap.TryGetValue(scaleFlag, out var v)) info = v;
                }
            }
            else if (slot == "TC" && kind == "TEMP")
            {
                info = new ProtocolConstants.ScaleInfo(6000.0, "°C", 1.0, 1);
            }
            else if (slot == "RES" && kind == "DIODE")
            {
                info = new ProtocolConstants.ScaleInfo(6000.0, "Ω", 1.0, 1);
            }

            if (info == null) return null;

            // counts 因子：device_counts / 60000（不同型号满量程不同）
            double factor = deviceCounts / 60000.0;
            var i = info.Value;
            return new ProtocolConstants.ScaleInfo(i.FullScale * factor, i.Unit, i.Multiplier, i.Decimals);
        }

        /// <summary>处理次/第三槽位的显示值（含 DUTY/TF/TI 直读，不走比例表）。</summary>
        private static (string Text, string Unit) ProcessSlot(string slotType, int counts, byte signFlag, string kind, int deviceCounts)
        {
            bool ol = counts == OverloadCounts;

            var resolved = ResolveSlotScale(slotType, kind, signFlag, deviceCounts);
            if (resolved != null)
            {
                var s = resolved.Value;
                if (ol) return ("OL", s.Unit);
                int sign = (signFlag & 0x01) != 0 ? -1 : 1;
                double val = counts * (s.FullScale / deviceCounts) * s.Multiplier * sign;
                return (FormatFixed(val, s.Decimals), s.Unit);
            }

            if (slotType == "DUTY" || slotType == "TF" || slotType == "TI")
            {
                string valStr = ol ? "OL" : (counts * 0.1).ToString("0.0", CultureInfo.InvariantCulture);
                if (slotType == "DUTY") return (valStr, "%");
                return (valStr, slotType == "TF" ? "°F" : "°C INT");
            }

            return (string.Empty, string.Empty);
        }

        private static void DecodeStatus(byte status, Measurement m)
        {
            m.BatteryLevel = status & 0x07;
            m.Charging = (status & 0x08) != 0;
            m.Locked = (status & 0x40) != 0;
            m.Hold = (status & 0x80) != 0;
        }

        private static bool StartsWithHeader(byte[] data)
        {
            var h = ProtocolConstants.MeasurementHeader;
            if (data.Length < h.Length) return false;
            for (int i = 0; i < h.Length; i++)
                if (data[i] != h[i]) return false;
            return true;
        }

        /// <summary>CRC：全帧字节和 &amp; 0xFF == 0。</summary>
        private static bool CrcValid(byte[] data)
        {
            if (data.Length == 0) return false;
            int sum = 0;
            foreach (var b in data) sum += b;
            return (sum & 0xFF) == 0;
        }

        private static string ToHex(byte[] data)
        {
            if (data.Length == 0) return string.Empty;
            var c = new char[data.Length * 3 - 1];
            for (int i = 0; i < data.Length; i++)
            {
                int j = i * 3;
                c[j] = ToHexNibble(data[i] >> 4);
                c[j + 1] = ToHexNibble(data[i] & 0xF);
                if (j + 2 < c.Length) c[j + 2] = ' ';
            }
            return new string(c);
        }

        private static char ToHexNibble(int v) => (char)(v < 10 ? '0' + v : 'A' + v - 10);

        private static string FormatFixed(double value, int decimals) =>
            value.ToString("F" + decimals, CultureInfo.InvariantCulture);

        /// <summary>近似 Python 的 "%.4g"：4 位有效数字。</summary>
        private static string FormatG(double value) =>
            value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}
