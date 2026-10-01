using System;
using System.IO;

namespace FtpJumperLite.Services
{
    /// <summary>日志接口（便于测试时注入空实现）。</summary>
    public interface ILog
    {
        void Info(string message);

        void Warn(string message);

        void Error(string message);
    }

    /// <summary>不输出任何内容的日志实现。</summary>
    public sealed class NullLog : ILog
    {
        public static readonly NullLog Instance = new NullLog();

        public void Info(string message)
        {
        }

        public void Warn(string message)
        {
        }

        public void Error(string message)
        {
        }
    }

    /// <summary>
    /// 应用日志：写入数据目录下的按日文件。
    /// 约定：任何调用方都不得把账号密码传入日志；本类型不做“事后清洗”，请从源头保证
    /// （调用处一律使用 FtpUrlBuilder.BuildMasked 的结果）。
    /// </summary>
    public static class AppLogger
    {
        private static readonly object SyncRoot = new object();
        private static ILog _current = NullLog.Instance;

        /// <summary>当前日志实现（默认空实现；程序启动时调用 Configure）。</summary>
        public static ILog Current
        {
            get { lock (SyncRoot) { return _current; } }
            set { lock (SyncRoot) { _current = value ?? NullLog.Instance; } }
        }

        public static void Configure(string logDirectory)
        {
            try
            {
                Directory.CreateDirectory(logDirectory);
                Current = new FileLog(logDirectory);
            }
            catch (Exception ex)
            {
                // 日志目录不可用时绝不能让应用崩溃。
                Current = NullLog.Instance;
                System.Diagnostics.Trace.WriteLine("日志初始化失败: " + ex.Message);
            }
        }

        public static void Info(string message) => Current.Info(message);

        public static void Warn(string message) => Current.Warn(message);

        public static void Error(string message) => Current.Error(message);

        private sealed class FileLog : ILog
        {
            private readonly string _directory;

            public FileLog(string directory)
            {
                _directory = directory;
            }

            public void Info(string message) => Write("INFO", message);

            public void Warn(string message) => Write("WARN", message);

            public void Error(string message) => Write("ERROR", message);

            private void Write(string level, string message)
            {
                try
                {
                    var fileName = "ftp-jumper-lite-" + DateTime.Now.ToString("yyyyMMdd") + ".log";
                    var line = string.Format(
                        System.Globalization.CultureInfo.InvariantCulture,
                        "[{0}] [{1}] {2}{3}",
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                        level,
                        message,
                        Environment.NewLine);
                    lock (SyncRoot)
                    {
                        File.AppendAllText(Path.Combine(_directory, fileName), line, System.Text.Encoding.UTF8);
                    }
                }
                catch
                {
                    // 日志写入失败不影响业务。
                }
            }
        }
    }
}
