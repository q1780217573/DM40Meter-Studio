using System.Globalization;
using System.Text;

namespace Dm40.Cli
{
    /// <summary>极简 JSON 序列化辅助（net48 无 System.Text.Json）。仅覆盖 CLI 需要的标量/简单对象。</summary>
    internal static class Json
    {
        /// <summary>转义字符串为 JSON 字符串字面量（含引号）。</summary>
        public static string Str(string s)
        {
            if (s == null) return "null";
            var b = new StringBuilder(s.Length + 2);
            b.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"': b.Append("\\\""); break;
                    case '\\': b.Append("\\\\"); break;
                    case '\n': b.Append("\\n"); break;
                    case '\r': b.Append("\\r"); break;
                    case '\t': b.Append("\\t"); break;
                    default:
                        b.Append(c < ' ' ? "\\u" + ((int)c).ToString("x4") : c.ToString());
                        break;
                }
            }
            b.Append('"');
            return b.ToString();
        }

        /// <summary>数值（不变区域性，小数点 '.'）。null 输出 null。
        /// 用 G10：工程读数有效数字 ≤6 位（60000 counts），10 位足以精确表示，同时消除二进制浮点尾噪（如 5.0020000000000007 → 5.002）。</summary>
        public static string Num(double? v) =>
            v.HasValue ? v.Value.ToString("G10", CultureInfo.InvariantCulture) : "null";

        public static string Bool(bool v) => v ? "true" : "false";
    }
}
