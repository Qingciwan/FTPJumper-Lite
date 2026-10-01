using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FtpJumperLite.Ui
{
    /// <summary>
    /// 应用图标：从 exe 内嵌的 Win32 图标资源读取（由 csproj 的 ApplicationIcon 嵌入）。
    /// 显式赋给窗体，确保标题栏、任务栏与 Alt+Tab 显示同一图标。
    ///
    /// 注意两点（都踩过）：
    /// 1) <c>Icon.ExtractAssociatedIcon</c> 只能拿到 32×32；在 2K@200% 的教室里标题栏/任务栏/Alt+Tab
    ///    需要更大的位图，直接用它会被放虚。这里改用 <c>PrivateExtractIcons</c> 按需取大图
    ///    （它能处理 Vista 之后 PNG 压缩的 256×256 帧，而 .NET 的 <c>Icon(文件, 宽, 高)</c> 读不了 PNG 帧）。
    /// 2) <c>new Icon(exePath, w, h)</c> 对 **exe 一律失败**（只对 .ico 有效），不能用来取 exe 内嵌图标。
    /// </summary>
    public static class AppIcon
    {
        /// <summary>取图标时优先尝试的尺寸（从大到小，取第一个成功的）。</summary>
        private static readonly int[] PreferredSizes = { 256, 128, 64, 48, 32 };

        private static readonly object SyncRoot = new object();
        private static bool _loaded;
        private static Icon? _icon;

        /// <summary>当前进程 exe 的图标（懒加载并缓存）。</summary>
        public static Icon? Value
        {
            get
            {
                lock (SyncRoot)
                {
                    if (!_loaded)
                    {
                        _loaded = true;
                        _icon = LoadFrom(Application.ExecutablePath);
                    }

                    return _icon;
                }
            }
        }

        /// <summary>
        /// 读取指定 exe 内嵌图标中尽可能大的一帧；失败时退回 <see cref="Icon.ExtractAssociatedIcon"/>。
        /// 单独暴露出来便于单元测试（测试宿主自己的 exe 不是被测程序）。
        /// </summary>
        public static Icon? LoadFrom(string? exePath)
        {
            if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
            {
                return null;
            }

            foreach (var size in PreferredSizes)
            {
                var handles = new IntPtr[1];
                try
                {
                    var count = PrivateExtractIcons(exePath!, 0, size, size, handles, null, 1, 0);
                    if (count > 0 && handles[0] != IntPtr.Zero)
                    {
                        using (var fromHandle = Icon.FromHandle(handles[0]))
                        {
                            // Clone 出独立托管副本后即可安全销毁原生句柄。
                            return (Icon)fromHandle.Clone();
                        }
                    }
                }
                catch
                {
                    // 换下一个尺寸再试。
                }
                finally
                {
                    if (handles[0] != IntPtr.Zero)
                    {
                        DestroyIcon(handles[0]);
                    }
                }
            }

            try
            {
                return Icon.ExtractAssociatedIcon(exePath!);
            }
            catch
            {
                // 取图标失败不影响程序运行。
                return null;
            }
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern int PrivateExtractIcons(
            string lpszFile,
            int nIconIndex,
            int cxIcon,
            int cyIcon,
            IntPtr[] phicon,
            int[]? piconid,
            int nIcons,
            int flags);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr hIcon);
    }
}
