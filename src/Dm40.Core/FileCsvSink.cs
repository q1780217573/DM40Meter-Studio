using System.IO;
using System.Text;

namespace Dm40.Core
{
    /// <summary>
    /// 真实文件 sink（生产用）：UTF-8 带 BOM 追加写入，保证 Excel 直接打开不乱码（PRD FR-008）。
    /// 路径由调用方（用户显式选择的目录）提供，防路径遍历在 UI/服务层校验。
    /// </summary>
    public sealed class FileCsvSink : ICsvSink
    {
        private static readonly Encoding Utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        private readonly StreamWriter _writer;

        public FileCsvSink(string path)
        {
            // append: true，编码带 BOM；新建文件时 BOM 自动写入文件头
            _writer = new StreamWriter(path, append: true, encoding: Utf8WithBom);
        }

        public void WriteLine(string line) => _writer.WriteLine(line);

        public void Flush() => _writer.Flush();

        public void Dispose() => _writer.Dispose();
    }
}
