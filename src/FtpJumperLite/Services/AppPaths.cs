using System;
using System.IO;

namespace FtpJumperLite.Services
{
    /// <summary>
    /// 应用路径策略：数据默认放在**应用文件夹**下（data / logs）；
    /// 应用文件夹不可写（只读共享等）时自动回退 %LOCALAPPDATA%\FtpJumperLite。
    /// </summary>
    public static class AppPaths
    {
        private static readonly object SyncRoot = new object();
        private static string? _dataRoot;
        private static bool _usingFallback;

        /// <summary>应用所在目录（exe 目录）。</summary>
        public static string BaseDirectory => AppContext.BaseDirectory;

        /// <summary>数据根目录：应用目录下的 data；不可写时回退 %LOCALAPPDATA%\FtpJumperLite\data。</summary>
        public static string DataRoot
        {
            get
            {
                lock (SyncRoot)
                {
                    if (_dataRoot == null)
                    {
                        _dataRoot = ResolveDataRoot(out _usingFallback);
                    }

                    return _dataRoot;
                }
            }
        }

        /// <summary>日志目录：数据根目录下的 logs。</summary>
        public static string LogDirectory => Path.Combine(DataRoot, "logs");

        /// <summary>是否已回退到用户 AppData（供界面/日志提示）。</summary>
        public static bool IsUsingFallbackDataRoot
        {
            get
            {
                lock (SyncRoot)
                {
                    _ = DataRoot;
                    return _usingFallback;
                }
            }
        }

        /// <summary>重置探测结果（仅测试用）。</summary>
        public static void ResetForTests()
        {
            lock (SyncRoot)
            {
                _dataRoot = null;
                _usingFallback = false;
            }
        }

        private static string ResolveDataRoot(out bool usedFallback)
        {
            var appData = Path.Combine(BaseDirectory, "data");
            if (TryEnsureWritable(appData))
            {
                usedFallback = false;
                return appData;
            }

            var fallback = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FtpJumperLite",
                "data");
            if (TryEnsureWritable(fallback))
            {
                usedFallback = true;
                return fallback;
            }

            // 两处都不可写：仍返回应用目录，让后续错误显式暴露给用户。
            usedFallback = false;
            return appData;
        }

        private static bool TryEnsureWritable(string directory)
        {
            try
            {
                Directory.CreateDirectory(directory);
                var probe = Path.Combine(directory, ".write-probe-" + Guid.NewGuid().ToString("N"));
                File.WriteAllText(probe, "ok");
                File.Delete(probe);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
