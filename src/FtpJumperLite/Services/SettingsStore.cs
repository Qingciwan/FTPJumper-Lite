using System;
using System.IO;
using System.Text;
using FtpJumperLite.Models;
using Newtonsoft.Json;

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

                var parsed = JsonConvert.DeserializeObject<UiSettings>(text);
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
