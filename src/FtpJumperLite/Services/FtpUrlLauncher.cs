using System;
using System.Diagnostics;

namespace FtpJumperLite.Services
{
    /// <summary>
    /// 打开 FTP 网址（lite 版的核心）：**写死用文件资源管理器打开**。
    ///
    /// 为什么不能交给系统关联：ftp 是 URL 协议关联
    /// （<c>HKCU\Software\Microsoft\Windows\Shell\Associations\UrlAssociations\ftp</c>），
    /// 教室一体机上常被第三方浏览器（经实测例如 360 浏览器）抢走，体验很差；
    /// 而 `explorer.exe "ftp://…"` 是让资源管理器**自己的 FTP 命名空间**
    /// （Microsoft FTP Folder，{63da6ec0-2e98-11cf-8d82-444553540000}）接管，绕开关联。
    ///
    /// 已在真实系统验证：`explorer.exe "ftp://user:pass@host:port/path/"` 会新开资源管理器窗口，
    /// 地址栏显示主机名，并正常列出远端目录（服务器端可见 USER/PASS/OPTS/SYST/SITE/PWD/NOOP/CWD/TYPE/PASV/LIST）。
    /// 兜底顺序：explorer.exe → ShellExecute(网址) → 只给出可手动粘贴的脱敏网址。
    ///
    /// 注意：进程拉起成功只代表“已交给资源管理器”，对方是否连上远端要等窗口里出现内容；
    /// 日志只记录脱敏网址。
    /// </summary>
    public static class FtpUrlLauncher
    {
        /// <summary>
        /// 用文件资源管理器打开网址。成功返回 true；<paramref name="error"/> 为中文错误说明。
        /// <paramref name="maskedUrlForLog"/> 仅用于日志/提示，绝不要传入含明文密码的网址。
        /// </summary>
        public static bool TryOpen(string url, string maskedUrlForLog, out string error)
        {
            error = "";
            if (string.IsNullOrWhiteSpace(url))
            {
                error = "网址为空。";
                return false;
            }

            // 1) 首选：把网址作为命令行参数交给 explorer.exe（绕开系统 ftp 关联）。
            var explorerError = "";
            if (ShellLauncher.TryOpenWith("explorer.exe", "\"" + url + "\"", out explorerError))
            {
                AppLogger.Info("已用文件资源管理器打开 FTP 网址：" + maskedUrlForLog);
                return true;
            }

            AppLogger.Warn("explorer.exe 启动失败（" + explorerError + "），改用系统默认处理器兜底：" + maskedUrlForLog);

            // 2) 兜底：系统默认的 ftp 处理器（可能是浏览器，总比什么都不开好）。
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = url,
                    UseShellExecute = true
                };
                using (Process.Start(psi))
                {
                }

                AppLogger.Info("已退回系统默认处理器打开 FTP 网址：" + maskedUrlForLog);
                return true;
            }
            catch (Exception ex)
            {
                // 3) 都失败：把脱敏网址交给老师手动粘贴。
                error = "无法用文件资源管理器打开该 FTP 网址。\r\n"
                        + "explorer.exe：" + explorerError + "\r\n"
                        + "系统默认处理器：" + ex.Message + "\r\n\r\n"
                        + "可手动把下面这行粘进资源管理器地址栏：\r\n" + maskedUrlForLog;
                AppLogger.Error("打开 FTP 网址失败：" + ex.GetType().Name + " - " + ex.Message);
                return false;
            }
        }
    }
}
