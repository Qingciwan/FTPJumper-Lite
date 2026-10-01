using System;
using System.IO;

namespace FtpJumperLite.Services
{
    /// <summary>
    /// 以“指定程序”打开网址（不经过系统的 URL 协议关联）。
    /// 用于强制用文件资源管理器打开：ftp 的 ShellExecute 关联可能被第三方浏览器抢走，
    /// 把网址直接作为命令行参数交给 explorer.exe 才能绕开这层关联
    /// （`explorer.exe ftp://…` 由资源管理器自己的 FTP 命名空间处理，已实测可行）。
    /// </summary>
    public static class ShellLauncher
    {
        /// <summary>用指定程序打开网址；返回是否成功把进程拉起来。</summary>
        public static bool TryOpenWith(string fileName, string arguments, out string error)
        {
            error = "";
            if (string.IsNullOrWhiteSpace(fileName))
            {
                error = "未指定要使用的程序。";
                return false;
            }

            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = true
                };
                using (System.Diagnostics.Process.Start(psi))
                {
                    // explorer.exe 会立即返回（已开着的实例由 DDE/COM 转发）。
                }

                return true;
            }
            catch (Exception ex)
            {
                error = "无法启动 " + Path.GetFileName(fileName) + "：" + ex.Message;
                return false;
            }
        }
    }
}
