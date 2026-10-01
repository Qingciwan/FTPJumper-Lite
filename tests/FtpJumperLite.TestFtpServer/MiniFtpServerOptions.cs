namespace FtpJumperLite.TestFtpServer
{
    /// <summary>LIST 命令的列表格式（用于覆盖资源管理器/客户端的解析分支）。</summary>
    public enum FtpListingStyle
    {
        /// <summary>UNIX 风格：-rw-r--r-- 1 ftp ftp 1234 Sep 12 20:18 name</summary>
        Unix,

        /// <summary>DOS 风格：09-12-26  08:18PM              1234 name</summary>
        Dos
    }

    /// <summary>测试用 FTP 服务端配置。</summary>
    public sealed class MiniFtpServerOptions
    {
        /// <summary>对外暴露的根目录（必须存在）。</summary>
        public string RootDirectory { get; set; } = "";

        public string User { get; set; } = "user";

        public string Password { get; set; } = "pass";

        /// <summary>监听端口；0 = 自动分配（测试推荐）。</summary>
        public int Port { get; set; }

        /// <summary>是否支持 MLSD（false 时返回 500，用于验证客户端的 LIST 回退）。</summary>
        public bool SupportMlsd { get; set; } = true;

        /// <summary>是否支持 OPTS UTF8 ON（false 时返回 501，列表按 ASCII 输出 —— 模拟只认 GBK 的老服务器）。</summary>
        public bool SupportUtf8 { get; set; } = true;

        /// <summary>LIST 输出格式。</summary>
        public FtpListingStyle ListingStyle { get; set; } = FtpListingStyle.Unix;

        /// <summary>发送欢迎语前的延迟（毫秒），用于验证客户端超时。</summary>
        public int BannerDelayMs { get; set; }

        /// <summary>RETR 限速（KB/s）；0 = 不限速。</summary>
        public int ThrottleKbPerSecond { get; set; }
    }
}
