using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using FtpJumperLite.Models;

namespace FtpJumperLite.Ui
{
    /// <summary>"#RRGGBB" / 色名 与 System.Drawing.Color 之间的转换（主题与科目颜色共用）。</summary>
    public static class ColorText
    {
        public static bool TryParse(string? text, out Color color)
        {
            color = Color.Empty;
            if (!SubjectStyles.TryNormalizeColor(text, out var normalized) || normalized == null)
            {
                return false;
            }

            if (!SubjectStyles.TryParseRgb(normalized, out var r, out var g, out var b))
            {
                return false;
            }

            color = Color.FromArgb(r, g, b);
            return true;
        }

        public static string ToText(Color color)
        {
            return SubjectStyles.ToHex(color.R, color.G, color.B);
        }

        /// <summary>按比例把两种颜色混合成不透明色（用不透明混色而不是 alpha，避免 ClearType 发虚）。</summary>
        public static Color Blend(Color baseColor, Color tint, double tintRatio)
        {
            var t = Math.Max(0.0, Math.Min(1.0, tintRatio));
            return Color.FromArgb(
                Mix(baseColor.R, tint.R, t),
                Mix(baseColor.G, tint.G, t),
                Mix(baseColor.B, tint.B, t));
        }

        private static int Mix(byte a, byte b, double t)
        {
            return Math.Max(0, Math.Min(255, (int)Math.Round(a + ((b - a) * t))));
        }

        /// <summary>按比例把颜色向黑（ratio &gt; 0）或白（ratio &lt; 0）靠，用于底纹的明暗变化。</summary>
        public static Color Shade(Color color, double ratio)
        {
            var t = Math.Max(0.0, Math.Min(1.0, Math.Abs(ratio)));
            return Blend(color, ratio >= 0 ? Color.Black : Color.White, t);
        }
    }
}
