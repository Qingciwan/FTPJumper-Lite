using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using FtpJumperLite.Models;

namespace FtpJumperLite.Ui
{
    /// <summary>
    /// 科目区的行高/内边距度量（全部是 96 DPI 逻辑像素，用前经 <see cref="SubjectListControl.DpiScale"/> 换算）。
    /// 放在单独的类型里，是为了让"档位 / 主题 / 以后的自定义"能只改这一个对象，而不碰绘制与交互代码。
    /// </summary>
    public sealed class RowMetrics
    {
        public int MinRowHeightLogical = 64;
        public int MaxRowHeightLogical = 152;
        public int DefaultRowHeightLogical = 92;
        public int MaxVisibleRows = 10;

        /// <summary>行距（卡片之间的竖向间隙）。</summary>
        public int RowGapLogical = 8;

        public int PadLeftLogical = 14;
        public int PadRightLogical = 14;
        public int PadTopLogical = 12;
        public int PadBottomLogical = 12;

        /// <summary>卡片内部左右留白（文字不贴圆角边）。</summary>
        public int CardPadXLogical = 12;

        /// <summary>卡片内部上下留白。</summary>
        public int CardPadYLogical = 6;

        /// <summary>Win11 卡片圆角。</summary>
        public int CornerRadiusLogical = 8;

        /// <summary>滚动条宽度（只占一根细条，浮在右内边距里）。</summary>
        public int ScrollBarWidthLogical = 6;

        /// <summary>字号拟合时预留的安全余量（逻辑像素）。</summary>
        public int TextSafetyLogical = 8;

        /// <summary>全局字号缩放倍数（阶段四接 settings.json 的 fontScale，100% = 1.0）。</summary>
        public float FontScale = 1F;

        public string FontFamilyName = "Microsoft YaHei UI";
    }

    /// <summary>
    /// 自绘的科目列表（取代原来的 TableLayoutPanel + 每行一个 Button）。
    ///
    /// 为什么自绘：
    /// 1) 原生 AutoScroll 滚动条不能换色、而且会与布局互相反馈（上一版那根"内容放得下却出现滚动条、
    ///    首行被滚出去"的 bug 就来自那个反馈环）。这里滚动条由我们自己按 ContentHeight/ClientHeight 判断，
    ///    完全不依赖 WinForms 的滚动实现，判定是确定性的。
    /// 2) 深色模式要求"主窗体 + 滚动条"可控，只有自绘能做到。
    /// 3) 每科自定义颜色/纹理（阶段四）需要在行底色与文字之间插一层绘制，自绘才有这个"层"。
    ///
    /// 布局是"一屏最多 MaxVisibleRows 行、行高统一"：行高由可用高度平分并夹在 [MinRowHeight, MaxRowHeight]、
    /// 再取内容下限（最小字号两行文本 + 内边距 + 安全余量），与上一版同一套口径。
    /// </summary>
    public sealed class SubjectListControl : Control
    {
        private const string SampleText = "国\r\n国";

        private IReadOnlyList<Subject> _subjects = new List<Subject>();
        private RowMetrics _metrics = new RowMetrics();
        private UiPalette _palette = UiPalette.Light();
        private readonly ToolTip _toolTip = new ToolTip();
        private Font? _titleFont;
        private Font? _subtitleFont;
        private Font? _hintFont;

        // 缩放后的像素值（RecalculateLayout 里算好）
        private int _padLeft, _padRight, _padTop, _padBottom;
        private int _gap, _corner, _cardPadX, _cardPadY, _sbWidth;
        private int _cardWidth, _cardHeight;

        private int _hoverIndex = -1;
        private int _pressedIndex = -1;
        private int _currentIndex = -1;
        private bool _draggingThumb;
        private int _dragOffsetY;
        private Rectangle _scrollTrack;
        private Rectangle _scrollThumb;

        public SubjectListControl()
        {
            // UserPaint + 双缓冲：自绘控件必须自己开，否则会闪烁。
            SetStyle(
                ControlStyles.UserPaint
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw
                | ControlStyles.Selectable,
                true);

            TabStop = true;
            Cursor = Cursors.Default;
            BackColor = _palette.FormBack;
            ForeColor = _palette.TitleText;
        }

        /// <summary>行被激活（单击 / 键盘 Enter）。</summary>
        public event EventHandler<SubjectEventArgs>? SubjectActivated;

        /// <summary>悬停时给 tooltip 的文本（由 MainForm 提供；返回 null 表示不显示）。</summary>
        public Func<Subject, string?>? TooltipProvider { get; set; }

        public IReadOnlyList<Subject> Subjects
        {
            get => _subjects;
            set
            {
                _subjects = value ?? new List<Subject>();
                if (_currentIndex >= _subjects.Count)
                {
                    _currentIndex = _subjects.Count - 1;
                }

                _hoverIndex = -1;
                _pressedIndex = -1;
                ScrollOffsetPx = 0;
                RelayoutNow();
            }
        }

        public RowMetrics Metrics
        {
            get => _metrics;
            set
            {
                _metrics = value ?? new RowMetrics();
                RelayoutNow();
            }
        }

        public UiPalette Palette
        {
            get => _palette;
            set
            {
                _palette = value ?? UiPalette.Light();
                BackColor = _palette.FormBack;
                ForeColor = _palette.TitleText;
                Invalidate();
            }
        }

        /// <summary>DPI 相对 96 的倍数（由 MainForm 注入，与全局缩放保持一致）。</summary>
        public float DpiScale
        {
            get => _dpiScale;
            set
            {
                if (value <= 0F)
                {
                    value = 1F;
                }

                if (Math.Abs(_dpiScale - value) > 0.001F)
                {
                    _dpiScale = value;
                    RelayoutNow();
                }
            }
        }

        private float _dpiScale = 1F;

        // ---------- 几何（对外只读，测试与 UiShot 都靠这套 API） ----------

        /// <summary>每行占用的竖向高度（含行距）。内容放得下时为"填满窗口"的行高。</summary>
        public int RowHeightPx { get; private set; }

        /// <summary>所有行的总高度（= 行数 × 行高），用于判断是否需要滚动条。</summary>
        public int ContentHeightPx { get; private set; }

        /// <summary>最大可滚动距离（0 表示不需要滚动条）。</summary>
        public int MaxScrollPx { get; private set; }

        /// <summary>当前滚动偏移（0 = 顶部）。</summary>
        public int ScrollOffsetPx { get; private set; }

        /// <summary>一屏能看到的行数（含半行，用于绘制循环）。</summary>
        public int VisibleRowCount
        {
            get
            {
                if (RowHeightPx <= 0)
                {
                    return 0;
                }

                var avail = ClientSize.Height - _padTop - _padBottom;
                return avail <= 0 ? 0 : (avail + RowHeightPx - 1) / RowHeightPx;
            }
        }

        /// <summary>当前键盘焦点所在的行（-1 = 无）。</summary>
        public int CurrentRowIndex
        {
            get => _currentIndex;
            set
            {
                var clamped = _subjects.Count == 0 ? -1 : Math.Max(0, Math.Min(value, _subjects.Count - 1));
                if (_currentIndex != clamped)
                {
                    _currentIndex = clamped;
                    ScrollToRow(clamped);
                    Invalidate();
                }
            }
        }

        /// <summary>当前科目名字号（诊断/测试用）。</summary>
        public float TitleFontSize => _titleFont?.Size ?? 0F;

        /// <summary>第 i 行的外框（客户区坐标，已含滚动偏移）。</summary>
        public Rectangle GetRowBounds(int index)
        {
            if (RowHeightPx <= 0 || index < 0 || index >= _subjects.Count)
            {
                return Rectangle.Empty;
            }

            return new Rectangle(
                _padLeft,
                _padTop + (index * RowHeightPx) - ScrollOffsetPx,
                Math.Max(1, _cardWidth),
                RowHeightPx);
        }

        /// <summary>命中的行号（-1 = 空白处）。</summary>
        public int GetRowIndexAt(Point point)
        {
            if (RowHeightPx <= 0 || _subjects.Count == 0)
            {
                return -1;
            }

            if (point.X < _padLeft || point.X > _padLeft + _cardWidth)
            {
                return -1;
            }

            var contentY = point.Y - _padTop + ScrollOffsetPx;
            if (contentY < 0)
            {
                return -1;
            }

            var index = contentY / RowHeightPx;
            return index >= 0 && index < _subjects.Count ? index : -1;
        }

        /// <summary>重新计算行高/卡片尺寸/字体（不依赖 WinForms 的布局反馈环，随时可安全调用）。</summary>
        public void RelayoutNow()
        {
            _padLeft = Scale(_metrics.PadLeftLogical);
            _padRight = Scale(_metrics.PadRightLogical);
            _padTop = Scale(_metrics.PadTopLogical);
            _padBottom = Scale(_metrics.PadBottomLogical);
            _gap = Scale(_metrics.RowGapLogical);
            _corner = Scale(_metrics.CornerRadiusLogical);
            _cardPadX = Scale(_metrics.CardPadXLogical);
            _cardPadY = Scale(_metrics.CardPadYLogical);
            _sbWidth = Scale(_metrics.ScrollBarWidthLogical);

            if (_subjects.Count == 0)
            {
                RowHeightPx = 0;
                ContentHeightPx = 0;
                MaxScrollPx = 0;
                ScrollOffsetPx = 0;
                _cardWidth = Math.Max(1, ClientSize.Width - _padLeft - _padRight);
                _cardHeight = 0;
                UpdateFonts(0F);
                Invalidate();
                return;
            }

            var availWidth = Math.Max(1, ClientSize.Width - _padLeft - _padRight);
            var availHeight = Math.Max(0, ClientSize.Height - _padTop - _padBottom);

            // 行高只取决于高度；滚动条只影响文本宽度，所以先算行高、再决定宽度，不存在循环依赖。
            var floor = ComputeContentFloorPx(availWidth);
            RowHeightPx = MainForm.ComputeRowHeightPx(
                availHeight,
                _subjects.Count,
                _dpiScale,
                floor);

            ContentHeightPx = RowHeightPx * _subjects.Count;
            MaxScrollPx = Math.Max(0, ContentHeightPx - availHeight);
            ScrollOffsetPx = Math.Max(0, Math.Min(ScrollOffsetPx, MaxScrollPx));

            var scrollVisible = MaxScrollPx > 0;
            var cardRight = ClientSize.Width - _padRight - (scrollVisible ? _sbWidth + Scale(4) : 0);
            _cardWidth = Math.Max(1, cardRight - _padLeft);
            _cardHeight = Math.Max(1, RowHeightPx - _gap);

            var textBudget = Math.Max(1, _cardHeight - (2 * _cardPadY) - Scale(_metrics.TextSafetyLogical));
            var textWidth = Math.Max(1, _cardWidth - (2 * _cardPadX));

            // 字号上下限一起按 fontScale 缩放：这样"全局字号"既能放大也能缩小，且始终被内容下限兜住。
            var titleSize = MainForm.ComputeFittingFontSize(
                SampleText,
                textWidth,
                textBudget,
                0,
                MainForm.MinSubjectNameFontSize * _metrics.FontScale,
                MainForm.MaxSubjectNameFontSize * _metrics.FontScale);
            UpdateFonts(titleSize);

            UpdateScrollGeometry(availHeight);
            Invalidate();
        }

        /// <summary>内容下限：最小字号下两行文本所需高度 + 卡片上下留白 + 安全余量（再留一行行距）。</summary>
        private int ComputeContentFloorPx(int availWidth)
        {
            if (_subjects.Count == 0)
            {
                return Scale(_metrics.MinRowHeightLogical);
            }

            var textWidth = Math.Max(1, availWidth - (2 * _cardPadX));
            using (var probe = new Font(
                _metrics.FontFamilyName,
                MainForm.MinSubjectNameFontSize * _metrics.FontScale,
                FontStyle.Bold))
            {
                var worst = 0;
                foreach (var subject in _subjects)
                {
                    var text = MainForm.Ellipsize(subject.Name, probe, textWidth)
                               + "\r\n"
                               + MainForm.Ellipsize(subject.DisplayLine, probe, textWidth);
                    worst = Math.Max(
                        worst,
                        MainForm.MeasureTwoLineHeight(text, MainForm.MinSubjectNameFontSize * _metrics.FontScale, textWidth));
                }

                return worst + (2 * _cardPadY) + Scale(_metrics.TextSafetyLogical) + _gap;
            }
        }

        public void ScrollToTop()
        {
            ScrollOffsetPx = 0;
            Invalidate();
        }

        /// <summary>把第 index 行滚进可视区（键盘导航用）。</summary>
        public void ScrollToRow(int index)
        {
            if (RowHeightPx <= 0 || index < 0)
            {
                return;
            }

            var availHeight = Math.Max(0, ClientSize.Height - _padTop - _padBottom);
            var top = index * RowHeightPx;
            var bottom = top + RowHeightPx;

            if (top < ScrollOffsetPx)
            {
                ScrollOffsetPx = top;
            }
            else if (bottom > ScrollOffsetPx + availHeight)
            {
                ScrollOffsetPx = bottom - availHeight;
            }

            ScrollOffsetPx = Math.Max(0, Math.Min(ScrollOffsetPx, MaxScrollPx));
        }

        // ---------- 绘制 ----------

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(_palette.FormBack);

            if (_subjects.Count == 0)
            {
                PaintHint(g);
                return;
            }

            // 文本用 GDI（TextRenderer）绘制：卡片底色是不透明实色，ClearType 才清晰。
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            var first = Math.Max(0, ScrollOffsetPx / Math.Max(1, RowHeightPx));
            var last = Math.Min(_subjects.Count - 1, first + VisibleRowCount);
            for (var i = first; i <= last; i++)
            {
                PaintRow(g, i);
            }

            PaintScrollBar(g);
        }

        private void PaintHint(Graphics g)
        {
            var text = "尚未加载到任何科目。\r\n\r\n请点击下方「打开配置目录」，"
                       + "用记事本编辑 subjects.json（参考随附示例）。\r\n保存后点击「刷新配置」。";
            var rect = new Rectangle(
                _padLeft + _cardPadX,
                _padTop,
                Math.Max(1, ClientSize.Width - _padLeft - _padRight - (2 * _cardPadX)),
                Math.Max(1, ClientSize.Height - _padTop - _padBottom));

            TextRenderer.DrawText(
                g,
                text,
                _hintFont ?? Font,
                rect,
                _palette.HintText,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
        }

        private void PaintRow(Graphics g, int index)
        {
            var bounds = GetRowBounds(index);
            if (bounds.IsEmpty || bounds.Bottom < 0 || bounds.Top > ClientSize.Height)
            {
                return;
            }

            // 卡片在行内上下各留半个行距 —— 与上一版按钮的 Margin 等效，视觉间距一致。
            var subject = _subjects[index];
            var card = new Rectangle(bounds.X, bounds.Y + (_gap / 2), _cardWidth, _cardHeight);
            var hover = index == _hoverIndex;
            var pressed = index == _pressedIndex && hover;

            var fill = pressed ? _palette.CardPressed : (hover ? _palette.CardHover : _palette.CardBack);

            // 每科强调色：极淡同色底用"不透明混色"做（不是 alpha），文字用 GDI 画上去才清晰。
            Color? subjectColor = null;
            if (ColorText.TryParse(subject.ColorHex, out var parsed))
            {
                subjectColor = parsed;
                fill = ColorText.Blend(fill, parsed, pressed ? 0.20 : hover ? 0.16 : 0.10);
            }

            using (var path = RoundedRect(card, _corner))
            {
                using (var brush = new SolidBrush(fill))
                {
                    g.FillPath(brush, path);
                }

                // 纹理：画在底色之上、文字之下，并裁在圆角卡片里（只影响卡片内部）。
                var texture = (subject.Texture ?? "").Trim().ToLowerInvariant();
                if (texture.Length > 0)
                {
                    var state = g.Save();
                    g.SetClip(path, CombineMode.Intersect);
                    PaintTexture(g, card, texture, subjectColor ?? _palette.Accent);
                    g.Restore(state);
                }

                // 左侧强调色条（有颜色才画）。
                if (subjectColor.HasValue)
                {
                    var state = g.Save();
                    g.SetClip(path, CombineMode.Intersect);
                    var barWidth = Math.Max(2, Scale(4));
                    var bar = new Rectangle(
                        card.X + Scale(5),
                        card.Y + Scale(6),
                        barWidth,
                        Math.Max(1, card.Height - Scale(12)));
                    using (var barPath = RoundedRect(bar, barWidth / 2))
                    using (var brush = new SolidBrush(subjectColor.Value))
                    {
                        g.FillPath(brush, barPath);
                    }

                    g.Restore(state);
                }

                using (var pen = new Pen(_palette.CardBorder))
                {
                    g.DrawPath(pen, path);
                }
            }

            // 键盘焦点：当前行内侧一圈强调色（2 逻辑像素），只在控件有焦点时出现。
            if (Focused && index == _currentIndex)
            {
                var ring = Rectangle.Inflate(card, -1, -1);
                using (var path = RoundedRect(ring, Math.Max(2, _corner - 1)))
                using (var pen = new Pen(_palette.Accent, Math.Max(1, Scale(2))))
                {
                    g.DrawPath(pen, path);
                }
            }

            // 有左侧色条时文字区右移一点，避免压住色条。
            var extraLeftPad = subjectColor.HasValue ? Scale(9) : 0;
            PaintRowText(g, card, subject, extraLeftPad);
        }

        /// <summary>预设底纹（后续再加图片文件）。全部用该科颜色以很低的不透明度绘制。</summary>
        private void PaintTexture(Graphics g, Rectangle card, string texture, Color color)
        {
            switch (texture)
            {
                case "stripe":
                {
                    using (var pen = new Pen(Color.FromArgb(24, color), 1F))
                    {
                        var step = Math.Max(4, Scale(9));
                        for (var x = card.X - card.Height; x < card.Right; x += step)
                        {
                            g.DrawLine(pen, x, card.Bottom, x + card.Height, card.Top);
                        }
                    }

                    break;
                }

                case "dots":
                {
                    var step = Math.Max(8, Scale(14));
                    var radius = Math.Max(1F, Scale(2) / 2F);
                    using (var brush = new SolidBrush(Color.FromArgb(32, color)))
                    {
                        for (var y = card.Y + (step / 2); y < card.Bottom; y += step)
                        {
                            for (var x = card.X + (step / 2); x < card.Right; x += step)
                            {
                                g.FillEllipse(brush, x - radius, y - radius, radius * 2, radius * 2);
                            }
                        }
                    }

                    break;
                }

                case "grid":
                {
                    var step = Math.Max(10, Scale(18));
                    using (var brush = new SolidBrush(Color.FromArgb(22, color)))
                    {
                        for (var x = card.X + step; x < card.Right; x += step)
                        {
                            g.FillRectangle(brush, x, card.Y, 1, card.Height);
                        }

                        for (var y = card.Y + step; y < card.Bottom; y += step)
                        {
                            g.FillRectangle(brush, card.X, y, card.Width, 1);
                        }
                    }

                    break;
                }

                case "gradient":
                {
                    using (var brush = new LinearGradientBrush(
                               card,
                               Color.FromArgb(34, color),
                               Color.FromArgb(0, color),
                               LinearGradientMode.Vertical))
                    {
                        g.FillRectangle(brush, card);
                    }

                    break;
                }
            }
        }

        private void PaintRowText(Graphics g, Rectangle card, Subject subject, int extraLeftPad)
        {
            if (_titleFont == null || _subtitleFont == null)
            {
                return;
            }

            var textLeft = card.X + _cardPadX + extraLeftPad;
            var textWidth = Math.Max(1, card.Width - (2 * _cardPadX) - extraLeftPad);
            var title = MainForm.Ellipsize(subject.Name, _titleFont, textWidth);
            var subtitle = MainForm.Ellipsize(subject.DisplayLine, _subtitleFont, textWidth);

            var singleLine = new Size(textWidth, int.MaxValue);
            var titleHeight = TextRenderer.MeasureText(
                title, _titleFont, singleLine, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Height;
            var subtitleHeight = TextRenderer.MeasureText(
                subtitle, _subtitleFont, singleLine, TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix).Height;

            var lineGap = Scale(2);
            var blockHeight = titleHeight + lineGap + subtitleHeight;
            var top = card.Top + Math.Max(0, (card.Height - blockHeight) / 2);

            var flags = TextFormatFlags.HorizontalCenter
                        | TextFormatFlags.SingleLine
                        | TextFormatFlags.NoPrefix
                        | TextFormatFlags.NoPadding;

            TextRenderer.DrawText(
                g,
                title,
                _titleFont,
                new Rectangle(textLeft, top, textWidth, titleHeight),
                _palette.TitleText,
                flags);

            TextRenderer.DrawText(
                g,
                subtitle,
                _subtitleFont,
                new Rectangle(textLeft, top + titleHeight + lineGap, textWidth, subtitleHeight),
                _palette.SubText,
                flags);
        }

        private void PaintScrollBar(Graphics g)
        {
            if (MaxScrollPx <= 0 || _scrollThumb.IsEmpty)
            {
                return;
            }

            var radius = Math.Max(2, _sbWidth / 2);
            var color = (_draggingThumb || _scrollThumb.Contains(PointToClient(Cursor.Position)))
                ? _palette.ScrollThumbHover
                : _palette.ScrollThumb;

            using (var path = RoundedRect(_scrollThumb, radius))
            using (var brush = new SolidBrush(color))
            {
                g.FillPath(brush, path);
            }
        }

        /// <summary>圆角矩形路径（半径按短边自动收敛，避免小尺寸下画歪）。</summary>
        private static GraphicsPath RoundedRect(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            if (rect.Width <= 0 || rect.Height <= 0)
            {
                return path;
            }

            var r = Math.Max(0, Math.Min(radius, Math.Min(rect.Width, rect.Height) / 2));
            if (r <= 0)
            {
                path.AddRectangle(rect);
                return path;
            }

            var d = r * 2;
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        private void UpdateFonts(float titleSize)
        {
            // 提示文字的尺寸也跟随全局字号（但保持在一个可用区间，避免缩到看不清）。
            var hintSize = 13F * Math.Max(0.7F, Math.Min(1.6F, _metrics.FontScale));

            if (titleSize <= 0F)
            {
                DisposeFont(ref _titleFont);
                DisposeFont(ref _subtitleFont);
                if (_hintFont == null || Math.Abs(_hintFont.Size - hintSize) > 0.01F)
                {
                    DisposeFont(ref _hintFont);
                    _hintFont = new Font(_metrics.FontFamilyName, hintSize);
                }

                return;
            }

            if (_hintFont == null || Math.Abs(_hintFont.Size - hintSize) > 0.01F)
            {
                DisposeFont(ref _hintFont);
                _hintFont = new Font(_metrics.FontFamilyName, hintSize);
            }

            var subtitleSize = Math.Max(9F, (float)Math.Round(titleSize * 0.8F, 1));
            if (_titleFont == null || Math.Abs(_titleFont.Size - titleSize) > 0.01F)
            {
                DisposeFont(ref _titleFont);
                _titleFont = new Font(_metrics.FontFamilyName, titleSize, FontStyle.Bold);
            }

            if (_subtitleFont == null || Math.Abs(_subtitleFont.Size - subtitleSize) > 0.01F)
            {
                DisposeFont(ref _subtitleFont);
                _subtitleFont = new Font(_metrics.FontFamilyName, subtitleSize, FontStyle.Regular);
            }
        }

        private static void DisposeFont(ref Font? font)
        {
            font?.Dispose();
            font = null;
        }

        private void UpdateScrollGeometry(int availHeight)
        {
            if (MaxScrollPx <= 0)
            {
                _scrollTrack = Rectangle.Empty;
                _scrollThumb = Rectangle.Empty;
                return;
            }

            var trackTop = _padTop;
            var trackHeight = Math.Max(1, availHeight);
            var trackX = ClientSize.Width - _padRight + Scale(4);

            _scrollTrack = new Rectangle(trackX, trackTop, _sbWidth, trackHeight);

            var thumbHeight = Math.Max(Scale(24), (int)(trackHeight * (double)availHeight / Math.Max(1, ContentHeightPx)));
            thumbHeight = Math.Min(thumbHeight, trackHeight);

            var travel = Math.Max(0, trackHeight - thumbHeight);
            var offset = MaxScrollPx <= 0 ? 0d : (double)ScrollOffsetPx / MaxScrollPx;
            var thumbTop = trackTop + (int)Math.Round(travel * offset);

            _scrollThumb = new Rectangle(trackX, thumbTop, _sbWidth, thumbHeight);
        }

        // ---------- 交互 ----------

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            RelayoutNow();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (_draggingThumb && !_scrollTrack.IsEmpty && MaxScrollPx > 0)
            {
                DragThumbTo(e.Y);
                return;
            }

            var index = GetRowIndexAt(e.Location);
            if (index != _hoverIndex)
            {
                _hoverIndex = index;
                Cursor = index >= 0 ? Cursors.Hand : Cursors.Default;
                UpdateTooltip(index);
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverIndex != -1 || _pressedIndex != -1)
            {
                _hoverIndex = -1;
                _pressedIndex = -1;
                Cursor = Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (!Enabled || e.Button != MouseButtons.Left)
            {
                return;
            }

            Focus();

            // 滚动条优先：点滑块开始拖动，点空白跳到该位置。
            if (MaxScrollPx > 0 && !_scrollThumb.IsEmpty && _scrollThumb.Contains(e.Location))
            {
                _draggingThumb = true;
                _dragOffsetY = e.Y - _scrollThumb.Top;
                Invalidate();
                return;
            }

            if (MaxScrollPx > 0 && !_scrollTrack.IsEmpty && _scrollTrack.Contains(e.Location))
            {
                DragThumbTo(e.Y);
                return;
            }

            var index = GetRowIndexAt(e.Location);
            if (index >= 0)
            {
                _pressedIndex = index;
                _currentIndex = index;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_draggingThumb)
            {
                _draggingThumb = false;
                Invalidate();
                return;
            }

            var pressed = _pressedIndex;
            _pressedIndex = -1;
            Invalidate();

            if (pressed < 0 || e.Button != MouseButtons.Left)
            {
                return;
            }

            var index = GetRowIndexAt(e.Location);
            if (index == pressed && index >= 0 && index < _subjects.Count)
            {
                SubjectActivated?.Invoke(this, new SubjectEventArgs(_subjects[index]));
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (MaxScrollPx <= 0)
            {
                return;
            }

            ScrollBy(-(e.Delta / 120) * Math.Max(1, RowHeightPx) * 3);
        }

        /// <summary>按像素滚动（正数向下）。</summary>
        public void ScrollBy(int deltaPx)
        {
            if (MaxScrollPx <= 0)
            {
                return;
            }

            var next = Math.Max(0, Math.Min(ScrollOffsetPx + deltaPx, MaxScrollPx));
            if (next != ScrollOffsetPx)
            {
                ScrollOffsetPx = next;
                UpdateScrollGeometry(Math.Max(0, ClientSize.Height - _padTop - _padBottom));
                Invalidate();
            }
        }

        private void DragThumbTo(int clientY)
        {
            if (_scrollTrack.IsEmpty || _scrollThumb.IsEmpty || MaxScrollPx <= 0)
            {
                return;
            }

            var travel = Math.Max(1, _scrollTrack.Height - _scrollThumb.Height);
            var top = Math.Max(0, Math.Min(clientY - _dragOffsetY - _scrollTrack.Top, travel));
            var target = (int)Math.Round((double)top / travel * MaxScrollPx);
            if (target != ScrollOffsetPx)
            {
                ScrollOffsetPx = Math.Max(0, Math.Min(target, MaxScrollPx));
                UpdateScrollGeometry(Math.Max(0, ClientSize.Height - _padTop - _padBottom));
                Invalidate();
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            switch (e.KeyCode)
            {
                case Keys.Up:
                    MoveCurrent(-1);
                    e.Handled = true;
                    break;
                case Keys.Down:
                    MoveCurrent(1);
                    e.Handled = true;
                    break;
                case Keys.PageUp:
                    MoveCurrent(-Math.Max(1, VisibleRowCount - 1));
                    e.Handled = true;
                    break;
                case Keys.PageDown:
                    MoveCurrent(Math.Max(1, VisibleRowCount - 1));
                    e.Handled = true;
                    break;
                case Keys.Home:
                    MoveCurrentTo(0);
                    e.Handled = true;
                    break;
                case Keys.End:
                    MoveCurrentTo(_subjects.Count - 1);
                    e.Handled = true;
                    break;
                case Keys.Enter:
                case Keys.Space:
                    if (_currentIndex >= 0 && _currentIndex < _subjects.Count)
                    {
                        SubjectActivated?.Invoke(this, new SubjectEventArgs(_subjects[_currentIndex]));
                    }

                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    break;
            }
        }

        private void MoveCurrent(int delta)
        {
            if (_subjects.Count == 0)
            {
                return;
            }

            MoveCurrentTo(_currentIndex < 0 ? 0 : _currentIndex + delta);
        }

        private void MoveCurrentTo(int index)
        {
            if (_subjects.Count == 0)
            {
                return;
            }

            _currentIndex = Math.Max(0, Math.Min(index, _subjects.Count - 1));
            ScrollToRow(_currentIndex);
            Invalidate();
        }

        // 方向键/Enter/空格要被当成输入键，否则会被窗体拿去导航。
        protected override bool IsInputKey(Keys keyData)
        {
            switch (keyData & Keys.KeyCode)
            {
                case Keys.Up:
                case Keys.Down:
                case Keys.Home:
                case Keys.End:
                case Keys.PageUp:
                case Keys.PageDown:
                case Keys.Enter:
                case Keys.Space:
                    return true;
            }

            return base.IsInputKey(keyData);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            if (_currentIndex < 0 && _subjects.Count > 0)
            {
                _currentIndex = 0;
            }

            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        private void UpdateTooltip(int index)
        {
            if (TooltipProvider == null || index < 0 || index >= _subjects.Count)
            {
                _toolTip.SetToolTip(this, null);
                return;
            }

            _toolTip.SetToolTip(this, TooltipProvider(_subjects[index]));
        }

        private int Scale(int logicalPixels)
        {
            return MainForm.ScaleFor(logicalPixels, _dpiScale);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                DisposeFont(ref _titleFont);
                DisposeFont(ref _subtitleFont);
                DisposeFont(ref _hintFont);
                _toolTip.Dispose();
            }

            base.Dispose(disposing);
        }
    }

    /// <summary>科目激活事件参数。</summary>
    public sealed class SubjectEventArgs : EventArgs
    {
        public SubjectEventArgs(Subject subject)
        {
            Subject = subject;
        }

        public Subject Subject { get; }
    }
}
