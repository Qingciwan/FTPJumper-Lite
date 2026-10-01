using System;
using Newtonsoft.Json;

namespace FtpJumperLite.Models
{
    /// <summary>
    /// 一个科目的 FTP 配置。账号密码按需求以明文形式保存于 subjects.json。
    /// 注意：本类型任何字段的值都不得被写入日志。
    /// </summary>
    public sealed class Subject
    {
        [JsonProperty("id")]
        public string Id { get; set; } = "";

        [JsonProperty("name")]
        public string Name { get; set; } = "";

        [JsonProperty("host")]
        public string Host { get; set; } = "";

        [JsonProperty("port")]
        public int Port { get; set; } = 21;

        [JsonProperty("user")]
        public string User { get; set; } = "";

        [JsonProperty("password")]
        public string Password { get; set; } = "";

        [JsonProperty("remotePath")]
        public string RemotePath { get; set; } = "/";

        /// <summary>凭据传递方式：inline = 账号密码直接写进 ftp:// 网址（默认）；prompt = 网址不带凭据，由资源管理器弹窗询问。</summary>
        [JsonProperty("credentialMode")]
        public string CredentialMode { get; set; } = "inline";

        /// <summary>最近打开时间（程序自动写回，可留空）。</summary>
        [JsonProperty("lastSyncAt")]
        public string LastSyncAt { get; set; } = "";

        /// <summary>是否为“网址内嵌凭据”模式（默认）。计算属性，不写入配置文件。</summary>
        [JsonIgnore]
        public bool IsInlineCredential =>
            !string.Equals((CredentialMode ?? "").Trim(), "prompt", StringComparison.OrdinalIgnoreCase);

        /// <summary>界面辅助显示：主机 · 远端路径。计算属性，不写入配置文件。</summary>
        [JsonIgnore]
        public string DisplayLine
        {
            get
            {
                if (string.IsNullOrWhiteSpace(Host))
                {
                    return "(未配置主机)";
                }

                var path = string.IsNullOrWhiteSpace(RemotePath) ? "/" : RemotePath;
                return Host.Trim() + " · " + path;
            }
        }
    }
}
