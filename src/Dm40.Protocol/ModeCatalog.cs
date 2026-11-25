using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Dm40.Protocol
{
    /// <summary>
    /// 一个可下发的测量模式（档位 + 量程）。Flag 即命令帧 AF 05 03 06 01 &lt;Flag&gt; 的第 6 字节。
    /// 由 <see cref="ProtocolConstants.FlagInfo"/> 生成，UI 与 CLI 共用同一份表，避免两处各写一套。
    /// </summary>
    public sealed class ModeEntry
    {
        public ModeEntry(byte flag, string kind, string range, string key, string label)
        {
            Flag = flag;
            Kind = kind;
            Range = range;
            Key = key;
            Label = label;
        }

        /// <summary>模式/量程标志字节（data[5]）。</summary>
        public byte Flag { get; }

        /// <summary>测量类型（VDC/VAC/ADC/.../TEMP）。</summary>
        public string Kind { get; }

        /// <summary>量程名（600mV / 6V / AUTO / AUTO+ ...）。</summary>
        public string Range { get; }

        /// <summary>稳定标识（如 vdc-600mv / res-online-auto）。CLI --cmd 用它。</summary>
        public string Key { get; }

        /// <summary>中文显示名（如「直流电压 600mV」）。</summary>
        public string Label { get; }

        /// <summary>是否自动量程（AUTO / AUTO+）。</summary>
        public bool IsAuto => Range.StartsWith("AUTO", StringComparison.Ordinal);

        public override string ToString() => $"0x{Flag:X2} {Kind} {Range}";
    }

    /// <summary>
    /// 档位/量程目录：把 FlagInfo 整理成有序、可按键名分发的模式表。
    /// 只含数据与纯函数，不含 IO。
    /// </summary>
    public static class ModeCatalog
    {
        // 档位展示顺序：电压 → 电流 → 电阻 → 其他
        private static readonly string[] KindOrder =
        {
            "VDC", "VDC+AC", "VAC",
            "ADC", "ADC+AC", "AAC",
            "RES", "RES_ONLINE",
            "CAP", "DIODE", "CONT", "FREQ", "TEMP",
        };

        // 量程由小到大，自动量程垫底
        private static readonly string[] RangeOrder =
        {
            "600mV", "6V", "60V", "600V", "1000V",
            "600uA", "6mA", "60mA", "600mA", "6A", "10A",
            "600Ω", "6kΩ", "60kΩ", "600kΩ", "6MΩ", "60MΩ",
            "AUTO", "AUTO+",
        };

        private static readonly Dictionary<string, string> KindLabel = new Dictionary<string, string>
        {
            { "VDC", "直流电压" },
            { "VDC+AC", "直流+交流电压" },
            { "VAC", "交流电压" },
            { "ADC", "直流电流" },
            { "ADC+AC", "直流+交流电流" },
            { "AAC", "交流电流" },
            { "RES", "电阻" },
            { "RES_ONLINE", "在线电阻" },
            { "CAP", "电容" },
            { "DIODE", "二极管" },
            { "CONT", "通断" },
            { "FREQ", "频率" },
            { "TEMP", "温度" },
        };

        /// <summary>全部模式，按 KindOrder → RangeOrder → Flag 排序。</summary>
        public static readonly IReadOnlyList<ModeEntry> All = Build();

        /// <summary>按档位分组的顺序（供 UI 下拉使用）。</summary>
        public static IReadOnlyList<string> Kinds => KindOrder;

        /// <summary>构造一条「切档/切量程」命令帧（6 字节，第 7 字节校验和由传输层追加）。</summary>
        public static byte[] BuildCommand(byte flag) => new byte[] { 0xAF, 0x05, 0x03, 0x06, 0x01, flag };

        /// <summary>按 Key 查找（区分大小写不敏感）。找不到返回 null。</summary>
        public static ModeEntry Find(string key) =>
            string.IsNullOrEmpty(key)
                ? null
                : All.FirstOrDefault(e => string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));

        /// <summary>取某档位下的全部模式（含 AUTO / AUTO+），按量程升序。</summary>
        public static IReadOnlyList<ModeEntry> ForKind(string kind) =>
            All.Where(e => string.Equals(e.Kind, kind, StringComparison.Ordinal)).ToList();

        /// <summary>档位中文名（未登记的返回原值）。</summary>
        public static string KindDisplayName(string kind) =>
            KindLabel.TryGetValue(kind, out var v) ? v : kind;

        private static List<ModeEntry> Build()
        {
            var list = new List<ModeEntry>(ProtocolConstants.FlagInfo.Count);
            foreach (var kv in ProtocolConstants.FlagInfo)
            {
                var kind = kv.Value.Kind;
                var range = kv.Value.Range;
                var key = Slug(kind) + "-" + Slug(range);
                var label = KindDisplayName(kind) + " " + range;
                list.Add(new ModeEntry(kv.Key, kind, range, key, label));
            }

            return list
                .OrderBy(e => IndexOf(KindOrder, e.Kind))
                .ThenBy(e => IndexOf(RangeOrder, e.Range))
                .ThenBy(e => e.Flag)
                .ToList();
        }

        private static int IndexOf(string[] table, string value)
        {
            for (int i = 0; i < table.Length; i++)
                if (string.Equals(table[i], value, StringComparison.Ordinal)) return i;
            return int.MaxValue;
        }

        // 只保留 ASCII，避免命令行/文件名出现特殊字符：Ω→ohm，+→-plus，_→-
        private static string Slug(string s)
        {
            var sb = new StringBuilder(s.Length + 4);
            foreach (var ch in s)
            {
                if (ch == 'Ω') sb.Append("ohm");
                else if (ch == '+') sb.Append("-plus");
                else if (ch == '_') sb.Append('-');
                else if (ch == ' ') { /* 忽略空格 */ }
                else sb.Append(char.ToLowerInvariant(ch));
            }
            return sb.ToString();
        }
    }
}