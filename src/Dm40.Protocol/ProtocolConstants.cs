using System.Collections.Generic;

namespace Dm40.Protocol
{
    /// <summary>
    /// DM40 协议常量与命令帧表。
    /// 逐字节移植自参考实现 maj113/DM40GUI 的 dm40/protocol_constants.py（PRD §11.2 指定其为权威来源）。
    /// 本类只含数据，不含行为；解析逻辑见 <see cref="MeasurementParser"/>。
    /// </summary>
    public static class ProtocolConstants
    {
        // ---- 命令帧（6 字节，AF 05 03 <cmd> <arg> <checksum>，checksum = (-sum(前5字节)) & 0xFF）----
        public static readonly byte[] CmdId = { 0xAF, 0x05, 0x03, 0x08, 0x00, 0x41 };
        public static readonly byte[] CmdRead = { 0xAF, 0x05, 0x03, 0x09, 0x00, 0x40 };

        public static readonly byte[] CmdHoldOn = { 0xAF, 0x05, 0x03, 0x04, 0x01, 0x01 };
        public static readonly byte[] CmdHoldOff = { 0xAF, 0x05, 0x03, 0x04, 0x01, 0x00 };
        public static readonly byte[] CmdAutoOn = { 0xAF, 0x05, 0x03, 0x03, 0x01, 0x01 };
        public static readonly byte[] CmdAutoOff = { 0xAF, 0x05, 0x03, 0x03, 0x01, 0x00 };
        public static readonly byte[] CmdRelative = { 0xAF, 0x05, 0x03, 0x05, 0x01, 0x01 };
        public static readonly byte[] CmdCap = { 0xAF, 0x05, 0x03, 0x06, 0x01, 0x03 };
        public static readonly byte[] CmdDiode = { 0xAF, 0x05, 0x03, 0x06, 0x01, 0x04 };
        public static readonly byte[] CmdCont = { 0xAF, 0x05, 0x03, 0x06, 0x01, 0x44 };
        public static readonly byte[] CmdHz = { 0xAF, 0x05, 0x03, 0x06, 0x01, 0x05 };
        public static readonly byte[] CmdTemp = { 0xAF, 0x05, 0x03, 0x06, 0x01, 0x45 };

        // 基础测量档位切换（_send_range_flag：AF 05 03 06 01 <flag>，flag = data[5]，见 FLAG_INFO）。
        // 与 CAP/DIODE 等同为 06 01 范围命令，WithChecksum 自动追加第7字节校验和。
        // 提供这些档位让用户能从 CAP/DIODE/CONT 等单功能档切回主测量档（解决"切到电容后无电压档可切回"）。
        public static readonly byte[] CmdVdc = { 0xAF, 0x05, 0x03, 0x06, 0x01, 0x28 };  // VDC AUTO
        public static readonly byte[] CmdVac = { 0xAF, 0x05, 0x03, 0x06, 0x01, 0x68 };  // VAC AUTO
        public static readonly byte[] CmdAdc = { 0xAF, 0x05, 0x03, 0x06, 0x01, 0x31 };  // ADC AUTO
        public static readonly byte[] CmdAac = { 0xAF, 0x05, 0x03, 0x06, 0x01, 0x71 };  // AAC AUTO
        public static readonly byte[] CmdRes = { 0xAF, 0x05, 0x03, 0x06, 0x01, 0x32 };  // RES AUTO

        /// <summary>测量帧头 DF 05 03 09。</summary>
        public static readonly byte[] MeasurementHeader = { 0xDF, 0x05, 0x03, 0x09 };

        /// <summary>型号应答前缀 DF 05 03 08 14。</summary>
        public static readonly byte[] ModelReplyPrefix = { 0xDF, 0x05, 0x03, 0x08, 0x14 };

        /// <summary>测量帧长度（含 CRC 字节）。</summary>
        /// <summary>测量帧最小长度（真实设备帧为 17 字节；数据字节相对帧尾定位，帧长可自适应）。</summary>
        public const int MeasurementFrameLength = 17;

        /// <summary>比例/符号查找表条目：满量程、显示单位、显示倍率、小数位。</summary>
        public readonly struct ScaleInfo
        {
            public ScaleInfo(double fullScale, string unit, double multiplier, int decimals)
            {
                FullScale = fullScale;
                Unit = unit;
                Multiplier = multiplier;
                Decimals = decimals;
            }

            public double FullScale { get; }
            public string Unit { get; }
            public double Multiplier { get; }
            public int Decimals { get; }
        }

        /// <summary>档位/模式标志字节 data[5] → (测量类型, 量程名)。</summary>
        public static readonly IReadOnlyDictionary<byte, (string Kind, string Range)> FlagInfo =
            new Dictionary<byte, (string, string)>
            {
                { 0x00, ("VDC", "600mV") }, { 0x08, ("VDC", "6V") }, { 0x10, ("VDC", "60V") },
                { 0x18, ("VDC", "600V") }, { 0x20, ("VDC", "1000V") }, { 0x28, ("VDC", "AUTO") }, { 0x30, ("VDC", "AUTO+") },
                { 0x40, ("VAC", "600mV") }, { 0x48, ("VAC", "6V") }, { 0x50, ("VAC", "60V") },
                { 0x58, ("VAC", "600V") }, { 0x60, ("VAC", "1000V") }, { 0x68, ("VAC", "AUTO") }, { 0x70, ("VAC", "AUTO+") },
                { 0x80, ("VDC+AC", "600mV") }, { 0x88, ("VDC+AC", "6V") }, { 0x90, ("VDC+AC", "60V") },
                { 0x98, ("VDC+AC", "600V") }, { 0xA0, ("VDC+AC", "1000V") }, { 0xA8, ("VDC+AC", "AUTO") }, { 0xB0, ("VDC+AC", "AUTO+") },
                { 0x01, ("ADC", "600uA") }, { 0x09, ("ADC", "6mA") }, { 0x11, ("ADC", "60mA") },
                { 0x19, ("ADC", "600mA") }, { 0x21, ("ADC", "6A") }, { 0x29, ("ADC", "10A") },
                { 0x31, ("ADC", "AUTO") }, { 0x39, ("ADC", "AUTO+") },
                { 0x41, ("AAC", "600uA") }, { 0x49, ("AAC", "6mA") }, { 0x51, ("AAC", "60mA") },
                { 0x59, ("AAC", "600mA") }, { 0x61, ("AAC", "6A") }, { 0x69, ("AAC", "10A") },
                { 0x71, ("AAC", "AUTO") }, { 0x79, ("AAC", "AUTO+") },
                { 0x81, ("ADC+AC", "600uA") }, { 0x89, ("ADC+AC", "6mA") }, { 0x91, ("ADC+AC", "60mA") },
                { 0x99, ("ADC+AC", "600mA") }, { 0xA1, ("ADC+AC", "6A") }, { 0xA9, ("ADC+AC", "10A") },
                { 0xB1, ("ADC+AC", "AUTO") }, { 0xB9, ("ADC+AC", "AUTO+") },
                { 0x02, ("RES", "600Ω") }, { 0x0A, ("RES", "6kΩ") }, { 0x12, ("RES", "60kΩ") },
                { 0x1A, ("RES", "600kΩ") }, { 0x22, ("RES", "6MΩ") }, { 0x2A, ("RES", "60MΩ") }, { 0x32, ("RES", "AUTO") },
                { 0x42, ("RES_ONLINE", "600Ω") }, { 0x4A, ("RES_ONLINE", "6kΩ") }, { 0x52, ("RES_ONLINE", "60kΩ") },
                { 0x5A, ("RES_ONLINE", "600kΩ") }, { 0x62, ("RES_ONLINE", "6MΩ") }, { 0x6A, ("RES_ONLINE", "60MΩ") }, { 0x72, ("RES_ONLINE", "AUTO") },
                { 0x03, ("CAP", "AUTO") }, { 0x04, ("DIODE", "AUTO") }, { 0x44, ("CONT", "AUTO") },
                { 0x05, ("FREQ", "AUTO") }, { 0x45, ("TEMP", "AUTO") },
            };

        /// <summary>电压/二极管档比例表（ALT_SCALE_MAP）。</summary>
        public static readonly IReadOnlyDictionary<byte, ScaleInfo> VoltScaleMap =
            new Dictionary<byte, ScaleInfo>
            {
                { 0x04, new ScaleInfo(0.6, "mV", 1e3, 2) },
                { 0x08, new ScaleInfo(6.0, "V", 1.0, 4) },
                { 0x18, new ScaleInfo(6.0, "V", 1.0, 4) },
                { 0x16, new ScaleInfo(60.0, "V", 1.0, 3) },
                { 0x14, new ScaleInfo(600.0, "V", 1.0, 2) },
                { 0x12, new ScaleInfo(6000.0, "V", 1.0, 1) },
            };

        /// <summary>电流档比例表（AMP_SCALE_MAP）。</summary>
        public static readonly IReadOnlyDictionary<byte, ScaleInfo> AmpScaleMap =
            new Dictionary<byte, ScaleInfo>
            {
                { 0x04, new ScaleInfo(600e-6, "uA", 1e6, 2) },
                { 0x02, new ScaleInfo(6000e-6, "uA", 1e6, 1) },
                { 0x16, new ScaleInfo(60e-3, "mA", 1e3, 3) },
                { 0x14, new ScaleInfo(600e-3, "mA", 1e3, 2) },
                { 0x28, new ScaleInfo(6.0, "A", 1.0, 4) },
                { 0x26, new ScaleInfo(60.0, "A", 1.0, 3) },
            };

        /// <summary>电阻/通断档比例表（RES_SCALE_MAP）。</summary>
        public static readonly IReadOnlyDictionary<byte, ScaleInfo> ResScaleMap =
            new Dictionary<byte, ScaleInfo>
            {
                { 0x04, new ScaleInfo(600.0, "Ω", 1.0, 2) },
                { 0x02, new ScaleInfo(6000.0, "Ω", 1.0, 1) },
                { 0x18, new ScaleInfo(6000.0, "kΩ", 0.001, 4) },
                { 0x16, new ScaleInfo(60000.0, "kΩ", 0.001, 3) },
                { 0x14, new ScaleInfo(600000.0, "kΩ", 0.001, 2) },
                { 0x28, new ScaleInfo(6e6, "MΩ", 1e-6, 4) },
                { 0x26, new ScaleInfo(6e7, "MΩ", 1e-6, 3) },
            };

        /// <summary>频率档比例表（FREQ_SCALE_MAP）。</summary>
        public static readonly IReadOnlyDictionary<byte, ScaleInfo> FreqScaleMap =
            new Dictionary<byte, ScaleInfo>
            {
                { 0x06, new ScaleInfo(60.0, "Hz", 1.0, 3) },
                { 0x04, new ScaleInfo(600.0, "Hz", 1.0, 2) },
                { 0x02, new ScaleInfo(6000.0, "Hz", 1.0, 1) },
                { 0x18, new ScaleInfo(6000.0, "kHz", 1e-3, 4) },
                { 0x16, new ScaleInfo(60000.0, "kHz", 1e-3, 3) },
                { 0x14, new ScaleInfo(600000.0, "kHz", 1e-3, 2) },
            };

        /// <summary>电容档比例表（CAP_SCALE_MAP）。</summary>
        public static readonly IReadOnlyDictionary<byte, ScaleInfo> CapScaleMap =
            new Dictionary<byte, ScaleInfo>
            {
                { 0x06, new ScaleInfo(6e-9, "nF", 1e9, 3) },
                { 0x04, new ScaleInfo(60e-9, "nF", 1e9, 2) },
                { 0x02, new ScaleInfo(600e-9, "nF", 1e9, 1) },
                { 0x16, new ScaleInfo(6e-6, "uF", 1e6, 3) },
                { 0x14, new ScaleInfo(60e-6, "uF", 1e6, 2) },
                { 0x12, new ScaleInfo(600e-6, "uF", 1e6, 1) },
                { 0x26, new ScaleInfo(6e-3, "mF", 1e3, 3) },
                { 0x24, new ScaleInfo(60e-3, "mF", 1e3, 2) },
            };

        /// <summary>测量类型 → 槽位序列（MODE_SLOT_MAP）。槽位最多 3 个：主/次/第三。</summary>
        public static readonly IReadOnlyDictionary<string, string[]> ModeSlotMap =
            new Dictionary<string, string[]>
            {
                { "VDC", new[] { "M1" } },
                { "VAC", new[] { "M1", "DUTY", "FREQ" } },
                { "VDC+AC", new[] { "M1", "DC", "AC" } },
                { "ADC", new[] { "M1" } },
                { "AAC", new[] { "M1", "DUTY", "FREQ" } },
                { "ADC+AC", new[] { "M1", "DC", "AC" } },
                { "RES", new[] { "M1" } },
                { "RES_ONLINE", new[] { "M1" } },
                { "CAP", new[] { "M1" } },
                { "CONT", new[] { "M1" } },
                { "DIODE", new[] { "M1", "RES" } },
                { "FREQ", new[] { "FREQ", "DUTY" } },
                { "TEMP", new[] { "TC", "TF", "TI" } },
            };

        /// <summary>设备型号 → 满量程计数（MODEL_TABLE）。影响工程值换算因子。
        /// 实测校正（2026-07-20，DM40B 接 5V 基准源）：M1 计数 50029 对应表显 5.0029V，
        /// 反推满量程 = 50029 × 6.0 / 5.0029 ≈ 60000。故 DM40B 实际为 60000 counts（非早期文档的 50000）。
        /// DM40 全系内部计数统一为 60000（显示位数差异由比例槽位/小数点决定，非满量程计数）。</summary>
        public static readonly IReadOnlyDictionary<string, int> ModelTable =
            new Dictionary<string, int>
            {
                { "DM40A", 60000 },
                { "DM40B", 60000 },
                { "DM40C", 60000 },
            };
    }
}
