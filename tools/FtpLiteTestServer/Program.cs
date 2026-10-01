using System;
using System.Globalization;
using System.IO;
using System.Text;
using FtpJumperLite.TestFtpServer;

namespace FtpJumperLite.Tools.FtpLiteTestServer
{
    /// <summary>
    /// 本地测试 FTP 服务器（仅回环地址、明文、被动模式），用于验证 lite 版拼出的 ftp:// 网址
    /// 能否被 Windows 文件资源管理器正常浏览（列目录、中文名、进入子目录）。
    /// lite 版本程序本身不下载文件，所以这里的重点是 **LIST/MLSD 能被资源管理器读懂**。
    /// </summary>
    internal static class Program
    {
        private static readonly object LogGate = new object();

        private static int Main(string[] args)
        {
            try
            {
                Console.OutputEncoding = new UTF8Encoding(false);
            }
            catch
            {
                // 控制台编码设置失败不影响运行。
            }

            string root = "";
            var port = 2121;
            var user = "class";
            var password = "123456";
            var noMlsd = false;
            var seed = false;
            var headless = false;
            var supportUtf8 = true;
            var style = FtpListingStyle.Unix;
            var throttleKbps = 0;

            for (var i = 0; i < args.Length; i++)
            {
                var arg = args[i];
                switch (arg)
                {
                    case "--root":
                        if (i + 1 < args.Length)
                        {
                            root = args[++i];
                        }

                        break;
                    case "--port":
                        if (i + 1 < args.Length && int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPort))
                        {
                            port = parsedPort;
                        }

                        break;
                    case "--user":
                        if (i + 1 < args.Length)
                        {
                            user = args[++i];
                        }

                        break;
                    case "--password":
                        if (i + 1 < args.Length)
                        {
                            password = args[++i];
                        }

                        break;
                    case "--list-mode":
                        if (i + 1 < args.Length)
                        {
                            style = string.Equals(args[++i], "dos", StringComparison.OrdinalIgnoreCase)
                                ? FtpListingStyle.Dos
                                : FtpListingStyle.Unix;
                        }

                        break;
                    case "--throttle-kbps":
                        if (i + 1 < args.Length && int.TryParse(args[++i], NumberStyles.Integer, CultureInfo.InvariantCulture, out var kbps))
                        {
                            throttleKbps = kbps;
                        }

                        break;
                    case "--no-mlsd":
                        noMlsd = true;
                        break;
                    case "--gbk":
                        // 模拟不认 OPTS UTF8 的老服务器：列表按 ASCII 输出。
                        supportUtf8 = false;
                        break;
                    case "--utf8":
                        supportUtf8 = true;
                        break;
                    case "--seed":
                        seed = true;
                        break;
                    case "--headless":
                        headless = true;
                        break;
                    case "--help":
                    case "-h":
                        PrintUsage();
                        return 0;
                }
            }

            if (string.IsNullOrWhiteSpace(root))
            {
                root = Path.Combine(AppContext.BaseDirectory, "root");
            }

            Directory.CreateDirectory(root);
            if (seed || IsEffectivelyEmpty(root))
            {
                SeedSampleContent(root);
                Log("已生成示例资料到：" + root);
            }

            var options = new MiniFtpServerOptions
            {
                RootDirectory = root,
                User = user,
                Password = password,
                Port = port,
                SupportMlsd = !noMlsd,
                SupportUtf8 = supportUtf8,
                ListingStyle = style,
                ThrottleKbPerSecond = throttleKbps
            };

            using (var server = new MiniFtpServer(options))
            {
                server.ConnectionOpened += address => Log("新连接：" + address);
                server.CommandReceived += command => Log("  收到命令 " + command);

                try
                {
                    server.Start();
                }
                catch (Exception ex)
                {
                    Console.WriteLine("启动失败：" + ex.Message);
                    Console.WriteLine("（端口 " + port + " 可能已被占用，可用 --port 换一个端口）");
                    return 2;
                }

                if (headless)
                {
                    Console.WriteLine("已启动（headless 模式）：127.0.0.1:" + server.Port + "，结束进程即可停止。");
                    using (var stop = new System.Threading.ManualResetEventSlim(false))
                    {
                        Console.CancelKeyPress += (_, e) =>
                        {
                            e.Cancel = true;
                            stop.Set();
                        };
                        stop.Wait();
                    }
                }
                else
                {
                    PrintBanner(server, root, noMlsd, supportUtf8, style);
                    Console.WriteLine("按 Enter 停止服务。");
                    Console.ReadLine();
                }
            }

            Console.WriteLine("服务已停止。");
            return 0;
        }

        private static bool IsEffectivelyEmpty(string directory)
        {
            return Directory.GetFiles(directory).Length == 0 && Directory.GetDirectories(directory).Length == 0;
        }

        private static void SeedSampleContent(string root)
        {
            Write(root, "欢迎使用.txt", "这是 FtpJumper Lite 本地测试服务器的示例文件。\r\n可以随意修改或删除。\r\n");
            Write(root, "成绩表.csv", "姓名,分数\r\n张三,95\r\n李四,88\r\n");
            Write(root, "课件\\第一课 课堂笔记.txt", "第一课：示例课堂笔记。\r\n");
            Write(root, "课件\\第二课 练习.txt", "第二课：示例练习。\r\n");
            Write(root, "图片\\说明.txt", "图片目录说明。\r\n");
        }

        private static void Write(string root, string relativePath, string content)
        {
            var path = Path.Combine(root, relativePath);
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(path, content, new UTF8Encoding(false));
        }

        private static void PrintBanner(MiniFtpServer server, string root, bool noMlsd, bool supportUtf8, FtpListingStyle style)
        {
            Console.WriteLine();
            Console.WriteLine("=== FtpJumper Lite 本地测试 FTP 服务器 ===");
            Console.WriteLine("监听地址 ：127.0.0.1:" + server.Port + "（仅本机可访问）");
            Console.WriteLine("用户名   ：" + server.User);
            Console.WriteLine("密码     ：" + server.Password);
            Console.WriteLine("根目录   ：" + root);
            Console.WriteLine("列表格式 ：" + (noMlsd ? "LIST(" + style + ")" : "MLSD")
                              + (supportUtf8 ? " + UTF8" : " + ASCII(不认 OPTS UTF8)"));
            Console.WriteLine();
            Console.WriteLine("把下面这段加到 subjects.json 的 subjects 数组里（注意前一条末尾要有逗号）：");
            Console.WriteLine();
            Console.WriteLine("    {");
            Console.WriteLine("      \"id\": \"local-test\",");
            Console.WriteLine("      \"name\": \"本机测试\",");
            Console.WriteLine("      \"host\": \"127.0.0.1\",");
            Console.WriteLine("      \"port\": " + server.Port + ",");
            Console.WriteLine("      \"user\": \"" + server.User + "\",");
            Console.WriteLine("      \"password\": \"" + server.Password + "\",");
            Console.WriteLine("      \"remotePath\": \"/\",");
            Console.WriteLine("      \"credentialMode\": \"inline\",");
            Console.WriteLine("      \"lastSyncAt\": \"\"");
            Console.WriteLine("    }");
            Console.WriteLine();
            Console.WriteLine("启动后也可以在浏览器/资源管理器地址栏直接试：");
            Console.WriteLine("    ftp://" + server.User + ":" + server.Password + "@127.0.0.1:" + server.Port + "/");
            Console.WriteLine();
            Console.WriteLine("提示：在 FtpJumper Lite 里点「刷新配置」后单击「本机测试」，应自动弹出资源管理器并列出示例资料。");
            Console.WriteLine();
        }

        private static void Log(string message)
        {
            lock (LogGate)
            {
                Console.WriteLine("[" + DateTime.Now.ToString("HH:mm:ss", CultureInfo.InvariantCulture) + "] " + message);
            }
        }

        private static void PrintUsage()
        {
            Console.WriteLine("用法：FtpLiteTestServer.exe [选项]");
            Console.WriteLine("  --root <目录>      对外暴露的根目录（默认 exe 同级 root 目录）");
            Console.WriteLine("  --port <端口>      监听端口（默认 2121，0 = 自动分配）");
            Console.WriteLine("  --user <用户名>    登录用户名（默认 class）");
            Console.WriteLine("  --password <密码>  登录密码（默认 123456）");
            Console.WriteLine("  --list-mode <unix|dos>  LIST 列表格式（默认 unix）");
            Console.WriteLine("  --no-mlsd          禁用 MLSD，只提供 LIST");
            Console.WriteLine("  --gbk              不认 OPTS UTF8（列表按 ASCII，模拟老服务器）");
            Console.WriteLine("  --throttle-kbps <N> RETR 限速（KB/s）");
            Console.WriteLine("  --seed             重新生成示例资料");
            Console.WriteLine("  --headless         不打印横幅/不等待 Enter（供脚本自动化使用）");
        }
    }
}
