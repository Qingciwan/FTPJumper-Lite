using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using FtpJumperLite.Models;
using Newtonsoft.Json;

namespace FtpJumperLite.Services
{
    /// <summary>
    /// subjects.json 的读写与校验。
    /// 账号密码按需求明文存储；本类型不写日志、不输出任何含密码的内容。
    ///
    /// 本程序不做同步，因此 <c>passive / encoding / timeoutSec / syncMode / localDir</c> 这些历史字段
    /// 不再使用（旧配置里存在也能正常加载，写回时会被自然丢弃）。
    /// </summary>
    public sealed class ConfigStore
    {
        private const int SupportedVersionMin = 1;
        private const int SupportedVersionMax = 2;

        /// <summary>配置文件默认位置：与 exe 同目录。</summary>
        public static string DefaultConfigPath => Path.Combine(AppContext.BaseDirectory, "subjects.json");

        public ConfigStore(string configPath)
        {
            ConfigPath = configPath ?? throw new ArgumentNullException(nameof(configPath));
        }

        public string ConfigPath { get; }

        /// <summary>默认本地目录（lite 版并不下载，仅作提示）：应用目录下 data\&lt;id&gt;。</summary>
        public static string ResolveLocalRoot(Subject subject)
        {
            if (subject == null)
            {
                throw new ArgumentNullException(nameof(subject));
            }

            return Path.Combine(AppPaths.DataRoot, SanitizeId(subject.Id));
        }

        /// <summary>
        /// 加载配置。文件缺失时自动创建示例配置并返回；解析或校验失败返回中文错误信息。
        /// </summary>
        public bool TryLoad(out AppConfig? config, out string? error)
        {
            config = null;
            error = null;
            try
            {
                if (!File.Exists(ConfigPath))
                {
                    AppLogger.Info("配置文件不存在，将创建示例文件：" + ConfigPath);
                    var sample = DefaultSample();
                    WriteFileAtomic(sample);
                    config = sample;
                    return true;
                }

                string text;
                try
                {
                    text = File.ReadAllText(ConfigPath, Encoding.UTF8);
                }
                catch (Exception ex) when (IsIoError(ex))
                {
                    error = "无法读取配置文件：" + ex.Message;
                    return false;
                }

                if (string.IsNullOrWhiteSpace(text))
                {
                    error = "配置文件为空。请打开配置目录检查 subjects.json。";
                    return false;
                }

                AppConfig parsed;
                try
                {
                    parsed = JsonConvert.DeserializeObject<AppConfig>(text)
                             ?? throw new JsonException("文件中没有找到科目配置对象。");
                }
                catch (JsonReaderException ex)
                {
                    error = string.Format(
                        CultureInfo.InvariantCulture,
                        "配置文件 JSON 解析失败（第 {0} 行，第 {1} 列）：{2}",
                        ex.LineNumber,
                        ex.LinePosition,
                        ex.Message);
                    return false;
                }
                catch (JsonException ex)
                {
                    error = "配置文件格式不正确：" + ex.Message;
                    return false;
                }

                var problems = ValidateAll(parsed);
                if (problems.Count > 0)
                {
                    error = "配置文件存在问题：\r\n" + string.Join("\r\n", problems);
                    return false;
                }

                config = parsed;
                return true;
            }
            catch (Exception ex) when (IsIoError(ex))
            {
                error = "读取配置文件失败：" + ex.Message;
                return false;
            }
        }

        /// <summary>整份配置的字段校验，返回中文问题列表（空 = 通过）。</summary>
        public static List<string> ValidateAll(AppConfig? config, string? configPath = null)
        {
            var problems = new List<string>();
            if (config == null)
            {
                problems.Add("配置为空。");
                return problems;
            }

            if (config.Version < SupportedVersionMin || config.Version > SupportedVersionMax)
            {
                problems.Add(string.Format(
                    CultureInfo.InvariantCulture,
                    "配置文件 version={0} 不受支持（应为 1~{1}）。",
                    config.Version,
                    SupportedVersionMax));
            }

            if (config.Subjects == null || config.Subjects.Count == 0)
            {
                problems.Add("subjects 为空：请至少配置一个科目。");
                return problems;
            }

            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (var i = 0; i < config.Subjects.Count; i++)
            {
                var s = config.Subjects[i];
                var label = "科目[" + (i + 1) + "]";
                var hasProblem = false;

                if (string.IsNullOrWhiteSpace(s.Id))
                {
                    problems.Add(label + " 缺少 id。");
                    hasProblem = true;
                }
                else
                {
                    if (s.Id.Length > 64 || !IsValidId(s.Id))
                    {
                        problems.Add(label + " 的 id=\"" + s.Id + "\" 只能包含字母/数字/-/_，且长度不超过 64。");
                        hasProblem = true;
                    }

                    if (!seenIds.Add(s.Id))
                    {
                        problems.Add(label + " 的 id=\"" + s.Id + "\" 与其它科目重复。");
                        hasProblem = true;
                    }
                }

                if (string.IsNullOrWhiteSpace(s.Name))
                {
                    problems.Add(label + " 缺少名称（name）。");
                    hasProblem = true;
                }
                else if (s.Name.Length > 40)
                {
                    problems.Add(label + " 名称过长（最多 40 个字符）。");
                    hasProblem = true;
                }

                if (string.IsNullOrWhiteSpace(s.Host))
                {
                    problems.Add(label + " 缺少主机地址（host）。");
                    hasProblem = true;
                }
                else
                {
                    var host = s.Host.Trim();
                    if (host.IndexOfAny(new[] { ' ', '\t', '/' }) >= 0 || host.IndexOf("://", StringComparison.Ordinal) >= 0)
                    {
                        problems.Add(label + " 的 host=\"" + host + "\" 不合法：请只填主机名或 IP（如 ftp.example.com），不要带 ftp:// 前缀。");
                        hasProblem = true;
                    }
                    else if (host.IndexOf(':') >= 0 && host[0] != '[')
                    {
                        problems.Add(label + " 的 host=\"" + host + "\" 含冒号：IPv6 请写成 [::1] 形式，端口请填在 port 字段。");
                        hasProblem = true;
                    }
                }

                if (s.Port < 1 || s.Port > 65535)
                {
                    problems.Add(label + " 的 port=" + s.Port.ToString(CultureInfo.InvariantCulture) + " 不合法（应为 1~65535）。");
                    hasProblem = true;
                }

                if (s.User != null && s.User.Length > 64)
                {
                    problems.Add(label + " 的用户名过长（最多 64 个字符）。");
                    hasProblem = true;
                }

                if (!string.IsNullOrWhiteSpace(s.RemotePath))
                {
                    var path = s.RemotePath.Replace('\\', '/');
                    if (!path.StartsWith("/", StringComparison.Ordinal))
                    {
                        problems.Add(label + " 的 remotePath 必须以 / 开头（如 /pub/语文）。");
                        hasProblem = true;
                    }
                    else if (SplitRemote(path).Any(p => p == ".."))
                    {
                        problems.Add(label + " 的 remotePath 不允许包含 .. 段。");
                        hasProblem = true;
                    }
                }

                var mode = (s.CredentialMode ?? "inline").Trim().ToLowerInvariant();
                if (mode != "inline" && mode != "prompt")
                {
                    problems.Add(label + " 的 credentialMode 仅支持 inline（网址带账号密码）或 prompt（由资源管理器询问密码）。");
                    hasProblem = true;
                }

                // 最后兜底：真正拼一次网址，确保该科目一定能生成可用的 ftp:// 地址。
                if (!hasProblem)
                {
                    try
                    {
                        _ = FtpUrlBuilder.Build(s, configPath);
                    }
                    catch (FtpUrlException ex)
                    {
                        problems.Add(label + "：" + ex.Message);
                    }
                    catch (Exception ex)
                    {
                        problems.Add(label + " 无法生成 FTP 网址：" + ex.Message);
                    }
                }
            }

            return problems;
        }

        /// <summary>把“最近打开时间”写回配置（先重读磁盘再合并，避免覆盖教师的外部修改）。</summary>
        public bool UpdateLastOpenUtc(string subjectId, DateTime localTime)
        {
            if (!TryLoad(out var config, out var error))
            {
                AppLogger.Warn("打开完成后无法更新配置：" + error);
                return false;
            }

            var subject = config!.Subjects.FirstOrDefault(x => x.Id == subjectId);
            if (subject == null)
            {
                AppLogger.Warn("打开完成后找不到科目 id=" + subjectId + "，跳过更新时间。");
                return false;
            }

            subject.LastSyncAt = localTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            try
            {
                WriteFileAtomic(config);
                AppLogger.Info("已更新科目 " + subjectId + " 的最近打开时间。");
                return true;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("写回打开时间失败：" + ex.Message);
                return false;
            }
        }

        private static IEnumerable<string> SplitRemote(string path)
        {
            return path.Replace('\\', '/')
                .Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static bool IsValidId(string id)
        {
            foreach (var c in id)
            {
                var ok = (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')
                         || c == '-' || c == '_';
                if (!ok)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>把一个自由文本转为可用于本地目录名的安全片段。</summary>
        public static string SanitizeId(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return "subject";
            }

            var chars = new List<char>(text!.Length);
            foreach (var c in text)
            {
                var ok = (c >= '0' && c <= '9') || (c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z')
                         || c == '-' || c == '_';
                if (ok)
                {
                    chars.Add(c);
                }
            }

            var result = new string(chars.ToArray());
            if (result.Length == 0)
            {
                return "subject";
            }

            return result.Length > 64 ? result.Substring(0, 64) : result;
        }

        private static bool IsIoError(Exception ex)
        {
            return ex is IOException || ex is UnauthorizedAccessException || ex is System.Security.SecurityException;
        }

        private static AppConfig DefaultSample()
        {
            var config = new AppConfig();
            config.Subjects.Add(new Subject
            {
                Id = "sample-yuwen",
                Name = "示例：语文课堂资料",
                Host = "ftp.example.com",
                Port = 21,
                User = "请替换用户名",
                Password = "请替换密码",
                RemotePath = "/",
                CredentialMode = "inline",
                LastSyncAt = ""
            });
            return config;
        }

        private void WriteFileAtomic(AppConfig config)
        {
            BackupExisting();
            var json = JsonConvert.SerializeObject(config, Formatting.Indented);
            var tmp = ConfigPath + ".tmp";
            File.WriteAllText(tmp, json, new UTF8Encoding(false));
            if (File.Exists(ConfigPath))
            {
                File.Delete(ConfigPath);
            }

            File.Move(tmp, ConfigPath);
        }

        private void BackupExisting()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    File.Copy(ConfigPath, ConfigPath + ".bak", true);
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("备份配置文件失败（不影响本次写入）：" + ex.Message);
            }
        }
    }
}
