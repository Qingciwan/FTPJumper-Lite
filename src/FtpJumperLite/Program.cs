using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using FtpJumperLite.Services;
using FtpJumperLite.Ui;

namespace FtpJumperLite
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            var options = ParseArgs(args);
            if (options.SelfCheck || options.UrlMode)
            {
                var output = CreateUtf8Stdout();
                return options.SelfCheck
                    ? SelfCheck.Run(options.ConfigPath, output)
                    : FtpUrlDiagnostics.Run(options.ConfigPath, options.UrlSubject, options.ShowPassword, options.Open, output);
            }

            EnableDpiAwareness();
            ConfigureGlobalHandlers();

            AppLogger.Configure(AppPaths.LogDirectory);

            AppLogger.Info("程序启动（lite 版：直接把 ftp:// 网址交给系统默认处理器）。数据目录：" + AppPaths.DataRoot
                           + (AppPaths.IsUsingFallbackDataRoot ? "（应用目录不可写，已回退到用户 AppData）" : ""));
            LogDisplayInfo();
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(options.ConfigPath));
            AppLogger.Info("程序正常退出。");
            return 0;
        }

        /// <summary>
        /// 记录显示环境（现场排障用）：进程看到的桌面尺寸、系统 DPI，以及本进程实际的 DPI 感知程度。
        /// 高 DPI 下布局异常时，先看这几行日志。
        /// </summary>
        private static void LogDisplayInfo()
        {
            try
            {
                var screen = Screen.PrimaryScreen;
                AppLogger.Info("显示环境：Bounds=" + (screen != null ? screen.Bounds.ToString() : "?")
                               + " WorkingArea=" + (screen != null ? screen.WorkingArea.ToString() : "?")
                               + " 系统DPI=" + GetDpiForSystem()
                               + " 桌面DPI=" + GetDpiForWindow(GetDesktopWindow()));
            }
            catch (Exception ex)
            {
                AppLogger.Warn("记录显示环境失败：" + ex.Message);
            }
        }

        [DllImport("user32.dll")]
        private static extern uint GetDpiForSystem();

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        /// <summary>无界面模式的输出：固定 UTF-8，无论是否被重定向、系统代码页为何。</summary>
        private static TextWriter CreateUtf8Stdout()
        {
            try
            {
                var stdout = Console.OpenStandardOutput();
                if (stdout != null && stdout.CanWrite)
                {
                    return new StreamWriter(stdout, new System.Text.UTF8Encoding(false)) { AutoFlush = true };
                }
            }
            catch
            {
                // 退回默认输出。
            }

            return Console.Out;
        }

        private static CommandLineOptions ParseArgs(string[] args)
        {
            var options = new CommandLineOptions();
            for (var i = 0; i < args.Length; i++)
            {
                if (string.Equals(args[i], "--selfcheck", StringComparison.OrdinalIgnoreCase))
                {
                    options.SelfCheck = true;
                }
                else if (string.Equals(args[i], "--url", StringComparison.OrdinalIgnoreCase)
                         || string.Equals(args[i], "--print-url", StringComparison.OrdinalIgnoreCase))
                {
                    options.UrlMode = true;

                    // 可选：紧跟一个科目 id 或名称；缺省则使用配置中的第一个科目。
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    {
                        options.UrlSubject = args[++i];
                    }
                }
                else if (string.Equals(args[i], "--show-password", StringComparison.OrdinalIgnoreCase))
                {
                    options.ShowPassword = true;
                }
                else if (string.Equals(args[i], "--open", StringComparison.OrdinalIgnoreCase))
                {
                    options.Open = true;
                }
                else if (string.Equals(args[i], "--cfg", StringComparison.OrdinalIgnoreCase))
                {
                    if (i + 1 < args.Length)
                    {
                        options.ConfigPath = args[++i];
                    }
                }
            }

            return options;
        }

        private static void EnableDpiAwareness()
        {
            try
            {
                if (Environment.OSVersion.Version.Major >= 6)
                {
                    SetProcessDPIAware();
                }
            }
            catch
            {
                // 进程级 DPI 感知失败不影响运行（仍有清单兜底）。
            }
        }

        private static void ConfigureGlobalHandlers()
        {
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (_, e) => HandleFatal(e.Exception);
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                HandleFatal(ex ?? new Exception("未知的未处理错误。"));
            };
        }

        private static void HandleFatal(Exception ex)
        {
            try
            {
                AppLogger.Error("发生未处理的错误：" + ex);
                MessageBox.Show(
                    "程序遇到未处理的错误：\r\n" + ex.Message + "\r\n\r\n详细信息已写入日志。",
                    "FtpJumper Lite",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
            catch
            {
                // 弹窗失败也绝不再次崩溃。
            }
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetProcessDPIAware();

        private sealed class CommandLineOptions
        {
            public bool SelfCheck;

            public bool UrlMode;

            public string? UrlSubject;

            public bool ShowPassword;

            public bool Open;

            public string? ConfigPath;
        }
    }
}
