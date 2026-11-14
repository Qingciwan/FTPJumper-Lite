using System;
using System.IO;
using System.Text;
using FtpJumperLite.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FtpJumperLite.Services
{
    /// <summary>
    /// settings.json 的读写。位置：与 subjects.json 同目录（数据根目录），文件名固定 settings.json。
    /// 约定：文件可以不存在（用默认值）；写回是"原子替换"，避免半截文件。
    /// </summary>
    public sealed class SettingsStore
    {
        private static readonly JsonSerializerSettings JsonOptions = new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            NullValueHandling = NullValueHandling.Ignore
        };

        public SettingsStore(string settingsPath)
        {
            Path = settingsPath ?? throw new ArgumentNullException(nameof(settingsPath));
        }

        public string Path { get; }

        /// <summary>与给定科目配置同目录的 settings.json。</summary>
        public static string PathForConfigFile(string configPath)
        {
            var dir = System.IO.Path.GetDirectoryName(configPath);
            return string.IsNullOrEmpty(dir)
                ? "settings.json"
                : System.IO.Path.Combine(dir, "settings.json");
        }

        /// <summary>读取设置。文件缺失 → 返回默认值（不写盘）；解析失败 → 返回默认值并给出错误信息。</summary>
        public bool TryLoad(out UiSettings settings, out string? error)
        {
            settings = UiSettings.CreateDefault();
            error = null;

            try
            {
                if (!File.Exists(Path))
                {
                    return true;
                }

                var text = File.ReadAllText(Path, Encoding.UTF8);
                if (string.IsNullOrWhiteSpace(text))
                {
                    error = "settings.json 是空文件，已使用默认设置。";
                    return false;
                }

                // 容错反序列化：
                //  1) 先严格解析（语法错误、字段类型错误都会在这里暴露）；
                //  2) 失败时只针对"palette 写成了非对象"这一类常见错误做一次清理重试 ——
                //     不能让 palette 的一个笔误把主题/字号/字体全部作废（现场就是这个问题）；
                //  3) 语法坏到不能解析的文件（JObject.Parse 也会抛）仍然如实报错。
                UiSettings? parsed;
                try
                {
                    parsed = JsonConvert.DeserializeObject<UiSettings>(text);
                }
                catch (JsonException)
                {
                    var root = JObject.Parse(text);
                    if (!TryCleanPalette(root))
                    {
                        throw;
                    }

                    parsed = root.ToObject<UiSettings>();
                }

                if (parsed == null)
                {
                    error = "settings.json 里没有设置对象，已使用默认设置。";
                    return false;
                }

                var notes = parsed.Normalize();
                if (notes.Length > 0)
                {
                    AppLogger.Warn("settings.json 已自动修正：" + notes);
                }

                settings = parsed;
                return true;
            }
            catch (JsonException ex)
            {
                error = "settings.json 解析失败：" + ex.Message;
                return false;
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
            {
                error = "无法读取 settings.json：" + ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 把 palette 里"不是对象"的坏字段摘掉（palette 本身、以及它的 light/dark/custom 三个子项）。
        /// 返回是否真的改动了内容（没改动说明是别的错误，交回上层照常报错）。
        /// </summary>
        private static bool TryCleanPalette(JObject root)
        {
            var cleaned = false;
            var palette = root["palette"];
            if (palette != null && palette.Type != JTokenType.Object && palette.Type != JTokenType.Null)
            {
                root.Remove("palette");
                AppLogger.Warn("settings.json：palette 应当写成对象，已忽略这一项（其余设置照常生效）。");
                return true;
            }

            if (!(palette is JObject paletteObject))
            {
                return false;
            }

            foreach (var name in new[] { "light", "dark", "custom" })
            {
                var child = paletteObject[name];
                if (child != null && child.Type != JTokenType.Object && child.Type != JTokenType.Null)
                {
                    paletteObject.Remove(name);
                    AppLogger.Warn("settings.json：palette." + name + " 应当写成对象，已忽略这一项。");
                    cleaned = true;
                }
            }

            return cleaned;
        }

        /// <summary>原子写回（先写临时文件再替换）。</summary>
        public bool TrySave(UiSettings settings, out string? error)
        {
            error = null;
            if (settings == null)
            {
                error = "设置对象为空。";
                return false;
            }

            settings.Normalize();

            var tempPath = Path + ".tmp";
            try
            {
                var dir = System.IO.Path.GetDirectoryName(Path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                var json = JsonConvert.SerializeObject(settings, JsonOptions);
                File.WriteAllText(tempPath, json, new UTF8Encoding(false));

                if (File.Exists(Path))
                {
                    var backup = Path + ".bak";
                    try
                    {
                        File.Delete(backup);
                    }
                    catch
                    {
                        // 备份失败不影响主流程。
                    }

                    File.Move(Path, backup);
                }

                File.Move(tempPath, Path);
                return true;
            }
            catch (Exception ex)
            {
                error = "无法写入 settings.json：" + ex.Message;
                try
                {
                    if (File.Exists(tempPath))
                    {
                        File.Delete(tempPath);
                    }
                }
                catch
                {
                    // 忽略清理失败。
                }

                return false;
            }
        }
    }
}
