using System;
using System.Drawing;
using System.IO;
using FtpJumperLite.Models;
using FtpJumperLite.Services;
using FtpJumperLite.Ui;
using Xunit;

namespace FtpJumperLite.Tests
{
    /// <summary>
    /// 个性化相关测试：颜色/纹理取值规则、主题推导（自定义浅深互转 + 对比度兜底）、
    /// 主题解析（模式选择）、settings.json 往返与容错、subjects.json 外观字段的规范化。
    /// </summary>
    public sealed class ThemeTests : IDisposable
    {
        private readonly string _dir;

        public ThemeTests()
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
                // 忽略清理失败。
            }
        }

        // ---------- 颜色 / 纹理取值 ----------

        [Theory]
        [InlineData("#abc", "#AABBCC")]
        [InlineData("abc123", "#ABC123")]
        [InlineData("#0078D4", "#0078D4")]
        [InlineData("红", "#D13438")]
        [InlineData("Blue", "#0078D4")]
        public void SubjectStyles_NormalizesColors(string input, string expected)
        {
            Assert.True(SubjectStyles.TryNormalizeColor(input, out var normalized));
            Assert.Equal(expected, normalized);
        }

        [Theory]
        [InlineData("")]
        [InlineData("   ")]
        public void SubjectStyles_EmptyColorMeansUnset(string input)
        {
            Assert.True(SubjectStyles.TryNormalizeColor(input, out var normalized));
            Assert.Null(normalized);
        }

        [Theory]
        [InlineData("#12")]
        [InlineData("#12345")]
        [InlineData("#GGGGGG")]
        [InlineData("不是颜色")]
        public void SubjectStyles_RejectsBadColors(string input)
        {
            Assert.False(SubjectStyles.TryNormalizeColor(input, out _));
        }

        [Theory]
        [InlineData("stripe", "stripe")]
        [InlineData("DOTS", "dots")]
        [InlineData("Grid", "grid")]
        [InlineData("gradient", "gradient")]
        public void SubjectStyles_AcceptsTextures(string input, string expected)
        {
            Assert.True(SubjectStyles.TryNormalizeTexture(input, out var normalized));
            Assert.Equal(expected, normalized);
        }

        [Fact]
        public void SubjectStyles_RejectsUnknownTexture()
        {
            Assert.False(SubjectStyles.TryNormalizeTexture("checkerboard", out var normalized));
            Assert.Null(normalized);
        }

        // ---------- 主题推导 ----------

        [Fact]
        public void ThemeDeriver_LightToDark_KeepsTextContrast()
        {
            var light = UiPalette.Light();
            Assert.False(ThemeDeriver.IsDarkish(light));

            var dark = ThemeDeriver.DeriveCounterpart(light);

            Assert.True(ThemeDeriver.IsDarkish(dark), "推导出的深色，卡片底色应明显更暗");
            Assert.True(
                ThemeDeriver.RelativeLuminance(dark.FormBack) < ThemeDeriver.RelativeLuminance(light.FormBack),
                "深色的页面底色应比浅色暗");
            Assert.True(
                ThemeDeriver.ContrastRatio(dark.TitleText, dark.CardBack) >= 4.5,
                "深色下科目名对比度应 ≥ 4.5:1，实际 " + ThemeDeriver.ContrastRatio(dark.TitleText, dark.CardBack));
            Assert.True(
                ThemeDeriver.ContrastRatio(dark.SubText, dark.CardBack) >= 4.5,
                "深色下副标题对比度应 ≥ 4.5:1，实际 " + ThemeDeriver.ContrastRatio(dark.SubText, dark.CardBack));
        }

        [Fact]
        public void ThemeDeriver_DarkToLight_StaysOnItsOwnSide()
        {
            var dark = UiPalette.Dark();
            Assert.True(ThemeDeriver.IsDarkish(dark));

            var light = ThemeDeriver.DeriveCounterpart(dark);
            Assert.False(ThemeDeriver.IsDarkish(light));
            Assert.InRange(ThemeDeriver.RelativeLuminance(light.CardBack), 0.85, 1.01);

            // 再推一次应回到深色一侧（不会漂到中间灰）
            var again = ThemeDeriver.DeriveCounterpart(light);
            Assert.True(ThemeDeriver.IsDarkish(again));
            Assert.InRange(ThemeDeriver.RelativeLuminance(again.CardBack), 0.0, 0.12);
        }

        [Fact]
        public void ThemeDeriver_KeepsHueOfAccent()
        {
            // 紫色强调色推导后应该还是紫色系（红蓝分量明显高于绿）
            var light = UiPalette.Light();
            light.Accent = Color.FromArgb(0x87, 0x64, 0xB8);

            var dark = ThemeDeriver.DeriveCounterpart(light);

            Assert.True(
                dark.Accent.R > dark.Accent.G && dark.Accent.B > dark.Accent.G,
                "强调色的色相应保留（紫色系）：" + dark.Accent);
        }

        // ---------- 主题解析 ----------

        [Fact]
        public void ThemeResolver_PicksSideByMode()
        {
            var settings = UiSettings.CreateDefault();
            Assert.Equal("system", settings.Theme);

            var bySystemLight = ThemeResolver.Resolve(settings, false, out var isDarkLight, out _);
            Assert.False(isDarkLight);
            Assert.Equal(UiPalette.Light().CardBack.ToArgb(), bySystemLight.CardBack.ToArgb());

            var bySystemDark = ThemeResolver.Resolve(settings, true, out var isDarkDark, out _);
            Assert.True(isDarkDark);
            Assert.Equal(UiPalette.Dark().CardBack.ToArgb(), bySystemDark.CardBack.ToArgb());

            // 强制浅色时，系统深色也不应影响结果
            var forcedLight = new UiSettings { Theme = "light" };
            ThemeResolver.Resolve(forcedLight, true, out var forcedIsDark, out _);
            Assert.False(forcedIsDark);

            var forcedDark = new UiSettings { Theme = "dark" };
            ThemeResolver.Resolve(forcedDark, false, out var forcedDarkIsDark, out _);
            Assert.True(forcedDarkIsDark);
        }

        [Fact]
        public void ThemeResolver_CustomPalette_DerivesTheOtherSide()
        {
            var settings = UiSettings.CreateDefault();
            settings.Theme = "custom";
            settings.Palette = new PaletteOverrides
            {
                Custom = new ThemeColors { CardBack = "#FFFFFF", TitleText = "#101010", Accent = "紫" }
            };

            var light = ThemeResolver.Resolve(settings, false, out var isDark, out var note);
            Assert.False(isDark);
            Assert.Null(note);
            Assert.Equal(Color.White.ToArgb(), light.CardBack.ToArgb());
            Assert.Equal(Color.FromArgb(0x87, 0x64, 0xB8).ToArgb(), light.Accent.ToArgb());

            var dark = ThemeResolver.Resolve(settings, true, out var isDarkDark, out _);
            Assert.True(isDarkDark);
            Assert.True(ThemeDeriver.IsDarkish(dark), "只写了浅色一侧时，深色一侧应被自动推导");
            Assert.True(ThemeDeriver.ContrastRatio(dark.TitleText, dark.CardBack) >= 4.5);
        }

        [Fact]
        public void ThemeResolver_InvalidColor_IsIgnoredWithNote()
        {
            var settings = UiSettings.CreateDefault();
            settings.Palette = new PaletteOverrides { Light = new ThemeColors { CardBack = "#ZZZZZZ" } };

            var light = ThemeResolver.Resolve(settings, false, out _, out var note);

            Assert.False(string.IsNullOrEmpty(note), "写错颜色应该留下提示");
            Assert.Equal(UiPalette.Light().CardBack.ToArgb(), light.CardBack.ToArgb());
        }

        // ---------- settings.json 往返 ----------

        [Fact]
        public void SettingsStore_MissingFile_UsesDefaults()
        {
            var store = new SettingsStore(Path.Combine(_dir, "settings.json"));

            Assert.True(store.TryLoad(out var settings, out var error));
            Assert.Null(error);
            Assert.Equal("system", settings.Theme);
            Assert.Equal(100, settings.FontScale);
            Assert.Null(settings.Palette);
        }

        [Fact]
        public void SettingsStore_RoundTripsAndNormalizes()
        {
            var path = Path.Combine(_dir, "settings.json");
            var store = new SettingsStore(path);

            var settings = new UiSettings
            {
                Theme = "custom",
                FontScale = 130,
                Palette = new PaletteOverrides { Custom = new ThemeColors { Accent = "#112233" } }
            };

            Assert.True(store.TrySave(settings, out var saveError), saveError);
            Assert.True(File.Exists(path));

            Assert.True(store.TryLoad(out var loaded, out var loadError), loadError);
            Assert.Equal("custom", loaded.Theme);
            Assert.Equal(130, loaded.FontScale);
            Assert.Equal("#112233", loaded.Palette?.Custom?.Accent);

            // 手写坏值：未知主题 → system；越界字号 → 夹紧到 200
            File.WriteAllText(path, "{\"version\":1,\"theme\":\"neon\",\"fontScale\":900}");
            Assert.True(store.TryLoad(out var fixedUp, out _));
            Assert.Equal("system", fixedUp.Theme);
            Assert.Equal(200, fixedUp.FontScale);
        }

        // ---------- subjects.json 的外观字段 ----------

        [Fact]
        public void ConfigStore_SanitizesSubjectAppearance()
        {
            var path = Path.Combine(_dir, "subjects.json");
            var json = "{\"version\":2,\"subjects\":[" +
                       "{\"id\":\"a\",\"name\":\"语文\",\"host\":\"ftp.example.com\",\"port\":21,\"user\":\"u\",\"password\":\"p\",\"remotePath\":\"/\",\"credentialMode\":\"inline\",\"lastSyncAt\":\"\",\"color\":\"红\",\"texture\":\"stripe\"}," +
                       "{\"id\":\"b\",\"name\":\"数学\",\"host\":\"ftp.example.com\",\"port\":21,\"user\":\"u\",\"password\":\"p\",\"remotePath\":\"/\",\"credentialMode\":\"inline\",\"lastSyncAt\":\"\",\"color\":\"#12\",\"texture\":\"checkerboard\"}]}";
            File.WriteAllText(path, json);

            var store = new ConfigStore(path);
            Assert.True(store.TryLoad(out var config, out var error), error);

            Assert.NotNull(config);
            Assert.Equal("#D13438", config!.Subjects[0].ColorHex);
            Assert.Equal("stripe", config.Subjects[0].Texture);

            // 写错的外观字段不致命：置空，但整份配置仍然可用
            Assert.Null(config.Subjects[1].ColorHex);
            Assert.Null(config.Subjects[1].Texture);
        }
        // ---------- 新增科目（字段级表单的保存路径） ----------

        [Fact]
        public void ConfigStore_TryUpdateAndDeleteSubject()
        {
            var path = Path.Combine(_dir, "subjects-edit.json");

            // 先写一份干净的两科目配置（不要走到"缺失→生成示例"那条路径）。
            File.WriteAllText(
                path,
                "{\"version\":2,\"subjects\":[" +
                "{\"id\":\"a\",\"name\":\"语文\",\"host\":\"ftp.example.com\",\"port\":21,\"user\":\"u\",\"password\":\"p\",\"remotePath\":\"/\",\"credentialMode\":\"inline\",\"lastSyncAt\":\"\"}," +
                "{\"id\":\"b\",\"name\":\"数学\",\"host\":\"ftp.example.com\",\"port\":21,\"user\":\"u\",\"password\":\"p\",\"remotePath\":\"/\",\"credentialMode\":\"inline\",\"lastSyncAt\":\"\"}]}");

            var store = new ConfigStore(path);

            // 修改：改名 + 加颜色/底纹
            var updated = new Subject
            {
                Id = "a", Name = "语文（新）", Host = "ftp.example.com", Port = 21,
                User = "u", Password = "p", RemotePath = "/pub", CredentialMode = "inline",
                ColorHex = "绿", Texture = "grid"
            };
            Assert.True(store.TryUpdateSubject("a", updated, out var e3), e3);

            Assert.True(store.TryLoad(out var config, out var e4), e4);
            var a = config!.Subjects.Find(s => s.Id == "a");
            Assert.NotNull(a);
            Assert.Equal("语文（新）", a!.Name);
            Assert.Equal("#107C10", a.ColorHex);
            Assert.Equal("grid", a.Texture);

            // 删除
            Assert.True(store.TryDeleteSubject("b", out var e5), e5);
            Assert.True(store.TryLoad(out var afterDelete, out var e6), e6);
            Assert.Single(afterDelete!.Subjects);
            Assert.Equal("a", afterDelete.Subjects[0].Id);

            // 不能把科目删光（至少保留一个）
            Assert.False(store.TryDeleteSubject("a", out var deleteError));
            Assert.False(string.IsNullOrEmpty(deleteError));
        }

        [Fact]
        public void ConfigStore_TryAddSubject_AppendsAndPersists()
        {
            var path = Path.Combine(_dir, "subjects-add.json");
            var store = new ConfigStore(path);

            var subject = new Subject
            {
                Id = "new1",
                Name = "美术",
                Host = "ftp.example.com",
                Port = 21,
                User = "u",
                Password = "p",
                RemotePath = "/pub/art",
                CredentialMode = "inline",
                ColorHex = "紫",
                Texture = "dots"
            };

            Assert.True(store.TryAddSubject(subject, out var error), error);
            Assert.True(File.Exists(path));

            Assert.True(store.TryLoad(out var config, out var loadError), loadError);
            Assert.NotNull(config);

            var added = config!.Subjects.Find(s => s.Id == "new1");
            Assert.NotNull(added);
            Assert.Equal("美术", added!.Name);
            Assert.Equal("#8764B8", added.ColorHex);
            Assert.Equal("dots", added.Texture);
        }
    }
}
