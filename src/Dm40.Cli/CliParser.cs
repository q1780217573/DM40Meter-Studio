using System;
using System.Collections.Generic;
using System.Globalization;

namespace Dm40.Cli
{
    /// <summary>CLI 参数解析器（CLI_Contract.md）。解析失败抛 <see cref="CliArgumentException"/>。</summary>
    public static class CliParser
    {
        /// <summary>解析命令行参数（不含 exe 名与 --cli 本身）。</summary>
        public static CliOptions Parse(string[] args)
        {
            var opt = new CliOptions();
            if (args == null || args.Length == 0)
                throw new CliArgumentException("缺少命令。用法: --cli <scan|stream|send|record> [options]");

            // 第一个非选项 token 是命令
            int i = 0;
            string cmd = args[0];
            switch (cmd)
            {
                case "scan": opt.Command = CliCommand.Scan; break;
                case "stream": opt.Command = CliCommand.Stream; break;
                case "send": opt.Command = CliCommand.Send; break;
                case "record": opt.Command = CliCommand.Record; break;
                default: throw new CliArgumentException("未知命令: " + cmd);
            }
            i = 1;

            while (i < args.Length)
            {
                string a = args[i];
                string Next() => i + 1 < args.Length ? args[++i] : throw new CliArgumentException(a + " 缺少值");

                switch (a)
                {
                    case "--format":
                        opt.Format = ParseFormat(Next()); break;
                    case "--mac":
                        opt.Mac = Next(); break;
                    case "--duration":
                        opt.DurationSeconds = ParseInt(Next(), a); break;
                    case "--cmd":
                        opt.SendCmd = Next(); break;
                    case "--out":
                        opt.OutFile = Next(); break;
                    case "--upper":
                        opt.UpperLimit = ParseDouble(Next(), a); break;
                    case "--lower":
                        opt.LowerLimit = ParseDouble(Next(), a); break;
                    default:
                        throw new CliArgumentException("未知选项: " + a);
                }
                i++;
            }

            Validate(opt);
            return opt;
        }

        private static void Validate(CliOptions o)
        {
            if (o.Command == CliCommand.Send && string.IsNullOrEmpty(o.SendCmd))
                throw new CliArgumentException("send 命令需要 --cmd <name>");
            if (o.Command == CliCommand.Record && string.IsNullOrEmpty(o.OutFile))
                throw new CliArgumentException("record 命令需要 --out <file>");
            if (o.UpperLimit.HasValue && o.LowerLimit.HasValue && o.UpperLimit.Value <= o.LowerLimit.Value)
                throw new CliArgumentException("--upper 必须大于 --lower");
            if (o.DurationSeconds < 0)
                throw new CliArgumentException("--duration 不能为负");
        }

        private static CliFormat ParseFormat(string v)
        {
            if (v == "json") return CliFormat.Json;
            if (v == "csv") return CliFormat.Csv;
            throw new CliArgumentException("--format 仅支持 json|csv");
        }

        private static int ParseInt(string v, string flag)
        {
            if (!int.TryParse(v, NumberStyles.Integer, CultureInfo.InvariantCulture, out var r) || r < 0)
                throw new CliArgumentException(flag + " 需为非负整数: " + v);
            return r;
        }

        private static double ParseDouble(string v, string flag)
        {
            if (!double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var r))
                throw new CliArgumentException(flag + " 需为数值: " + v);
            return r;
        }
    }

    /// <summary>CLI 参数错误（退出码 1）。</summary>
    public sealed class CliArgumentException : Exception
    {
        public CliArgumentException(string message) : base(message) { }
    }
}
