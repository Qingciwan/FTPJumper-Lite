using System;
using System.Collections.Generic;
using System.Globalization;

namespace FtpJumperLite.Models
{
    /// <summary>
    /// 科目外观的取值规则（颜色 / 纹理）。放在 Models 里不放 System.Drawing：
    /// 这里只做"文本 → 规范化的 #RRGGBB 字符串"，绘制时再转成 Color。
    /// </summary>
    public static class SubjectStyles
    {
        /// <summary>支持的纹理（预设图案）。后续再加图片文件。</summary>
        public static readonly string[] Textures = { "stripe", "dots", "grid", "gradient" };

        private static readonly Dictionary<string, string> NamedColors =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "红", "#D13438" }, { "red", "#D13438" },
                { "橙", "#CA5010" }, { "orange", "#CA5010" },
                { "黄", "#C19C00" }, { "yellow", "#C19C00" },
                { "绿", "#107C10" }, { "green", "#107C10" },
                { "青", "#038387" }, { "teal", "#038387" },
                { "蓝", "#0078D4" }, { "blue", "#0078D4" },
                { "紫", "#8764B8" }, { "purple", "#8764B8" },
                { "粉", "#C239B3" }, { "pink", "#C239B3" },
                { "棕", "#8E562E" }, { "brown", "#8E562E" },
                { "灰", "#69797E" }, { "gray", "#69797E" }, { "grey", "#69797E" }
            };

        /// <summary>
        /// 把用户写的颜色（"#RRGGBB" / "RRGGBB" / 中文或英文色名）规范成 "#RRGGBB"。
        /// 空字符串视为"未配置"（返回 true、null）。非法值返回 false。
        /// </summary>
        public static bool TryNormalizeColor(string? text, out string? normalized)
        {
            normalized = null;
            var value = (text ?? "").Trim();
            if (value.Length == 0)
            {
                return true;
            }

            if (NamedColors.TryGetValue(value, out var named))
            {
                normalized = named;
                return true;
            }

            if (value.StartsWith("#", StringComparison.Ordinal))
            {
                value = value.Substring(1);
            }

            if (value.Length == 3)
            {
                // #ABC → #AABBCC
                value = new string(new[] { value[0], value[0], value[1], value[1], value[2], value[2] });
            }

            if (value.Length != 6)
            {
                return false;
            }

            foreach (var ch in value)
            {
                if (!Uri.IsHexDigit(ch))
                {
                    return false;
                }
            }

            normalized = "#" + value.ToUpperInvariant();
            return true;
        }

        /// <summary>规范化纹理名：空 → null；认识 → 小写；不认识 → false。</summary>
        public static bool TryNormalizeTexture(string? text, out string? normalized)
        {
            normalized = null;
            var value = (text ?? "").Trim().ToLowerInvariant();
            if (value.Length == 0)
            {
                return true;
            }

            foreach (var known in Textures)
            {
                if (known == value)
                {
                    normalized = known;
                    return true;
                }
            }

            return false;
        }

        /// <summary>规范成 "AABBCC"（不含 #），便于转 Color。</summary>
        public static bool TryParseRgb(string? text, out int r, out int g, out int b)
        {
            r = g = b = 0;
            if (!TryNormalizeColor(text, out var normalized) || normalized == null)
            {
                return false;
            }

            var hex = normalized.Substring(1);
            r = int.Parse(hex.Substring(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            g = int.Parse(hex.Substring(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            b = int.Parse(hex.Substring(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            return true;
        }

        public static string ToHex(byte r, byte g, byte b)
        {
            return string.Format(CultureInfo.InvariantCulture, "#{0:X2}{1:X2}{2:X2}", r, g, b);
        }
    }
}
