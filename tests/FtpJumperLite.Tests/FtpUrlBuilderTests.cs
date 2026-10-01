using System;
using FtpJumperLite.Models;
using FtpJumperLite.Services;
using Xunit;

namespace FtpJumperLite.Tests
{
    /// <summary>
    /// FTP 网址拼装测试（lite 版的核心逻辑）：格式、编码、凭据模式、脱敏与非法主机。
    /// </summary>
    public sealed class FtpUrlBuilderTests
    {
        private static Subject MakeSubject(
            string host = "ftp.example.com",
            int port = 21,
            string user = "yuwen",
            string password = "123456",
            string remotePath = "/",
            string credentialMode = "inline",
            string id = "s1",
            string name = "语文")
        {
            return new Subject
            {
                Id = id,
                Name = name,
                Host = host,
                Port = port,
                User = user,
                Password = password,
                RemotePath = remotePath,
                CredentialMode = credentialMode,
                LastSyncAt = ""
            };
        }

        [Fact]
        public void DefaultPort_IsOmitted_AndRootPathGetsTrailingSlash()
        {
            var url = FtpUrlBuilder.Build(MakeSubject());

            Assert.Equal("ftp://yuwen:123456@ftp.example.com/", url);
        }

        [Fact]
        public void NonDefaultPort_IsIncluded()
        {
            var url = FtpUrlBuilder.Build(MakeSubject(port: 2121));

            Assert.Equal("ftp://yuwen:123456@ftp.example.com:2121/", url);
        }

        [Fact]
        public void RemotePath_KeepsChineseAndSpaces_AndNormalizesSlashes()
        {
            var url = FtpUrlBuilder.Build(MakeSubject(remotePath: "\\pub\\课件 目录\\"));

            Assert.Equal("ftp://yuwen:123456@ftp.example.com/pub/课件 目录/", url);
        }

        [Fact]
        public void RemotePath_DropsDotSegments()
        {
            var url = FtpUrlBuilder.Build(MakeSubject(remotePath: "/pub/./yuwen"));

            Assert.Equal("ftp://yuwen:123456@ftp.example.com/pub/yuwen/", url);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("/")]
        [InlineData(null)]
        public void EmptyRemotePath_MeansSiteRoot(string? remotePath)
        {
            // remotePath 留空 / 纯空格 / 省略 / 根斜杠，都等价于 FTP 根目录。
            var url = FtpUrlBuilder.Build(MakeSubject(remotePath: remotePath!));

            Assert.Equal("ftp://yuwen:123456@ftp.example.com/", url);
        }

        [Fact]
        public void SpecialCharactersInPassword_AreEscaped()
        {
            var url = FtpUrlBuilder.Build(MakeSubject(password: "P@ss w0rd!"));

            Assert.Equal("ftp://yuwen:P%40ss w0rd!@ftp.example.com/", url);
        }

        [Fact]
        public void ChinesePassword_IsPercentEncodedAsUtf8()
        {
            var url = FtpUrlBuilder.Build(MakeSubject(password: "中文"));

            Assert.Equal("ftp://yuwen:%E4%B8%AD%E6%96%87@ftp.example.com/", url);
        }

        [Fact]
        public void PromptMode_OmitsCredentialsEntirely()
        {
            var url = FtpUrlBuilder.Build(MakeSubject(credentialMode: "prompt"));

            Assert.Equal("ftp://ftp.example.com/", url);
        }

        [Fact]
        public void Ipv6Host_IsBracketed()
        {
            var url = FtpUrlBuilder.Build(MakeSubject(host: "::1"));

            Assert.Equal("ftp://yuwen:123456@[::1]/", url);
        }

        [Fact]
        public void AlreadyBracketedIpv6Host_IsKeptAsIs()
        {
            var url = FtpUrlBuilder.Build(MakeSubject(host: "[2001:db8::1]"));

            Assert.Equal("ftp://yuwen:123456@[2001:db8::1]/", url);
        }

        [Fact]
        public void Masked_HidesPasswordOnly()
        {
            var subject = MakeSubject(password: "P@ss w0rd!", remotePath: "/pub/yuwen");

            var full = FtpUrlBuilder.Build(subject);
            var masked = FtpUrlBuilder.BuildMasked(subject);

            Assert.Equal("ftp://yuwen:***@ftp.example.com/pub/yuwen/", masked);
            Assert.DoesNotContain("P%40ss", masked, StringComparison.Ordinal);
            // 除密码段外，两者完全一致。
            Assert.Equal("ftp://yuwen:P%40ss w0rd!@ftp.example.com/pub/yuwen/", full);
        }

        [Fact]
        public void GeneratedUrl_IsParsedBackBySystemUri()
        {
            var subject = MakeSubject(host: "127.0.0.1", port: 2121, remotePath: "/课件");

            var uri = new Uri(FtpUrlBuilder.Build(subject));

            Assert.Equal("ftp", uri.Scheme);
            Assert.Equal("127.0.0.1", uri.Host);
            Assert.Equal(2121, uri.Port);
            Assert.Equal("/课件/", Uri.UnescapeDataString(uri.AbsolutePath));
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("ftp://ftp.example.com")]
        [InlineData("ftp.example.com/pub")]
        [InlineData("host name")]
        [InlineData("[::1")]
        [InlineData("::1]")]
        [InlineData("[::1]]")]
        public void InvalidHost_ThrowsFtpUrlException(string host)
        {
            var subject = MakeSubject(host: host);

            Assert.Throws<FtpUrlException>(() => FtpUrlBuilder.Build(subject));
        }

        [Fact]
        public void AnonymousSubject_StillProducesUsableUrl()
        {
            var url = FtpUrlBuilder.Build(MakeSubject(user: "", password: ""));

            Assert.Equal("ftp://ftp.example.com/", url);
        }

        [Fact]
        public void Launcher_RejectsEmptyUrl_WithoutStartingAnything()
        {
            // 只测试“空网址”这条纯逻辑分支：不会真的拉起进程。
            var opened = FtpUrlLauncher.TryOpen("", "ftp://user:***@host/", out var error);

            Assert.False(opened);
            Assert.Contains("网址为空", error);
        }
    }
}
