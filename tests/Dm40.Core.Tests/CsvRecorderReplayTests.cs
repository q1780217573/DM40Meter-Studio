using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Dm40.Core.Tests
{
    /// <summary>内存版 sink：测试录制器行为用。可注入故障模拟磁盘写失败。</summary>
    internal sealed class InMemoryCsvSink : ICsvSink
    {
        public readonly List<string> Lines = new List<string>();
        public int FlushCount;
        public bool FailOnWrite;
        public bool Disposed;

        public void WriteLine(string line)
        {
            if (FailOnWrite) throw new System.IO.IOException("模拟磁盘写失败");
            Lines.Add(line);
        }

        public void Flush() => FlushCount++;
        public void Dispose() => Disposed = true;
    }

    /// <summary>
    /// CSV 录制器单元测试（对应 PRD FR-009、§5.2 规则5、录制状态机）。
    /// 内存队列缓冲 + 每秒 flush；写失败立即停止录制并保留已录数据。
    /// </summary>
    [TestClass]
    public class CsvRecorderTests
    {
        private static CsvRecord Rec(int sec) => new CsvRecord
        {
            Timestamp = new DateTime(2026, 7, 17, 10, 0, sec),
            Value = 3.3, Unit = "V", Mode = "DCV", Range = "6V",
        };

        [TestMethod]
        public void Start_WritesHeader()
        {
            var sink = new InMemoryCsvSink();
            using (var r = new CsvRecorder(sink, new FakeClock()))
            {
                r.Start();
            }
            Assert.AreEqual(CsvSerializer.Header, sink.Lines[0]);
        }

        [TestMethod]
        public void Write_BuffersUntilFlushInterval()
        {
            var clock = new FakeClock();
            var sink = new InMemoryCsvSink();
            using (var r = new CsvRecorder(sink, clock))
            {
                r.Start();
                r.Write(Rec(0));
                r.Write(Rec(0));
                // 未到 1s，仍在缓冲
                Assert.AreEqual(1, sink.Lines.Count); // 仅表头
                Assert.AreEqual(2, r.BufferedCount);
            }
        }

        [TestMethod]
        public void Tick_AfterOneSecond_FlushesBuffer()
        {
            var clock = new FakeClock();
            var sink = new InMemoryCsvSink();
            using (var r = new CsvRecorder(sink, clock))
            {
                r.Start();
                r.Write(Rec(0));
                r.Write(Rec(0));
                clock.AdvanceMs(1100);
                r.Tick(); // 由外部定时器每秒调用
                Assert.AreEqual(1 + 2, sink.Lines.Count); // 表头 + 2 数据行
                Assert.AreEqual(0, r.BufferedCount);
                Assert.IsTrue(sink.FlushCount >= 1);
            }
        }

        [TestMethod]
        public void Stop_FlushesRemainingBuffer()
        {
            var clock = new FakeClock();
            var sink = new InMemoryCsvSink();
            var r = new CsvRecorder(sink, clock);
            r.Start();
            r.Write(Rec(0));
            r.Stop();
            // 停止时已录数据全部落盘
            Assert.AreEqual(1 + 1, sink.Lines.Count);
            Assert.IsFalse(r.IsRecording);
        }

        [TestMethod]
        public void Dispose_ReleasesSink()
        {
            var sink = new InMemoryCsvSink();
            var r = new CsvRecorder(sink, new FakeClock());
            r.Start();
            r.Write(Rec(0));
            r.Dispose();
            Assert.IsTrue(sink.Disposed);
        }

        [TestMethod]
        public void WriteFailure_StopsRecording_KeepsData()
        {
            var clock = new FakeClock();
            var sink = new InMemoryCsvSink();
            var r = new CsvRecorder(sink, clock);
            r.Start();
            r.Write(Rec(0));
            sink.FailOnWrite = true;
            clock.AdvanceMs(1100);
            r.Tick(); // flush 时写失败
            Assert.IsFalse(r.IsRecording);
            Assert.IsTrue(r.HasError);
            // 已录的表头保留
            Assert.AreEqual(CsvSerializer.Header, sink.Lines[0]);
        }

        [TestMethod]
        public void Write_WhenNotRecording_IsIgnored()
        {
            var sink = new InMemoryCsvSink();
            using (var r = new CsvRecorder(sink, new FakeClock()))
            {
                r.Write(Rec(0)); // 未 Start
                Assert.AreEqual(0, r.BufferedCount);
                Assert.AreEqual(0, sink.Lines.Count);
            }
        }

        [TestMethod]
        public void Start_Twice_IsIdempotent()
        {
            var sink = new InMemoryCsvSink();
            using (var r = new CsvRecorder(sink, new FakeClock()))
            {
                r.Start();
                r.Start(); // 重复启动不应重复写表头
                Assert.AreEqual(1, sink.Lines.Count);
            }
        }
    }

    /// <summary>
    /// CSV 回放源单元测试（对应 PRD FR-010）。
    /// 加载本软件生成的 CSV；坏行跳过；超限提示仅加载前 N 行；不产生新数据落盘。
    /// </summary>
    [TestClass]
    public class CsvReplaySourceTests
    {
        private static string[] MakeLines(int n)
        {
            var list = new List<string> { CsvSerializer.Header };
            for (int i = 0; i < n; i++)
            {
                list.Add(CsvSerializer.Serialize(new CsvRecord
                {
                    Timestamp = new DateTime(2026, 7, 17, 10, 0, 0).AddSeconds(i),
                    Value = i * 0.1, Unit = "V", Mode = "DCV", Range = "6V",
                }));
            }
            return list.ToArray();
        }

        [TestMethod]
        public void Load_ParsesAllValidRows()
        {
            var src = CsvReplaySource.LoadFromLines(MakeLines(10));
            Assert.AreEqual(10, src.Count);
        }

        [TestMethod]
        public void Load_SkipsCorruptRows()
        {
            var lines = MakeLines(5).ToList();
            lines.Insert(3, "garbage,not,a,record");
            var src = CsvReplaySource.LoadFromLines(lines);
            Assert.AreEqual(5, src.Count); // 坏行被跳过
        }

        [TestMethod]
        public void Load_RespectsRowCap_AndReportsTruncation()
        {
            var src = CsvReplaySource.LoadFromLines(MakeLines(100), maxRows: 50);
            Assert.AreEqual(50, src.Count);
            Assert.IsTrue(src.WasTruncated);
        }

        [TestMethod]
        public void Load_NotTruncated_WhenUnderCap()
        {
            var src = CsvReplaySource.LoadFromLines(MakeLines(10), maxRows: 50);
            Assert.IsFalse(src.WasTruncated);
        }

        [TestMethod]
        public void Load_NoValidRows_Throws()
        {
            Assert.ThrowsException<CsvFormatException>(() =>
                CsvReplaySource.LoadFromLines(new[] { CsvSerializer.Header, "bad,line" }));
        }

        [TestMethod]
        public void Records_AreChronologicallyAccessible()
        {
            var src = CsvReplaySource.LoadFromLines(MakeLines(5));
            var first = src.GetAt(0);
            var last = src.GetAt(4);
            Assert.IsTrue(first.Timestamp < last.Timestamp);
            Assert.AreEqual(0.4, last.Value, 1e-9);
        }
    }
}
