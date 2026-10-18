using System;
using System.Drawing;
using FtpJumperLite.Models;
using Microsoft.Win32;

namespace FtpJumperLite.Ui
{
    /// <summary>一套主题的浅/深两侧（由 settings.json 决定，可能是内置、被覆盖，或由自定义一侧推导）。</summary>
    public sealed class ThemeSet
    {
        public ThemeSet(UiPalette light, UiPalette dark, string? note)
        {
            Light = light;
            Dark = dark;
            Note = note;
        }

        public UiPalette Light { get; }

        public UiPalette Dark { get; }

        /// <summary>解析过程中的提示（例如某个颜色写错被忽略），空 = 无。</summary>
        public string? Note { get; }
    }

    /// <summary>
    /// 主题解析：settings.json + 系统深浅色 → 具体调色板。
    /// 语义：主题模式只决定"用哪一侧"，颜色永远以调色板为准（自定义主题存在时优先）。
    /// </summary>
    public static class ThemeResolver
    {
        /// <summary>系统当前是否是深色（Win10 1809+ / Win11 的"应用"主题）。读不到按浅色处理。</summary>
        public static bool IsSystemDarkTheme()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(
                           @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                {
                    var value = key?.GetValue("AppsUseLightTheme");
                    if (value is int light)
                    {
                        return light == 0;
                    }
                }
            }
            catch
            {
                // 读不到就按浅色。
            }

            return false;
        }

        /// <summary>把 settings 的覆盖套到内置调色板上，得到最终的浅/深两侧。</summary>
        public static ThemeSet BuildThemeSet(UiSettings? settings)
        {
            var note = string.Empty;
            var light = UiPalette.Light();
            var dark = UiPalette.Dark();

            var overrides = settings?.Palette;
            if (overrides != null)
            {
                Apply(light, overrides.Light, ref note);
                Apply(dark, overrides.Dark, ref note);

                // 自定义主题：写一侧，另一侧按语义角色推导（这就是"自定义主题允许浅深互转"）。
                if (overrides.Custom != null && !overrides.Custom.IsEmpty)
                {
                    var given = UiPalette.Light();
                    Apply(given, overrides.Custom, ref note);
                    var other = ThemeDeriver.DeriveCounterpart(given);

                    if (ThemeDeriver.IsDarkish(given))
                    {
                        dark = given;
                        light = other;
                    }
                    else
                    {
                        light = given;
                        dark = other;
                    }
                }
            }

            return new ThemeSet(light, dark, note.Length == 0 ? null : note);
        }

        /// <summary>按模式与系统深浅色选一侧。</summary>
        public static UiPalette Resolve(UiSettings? settings, bool systemDark, out bool isDark, out string? note)
        {
            var set = BuildThemeSet(settings);
            note = set.Note;

            var mode = (settings?.Theme ?? "system").Trim().ToLowerInvariant();
            isDark = mode == "dark" || ((mode == "system" || mode == "custom") && systemDark);
            return isDark ? set.Dark : set.Light;
        }

        /// <summary>把 ThemeColors 里写到的键覆盖到调色板上；写错的颜色忽略并累积提示。</summary>
        private static void Apply(UiPalette palette, ThemeColors? colors, ref string note)
        {
            if (palette == null || colors == null)
            {
                return;
            }

            palette.FormBack = Pick(colors.FormBack, palette.FormBack, "formBack", ref note);
            palette.CardBack = Pick(colors.CardBack, palette.CardBack, "cardBack", ref note);
            palette.CardBorder = Pick(colors.CardBorder, palette.CardBorder, "cardBorder", ref note);
            palette.CardHover = Pick(colors.CardHover, palette.CardHover, "cardHover", ref note);
            palette.CardPressed = Pick(colors.CardPressed, palette.CardPressed, "cardPressed", ref note);
            palette.TitleText = Pick(colors.TitleText, palette.TitleText, "titleText", ref note);
            palette.SubText = Pick(colors.SubText, palette.SubText, "subText", ref note);
            palette.Accent = Pick(colors.Accent, palette.Accent, "accent", ref note);
            palette.ScrollThumb = Pick(colors.ScrollThumb, palette.ScrollThumb, "scrollThumb", ref note);
            palette.ScrollThumbHover = Pick(colors.ScrollThumbHover, palette.ScrollThumbHover, "scrollThumbHover", ref note);
            palette.HintText = Pick(colors.HintText, palette.HintText, "hintText", ref note);
        }

        private static Color Pick(string? text, Color fallback, string key, ref string note)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return fallback;
            }

            if (ColorText.TryParse(text, out var color))
            {
                return color;
            }

            note += "palette." + key + "=" + text + " 不是合法颜色，已忽略；";
            return fallback;
        }
    }
}
