using System;
using System.Collections.Generic;
using System.Text;
using FtpJumperLite.Models;

namespace FtpJumperLite.Services
{
    /// <summary>
    /// 把科目配置拼成一个经典 FTP 网址：<c>ftp://用户名:密码@主机[:端口]/远端路径/</c>。
    /// 纯静态、无 IO，便于单元测试。
    ///
    /// 编码约定（两条规则分开，尽量贴近 Windows FTP 处理器的解析习惯）：
    /// <list type="bullet">
    /// <item>凭据部分：只做最保守的转义 —— 非 ASCII 字符按 UTF-8 百分号编码，并转义 <c>% / ? # [ ]</c>
    /// 以及会破坏语法的 <c>:</c> <c>@</c>；中文密码依赖 Windows 能解码 %XX。</item>
    /// <item>路径部分：每个路径段独立编码，空格与中文直接保留（资源管理器显示更自然），
    /// 只转义控制字符与 <c>% ? # " &lt; &gt; \ | * :</c>。</item>
    /// </list>
    /// 端口为默认 21 时省略，网址更简短。
    /// </summary>
    public static class FtpUrlBuilder
    {
        /// <summary>把科目配置拼成可直接交给系统的 ftp:// 网址。</summary>
        public static string Build(Subject subject, string? configPath = null)
        {
            return BuildCore(subject, configPath, maskPassword: false);
        }

        /// <summary>同 <see cref="Build"/>，但密码一律显示为 <c>***</c>（用于界面状态栏与日志）。</summary>
        public static string BuildMasked(Subject subject, string? configPath = null)
        {
            return BuildCore(subject, configPath, maskPassword: true);
        }

        private static string BuildCore(Subject subject, string? configPath, bool maskPassword)
        {
            if (subject == null)
            {
                throw new ArgumentNullException(nameof(subject));
            }

            var host = NormalizeHost(subject.Host, configPath);
            var builder = new StringBuilder("ftp://");

            // 凭据：默认内嵌；prompt 模式完全不写（由资源管理器弹窗询问）。
            if (subject.IsInlineCredential)
            {
                var user = EncodeUserInfo(subject.User ?? "");
                var password = maskPassword ? "***" : EncodeUserInfo(subject.Password ?? "");
                if (user.Length > 0 || password.Length > 0)
                {
                    builder.Append(user).Append(':').Append(password).Append('@');
                }
            }

            builder.Append(host);
            if (subject.Port > 0 && subject.Port != 21)
            {
                builder.Append(':').Append(subject.Port.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }

            builder.Append(NormalizeRemotePathAsUrlPath(subject.RemotePath));
            return builder.ToString();
        }

        /// <summary>
        /// 远端路径转 URL 路径：统一斜杠、去掉空段与 "." 段、每段独立编码，末尾补一个 "/"。
        /// 根目录结果为 "/"。
        /// </summary>
        public static string NormalizeRemotePathAsUrlPath(string? remotePath)
        {
            if (string.IsNullOrWhiteSpace(remotePath))
            {
                return "/";
            }

            var raw = remotePath!.Replace('\\', '/');
            var segments = new List<string>();
            foreach (var part in raw.Split('/'))
            {
                if (part.Length == 0 || part == ".")
                {
                    continue;
                }

                segments.Add(EncodePathSegment(part));
            }

            if (segments.Count == 0)
            {
                return "/";
            }

            return "/" + string.Join("/", segments) + "/";
        }

        private static string NormalizeHost(string? host, string? configPath)
        {
            var value = (host ?? "").Trim();
            if (value.Length == 0)
            {
                throw new FtpUrlException("科目未配置主机地址（host）。", configPath);
            }

            foreach (var c in value)
            {
                if (char.IsWhiteSpace(c) || char.IsControl(c) || c == '/' || c == '\\')
                {
                    throw new FtpUrlException(
                        "主机地址 \"" + value + "\" 不合法：请只填主机名或 IP（如 ftp.example.com），不要带 ftp:// 前缀或路径。",
                        configPath);
                }
            }

            // 方括号：只有 IPv6 字面量才允许，且必须成对。
            var bracketed = value[0] == '[';
            var hasColon = value.IndexOf(':') >= 0;
            if (bracketed)
            {
                if (value[value.Length - 1] != ']')
                {
                    throw new FtpUrlException("主机地址 \"" + value + "\" 的方括号不匹配。", configPath);
                }

                if (value.IndexOf('[') != 0 || value.IndexOf(']') != value.Length - 1)
                {
                    throw new FtpUrlException("主机地址 \"" + value + "\" 的方括号位置不正确。", configPath);
                }

                return value;
            }

            if (value.IndexOf(']') >= 0)
            {
                throw new FtpUrlException("主机地址 \"" + value + "\" 的方括号不匹配。", configPath);
            }

            // IPv6 字面量必须加方括号，否则 URL 里的冒号会被当成端口分隔符。
            return hasColon ? "[" + value + "]" : value;
        }

        private static readonly char[] UserInfoAlwaysEncode = { '%', '/', '?', '#', '[', ']', ':', '@' };

        private static string EncodeUserInfo(string value)
        {
            return Encode(value, UserInfoAlwaysEncode, keepSpaceAndUnicodeLiteral: false);
        }

        private static readonly char[] PathAlwaysEncode = { '%', '?', '#', '"', '<', '>', '\\', '|', '*', ':' };

        private static string EncodePathSegment(string value)
        {
            // 路径段：中文与空格保留原样，资源管理器里显示更自然。
            return Encode(value, PathAlwaysEncode, keepSpaceAndUnicodeLiteral: true);
        }

        private static string Encode(string value, char[] alwaysEncode, bool keepSpaceAndUnicodeLiteral)
        {
            var builder = new StringBuilder(value.Length + 8);
            foreach (var c in value)
            {
                if (char.IsControl(c) || Array.IndexOf(alwaysEncode, c) >= 0)
                {
                    AppendEncoded(builder, c);
                    continue;
                }

                if (c > 127)
                {
                    if (keepSpaceAndUnicodeLiteral)
                    {
                        builder.Append(c);
                    }
                    else
                    {
                        AppendEncoded(builder, c);
                    }

                    continue;
                }

                if (c == ' ' && keepSpaceAndUnicodeLiteral)
                {
                    builder.Append(' ');
                    continue;
                }

                builder.Append(c);
            }

            return builder.ToString();
        }

        private static void AppendEncoded(StringBuilder builder, char c)
        {
            var bytes = Encoding.UTF8.GetBytes(new[] { c });
            foreach (var b in bytes)
            {
                builder.Append('%').Append(b.ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }
        }
    }
}
