using System;
using System.Collections.Generic;

namespace Dm40.Core
{
    /// <summary>
    /// CSV 回放数据源（PRD FR-010）。
    /// 加载本软件生成的 CSV：坏行跳过、超过 maxRows 截断并标记 WasTruncated、
    /// 全文件无有效行时抛 <see cref="CsvFormatException"/>（不进入回放）。只读，不产生新落盘数据。
    /// 播放/暂停/进度/倍速属 UI 层时序控制，本类只提供按索引的顺序访问。
    /// </summary>
    public sealed class CsvReplaySource
    {
        /// <summary>PRD 规定的回放加载行数上限（100 万行）。</summary>
        public const int DefaultMaxRows = 1_000_000;

        private readonly List<CsvRecord> _records;

        private CsvReplaySource(List<CsvRecord> records, bool truncated)
        {
            _records = records;
            WasTruncated = truncated;
        }

        /// <summary>有效记录数。</summary>
        public int Count => _records.Count;

        /// <summary>是否因超过 maxRows 被截断（提示"仅加载前 N 行"）。</summary>
        public bool WasTruncated { get; }

        /// <summary>按索引取记录（0 起，按文件顺序即时间顺序）。</summary>
        public CsvRecord GetAt(int index) => _records[index];

        /// <summary>全部记录（只读视图）。</summary>
        public IReadOnlyList<CsvRecord> Records => _records;

        /// <summary>
        /// 从文本行加载（测试 seam，便于内存构造）。坏行跳过；表头行忽略。
        /// </summary>
        public static CsvReplaySource LoadFromLines(IEnumerable<string> lines, int maxRows = DefaultMaxRows)
        {
            if (lines == null) throw new ArgumentNullException(nameof(lines));
            var records = new List<CsvRecord>();
            bool truncated = false;

            foreach (var line in lines)
            {
                if (records.Count >= maxRows) { truncated = true; break; }
                if (CsvSerializer.TryParse(line, out var rec))
                    records.Add(rec);
                // 坏行/表头：跳过，不中断
            }

            if (records.Count == 0)
                throw new CsvFormatException("文件不含任何有效测量记录，无法回放。");

            return new CsvReplaySource(records, truncated);
        }
    }
}
