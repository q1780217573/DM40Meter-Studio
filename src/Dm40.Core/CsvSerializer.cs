using System;
using System.Globalization;

namespace Dm40.Core
{
    /// <summary>CSV 行格式非法（列缺失/时间戳或数值解析失败）。PRD FR-010：给明确错误提示，不进入回放。</summary>
    public sealed class CsvFormatException : FormatException
    {
        public CsvFormatException(string message) : base(message) { }
    }

    /// <summary>
    /// CSV 序列化/解析器（PRD §9.1 表一）。录制、导出、回放三方共用同一格式，保证"三方对账"一致。
    /// 列序固定：Timestamp,Value,Unit,Mode,Range,Aux1,Aux2,Alarm。
    /// Timestamp 用 "yyyy-MM-dd HH:mm:ss.fff"；数值用不变区域性（小数点 '.'），与区域设置无关。
    /// 文件级 UTF-8 带 BOM 由 <see cref="CsvRecorder"/> 负责，本类只处理单行文本。
    /// </summary>
    public static class CsvSerializer
    {
        /// <summary>表头行。</summary>
        public const string Header = "Timestamp,Value,Unit,Mode,Range,Aux1,Aux2,Alarm";

        private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss.fff";
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>序列化一行为 CSV 文本（不含换行符）。</summary>
        public static string Serialize(CsvRecord r)
        {
            if (r == null) throw new ArgumentNullException(nameof(r));
            // 数值用 G10：有效数字 ≤6 位（60000 counts），消除二进制浮点尾噪（5.0020000000000007 → 5.002）
            return string.Join(",",
                r.Timestamp.ToString(TimestampFormat, Inv),
                r.Value.ToString("G10", Inv),
                Escape(r.Unit),
                Escape(r.Mode),
                Escape(r.Range),
                r.Aux1.HasValue ? r.Aux1.Value.ToString("G10", Inv) : string.Empty,
                r.Aux2.HasValue ? r.Aux2.Value.ToString("G10", Inv) : string.Empty,
                r.Alarm ?? string.Empty);
        }

        /// <summary>解析一行 CSV 文本。表头行或非法行抛 <see cref="CsvFormatException"/>。</summary>
        public static CsvRecord Parse(string line)
        {
            if (TryParse(line, out var rec))
                return rec;
            throw new CsvFormatException("非法的 CSV 数据行: " + (line ?? "<null>"));
        }

        /// <summary>尝试解析一行。表头行/非法行返回 false，不抛异常（供回放加载跳过坏行）。</summary>
        public static bool TryParse(string line, out CsvRecord record)
        {
            record = null;
            if (string.IsNullOrWhiteSpace(line)) return false;
            if (line == Header) return false;

            var parts = line.Split(',');
            if (parts.Length != 8) return false;

            if (!DateTime.TryParseExact(parts[0], TimestampFormat, Inv, DateTimeStyles.None, out var ts))
                return false;
            if (!double.TryParse(parts[1], NumberStyles.Float, Inv, out var value))
                return false;

            var rec = new CsvRecord
            {
                Timestamp = ts,
                Value = value,
                Unit = parts[2],
                Mode = parts[3],
                Range = parts[4],
                Aux1 = ParseNullable(parts[5]),
                Aux2 = ParseNullable(parts[6]),
                Alarm = string.IsNullOrEmpty(parts[7]) ? null : parts[7],
            };
            record = rec;
            return true;
        }

        private static double? ParseNullable(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            return double.TryParse(s, NumberStyles.Float, Inv, out var v) ? v : (double?)null;
        }

        // 单位/模式/量程当前不含逗号或引号，无需转义；保留占位以防未来扩展。
        private static string Escape(string s) => s ?? string.Empty;
    }
}
