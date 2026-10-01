using System;
using System.Globalization;
using System.IO;
using System.Linq;
using FtpJumperLite.Models;

namespace FtpJumperLite.Services
{
    /// <summary>
    /// 无界面网址诊断：把某科目拼出的 ftp:// 网址打印出来，便于在教室现场核对格式。
    /// 默认只打印脱敏网址（密码显示为 ***）；加 <c>--show-password</c> 才打印明文。
    /// 退出码：0=通过；2=打开/网址问题；3=参数或配置问题。
    /// </summary>
    public static class FtpUrlDiagnostics
    {
        public static int Run(string? configPath, string? subjectKey, bool showPassword, bool open, TextWriter output)
        {
            var path = string.IsNullOrWhiteSpace(configPath) ? ConfigStore.DefaultConfigPath : configPath!;
            output.WriteLine("FtpJumper Lite 网址诊断");
            output.WriteLine("配置文件：" + path);

            var store = new ConfigStore(path);
            if (!store.TryLoad(out var config, out var error))
            {
                output.WriteLine("配置存在问题：");
                output.WriteLine(error);
                return 3;
            }

            var subject = FindSubject(config!, subjectKey);
            if (subject == null)
            {
                output.WriteLine("未找到科目：\"" + subjectKey + "\"");
                output.WriteLine("可用科目 id：" + string.Join("、", config!.Subjects.Select(s => s.Id)));
                return 3;
            }

            string masked;
            string url;
            try
            {
                masked = FtpUrlBuilder.BuildMasked(subject, path);
                url = FtpUrlBuilder.Build(subject, path);
            }
            catch (FtpUrlException ex)
            {
                output.WriteLine("无法生成网址：" + ex.Message);
                return 2;
            }

            output.WriteLine("科目：" + subject.Name + "（id=" + subject.Id + "）");
            output.WriteLine("主机：" + subject.Host + ":" + subject.Port.ToString(CultureInfo.InvariantCulture));
            output.WriteLine("用户名：" + (string.IsNullOrEmpty(subject.User) ? "(匿名)" : subject.User));
            output.WriteLine("远端路径：" + (string.IsNullOrWhiteSpace(subject.RemotePath) ? "/" : subject.RemotePath));
            output.WriteLine("凭据方式：" + (subject.IsInlineCredential ? "网址内嵌（密码在网址里）" : "prompt（由资源管理器询问）"));
            output.WriteLine("");
            output.WriteLine("网址（脱敏）：" + masked);
            if (showPassword)
            {
                output.WriteLine("网址（完整）：" + url);
            }
            else
            {
                output.WriteLine("（加 --show-password 可打印含明文密码的完整网址）");
            }

            if (!open)
            {
                output.WriteLine("");
                output.WriteLine("结果：通过（未打开；加 --open 可交给系统默认 FTP 处理器打开）");
                return 0;
            }

            output.WriteLine("");
            if (FtpUrlLauncher.TryOpen(url, masked, out var openError))
            {
                output.WriteLine("结果：已交给系统默认 FTP 处理器打开。");
                return 0;
            }

            output.WriteLine("结果：打开失败");
            output.WriteLine(openError);
            return 2;
        }

        private static Subject? FindSubject(AppConfig config, string? key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return config.Subjects.FirstOrDefault();
            }

            var trimmed = key!.Trim();
            return config.Subjects.FirstOrDefault(s => string.Equals(s.Id, trimmed, StringComparison.OrdinalIgnoreCase))
                   ?? config.Subjects.FirstOrDefault(s => string.Equals(s.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        }
    }
}
