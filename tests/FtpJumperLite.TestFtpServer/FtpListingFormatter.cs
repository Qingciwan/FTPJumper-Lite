using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace FtpJumperLite.TestFtpServer
{
    /// <summary>目录列表文本生成（MLSD / UNIX LIST / DOS LIST）。</summary>
    public static class FtpListingFormatter
    {
        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        /// <summary>MLSD（RFC 3659）格式。</summary>
        public static string BuildMlsd(string diskDirectory)
        {
            var sb = new StringBuilder();
            foreach (var dir in Directory.GetDirectories(diskDirectory).OrderBy(x => x, StringComparer.Ordinal))
            {
                var info = new DirectoryInfo(dir);
                sb.Append("type=dir;modify=")
                  .Append(info.LastWriteTimeUtc.ToString("yyyyMMddHHmmss", Inv))
                  .Append("; ")
                  .Append(info.Name)
                  .Append("\r\n");
            }

            foreach (var file in Directory.GetFiles(diskDirectory).OrderBy(x => x, StringComparer.Ordinal))
            {
                var info = new FileInfo(file);
                sb.Append("type=file;size=")
                  .Append(info.Length.ToString(Inv))
                  .Append(";modify=")
                  .Append(info.LastWriteTimeUtc.ToString("yyyyMMddHHmmss", Inv))
                  .Append("; ")
                  .Append(info.Name)
                  .Append("\r\n");
            }

            return sb.ToString();
        }

        /// <summary>经典 LIST 格式（UNIX 或 DOS）。</summary>
        public static string BuildList(string diskDirectory, FtpListingStyle style)
        {
            var sb = new StringBuilder();
            foreach (var dir in Directory.GetDirectories(diskDirectory).OrderBy(x => x, StringComparer.Ordinal))
            {
                var info = new DirectoryInfo(dir);
                sb.Append(Format(style, info.LastWriteTime, isDirectory: true, size: 0, name: info.Name)).Append("\r\n");
            }

            foreach (var file in Directory.GetFiles(diskDirectory).OrderBy(x => x, StringComparer.Ordinal))
            {
                var info = new FileInfo(file);
                sb.Append(Format(style, info.LastWriteTime, isDirectory: false, size: info.Length, name: info.Name)).Append("\r\n");
            }

            return sb.ToString();
        }

        private static string Format(FtpListingStyle style, DateTime modified, bool isDirectory, long size, string name)
        {
            return style == FtpListingStyle.Dos
                ? FormatDos(modified, isDirectory, size, name)
                : FormatUnix(modified, isDirectory, size, name);
        }

        private static string FormatUnix(DateTime modified, bool isDirectory, long size, string name)
        {
            return string.Format(
                Inv,
                "{0} 1 ftp ftp {1,12} {2} {3,2} {4:HH:mm} {5}",
                isDirectory ? "drwxr-xr-x" : "-rw-r--r--",
                size,
                modified.ToString("MMM", Inv),
                modified.Day,
                modified,
                name);
        }

        private static string FormatDos(DateTime modified, bool isDirectory, long size, string name)
        {
            var sizeOrDir = isDirectory ? "<DIR>".PadRight(14) : size.ToString(Inv).PadLeft(14);
            return string.Format(
                Inv,
                "{0}  {1} {2} {3}",
                modified.ToString("MM-dd-yy", Inv),
                modified.ToString("hh:mmtt", Inv),
                sizeOrDir,
                name);
        }
    }
}
