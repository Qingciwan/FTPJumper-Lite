using System.Drawing;

namespace FtpJumperLite.Ui
{
    /// <summary>
    /// 配色表（阶段二先用内置的 Win11 近似值；阶段四由 settings.json 的调色板填充，
    /// 深色/浅色各一套，自定义主题也走这个结构）。
    /// 只包含"颜色"这一个维度 —— 字体、尺寸不在里面。
    /// </summary>
    public sealed class UiPalette
    {
        /// <summary>页面底色（卡片之外的部分）。</summary>
        public Color FormBack { get; set; }

        /// <summary>科目卡片底色。</summary>
        public Color CardBack { get; set; }

        /// <summary>卡片描边（1 逻辑像素）。</summary>
        public Color CardBorder { get; set; }

        /// <summary>鼠标悬停时的卡片底色。</summary>
        public Color CardHover { get; set; }

        /// <summary>按下时的卡片底色。</summary>
        public Color CardPressed { get; set; }

        /// <summary>科目名颜色。</summary>
        public Color TitleText { get; set; }

        /// <summary>副标题（主机 · 路径）颜色。</summary>
        public Color SubText { get; set; }

        /// <summary>强调色：焦点圈、滚动条按下态等。</summary>
        public Color Accent { get; set; }

        /// <summary>滚动条滑块。</summary>
        public Color ScrollThumb { get; set; }

        /// <summary>滚动条滑块（悬停/拖动）。</summary>
        public Color ScrollThumbHover { get; set; }

        /// <summary>空列表提示文字颜色。</summary>
        public Color HintText { get; set; }

        /// <summary>Win11 浅色近似值：页面 #F3F3F3、卡片纯白、描边 #E5E5E5、正文近黑、副文本灰。</summary>
        public static UiPalette Light()
        {
            return new UiPalette
            {
                FormBack = Color.FromArgb(0xF3, 0xF3, 0xF3),
                CardBack = Color.White,
                CardBorder = Color.FromArgb(0xE5, 0xE5, 0xE5),
                CardHover = Color.FromArgb(0xF7, 0xF8, 0xFA),
                CardPressed = Color.FromArgb(0xEF, 0xF1, 0xF4),
                TitleText = Color.FromArgb(0x1A, 0x1A, 0x1A),
                SubText = Color.FromArgb(0x6E, 0x6E, 0x6E),
                Accent = Color.FromArgb(0x00, 0x67, 0xC0),
                ScrollThumb = Color.FromArgb(0xC8, 0xC8, 0xC8),
                ScrollThumbHover = Color.FromArgb(0xA6, 0xA6, 0xA6),
                HintText = Color.FromArgb(0x6E, 0x6E, 0x6E)
            };
        }

        /// <summary>Win11 深色近似值（阶段四接主题时直接用）。</summary>
        public static UiPalette Dark()
        {
            return new UiPalette
            {
                FormBack = Color.FromArgb(0x20, 0x20, 0x20),
                CardBack = Color.FromArgb(0x2B, 0x2B, 0x2B),
                CardBorder = Color.FromArgb(0x3A, 0x3A, 0x3A),
                CardHover = Color.FromArgb(0x32, 0x32, 0x32),
                CardPressed = Color.FromArgb(0x3B, 0x3B, 0x3B),
                TitleText = Color.FromArgb(0xF0, 0xF0, 0xF0),
                SubText = Color.FromArgb(0xA8, 0xA8, 0xA8),
                Accent = Color.FromArgb(0x4C, 0xC2, 0xFF),
                ScrollThumb = Color.FromArgb(0x5A, 0x5A, 0x5A),
                ScrollThumbHover = Color.FromArgb(0x78, 0x78, 0x78),
                HintText = Color.FromArgb(0xA8, 0xA8, 0xA8)
            };
        }
    }
}
