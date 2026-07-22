using System;

namespace Dm40.Core
{
    /// <summary>
    /// CSV 测量文件的一行（PRD §9.1 表一：录制/导出/回放共用格式）。
    /// Timestamp 用 ISO8601 本地时区毫秒精度；Value 为已乘比例的工程值。
    /// </summary>
    public sealed class CsvRecord
    {
        /// <summary>采样接收时刻（本地时区，yyyy-MM-dd HH:mm:ss.fff）。</summary>
        public DateTime Timestamp { get; set; }

        /// <summary>主读数工程值。</summary>
        public double Value { get; set; }

        /// <summary>单位（V/A/Ω/F/Hz/℃ 等）。</summary>
        public string Unit { get; set; } = string.Empty;

        /// <summary>测量模式（DCV/ACV/DCA/ACA/RES/CAP/DIODE/CONT/HZ/TEMP）。</summary>
        public string Mode { get; set; } = string.Empty;

        /// <summary>量程（AUTO 或具体档位）。</summary>
        public string Range { get; set; } = string.Empty;

        /// <summary>辅助读数 1（无则 null）。</summary>
        public double? Aux1 { get; set; }

        /// <summary>辅助读数 2（无则 null）。</summary>
        public double? Aux2 { get; set; }

        /// <summary>越限方向标记：HIGH / LOW / null。</summary>
        public string Alarm { get; set; }
    }
}
