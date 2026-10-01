using System;

namespace FtpJumperLite.Services
{
    /// <summary>
    /// FTP 网址无法拼装时抛出（主机非法等）。消息为中文、可直接展示给老师，且不包含密码。
    /// </summary>
    public sealed class FtpUrlException : Exception
    {
        public FtpUrlException(string message, string? configPath = null)
            : base(message)
        {
            ConfigPath = configPath;
        }

        /// <summary>相关配置文件路径（可为空）。</summary>
        public string? ConfigPath { get; }
    }
}
