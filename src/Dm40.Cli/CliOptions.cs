namespace Dm40.Cli
{
    /// <summary>CLI 命令。</summary>
    public enum CliCommand
    {
        None,
        Scan,
        Stream,
        Send,
        Record,
        Probe,
    }

    /// <summary>输出格式。</summary>
    public enum CliFormat
    {
        Json,
        Csv,
    }

    /// <summary>解析后的 CLI 参数（对应 CLI_Contract.md）。数据源恒为真实 BLE（模拟源已移除）。</summary>
    public sealed class CliOptions
    {
        public CliCommand Command = CliCommand.None;
        public CliFormat Format = CliFormat.Json;
        public string Mac;
        public int DurationSeconds;        // 0 = 持续
        public string SendCmd;
        public string OutFile;
        public double? UpperLimit;
        public double? LowerLimit;

        /// <summary>probe 命令：每个档位发出后的观察窗口（毫秒）。</summary>
        public int SettleMs = 800;
    }
}
