using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

namespace FtpJumperLite.TestFtpServer
{
    /// <summary>单个控制连接的会话处理（顺序处理，一次只服务一个数据连接）。</summary>
    internal sealed class FtpSession
    {
        private readonly MiniFtpServerOptions _options;
        private readonly TcpClient _control;
        private readonly Action<string> _log;
        private readonly Encoding _encoding;
        private StreamReader? _reader;
        private StreamWriter? _writer;
        private TcpListener? _dataListener;
        private string _cwd = "/";
        private bool _userAccepted;
        private bool _authenticated;

        public FtpSession(MiniFtpServerOptions options, TcpClient control, Action<string> log)
        {
            _options = options;
            _control = control;
            _log = log;
            _encoding = new UTF8Encoding(false);
        }

        /// <summary>是否已完成登录（供测试断言）。</summary>
        public bool IsAuthenticated => _authenticated;

        public void Run()
        {
            _control.ReceiveTimeout = 15000;
            _control.SendTimeout = 15000;

            using (var stream = _control.GetStream())
            {
                _reader = new StreamReader(stream, _encoding, false, 4096, true);
                _writer = new StreamWriter(stream, _encoding) { NewLine = "\r\n", AutoFlush = true };

                if (_options.BannerDelayMs > 0)
                {
                    Thread.Sleep(_options.BannerDelayMs);
                }

                Reply("220 MiniFTP test server ready");

                while (true)
                {
                    string? line;
                    try
                    {
                        line = _reader.ReadLine();
                    }
                    catch (IOException)
                    {
                        return;
                    }
                    catch (ObjectDisposedException)
                    {
                        return;
                    }

                    if (line == null)
                    {
                        return;
                    }

                    if (!Handle(line))
                    {
                        return;
                    }
                }
            }
        }

        private bool Handle(string line)
        {
            var space = line.IndexOf(' ');
            var command = (space < 0 ? line : line.Substring(0, space)).ToUpperInvariant();
            var argument = space < 0 ? "" : line.Substring(space + 1).Trim();
            _log(command);

            switch (command)
            {
                case "USER":
                    _userAccepted = string.Equals(argument, _options.User, StringComparison.Ordinal);
                    Reply("331 Password required");
                    return true;

                case "PASS":
                    if (_userAccepted && string.Equals(argument, _options.Password, StringComparison.Ordinal))
                    {
                        _authenticated = true;
                        Reply("230 Logged in");
                    }
                    else
                    {
                        Reply("530 Login incorrect");
                    }

                    return true;

                case "SYST":
                    Reply("215 UNIX Type: L8");
                    return true;

                case "FEAT":
                    ReplyRaw("211-Features:\r\n MLSD\r\n UTF8\r\n211 End");
                    return true;

                case "OPTS":
                    if (_options.SupportUtf8 && argument.StartsWith("UTF8", StringComparison.OrdinalIgnoreCase))
                    {
                        Reply("200 UTF8 enabled");
                    }
                    else
                    {
                        Reply("501 Option not supported");
                    }

                    return true;

                case "TYPE":
                    Reply("200 Type set to I");
                    return true;

                case "PWD":
                    Reply("257 \"" + _cwd + "\"");
                    return true;

                case "NOOP":
                    Reply("200 OK");
                    return true;

                case "CWD":
                    HandleCwd(argument);
                    return true;

                case "PASV":
                    HandlePasv();
                    return true;

                case "MLSD":
                    HandleList(argument, mlsd: true);
                    return true;

                case "LIST":
                    HandleList(argument, mlsd: false);
                    return true;

                case "SIZE":
                    HandleSize(argument);
                    return true;

                case "RETR":
                    HandleRetr(argument);
                    return true;

                case "QUIT":
                    Reply("221 Bye");
                    return false;

                default:
                    Reply("502 Command not implemented");
                    return true;
            }
        }

        private void HandleCwd(string argument)
        {
            if (!TryResolve(argument, out var disk) || !Directory.Exists(disk))
            {
                Reply("550 Not a directory");
                return;
            }

            var relative = disk.Substring(Path.GetFullPath(_options.RootDirectory).TrimEnd(Path.DirectorySeparatorChar).Length)
                .Replace(Path.DirectorySeparatorChar, '/');
            _cwd = relative.Length == 0 ? "/" : "/" + relative.Trim('/');
            Reply("250 Directory changed");
        }

        private void HandlePasv()
        {
            CloseDataListener();
            _dataListener = new TcpListener(IPAddress.Loopback, 0);
            _dataListener.Start();
            var port = ((IPEndPoint)_dataListener.LocalEndpoint).Port;
            Reply("227 Entering Passive Mode (127,0,0,1," + (port / 256) + "," + (port % 256) + ")");
        }

        private void HandleList(string argument, bool mlsd)
        {
            if (mlsd && !_options.SupportMlsd)
            {
                Reply("500 Unknown command");
                return;
            }

            if (!TryResolve(argument, out var disk) || !Directory.Exists(disk))
            {
                Reply("550 Not a directory");
                return;
            }

            using (var data = AcceptDataConnection())
            {
                if (data == null)
                {
                    return;
                }

                Reply("150 Opening data connection");
                var text = mlsd
                    ? FtpListingFormatter.BuildMlsd(disk)
                    : FtpListingFormatter.BuildList(disk, _options.ListingStyle);
                var bytes = _options.SupportUtf8 ? _encoding.GetBytes(text) : Encoding.ASCII.GetBytes(text);
                try
                {
                    var stream = data.GetStream();
                    stream.Write(bytes, 0, bytes.Length);
                    stream.Flush();
                }
                catch (IOException)
                {
                    // 客户端提前断开。
                }

                Reply("226 Transfer complete");
            }
        }

        private void HandleSize(string argument)
        {
            if (!TryResolve(argument, out var disk) || !File.Exists(disk))
            {
                Reply("550 File not found");
                return;
            }

            Reply("213 " + new FileInfo(disk).Length);
        }

        private void HandleRetr(string argument)
        {
            if (!TryResolve(argument, out var disk) || !File.Exists(disk))
            {
                Reply("550 Failed to open file");
                return;
            }

            using (var data = AcceptDataConnection())
            {
                if (data == null)
                {
                    return;
                }

                Reply("150 Opening data connection");
                try
                {
                    var stream = data.GetStream();
                    using (var file = File.OpenRead(disk))
                    {
                        var buffer = new byte[32768];
                        int read;
                        while ((read = file.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            stream.Write(buffer, 0, read);
                            Throttle(read);
                        }
                    }

                    stream.Flush();
                }
                catch (IOException)
                {
                    // 客户端取消/断开：结束本次传输即可。
                }

                Reply("226 Transfer complete");
            }
        }

        private TcpClient? AcceptDataConnection()
        {
            if (_dataListener == null)
            {
                Reply("425 Use PASV first");
                return null;
            }

            try
            {
                var client = _dataListener.AcceptTcpClient();
                client.SendTimeout = 15000;
                return client;
            }
            catch (SocketException)
            {
                Reply("425 Cannot open data connection");
                return null;
            }
            catch (ObjectDisposedException)
            {
                return null;
            }
            finally
            {
                CloseDataListener();
            }
        }

        private void Throttle(int byteCount)
        {
            if (_options.ThrottleKbPerSecond <= 0)
            {
                return;
            }

            var milliseconds = (int)Math.Round(byteCount / (double)(_options.ThrottleKbPerSecond * 1024) * 1000);
            if (milliseconds > 0)
            {
                Thread.Sleep(milliseconds);
            }
        }

        private bool TryResolve(string? argument, out string diskPath)
        {
            diskPath = "";
            var raw = (argument ?? "").Trim().Trim('"');
            if (raw.Length == 0)
            {
                raw = _cwd;
            }

            if (!raw.StartsWith("/", StringComparison.Ordinal))
            {
                raw = (_cwd == "/" ? "" : _cwd) + "/" + raw;
            }

            var rootFull = Path.GetFullPath(_options.RootDirectory).TrimEnd(Path.DirectorySeparatorChar);
            var combined = rootFull;
            foreach (var part in raw.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries))
            {
                if (part == ".")
                {
                    continue;
                }

                if (part == "..")
                {
                    return false;
                }

                combined = Path.Combine(combined, part);
            }

            var full = Path.GetFullPath(combined);
            if (!string.Equals(full, rootFull, StringComparison.OrdinalIgnoreCase) &&
                !full.StartsWith(rootFull + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            diskPath = full;
            return true;
        }

        private void Reply(string text)
        {
            try
            {
                _writer?.WriteLine(text);
            }
            catch (IOException)
            {
                // 控制连接已断开。
            }
            catch (ObjectDisposedException)
            {
                // 同上。
            }
        }

        private void ReplyRaw(string text)
        {
            try
            {
                _writer?.Write(text);
                _writer?.Write("\r\n");
            }
            catch (IOException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        private void CloseDataListener()
        {
            try
            {
                _dataListener?.Stop();
            }
            catch
            {
                // 忽略。
            }

            _dataListener = null;
        }
    }
}
