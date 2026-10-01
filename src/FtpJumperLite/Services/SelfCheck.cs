using System;
using System.IO;

namespace FtpJumperLite.Services
{
    /// <summary>无界面自检入口（供 CI / 排障使用）：只读配置，不访问网络。</summary>
    public static class SelfCheck
    {
        /// <summary>退出码：0=通过；2=配置存在但解析/校验失败；3=使用方式错误。</summary>
        public static int Run(string? configPath, TextWriter output)
        {
            var path = string.IsNullOrWhiteSpace(configPath) ? ConfigStore.DefaultConfigPath : configPath!;
            output.WriteLine("FtpJumper Lite 自检（不访问网络，不会输出密码明文）…");
            output.WriteLine("配置文件：" + path);

            try
            {
                var store = new ConfigStore(path);
                if (!store.TryLoad(out var config, out var error))
                {
                    output.WriteLine("结果：配置存在问题");
                    output.WriteLine(error);
                    return 2;
                }

                output.WriteLine("结果：通过");
                output.WriteLine("共 " + config!.Subjects.Count + " 个科目：");
                foreach (var s in config.Subjects)
                {
                    output.WriteLine("  - " + s.Name + "（id=" + s.Id + "）"
                                     + " 凭据 " + (s.IsInlineCredential ? "网址内嵌" : "由资源管理器询问")
                                     + "\r\n      网址 " + FtpUrlBuilder.BuildMasked(s, path));
                }

                return 0;
            }
            catch (Exception ex)
            {
                output.WriteLine("结果：运行错误");
                output.WriteLine(ex.Message);
                return 3;
            }
        }
    }
}
