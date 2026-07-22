namespace Dm40.Protocol
{
    /// <summary>
    /// 一帧测量数据的解析结果（对应参考实现的 Measurement）。
    /// 解析是纯函数：输入 16 字节测量帧，输出本对象；不产生副作用、不依赖 UI/BLE/IO。
    /// 未知比例 / 畸形帧不会抛异常，而是以 Unknown/--- 降级呈现（PRD §5.3）。
    /// </summary>
    public sealed class Measurement
    {
        /// <summary>原始帧的 HEX 字符串（空格分隔大写），用于报文查看器留痕。</summary>
        public string Raw { get; set; } = string.Empty;

        /// <summary>CRC 校验是否通过（全帧字节和 &amp; 0xFF == 0）。</summary>
        public bool CrcOk { get; set; }

        /// <summary>帧是否可解析（长度/帧头/比例表均有效）。false 时读数区显示 ---。</summary>
        public bool IsValid { get; set; }

        /// <summary>测量类型（VDC/VAC/ADC/.../TEMP），未识别为 "---"。</summary>
        public string Kind { get; set; } = "---";

        /// <summary>量程显示名（如 600mV / 6V / AUTO）。</summary>
        public string Range { get; set; } = string.Empty;

        /// <summary>显示单位（mV/V/uA/Ω/...）。</summary>
        public string DisplayUnit { get; set; } = string.Empty;

        /// <summary>格式化后的主读数字符串（含符号、按档位小数位）。过载为 "OL"，未知为 "---"。</summary>
        public string ValueText { get; set; } = "---";

        /// <summary>主读数归一化工程值（SI 基本单位，未乘显示倍率）。过载/未知为 null。供统计/报警/CSV/波形使用。</summary>
        public double? NormValue { get; set; }

        /// <summary>主读数换算到显示单位的值（NormValue × 显示倍率，单位与 DisplayUnit 一致）。供副显高精度展示。</summary>
        public double? DisplayValue { get; set; }

        /// <summary>是否过载（主计数 == 0xFFFF）。</summary>
        public bool Overload { get; set; }

        /// <summary>小数位（按档位）。</summary>
        public int Decimals { get; set; } = 2;

        /// <summary>次读数显示文本（无则空）。</summary>
        public string SecondaryText { get; set; } = string.Empty;

        /// <summary>次读数单位。</summary>
        public string SecondaryUnit { get; set; } = string.Empty;

        /// <summary>第三读数显示文本（无则空）。</summary>
        public string ThirdText { get; set; } = string.Empty;

        /// <summary>第三读数单位。</summary>
        public string ThirdUnit { get; set; } = string.Empty;

        /// <summary>电池电量 0-5（data[6] &amp; 0x07）。</summary>
        public int BatteryLevel { get; set; }

        /// <summary>是否充电中（data[6] &amp; 0x08）。</summary>
        public bool Charging { get; set; }

        /// <summary>是否屏幕锁定（data[6] &amp; 0x40）。</summary>
        public bool Locked { get; set; }

        /// <summary>是否 Hold（data[6] &amp; 0x80）。</summary>
        public bool Hold { get; set; }
    }
}
