using System;
using System.Globalization;
using System.IO;
using System.Net.Sockets;
using System.Text;
using FtpJumperLite.Models;
using FtpJumperLite.Services;
using FtpJumperLite.TestFtpServer;
using Xunit;

namespace FtpJumperLite.Tests
{
    /// <summary>
    /// 网址端到端测试：lite 版拼出的 ftp:// 网址，其主机/端口/凭据/路径必须真的能连上并被服务端接受。
    /// 这样“交给资源管理器打开”之前，网址的四个组成部分都已验证过（资源管理器本身在测试里无法断言）。
    /// </summary>
    public sealed class FtpUrlIntegrationTests : IDisposable
    {
        private const string ServerUser = "class";
        private const string ServerPassword = "123456";

        private readonly string _baseDir;
        private readonly string _serverRoot;

        public FtpUrlIntegrationTests()
        {
            _baseDir = Path.Combine(Path.GetTempPath(), "FtpJumperLiteTests", Guid.NewGuid().ToString("N"));
            _serverRoot = Path.Combine(_baseDir, "server");
            Directory.CreateDirectory(_serverRoot);

            WriteServerFile("欢迎使用.txt", "示例");
            WriteServerFile("课件\\第一课 课堂笔记.txt", "第一课");
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_baseDir))
                {
                    Directory.Delete(_baseDir, true);
                }
            }
            catch
            {
                // 清理失败忽略。
            }
        }

        private void WriteServerFile(string relativePath, string content)
        {
            var path = Path.Combine(_serverRoot, relativePath);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        private MiniFtpServer StartServer()
        {
            var server = new MiniFtpServer(new MiniFtpServerOptions
            {
                RootDirectory = _serverRoot,
                User = ServerUser,
                Password = ServerPassword,
                Port = 0
            });
            server.Start();
            return server;
        }

        private static Subject SubjectFor(MiniFtpServer server, string remotePath = "/")
        {
            return new Subject
            {
                Id = "local-test",
                Name = "本机测试",
                Host = "127.0.0.1",
                Port = server.Port,
                User = ServerUser,
                Password = ServerPassword,
                RemotePath = remotePath,
                CredentialMode = "inline",
                LastSyncAt = ""
            };
        }

        [Fact]
        public void GeneratedUrl_TargetsThePortTheServerActuallyListensOn()
        {
            using (var server = StartServer())
            {
                var uri = new Uri(FtpUrlBuilder.Build(SubjectFor(server)));

                Assert.Equal("127.0.0.1", uri.Host);
                Assert.Equal(server.Port, uri.Port);
                Assert.Equal(ServerUser, uri.UserInfo.Split(':')[0]);
                Assert.Equal(ServerPassword, uri.UserInfo.Split(':')[1]);
            }
        }

        [Fact]
        public void ParsedUrl_CredentialsAndPath_AreAcceptedByAServer()
        {
            using (var server = StartServer())
            {
                var uri = new Uri(FtpUrlBuilder.Build(SubjectFor(server)));

                using (var control = new TcpClient())
                {
                    control.Connect(uri.Host, uri.Port);
                    control.ReceiveTimeout = 10000;
                    using (var stream = control.GetStream())
                    using (var reader = new StreamReader(stream, new UTF8Encoding(false)))
                    using (var writer = new StreamWriter(stream, new UTF8Encoding(false)) { NewLine = "\r\n", AutoFlush = true })
                    {
                        Assert.StartsWith("220", ReadReply(reader));

                        var parts = uri.UserInfo.Split(':');
                        writer.WriteLine("USER " + parts[0]);
                        Assert.StartsWith("331", ReadReply(reader));
                        writer.WriteLine("PASS " + parts[1]);
                        Assert.StartsWith("230", ReadReply(reader));

                        // 被动模式：先拿到数据端口并连上，服务端才会进入传输状态。
                        writer.WriteLine("PASV");
                        var pasv = ReadReply(reader);
                        Assert.StartsWith("227", pasv);

                        using (var data = ConnectPassiveDataPort(pasv))
                        {
                            // 用网址里的路径发 MLSD：证明 lite 版拼出的路径是服务端认识的。
                            writer.WriteLine("MLSD " + Uri.UnescapeDataString(uri.AbsolutePath));
                            Assert.StartsWith("150", ReadReply(reader));
                            Assert.StartsWith("226", ReadReply(reader));

                            using (var dataReader = new StreamReader(data.GetStream(), new UTF8Encoding(false)))
                            {
                                var listing = dataReader.ReadToEnd();
                                Assert.Contains("欢迎使用.txt", listing, StringComparison.Ordinal);
                                Assert.Contains("课件", listing, StringComparison.Ordinal);
                            }
                        }
                    }
                }

                Assert.Contains("MLSD", server.Commands);
            }
        }

        private static TcpClient ConnectPassiveDataPort(string pasvReply)
        {
            var open = pasvReply.IndexOf('(');
            var close = pasvReply.IndexOf(')', open + 1);
            Assert.True(open > 0 && close > open, "无法解析 PASV 应答：" + pasvReply);

            var numbers = pasvReply.Substring(open + 1, close - open - 1).Split(',');
            Assert.Equal(6, numbers.Length);

            var host = string.Join(".", numbers, 0, 4);
            var port = (int.Parse(numbers[4], CultureInfo.InvariantCulture) * 256)
                       + int.Parse(numbers[5], CultureInfo.InvariantCulture);

            var data = new TcpClient();
            data.Connect(host, port);
            data.ReceiveTimeout = 10000;
            return data;
        }

        private static string ReadReply(StreamReader reader)
        {
            var line = reader.ReadLine();
            Assert.NotNull(line);
            return line!;
        }
    }
}
