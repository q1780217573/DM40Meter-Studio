using System;
using System.IO;
using Dm40.Core;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dm40.Cli.Tests
{
    /// <summary>即时返回的 waiter：等待时不真正阻塞。可挂回调用于驱动模拟源产生帧。</summary>
    internal sealed class FakeWaitHandle : IWaitHandle
    {
        public Action OnWait;
        public void Wait(TimeSpan duration) => OnWait?.Invoke();
        public void WaitIndefinitely() { /* 测试不阻塞 */ }
    }

    internal static class CliTestHost
    {
        /// <summary>用真实帧数据源运行 CLI，捕获 stdout/stderr，返回 (退出码, stdout, stderr)。</summary>
        public static (int Code, string Out, string Err) Run(
            string[] args,
            Action<FakeMeterSource, FakeClock> onBeforeWait = null)
        {
            var clock = new FakeClock();
            var src = new FakeMeterSource();
            var waiter = new FakeWaitHandle();

            var program = new CliProgram(() => src, clock, waiter);

            // 让"等待"驱动帧：每次 Wait 推若干真实 5V 帧（模拟连接后持续推送）
            waiter.OnWait = () =>
            {
                onBeforeWait?.Invoke(src, clock);
                if (src.State == ConnectionState.Measuring)
                    for (int i = 0; i < 4; i++) { src.PushFrame(RealFrames.Dcv5VSequence[i % 3]); clock.AdvanceMs(250); }
            };

            var outSw = new StringWriter();
            var errSw = new StringWriter();
            int code = program.Run(args, outSw, errSw);
            return (code, outSw.ToString(), errSw.ToString());
        }
    }

    [TestClass]
    public class CliParserTests
    {
        [TestMethod]
        public void Parse_Scan_Defaults()
        {
            var o = CliParser.Parse(new[] { "scan" });
            Assert.AreEqual(CliCommand.Scan, o.Command);
            Assert.AreEqual(CliFormat.Json, o.Format);
        }

        [TestMethod]
        public void Parse_Stream_AllOptions()
        {
            var o = CliParser.Parse(new[] { "stream", "--format", "csv", "--duration", "10", "--upper", "3.5", "--lower", "3.1" });
            Assert.AreEqual(CliCommand.Stream, o.Command);
            Assert.AreEqual(CliFormat.Csv, o.Format);
            Assert.AreEqual(10, o.DurationSeconds);
            Assert.AreEqual(3.5, o.UpperLimit.Value, 1e-9);
            Assert.AreEqual(3.1, o.LowerLimit.Value, 1e-9);
        }

        [TestMethod]
        public void Parse_SourceOption_NoLongerSupported()
        {
            // --source 已随模拟源移除而废弃 → 报参数错误
            Assert.ThrowsException<CliArgumentException>(() => CliParser.Parse(new[] { "stream", "--source", "sim" }));
        }

        [TestMethod]
        public void Parse_UnknownCommand_Throws()
        {
            Assert.ThrowsException<CliArgumentException>(() => CliParser.Parse(new[] { "frobnicate" }));
        }

        [TestMethod]
        public void Parse_SendWithoutCmd_Throws()
        {
            Assert.ThrowsException<CliArgumentException>(() => CliParser.Parse(new[] { "send" }));
        }

        [TestMethod]
        public void Parse_RecordWithoutOut_Throws()
        {
            Assert.ThrowsException<CliArgumentException>(() => CliParser.Parse(new[] { "record", "--duration", "5" }));
        }

        [TestMethod]
        public void Parse_UpperNotGreaterThanLower_Throws()
        {
            Assert.ThrowsException<CliArgumentException>(() =>
                CliParser.Parse(new[] { "stream", "--upper", "1", "--lower", "5" }));
        }
    }

    [TestClass]
    public class CliProgramTests
    {
        [TestMethod]
        public void Scan_Json_OutputsDevice()
        {
            var (code, out_, err) = CliTestHost.Run(new[] { "scan" });
            Assert.AreEqual(CliProgram.ExitOk, code, "stderr: " + err);
            StringAssert.Contains(out_, "\"name\":\"DM40");
            StringAssert.Contains(out_, "\"mac\":");
            StringAssert.Contains(out_, "\"rssi\":");
        }

        [TestMethod]
        public void Scan_Csv_OutputsHeaderAndRow()
        {
            var (code, out_, err) = CliTestHost.Run(new[] { "scan", "--format", "csv" });
            Assert.AreEqual(CliProgram.ExitOk, code);
            StringAssert.StartsWith(out_, "name,mac,rssi");
            StringAssert.Contains(out_, "DM40");
        }

        [TestMethod]
        public void Stream_Json_EmitsJsonLines()
        {
            var (code, out_, err) = CliTestHost.Run(new[] { "stream", "--duration", "1" });
            Assert.AreEqual(CliProgram.ExitOk, code, "stderr: " + err);
            StringAssert.Contains(out_, "\"value\":");
            StringAssert.Contains(out_, "\"unit\":\"V\"");
            StringAssert.Contains(out_, "\"mode\":\"VDC\"");
            StringAssert.Contains(out_, "\"overload\":false");
        }

        [TestMethod]
        public void Stream_Csv_EmitsHeaderAndRows()
        {
            var (code, out_, err) = CliTestHost.Run(new[] { "stream", "--duration", "1", "--format", "csv" });
            Assert.AreEqual(CliProgram.ExitOk, code);
            StringAssert.StartsWith(out_, CsvSerializer.Header);
            StringAssert.Contains(out_, ",V,VDC,");
        }

        [TestMethod]
        public void Stream_ValueNear5VReference()
        {
            var (code, out_, err) = CliTestHost.Run(new[] { "stream", "--duration", "1" });
            Assert.AreEqual(CliProgram.ExitOk, code);
            StringAssert.Contains(out_, "5.002"); // 真实 5V 基准源帧
        }

        [TestMethod]
        public void Send_HoldOn_OutputsConfirmation()
        {
            var (code, out_, err) = CliTestHost.Run(new[] { "send", "--cmd", "hold-on" });
            Assert.AreEqual(CliProgram.ExitOk, code, "stderr: " + err);
            StringAssert.Contains(out_, "\"sent\":\"hold-on\"");
            StringAssert.Contains(out_, "AF 05 03 04 01 01");
        }

        [TestMethod]
        public void Send_UnknownCmd_ReturnsArgError()
        {
            var (code, out_, err) = CliTestHost.Run(new[] { "send", "--cmd", "explode" });
            Assert.AreEqual(CliProgram.ExitArgError, code);
        }

        [TestMethod]
        public void ArgError_UnknownCommand_Returns1()
        {
            var (code, out_, err) = CliTestHost.Run(new[] { "bogus" });
            Assert.AreEqual(CliProgram.ExitArgError, code);
            StringAssert.Contains(err, "未知命令");
        }
    }
}
