namespace Dm40.Core
{
    /// <summary>
    /// 追加写入抽象（测试 seam）：CsvRecorder 经它落盘，测试用内存实现替代真实文件。
    /// 实现需保证：构造即写入表头与 UTF-8 BOM；WriteLine 追加一行；Flush 落盘。
    /// </summary>
    public interface ICsvSink : System.IDisposable
    {
        /// <summary>追加一行（不含换行）。失败应抛异常，由录制器转为停止录制。</summary>
        void WriteLine(string line);

        /// <summary>将缓冲数据落盘。</summary>
        void Flush();

        /// <summary>关闭并释放底层句柄。</summary>
        new void Dispose();
    }
}
