using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using FtpJumperLite.Models;

namespace FtpJumperLite.Ui
{
    /// <summary>
    /// 界面字体：把 settings.json 的 fontFamily / fontFile 解析成一个可用的 FontFamily。
    ///
    /// 为什么要单独封装：
    ///  * 未安装到系统的字体（比如随程序分发的 .ttf）必须用 PrivateFontCollection 加载，
    ///    之后**只能**用 FontFamily 对象建 Font —— 拿家族名 new Font("名字") 会静默回退到系统默认字体。
    ///  * FontFamily 的生命周期必须比所有 Font 长，所以私有字体集合按文件路径缓存、进程内不释放。
    ///  * 任何一步失败都要能回退到"微软雅黑 UI"，并在日志里留下原因，不能让程序起不来。
    /// </summary>
    public sealed class UiFontSet
    {
        public const string DefaultFamilyName = "Microsoft YaHei UI";

        private static readonly object Gate = new object();
        private static readonly Dictionary<string, UiFontSet> Cache =
            new Dictionary<string, UiFontSet>(StringComparer.OrdinalIgnoreCase);

        private static UiFontSet? _default;

        private readonly PrivateFontCollection? _privateCollection;

        private UiFontSet(FontFamily family, PrivateFontCollection? collection, string? sourcePath, string? note)
        {
            Family = family;
            _privateCollection = collection;
            SourcePath = sourcePath;
            Note = note;
        }

        /// <summary>最终生效的字体家族（永远非空）。</summary>
        public FontFamily Family { get; }

        public string FamilyName => Family.Name;

        /// <summary>是否来自外部字体文件（未安装到系统）。</summary>
        public bool IsCustom => _privateCollection != null;

        /// <summary>私有字体文件的完整路径（系统字体为 null）。</summary>
        public string? SourcePath { get; }

        /// <summary>回退/降级原因（空 = 一切正常）。</summary>
        public string? Note { get; }

        /// <summary>给界面显示的名字。</summary>
        public string DisplayName => IsCustom ? FamilyName + "（字体文件）" : FamilyName;

        /// <summary>系统默认字体（微软雅黑 UI，缺失时退回 SansSerif）。</summary>
        public static UiFontSet Default
        {
            get
            {
                lock (Gate)
                {
                    if (_default != null)
                    {
                        return _default;
                    }

                    FontFamily family;
                    try
                    {
                        family = new FontFamily(DefaultFamilyName);
                    }
                    catch
                    {
                        family = new FontFamily(GenericFontFamilies.SansSerif);
                    }

                    _default = new UiFontSet(family, null, null, null);
                    return _default;
                }
            }
        }

        /// <summary>按 settings.json 的配置解析字体；任何问题都回退到默认字体并写入 Note。</summary>
        public static UiFontSet Load(UiSettings? settings, string? configPath)
        {
            settings ??= UiSettings.CreateDefault();

            var file = (settings.FontFile ?? string.Empty).Trim();
            if (file.Length > 0)
            {
                var full = ResolveFile(file, configPath);
                if (full == null)
                {
                    return WithNote(Default, "找不到字体文件：" + file + "（已回退到" + Default.FamilyName + "）");
                }

                try
                {
                    return LoadPrivate(full);
                }
                catch (Exception ex)
                {
                    return WithNote(Default, "字体文件加载失败：" + Path.GetFileName(full) + "（" + ex.Message + "）");
                }
            }

            var familyName = (settings.FontFamily ?? string.Empty).Trim();
            if (familyName.Length > 0)
            {
                var installed = FindInstalled(familyName);
                if (installed != null)
                {
                    return FromInstalled(installed);
                }

                return WithNote(Default, "字体「" + familyName + "」未安装（已回退到" + Default.FamilyName + "）");
            }

            return Default;
        }

        /// <summary>设置窗调用：显式加载一个字体文件，失败时抛出可读错误。</summary>
        public static UiFontSet LoadFile(string fullPath)
        {
            var full = Path.GetFullPath(fullPath);
            if (!File.Exists(full))
            {
                throw new FileNotFoundException("找不到字体文件：" + full, full);
            }

            return LoadPrivate(full);
        }

        /// <summary>创建字体；家族没有该字重时退回 Regular（私有字体常见只有单一字重）。</summary>
        public Font Create(float emSize, FontStyle style = FontStyle.Regular)
        {
            var useStyle = Family.IsStyleAvailable(style) ? style : FontStyle.Regular;
            return new Font(Family, Math.Max(1F, emSize), useStyle, GraphicsUnit.Point);
        }

        /// <summary>把绝对路径转成"相对 settings.json 目录"的形式（不在其下则原样返回绝对路径）。</summary>
        public static string MakeStorablePath(string fullPath, string? baseDirectory)
        {
            if (string.IsNullOrEmpty(baseDirectory))
            {
                return fullPath;
            }

            try
            {
                var baseFull = Path.GetFullPath(baseDirectory);
                var fileFull = Path.GetFullPath(fullPath);
                var prefix = baseFull.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                             + Path.DirectorySeparatorChar;

                if (fileFull.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return fileFull.Substring(prefix.Length);
                }
            }
            catch
            {
                // 路径非法就退回绝对路径。
            }

            return fullPath;
        }

        /// <summary>相对 settings.json 目录 / exe 目录解析字体文件；找不到返回 null。</summary>
        public static string? ResolveFile(string file, string? configPath)
        {
            if (string.IsNullOrWhiteSpace(file))
            {
                return null;
            }

            if (Path.IsPathRooted(file))
            {
                return File.Exists(file) ? Path.GetFullPath(file) : null;
            }

            var dirs = new List<string?>();
            if (!string.IsNullOrEmpty(configPath))
            {
                dirs.Add(Path.GetDirectoryName(configPath));
            }

            dirs.Add(AppDomain.CurrentDomain.BaseDirectory);

            foreach (var dir in dirs)
            {
                if (string.IsNullOrEmpty(dir))
                {
                    continue;
                }

                try
                {
                    var candidate = Path.GetFullPath(Path.Combine(dir!, file));
                    if (File.Exists(candidate))
                    {
                        return candidate;
                    }
                }
                catch
                {
                    // 非法路径，继续尝试下一个目录。
                }
            }

            return null;
        }

        private static UiFontSet LoadPrivate(string fullPath)
        {
            lock (Gate)
            {
                if (Cache.TryGetValue("file:" + fullPath, out var cached))
                {
                    return cached;
                }

                var collection = new PrivateFontCollection();
                try
                {
                    collection.AddFontFile(fullPath);
                    if (collection.Families.Length == 0)
                    {
                        throw new InvalidOperationException("字体文件里没有可用字体");
                    }

                    var set = new UiFontSet(collection.Families[0], collection, fullPath, null);
                    Cache["file:" + fullPath] = set;
                    return set;
                }
                catch
                {
                    collection.Dispose();
                    throw;
                }
            }
        }

        private static UiFontSet FromInstalled(FontFamily family)
        {
            lock (Gate)
            {
                var key = "installed:" + family.Name;
                if (Cache.TryGetValue(key, out var cached))
                {
                    return cached;
                }

                var set = new UiFontSet(family, null, null, null);
                Cache[key] = set;
                return set;
            }
        }

        private static FontFamily? FindInstalled(string name)
        {
            try
            {
                foreach (var family in FontFamily.Families)
                {
                    if (string.Equals(family.Name, name, StringComparison.OrdinalIgnoreCase))
                    {
                        return family;
                    }
                }
            }
            catch
            {
                // 枚举字体失败按"未安装"处理。
            }

            return null;
        }

        private static UiFontSet WithNote(UiFontSet basis, string note)
        {
            return new UiFontSet(basis.Family, basis._privateCollection, basis.SourcePath, note);
        }
    }
}
