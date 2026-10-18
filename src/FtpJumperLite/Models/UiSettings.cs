using System;
using Newtonsoft.Json;

namespace FtpJumperLite.Models
{
    /// <summary>
    /// settings.json 的根结构（个性化配置）。与 subjects.json 放在同一目录。
    /// 约定：这里只放"界面/个性化"配置；每科的颜色与纹理属于科目数据，写在 subjects.json 里。
    /// </summary>
    public sealed class UiSettings
    {
        [JsonProperty("version")]
        public int Version { get; set; } = 1;

        /// <summary>主题模式：system（跟随系统，默认）/ light / dark / custom。</summary>
        [JsonProperty("theme")]
        public string Theme { get; set; } = "system";

        /// <summary>全局字号缩放（百分比整数，60~200，100 = 默认）。</summary>
        [JsonProperty("fontScale")]
        public int FontScale { get; set; } = 100;

        /// <summary>调色板覆盖（可全部留空）。</summary>
        [JsonProperty("palette")]
        public PaletteOverrides? Palette { get; set; }

        public static UiSettings CreateDefault()
        {
            return new UiSettings();
        }

        /// <summary>把外部输入（手改的 json / 设置窗口）收敛到合法值，返回被修正项的说明（空 = 无需修正）。</summary>
        public string Normalize()
        {
            var notes = string.Empty;

            var theme = (Theme ?? "").Trim().ToLowerInvariant();
            if (theme != "system" && theme != "light" && theme != "dark" && theme != "custom")
            {
                if (theme.Length > 0)
                {
                    notes += "theme=" + Theme + " 不认识，已回退为 system；";
                }

                theme = "system";
            }

            Theme = theme;

            if (FontScale < 60 || FontScale > 200)
            {
                notes += "fontScale=" + FontScale + " 超出 60~200，已夹紧；";
                FontScale = Math.Max(60, Math.Min(200, FontScale <= 0 ? 100 : FontScale));
            }

            if (Version <= 0)
            {
                Version = 1;
            }

            return notes;
        }
    }

    /// <summary>调色板覆盖：内置浅/深各自可覆盖，custom 则给自定义主题的两侧（可只写一侧，另一侧自动推导）。</summary>
    public sealed class PaletteOverrides
    {
        [JsonProperty("light")]
        public ThemeColors? Light { get; set; }

        [JsonProperty("dark")]
        public ThemeColors? Dark { get; set; }

        /// <summary>自定义主题：可只写 light 或只写 dark，缺的一侧按语义角色推导。</summary>
        [JsonProperty("custom")]
        public ThemeColors? Custom { get; set; }
    }

    /// <summary>
    /// 一套主题的 11 个颜色（都写成 "#RRGGBB"）。所有字段可空：只覆盖写到的键，其余用内置值。
    /// </summary>
    public sealed class ThemeColors
    {
        [JsonProperty("formBack")]
        public string? FormBack { get; set; }

        [JsonProperty("cardBack")]
        public string? CardBack { get; set; }

        [JsonProperty("cardBorder")]
        public string? CardBorder { get; set; }

        [JsonProperty("cardHover")]
        public string? CardHover { get; set; }

        [JsonProperty("cardPressed")]
        public string? CardPressed { get; set; }

        [JsonProperty("titleText")]
        public string? TitleText { get; set; }

        [JsonProperty("subText")]
        public string? SubText { get; set; }

        [JsonProperty("accent")]
        public string? Accent { get; set; }

        [JsonProperty("scrollThumb")]
        public string? ScrollThumb { get; set; }

        [JsonProperty("scrollThumbHover")]
        public string? ScrollThumbHover { get; set; }

        [JsonProperty("hintText")]
        public string? HintText { get; set; }

        /// <summary>是否一个键都没写。</summary>
        public bool IsEmpty =>
            FormBack == null && CardBack == null && CardBorder == null && CardHover == null
            && CardPressed == null && TitleText == null && SubText == null && Accent == null
            && ScrollThumb == null && ScrollThumbHover == null && HintText == null;
    }
}
