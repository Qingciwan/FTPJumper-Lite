using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using FtpJumperLite.Models;
using FtpJumperLite.Services;

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

            // 先做结构校验：GDI+ 的 PrivateFontCollection 遇到损坏/不支持的字体时，
            // 有可能直接在原生代码里崩掉整个进程（托管 try/catch 拦不住），必须先挡在门外。
            if (!ValidateFontFile(full, out var problem))
            {
                throw new InvalidOperationException(problem);
            }

            return LoadPrivate(full);
        }

        /// <summary>
        /// 字体文件的结构校验（只看文件头与表目录，不碰 GDI+，所以本身不会崩）。
        /// 拒绝：非字体文件、字体集合（.ttc，PrivateFontCollection 不支持）、表目录越界的截断文件。
        /// </summary>
        public static bool ValidateFontFile(string fullPath, out string error)
        {
            error = string.Empty;

            long length;
            byte[] header;
            try
            {
                var info = new FileInfo(fullPath);
                if (!info.Exists)
                {
                    error = "找不到字体文件：" + fullPath;
                    return false;
                }

                length = info.Length;
                if (length < 256)
                {
                    error = "文件太小，不像是字体文件（" + length + " 字节）。";
                    return false;
                }

                using (var stream = File.OpenRead(fullPath))
                {
                    header = new byte[4];
                    if (stream.Read(header, 0, header.Length) != header.Length)
                    {
                        error = "读不到文件头。";
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                error = "读不了这个文件：" + ex.Message;
                return false;
            }

            var tag = ((uint)header[0] << 24) | ((uint)header[1] << 16) | ((uint)header[2] << 8) | header[3];

            // 'ttcf'：字体集合（.ttc）。GDI+ 的 AddFontFile 能直接吃（实测 msyh.ttc 可加载出 2 个家族），
            // 所以放行；它的文件头不是普通表目录，后面那段检查跳过。
            if (tag == 0x74746366)
            {
                return true;
            }

            var known = tag == 0x00010000 // TrueType
                        || tag == 0x4F54544F // 'OTTO'：OpenType/CFF
                        || tag == 0x74727565 // 'true'
                        || tag == 0x74797031; // 'typ1'
            if (!known)
            {
                error = "不是 TrueType/OpenType 字体（文件头 0x" + tag.ToString("X8") + "）。";
                return false;
            }

            // 表目录边界检查：数量合理，且每张表的 [offset, offset+length) 都落在文件内。
            try
            {
                using (var stream = File.OpenRead(fullPath))
                using (var reader = new BinaryReader(stream))
                {
                    stream.Position = 4;
                    var numTables = ReadBigEndianUInt16(reader);
                    if (numTables == 0 || numTables > 512)
                    {
                        error = "字体表数量异常（" + numTables + "）。";
                        return false;
                    }

                    if (12L + (12L * numTables) > length)
                    {
                        error = "字体文件不完整（表目录越界）。";
                        return false;
                    }

                    for (var i = 0; i < numTables; i++)
                    {
                        stream.Position = 12L + (16L * i) + 8L; // tag(4) + checksum(4) → offset(4) + length(4)
                        var offset = ReadBigEndianUInt32(reader);
                        var size = ReadBigEndianUInt32(reader);
                        if (offset > length || (long)offset + size > length + 4)
                        {
                            error = "字体数据越界（第 " + (i + 1) + " 张表超出文件末尾），文件可能已损坏。";
                            return false;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                error = "字体结构校验失败：" + ex.Message;
                return false;
            }

            return true;
        }

        private static ushort ReadBigEndianUInt16(BinaryReader reader)
        {
            var bytes = reader.ReadBytes(2);
            if (bytes.Length != 2)
            {
                throw new EndOfStreamException();
            }

            return (ushort)((bytes[0] << 8) | bytes[1]);
        }

        private static uint ReadBigEndianUInt32(BinaryReader reader)
        {
            var bytes = reader.ReadBytes(4);
            if (bytes.Length != 4)
            {
                throw new EndOfStreamException();
            }

            return ((uint)bytes[0] << 24) | ((uint)bytes[1] << 16) | ((uint)bytes[2] << 8) | bytes[3];
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
                    // 先把"要加载哪个文件"写进日志：万一 GDI+ 在原生层崩掉进程，
                    // 日志里至少能看到它死在哪个文件上（这是唯一能留下的线索）。
                    var fileSize = new FileInfo(fullPath).Length;
                    AppLogger.Info("界面字体：开始加载 " + fullPath + "（" + fileSize + " 字节）");

                    collection.AddFontFile(fullPath);
                    if (collection.Families.Length == 0)
                    {
                        throw new InvalidOperationException("字体文件里没有可用字体");
                    }

                    var set = new UiFontSet(collection.Families[0], collection, fullPath, null);
                    Cache["file:" + fullPath] = set;
                    AppLogger.Info("界面字体：加载成功 " + set.FamilyName);
                    return set;
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("界面字体：加载失败 " + fullPath + " → " + ex.GetType().Name + "：" + ex.Message);
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
