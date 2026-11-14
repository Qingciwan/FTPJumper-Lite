using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using FtpJumperLite.Models;
using FtpJumperLite.Ui;
using Newtonsoft.Json;
using Xunit;

namespace FtpJumperLite.Tests
{
    /// <summary>
    /// 自定义字体（settings.json 的 fontFile / fontFamily）回归测试。
    ///
    /// 关键约束：未安装到系统的字体只能通过 PrivateFontCollection 的 FontFamily 对象创建 Font；
    /// 用家族名 new Font("名字") 会静默回退到系统默认字体 —— 这条如果回归了，界面上会"看起来没生效"。
    /// </summary>
    public sealed class UiFontTests : IDisposable
    {
        private readonly string _dir;

        public UiFontTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "FtpJumperLiteFontTests", Guid.NewGuid().ToString("N"));
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

        [Fact]
        public void Default_UsesMicrosoftYaHeiUi()
        {
            var set = UiFontSet.Load(UiSettings.CreateDefault(), null);
            Assert.False(set.IsCustom);
            Assert.Equal(UiFontSet.DefaultFamilyName, set.FamilyName);
            Assert.Null(set.Note);
        }

        [Fact]
        public void LoadsFontFileAsPrivateFamily()
        {
            var file = CopySystemFont("custom.ttf");

            var set = UiFontSet.Load(new UiSettings { FontFile = file }, null);

            Assert.True(set.IsCustom, "用文件加载的字体应标记为自定义");
            Assert.False(string.IsNullOrEmpty(set.FamilyName));
            Assert.Equal(Path.GetFullPath(file), set.SourcePath);
            Assert.Null(set.Note);

            using (var font = set.Create(12F, FontStyle.Bold))
            {
                Assert.True(font.Height > 0);
                Assert.Equal(set.FamilyName, font.FontFamily.Name);
            }
        }

        [Fact]
        public void RelativeFontFile_ResolvesFromSettingsDirectory()
        {
            CopySystemFont("relative.ttf");
            var configPath = Path.Combine(_dir, "subjects.json");
            File.WriteAllText(configPath, "{}");

            var set = UiFontSet.Load(new UiSettings { FontFile = "relative.ttf" }, configPath);

            Assert.True(set.IsCustom);
            Assert.Equal(Path.GetFullPath(Path.Combine(_dir, "relative.ttf")), set.SourcePath);
        }

        [Fact]
        public void MissingFontFile_FallsBackWithNote()
        {
            var set = UiFontSet.Load(new UiSettings { FontFile = "not-here.ttf" }, Path.Combine(_dir, "subjects.json"));

            Assert.False(set.IsCustom);
            Assert.Equal(UiFontSet.DefaultFamilyName, set.FamilyName);
            Assert.Contains("not-here.ttf", set.Note);
        }

        [Fact]
        public void UnknownInstalledFamily_FallsBackWithNote()
        {
            var set = UiFontSet.Load(new UiSettings { FontFamily = "绝不存在的中文字体 XYZ" }, null);

            Assert.False(set.IsCustom);
            Assert.Equal(UiFontSet.DefaultFamilyName, set.FamilyName);
            Assert.Contains("未安装", set.Note);
        }

        [Fact]
        public void SettingsJson_RoundTripsFontFields()
        {
            var json = "{\"version\":1,\"theme\":\"dark\",\"fontScale\":120,"
                       + "\"fontFamily\":\"  Arial  \",\"fontFile\":\" fonts/custom.ttf \"}";

            var settings = JsonConvert.DeserializeObject<UiSettings>(json);
            Assert.NotNull(settings);
            Assert.Equal("  Arial  ", settings!.FontFamily);
            Assert.Equal(" fonts/custom.ttf ", settings.FontFile);

            var notes = settings.Normalize();
            Assert.Equal(string.Empty, notes);
            Assert.Equal("Arial", settings.FontFamily);
            Assert.Equal("fonts/custom.ttf", settings.FontFile);

            var again = JsonConvert.DeserializeObject<UiSettings>(JsonConvert.SerializeObject(settings));
            Assert.Equal("Arial", again!.FontFamily);
            Assert.Equal("fonts/custom.ttf", again.FontFile);
        }

        [Fact]
        public void MainForm_AppliesCustomFontToFormAndSubjectList()
        {
            CopySystemFont("form-font.ttf");
            var configPath = Path.Combine(_dir, "subjects.json");
            File.WriteAllText(configPath, BuildConfigJson());
            File.WriteAllText(
                Path.Combine(_dir, "settings.json"),
                "{\"version\":1,\"theme\":\"light\",\"fontScale\":100,\"fontFile\":\"form-font.ttf\"}");

            string formFamily = string.Empty;
            string listFamily = string.Empty;

            RunOnSta(() =>
            {
                using (var form = new MainForm(configPath))
                {
                    var list = GetPrivateField<SubjectListControl>(form, "_subjectList");
                    formFamily = form.Font.FontFamily.Name;
                    listFamily = list.Metrics.FontFamilyName;

                    // FontFamily 每次从 Font 上取都可能返回新的包装对象，所以比名字而不是引用。
                    Assert.True(list.Metrics.FontFamily != null, "科目区应拿到 FontFamily 对象（私有字体不能只给名字）");
                    Assert.Equal(form.Font.FontFamily.Name, list.Metrics.FontFamily!.Name);
                }
            });

            Assert.Equal(listFamily, formFamily);
            Assert.True(formFamily.Length > 0);
        }

        [Fact]
        public void MainForm_ReloadsFontWhenSettingsChange()
        {
            CopySystemFont("switch.ttf");
            var configPath = Path.Combine(_dir, "subjects.json");
            var settingsPath = Path.Combine(_dir, "settings.json");
            File.WriteAllText(configPath, BuildConfigJson());
            File.WriteAllText(settingsPath, "{\"version\":1,\"theme\":\"light\",\"fontScale\":100}");

            var initial = string.Empty;
            var switched = string.Empty;
            var listFamily = string.Empty;

            RunOnSta(() =>
            {
                using (var form = new MainForm(configPath))
                {
                    initial = form.Font.FontFamily.Name;

                    // 等价于老师改完 settings.json 后程序自动重新加载。
                    File.WriteAllText(
                        settingsPath,
                        "{\"version\":1,\"theme\":\"light\",\"fontScale\":100,\"fontFile\":\"switch.ttf\"}");

                    var reload = typeof(MainForm).GetMethod(
                        "ReloadSettings",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.NotNull(reload);
                    reload!.Invoke(form, null);

                    switched = form.Font.FontFamily.Name;
                    listFamily = GetPrivateField<SubjectListControl>(form, "_subjectList").Metrics.FontFamilyName;
                }
            });

            Assert.NotEqual(initial, switched);
            Assert.Equal(switched, listFamily);
        }

        [Fact]
        public void SettingsForm_ControlsFitInsideClientArea()
        {
            RunOnSta(() =>
            {
                using (var form = new SettingsForm(
                           UiSettings.CreateDefault(),
                           UiPalette.Light(),
                           2F,
                           UiFontSet.Default,
                           null))
                {
                    form.CreateControl();
                    form.PerformLayout();

                    foreach (Control control in form.Controls)
                    {
                        Assert.True(
                            control.Right <= form.ClientSize.Width + 1,
                            "控件超出右边界：" + control.GetType().Name + " right=" + control.Right
                            + " client=" + form.ClientSize.Width);
                        Assert.True(
                            control.Bottom <= form.ClientSize.Height + 1,
                            "控件超出下边界：" + control.GetType().Name + " bottom=" + control.Bottom
                            + " client=" + form.ClientSize.Height);
                    }
                }
            });
        }

        // ---------- 结构校验：防止 GDI+ 在原生层把进程搞崩 ----------

        [Fact]
        public void ValidateFontFile_AcceptsRealFont()
        {
            Assert.True(UiFontSet.ValidateFontFile(FindSystemFontFile(), out var error), error);
            Assert.Equal(string.Empty, error);
        }

        [Fact]
        public void ValidateFontFile_AcceptsFontCollection()
        {
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var ttc = Path.Combine(windows, "Fonts", "msyh.ttc");
            if (!File.Exists(ttc))
            {
                return; // 这台机器没有 .ttc 字体，跳过。
            }

            // .ttc 是"字体集合"：文件头不是普通表目录，但 GDI+ 的 AddFontFile 能直接加载，
            // 所以校验要放行，不能像我一开始那样把它当坏文件拒掉。
            Assert.True(UiFontSet.ValidateFontFile(ttc, out var error), error);

            var set = UiFontSet.LoadFile(ttc);
            Assert.True(set.IsCustom);
            Assert.False(string.IsNullOrEmpty(set.FamilyName));
        }

        [Fact]
        public void ValidateFontFile_RejectsGarbageAndTruncatedFiles()
        {
            // 不是字体：文件头对不上（要有 256 字节以上，才不会先被"太小"拦下）
            var junk = Path.Combine(_dir, "junk.ttf");
            File.WriteAllText(junk, new string('A', 400));
            Assert.False(UiFontSet.ValidateFontFile(junk, out var junkError));
            Assert.Contains("不是 TrueType", junkError);

            // 太小
            var tiny = Path.Combine(_dir, "tiny.ttf");
            File.WriteAllBytes(tiny, new byte[10]);
            Assert.False(UiFontSet.ValidateFontFile(tiny, out var tinyError));
            Assert.Contains("太小", tinyError);

            // 截断的真实字体：表目录会指到文件末尾之外
            var source = File.ReadAllBytes(FindSystemFontFile());
            var truncated = Path.Combine(_dir, "truncated.ttf");
            var head = new byte[400];
            Array.Copy(source, head, head.Length);
            File.WriteAllBytes(truncated, head);
            Assert.False(UiFontSet.ValidateFontFile(truncated, out var truncatedError));
            Assert.False(string.IsNullOrEmpty(truncatedError));

            // 文件不存在
            Assert.False(UiFontSet.ValidateFontFile(Path.Combine(_dir, "missing.ttf"), out var missingError));
            Assert.Contains("找不到", missingError);
        }

        private string CopySystemFont(string fileName)
        {
            var source = FindSystemFontFile();
            var target = Path.Combine(_dir, fileName);
            File.Copy(source, target, true);
            return target;
        }

        private static string FindSystemFontFile()
        {
            var windows = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            foreach (var name in new[] { "arial.ttf", "segoeui.ttf", "tahoma.ttf" })
            {
                var path = Path.Combine(windows, "Fonts", name);
                if (File.Exists(path))
                {
                    return path;
                }
            }

            throw new InvalidOperationException("找不到可用于测试的系统字体文件");
        }

        private static string BuildConfigJson()
        {
            var config = new AppConfig { Version = 2 };
            config.Subjects.Add(new Subject
            {
                Id = "s1",
                Name = "科目1",
                Host = "ftp.example.com",
                Port = 21,
                User = "u1",
                Password = "p1",
                RemotePath = "/pub/1",
                CredentialMode = "inline",
                LastSyncAt = ""
            });

            return JsonConvert.SerializeObject(config, Formatting.Indented);
        }

        private static T GetPrivateField<T>(object instance, string fieldName)
            where T : class
        {
            var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            return (T)field!.GetValue(instance)!;
        }

        private static void RunOnSta(Action action)
        {
            Exception? failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            Assert.True(failure == null, "STA 线程里执行失败：" + failure);
        }
    }
}
