using System;
using System.Drawing;

namespace FtpJumperLite.Ui
{
    /// <summary>
    /// 调色板推导：自定义主题只写一侧时，用它生成另一侧。
    ///
    /// 规则刻意保持"可预期"：色相（Hue）原样保留，饱和度略降（大面积深色太鲜艳会刺眼），
    /// 明度换成目标侧的区间；文字类颜色最后再做一次对比度兜底（正文对卡片 ≥ 4.5:1）。
    /// </summary>
    public static class ThemeDeriver
    {
        /// <summary>这一侧是不是"深色"（用卡片底色判断）。</summary>
        public static bool IsDarkish(UiPalette palette)
        {
            return RelativeLuminance(palette.CardBack) < 0.35;
        }

        /// <summary>由给定的那一侧推导出另一侧。</summary>
        public static UiPalette DeriveCounterpart(UiPalette given)
        {
            return IsDarkish(given) ? Derive(given, toDark: false) : Derive(given, toDark: true);
        }

        private static UiPalette Derive(UiPalette src, bool toDark)
        {
            var result = new UiPalette
            {
                FormBack = Move(src.FormBack, toDark, 0.10, 0.13, 0.92, 0.95, 0.55),
                CardBack = Move(src.CardBack, toDark, 0.15, 0.18, 0.97, 1.00, 0.55),
                CardBorder = Move(src.CardBorder, toDark, 0.22, 0.28, 0.84, 0.90, 0.55),
                CardHover = Move(src.CardHover, toDark, 0.18, 0.21, 0.94, 0.97, 0.55),
                CardPressed = Move(src.CardPressed, toDark, 0.21, 0.25, 0.90, 0.94, 0.55),
                ScrollThumb = Move(src.ScrollThumb, toDark, 0.30, 0.38, 0.72, 0.80, 0.55),
                ScrollThumbHover = Move(src.ScrollThumbHover, toDark, 0.42, 0.52, 0.60, 0.68, 0.55),
                TitleText = Move(src.TitleText, toDark, 0.88, 0.94, 0.08, 0.14, 1.0),
                SubText = Move(src.SubText, toDark, 0.60, 0.70, 0.38, 0.48, 1.0),
                Accent = Move(src.Accent, toDark, 0.58, 0.72, 0.34, 0.46, 1.0)
            };

            result.HintText = result.SubText;
            result.TitleText = EnsureContrast(result.TitleText, result.CardBack, result.TitleText, toDark);
            result.SubText = EnsureContrast(result.SubText, result.CardBack, result.TitleText, toDark);
            result.HintText = result.SubText;
            return result;
        }

        /// <summary>换明度并落到目标区间；色相保留、饱和度按比例缩放。</summary>
        private static Color Move(Color color, bool toDark, double darkMin, double darkMax, double lightMin, double lightMax, double satScale)
        {
            double h, s, l;
            ToHsl(color, out h, out s, out l);

            var min = toDark ? darkMin : lightMin;
            var max = toDark ? darkMax : lightMax;
            var target = Clamp(1.0 - l, min, max);

            return FromHsl(h, Clamp(s * satScale, 0.0, 1.0), target);
        }

        /// <summary>对比度兜底：不够就把文字明度往"更远离卡片底色"的方向推，直到达标或触边。</summary>
        private static Color EnsureContrast(Color ink, Color surface, Color fallbackInk, bool toDark)
        {
            if (ContrastRatio(ink, surface) >= 4.5)
            {
                return ink;
            }

            double h, s, l;
            ToHsl(ink, out h, out s, out l);

            var step = toDark ? 0.03 : -0.03;
            for (var i = 0; i < 24; i++)
            {
                l = Clamp(l + step, 0.0, 1.0);
                var candidate = FromHsl(h, s, l);
                if (ContrastRatio(candidate, surface) >= 4.5)
                {
                    return candidate;
                }

                if ((toDark && l >= 1.0) || (!toDark && l <= 0.0))
                {
                    break;
                }
            }

            // 实在调不出来（极端自定义配色）就退回该侧的高对比默认色。
            return ContrastRatio(fallbackInk, surface) >= 4.5 ? fallbackInk : ink;
        }

        // ---------- 颜色数学 ----------

        public static double RelativeLuminance(Color color)
        {
            return 0.2126 * Channel(color.R) + 0.7152 * Channel(color.G) + 0.0722 * Channel(color.B);

            double Channel(byte v)
            {
                var c = v / 255.0;
                return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4);
            }
        }

        /// <summary>WCAG 对比度（1~21）。</summary>
        public static double ContrastRatio(Color a, Color b)
        {
            var la = RelativeLuminance(a);
            var lb = RelativeLuminance(b);
            var light = Math.Max(la, lb);
            var dark = Math.Min(la, lb);
            return (light + 0.05) / (dark + 0.05);
        }

        private static void ToHsl(Color color, out double h, out double s, out double l)
        {
            var r = color.R / 255.0;
            var g = color.G / 255.0;
            var b = color.B / 255.0;
            var max = Math.Max(r, Math.Max(g, b));
            var min = Math.Min(r, Math.Min(g, b));
            l = (max + min) / 2.0;

            if (Math.Abs(max - min) < 0.0001)
            {
                h = 0;
                s = 0;
                return;
            }

            var d = max - min;
            s = l > 0.5 ? d / (2.0 - max - min) : d / (max + min);

            if (Math.Abs(max - r) < 0.0001)
            {
                h = (g - b) / d + (g < b ? 6.0 : 0.0);
            }
            else if (Math.Abs(max - g) < 0.0001)
            {
                h = (b - r) / d + 2.0;
            }
            else
            {
                h = (r - g) / d + 4.0;
            }

            h /= 6.0;
        }

        private static Color FromHsl(double h, double s, double l)
        {
            h = ((h % 1.0) + 1.0) % 1.0;
            s = Clamp(s, 0.0, 1.0);
            l = Clamp(l, 0.0, 1.0);

            var q = l < 0.5 ? l * (1 + s) : l + s - (l * s);
            var p = (2 * l) - q;

            return Color.FromArgb(
                ToByte(HueToRgb(p, q, h + (1.0 / 3.0))),
                ToByte(HueToRgb(p, q, h)),
                ToByte(HueToRgb(p, q, h - (1.0 / 3.0))));
        }

        private static double HueToRgb(double p, double q, double t)
        {
            t = ((t % 1.0) + 1.0) % 1.0;
            if (t < 1.0 / 6.0)
            {
                return p + ((q - p) * 6.0 * t);
            }

            if (t < 0.5)
            {
                return q;
            }

            if (t < 2.0 / 3.0)
            {
                return p + ((q - p) * 6.0 * ((2.0 / 3.0) - t));
            }

            return p;
        }

        private static int ToByte(double v)
        {
            return Math.Max(0, Math.Min(255, (int)Math.Round(v * 255.0)));
        }

        private static double Clamp(double v, double min, double max)
        {
            return v < min ? min : v > max ? max : v;
        }
    }
}
