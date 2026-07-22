using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using Dm40.Cli;
using Dm40.Core;

namespace Dm40.App
{
    /// <summary>
    /// 应用入口：双模式。
    ///  - 无参数 → WPF 图形界面。
    ///  - 带 --cli → 无头 CLI 模式（数据走 stdout，供 AI/自动化消费，见 docs/CLI_Contract.md）。
    /// GUI 子系统进程默认无控制台，CLI 模式需 AttachConsole 附加到父进程控制台才能输出。
    /// </summary>
    public partial class App : Application
    {
        [DllImport("kernel32.dll")] private static extern bool AttachConsole(int dwProcessId);
        [DllImport("kernel32.dll")] private static extern bool AllocConsole();
        [DllImport("kernel32.dll")] private static extern bool FreeConsole();
        private const int ATTACH_PARENT_PROCESS = -1;

        protected override void OnStartup(StartupEventArgs e)
        {
            // 全局未处理异常落盘（WPF BAML/绑定崩溃时拿到完整内部消息，避免 GBK 事件日志截断）
            AppDomain.CurrentDomain.UnhandledException += (s, ev) =>
                DumpFatal(ev.ExceptionObject as Exception);
            DispatcherUnhandledException += (s, ev) =>
            {
                DumpFatal(ev.Exception);
                ev.Handled = false; // 保持默认崩溃语义，仅记录
            };

            // 检测 --cli（可出现在任意位置）
            int cliIndex = Array.IndexOf(e.Args, "--cli");
            if (cliIndex >= 0)
            {
                RunCli(e.Args, cliIndex);
                return; // 不启动 WPF
            }

            base.OnStartup(e);

            // 数据源恒为真实 BLE（模拟源已移除，软件"真实可用"：双击即连真机 DM40）。
            new Views.MainWindow(CreateBleSource()).Show();
        }

        private static IMeterDataSource CreateBleSource()
        {
            // 有项目引用，直接构造（程序集必然存在）
            return new Dm40.Transport.BleMeterSource();
        }

        private void RunCli(string[] args, int cliIndex)
        {
            // 关键：WPF 启动会安装 DispatcherSynchronizationContext。CLI 模式没有消息泵，
            // 数据源里的 await 延续若 Marshal 回 Dispatcher 将永远排队（连接挂起、无输出）。
            // 清掉同步上下文，让 await 延续走线程池，BLE 异步调用才能正常完成。
            System.Threading.SynchronizationContext.SetSynchronizationContext(null);

            // 附加父控制台以输出（从终端运行时）；失败则新建一个（双击/脚本重定向时）
            if (!AttachConsole(ATTACH_PARENT_PROCESS))
                AllocConsole();

            // 重建 stdout/stderr 句柄到刚附加的控制台
            var stdout = new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true };
            var stderr = new StreamWriter(Console.OpenStandardError()) { AutoFlush = true };
            Console.SetOut(stdout);
            Console.SetError(stderr);

            // 组装 --cli 之后的参数（去掉 --cli 本身及其前的内容）
            var cliArgs = new string[args.Length - cliIndex - 1];
            Array.Copy(args, cliIndex + 1, cliArgs, 0, cliArgs.Length);

            var program = new CliProgram(CreateSource, SystemClock.Instance, new RealWaitHandle());
            int code = program.Run(cliArgs, stdout, stderr);

            stdout.WriteLine(); // 让父 shell 提示符换行
            Console.Out.Flush();
            Environment.Exit(code);
        }

        // 数据源工厂：恒为真实 BLE（模拟源已移除）。
        private static IMeterDataSource CreateSource() => new Dm40.Transport.BleMeterSource();

        // 把完整异常链（含全部 InnerException）写到 exe 旁的 fatal-error.log（UTF-8，避免乱码）。
        private static void DumpFatal(Exception ex)
        {
            if (ex == null) return;
            try
            {
                var sb = new System.Text.StringBuilder();
                sb.AppendLine("==== " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " ====");
                for (var cur = ex; cur != null; cur = cur.InnerException)
                {
                    sb.AppendLine(cur.GetType().FullName + ": " + cur.Message);
                    sb.AppendLine(cur.StackTrace);
                    sb.AppendLine("---- inner ----");
                }
                var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fatal-error.log");
                File.AppendAllText(path, sb.ToString(), new System.Text.UTF8Encoding(false));
            }
            catch { /* 记录失败不影响原崩溃 */ }
        }

        // 生产等待：真实阻塞
        private sealed class RealWaitHandle : IWaitHandle
        {
            public void Wait(TimeSpan duration) => System.Threading.Thread.Sleep(duration);
            public void WaitIndefinitely()
            {
                // 持续模式：阻塞直到 Ctrl+C（Console.CancelKeyPress 默认终止进程）
                System.Threading.Thread.Sleep(System.Threading.Timeout.Infinite);
            }
        }
    }
}
