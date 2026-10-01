using System;
using System.IO;
using FtpJumperLite.Models;
using FtpJumperLite.Services;
using Newtonsoft.Json;
using Xunit;

namespace FtpJumperLite.Tests
{
    /// <summary>配置读写与校验测试。</summary>
    public sealed class ConfigStoreTests : IDisposable
    {
        private readonly string _dir;

        public ConfigStoreTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "FtpJumperLiteTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_dir))
                {
                    Directory.Delete(_dir, true);
                }
            }
            catch
            {
                // 清理失败忽略。
            }
        }

        private string CfgPath(string name = "subjects.json")
        {
            return Path.Combine(_dir, name);
        }

        private static string Serialize(AppConfig config)
        {
            return JsonConvert.SerializeObject(config, Formatting.Indented);
        }

        private static AppConfig MakeConfig(params Subject[] subjects)
        {
            var config = new AppConfig { Version = 2 };
            config.Subjects.AddRange(subjects);
            return config;
        }

        private static Subject MakeSubject(
            string id = "s1",
            string name = "语文",
            string host = "ftp.test.local",
            string remotePath = "/",
            string password = "P@ss w0rd!",
            string credentialMode = "inline")
        {
            return new Subject
            {
                Id = id,
                Name = name,
                Host = host,
                Port = 21,
                User = "u",
                Password = password,
                RemotePath = remotePath,
                CredentialMode = credentialMode,
                LastSyncAt = ""
            };
        }

        [Fact]
        public void MissingFile_CreatesSampleAndLoads()
        {
            var store = new ConfigStore(CfgPath());
            var ok = store.TryLoad(out var config, out var error);

            Assert.True(ok, error);
            Assert.NotNull(config);
            Assert.True(config!.Subjects.Count >= 1);
            Assert.True(File.Exists(CfgPath()));
        }

        [Fact]
        public void InvalidJson_ReturnsChineseError()
        {
            File.WriteAllText(CfgPath(), "{ this is not json");
            var store = new ConfigStore(CfgPath());

            var ok = store.TryLoad(out _, out var error);

            Assert.False(ok);
            Assert.Contains("JSON", error, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void EmptySubjects_ReturnsError()
        {
            File.WriteAllText(CfgPath(), Serialize(MakeConfig()));
            var store = new ConfigStore(CfgPath());

            var ok = store.TryLoad(out _, out var error);

            Assert.False(ok);
            Assert.Contains("为空", error);
        }

        [Fact]
        public void DuplicateId_ReturnsError()
        {
            File.WriteAllText(CfgPath(), Serialize(MakeConfig(MakeSubject("s1"), MakeSubject("s1", "数学"))));
            var store = new ConfigStore(CfgPath());

            var ok = store.TryLoad(out _, out var error);

            Assert.False(ok);
            Assert.Contains("重复", error);
        }

        [Fact]
        public void MissingName_ReturnsError()
        {
            var s = MakeSubject();
            s.Name = "";
            File.WriteAllText(CfgPath(), Serialize(MakeConfig(s)));
            var store = new ConfigStore(CfgPath());

            var ok = store.TryLoad(out _, out var error);

            Assert.False(ok);
            Assert.Contains("名称", error);
        }

        [Fact]
        public void HostWithScheme_ReturnsError()
        {
            File.WriteAllText(CfgPath(), Serialize(MakeConfig(MakeSubject(host: "ftp://ftp.test.local"))));
            var store = new ConfigStore(CfgPath());

            var ok = store.TryLoad(out _, out var error);

            Assert.False(ok);
            Assert.Contains("host", error);
        }

        [Fact]
        public void BadCredentialMode_ReturnsError()
        {
            File.WriteAllText(CfgPath(), Serialize(MakeConfig(MakeSubject(credentialMode: "typo"))));
            var store = new ConfigStore(CfgPath());

            var ok = store.TryLoad(out _, out var error);

            Assert.False(ok);
            Assert.Contains("credentialMode", error);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        [InlineData("/")]
        public void EmptyRemotePath_IsAllowed(string remotePath)
        {
            // 留空 remotePath = 站点根目录，属于合法配置（不该报“必须以 / 开头”）。
            File.WriteAllText(CfgPath(), Serialize(MakeConfig(MakeSubject(remotePath: remotePath))));
            var store = new ConfigStore(CfgPath());

            var ok = store.TryLoad(out var config, out var error);

            Assert.True(ok, error);
            Assert.Equal("ftp://u:P%40ss w0rd!@ftp.test.local/", FtpUrlBuilder.Build(config!.Subjects[0]));
        }

        [Theory]
        [InlineData("pub/yuwen")] // 未以 / 开头
        [InlineData("/a/../b")]   // 含 ..
        public void BadRemotePath_ReturnsError(string remotePath)
        {
            var s = MakeSubject(remotePath: remotePath);
            File.WriteAllText(CfgPath(), Serialize(MakeConfig(s)));
            var store = new ConfigStore(CfgPath());

            var ok = store.TryLoad(out _, out var error);

            Assert.False(ok);
            Assert.Contains("remotePath", error);
        }

        [Fact]
        public void LegacyConfig_WithExtraFields_StillLoads()
        {
            // 旧配置（含 passive/encoding/timeoutSec/syncMode/localDir 等历史字段）必须能直接读取。
            const string legacy = @"{
  ""version"": 2,
  ""subjects"": [
    {
      ""id"": ""yuwen-2026"",
      ""name"": ""语文"",
      ""host"": ""ftp.example.com"",
      ""port"": 21,
      ""user"": ""yuwen"",
      ""password"": ""123456"",
      ""remotePath"": ""/pub/yuwen"",
      ""localDir"": """",
      ""passive"": true,
      ""encoding"": ""utf8"",
      ""timeoutSec"": 30,
      ""syncMode"": ""incremental"",
      ""lastSyncAt"": """"
    }
  ]
}";
            File.WriteAllText(CfgPath(), legacy);
            var store = new ConfigStore(CfgPath());

            var ok = store.TryLoad(out var config, out var error);

            Assert.True(ok, error);
            var subject = Assert.Single(config!.Subjects);
            Assert.Equal("语文", subject.Name);
            Assert.True(subject.IsInlineCredential); // 缺省即内嵌凭据
            Assert.Equal("ftp://yuwen:123456@ftp.example.com/pub/yuwen/", FtpUrlBuilder.Build(subject));
        }

        [Fact]
        public void UpdateLastOpen_PreservesPlaintextPassword_AndDropsUnusedFields()
        {
            const string secret = "P@ss w0rd! 明文";
            var config = MakeConfig(MakeSubject(password: secret));
            File.WriteAllText(CfgPath(), Serialize(config));
            var store = new ConfigStore(CfgPath());

            var updated = store.UpdateLastOpenUtc("s1", new DateTime(2026, 1, 2, 3, 4, 5));

            Assert.True(updated);
            var raw = File.ReadAllText(CfgPath());
            Assert.Contains(secret, raw, StringComparison.Ordinal); // 明文保留
            Assert.Contains("\"lastSyncAt\": \"2026-01-02 03:04:05\"", raw, StringComparison.Ordinal);
            Assert.DoesNotContain("\"syncMode\"", raw, StringComparison.Ordinal); // lite 版不再写回无用字段
            // 只读计算属性不得被写回配置文件（否则每次“打开”都会往 subjects.json 里塞垃圾字段）
            Assert.DoesNotContain("\"DisplayLine\"", raw, StringComparison.Ordinal);
            Assert.DoesNotContain("\"IsInlineCredential\"", raw, StringComparison.Ordinal);

            var ok = store.TryLoad(out var reloaded, out var error);
            Assert.True(ok, error);
            Assert.Equal("2026-01-02 03:04:05", reloaded!.Subjects[0].LastSyncAt);
        }

        [Fact]
        public void ResolveLocalRoot_IsHintPathUnderDataRoot()
        {
            var root = ConfigStore.ResolveLocalRoot(MakeSubject(id: "A_B-1"));

            Assert.True(Path.IsPathRooted(root));
            Assert.EndsWith(Path.DirectorySeparatorChar + "A_B-1", root, StringComparison.Ordinal);
        }

        [Fact]
        public void SanitizeId_StripsUnsafeCharacters()
        {
            Assert.Equal("abc-1_2", ConfigStore.SanitizeId("abc-1_2"));
            Assert.Equal("subject", ConfigStore.SanitizeId("中文"));
            Assert.Equal("subject", ConfigStore.SanitizeId(""));
        }
    }
}
