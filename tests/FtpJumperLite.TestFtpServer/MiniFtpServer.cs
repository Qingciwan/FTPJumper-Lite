using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace FtpJumperLite.TestFtpServer
{
    /// <summary>
    /// 测试用极简 FTP 服务端：仅被动模式（PASV）+ 明文登录，仅监听回环地址。
    /// 支持 USER / PASS / SYST / FEAT / OPTS / TYPE / PWD / NOOP / CWD / PASV / MLSD / LIST / SIZE / RETR / QUIT。
    /// 用途：让 Windows 文件资源管理器（或任何 FTP 客户端）对着它浏览 lite 版拼出来的 ftp:// 网址。
    /// </summary>
    public sealed class MiniFtpServer : IDisposable
    {
        private readonly MiniFtpServerOptions _options;
        private readonly List<string> _commands = new List<string>();
        private readonly object _logGate = new object();
        private TcpListener? _listener;
        private Thread? _acceptThread;
        private volatile bool _stopping;
        private int _connectionCount;

        public MiniFtpServer(MiniFtpServerOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            if (string.IsNullOrWhiteSpace(options.RootDirectory))
            {
                throw new ArgumentException("RootDirectory 不能为空。", nameof(options));
            }

            if (!Directory.Exists(options.RootDirectory))
            {
                throw new DirectoryNotFoundException("根目录不存在：" + options.RootDirectory);
            }
        }

        /// <summary>实际监听端口（Start 之后有效；配置 Port=0 时为系统自动分配的端口）。</summary>
        public int Port { get; private set; }

        public string RootDirectory => _options.RootDirectory;

        public string User => _options.User;

        public string Password => _options.Password;

        /// <summary>已收到的命令序列（用于断言客户端行为）。</summary>
        public IReadOnlyList<string> Commands
        {
            get
            {
                lock (_logGate)
                {
                    return _commands.ToArray();
                }
            }
        }

        public int ConnectionCount => Volatile.Read(ref _connectionCount);

        /// <summary>收到命令时触发（参数为命令名，如 USER/MLSD/RETR）。</summary>
        public event Action<string>? CommandReceived;

        /// <summary>有新连接时触发（参数为远端地址）。</summary>
        public event Action<string>? ConnectionOpened;

        public void Start()
        {
            if (_listener != null)
            {
                throw new InvalidOperationException("服务已启动。");
            }

            _listener = new TcpListener(IPAddress.Loopback, _options.Port);
            _listener.Start();
            Port = ((IPEndPoint)_listener.LocalEndpoint).Port;

            _acceptThread = new Thread(AcceptLoop)
            {
                IsBackground = true,
                Name = "MiniFtpServer.Accept"
            };
            _acceptThread.Start();
        }

        public void ClearCommandLog()
        {
            lock (_logGate)
            {
                _commands.Clear();
            }
        }

        public void Dispose()
        {
            _stopping = true;
            try
            {
                _listener?.Stop();
            }
            catch
            {
                // 关闭监听失败可忽略。
            }

            _listener = null;
        }

        private void AcceptLoop()
        {
            while (!_stopping)
            {
                TcpClient client;
                try
                {
                    client = _listener!.AcceptTcpClient();
                }
                catch (SocketException)
                {
                    if (_stopping)
                    {
                        return;
                    }

                    continue;
                }
                catch (ObjectDisposedException)
                {
                    return;
                }
                catch (InvalidOperationException)
                {
                    return;
                }

                Interlocked.Increment(ref _connectionCount);
                ConnectionOpened?.Invoke(client.Client.RemoteEndPoint?.ToString() ?? "未知地址");
                var session = new FtpSession(_options, client, LogCommand);
                var thread = new Thread(() =>
                {
                    try
                    {
                        session.Run();
                    }
                    catch
                    {
                        // 会话异常（多为客户端中断）不影响其它会话。
                    }
                    finally
                    {
                        try
                        {
                            client.Close();
                        }
                        catch
                        {
                            // 关闭失败可忽略。
                        }
                    }
                })
                {
                    IsBackground = true,
                    Name = "MiniFtpServer.Session"
                };
                thread.Start();
            }
        }

        private void LogCommand(string command)
        {
            lock (_logGate)
            {
                _commands.Add(command);
            }

            CommandReceived?.Invoke(command);
        }
    }
}
