using System;
using System.IO;
using System.Text;

namespace Dm40.App
{
    /// <summary>
    /// 本地设置持久化（PRD §6.6.2 要求，2026-10-26 版缺失：设置只改内存，重启即丢）。
    /// 用 ini 风格纯文本，落在数据根目录（DM40_DATA_DIR 或系统「文档」）下的 settings.ini，
    /// 与录制/截图同处，便于整体搬迁；解析失败一律回落默认值，不影响启动。
    /// </summary>
    public sealed class AppSettings
    {
        public int WindowSeconds = 60;
        public bool AutoConnect = true;
        public string ScreenshotDir = "";

        /// <summary>本实例对应的配置文件路径（Load 时写入）。</summary>
        public string FilePath = "";

        public static string DefaultPath(string dataRoot) => Path.Combine(dataRoot, "settings.ini");

        /// <summary>读取设置；文件不存在或损坏时返回默认值（ScreenshotDir 用传入的默认目录）。</summary>
        public static AppSettings Load(string dataRoot, string defaultScreenshotDir)
        {
            var s = new AppSettings
            {
                ScreenshotDir = defaultScreenshotDir ?? "",
                FilePath = DefaultPath(dataRoot),
            };

            try
            {
                if (!File.Exists(s.FilePath)) return s;
                foreach (var raw in File.ReadAllLines(s.FilePath, Encoding.UTF8))
                {
                    var line = raw.Trim();
                    if (line.Length == 0 || line[0] == '#' || line[0] == ';') continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;

                    var key = line.Substring(0, eq).Trim();
                    var val = line.Substring(eq + 1).Trim();

                    switch (key.ToLowerInvariant())
                    {
                        case "windowseconds":
                            if (int.TryParse(val, out var w) && w >= 5) s.WindowSeconds = w;
                            break;
                        case "autoconnect":
                            s.AutoConnect = val == "1" || string.Equals(val, "true", StringComparison.OrdinalIgnoreCase);
                            break;
                        case "screenshotdir":
                            if (val.Length > 0) s.ScreenshotDir = val;
                            break;
                    }
                }
            }
            catch
            {
                // 配置损坏不影响启动：保留默认值
            }

            return s;
        }

        /// <summary>落盘（先写临时文件再替换，避免断电留半截文件）。</summary>
        public bool Save(out string error)
        {
            error = null;
            try
            {
                var dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var sb = new StringBuilder();
                sb.AppendLine("# DM40MeterStudio 设置（自动生成，可直接编辑）");
                sb.AppendLine("WindowSeconds=" + WindowSeconds);
                sb.AppendLine("AutoConnect=" + (AutoConnect ? "1" : "0"));
                sb.AppendLine("ScreenshotDir=" + (ScreenshotDir ?? ""));

                var tmp = FilePath + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(tmp, FilePath);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}