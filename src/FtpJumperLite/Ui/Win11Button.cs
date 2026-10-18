using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace FtpJumperLite.Ui
{
    /// <summary>
    /// Win11 风格的扁平圆角按钮（自绘）。底部工具条的三个按钮用它，避免"卡片 + 立体按钮"的风格割裂。
    /// 只负责画法与悬停/按下状态；尺寸行为仍由 Button 的 AutoSize / MinimumSize 决定（与原来一致）。
    /// </summary>
    public sealed class Win11Button : Button
    {
        private bool _hover;
        private bool _pressed;

        public Win11Button()
        {
            SetStyle(
                ControlStyles.UserPaint
                | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.ResizeRedraw,
                true);

            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            UseVisualStyleBackColor = false;
            Cursor = Cursors.Hand;
        }

        /// <summary>DPI 相对 96 的倍数（由 MainForm 注入；DeviceDpi 在本进程里不可靠）。</summary>
        public float DpiScale { get; set; } = 1F;

        public UiPalette Palette { get; set; } = UiPalette.Light();

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.Half;
            g.Clear(Palette.FormBack);

            var radius = Math.Max(2, MainForm.ScaleFor(6, DpiScale));
            var rect = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));

            var fill = !Enabled
                ? Palette.CardBack
                : _pressed
                    ? Palette.CardPressed
                    : _hover
                        ? Palette.CardHover
                        : Palette.CardBack;

            using (var path = RoundedRect(rect, radius))
            using (var brush = new SolidBrush(fill))
            using (var pen = new Pen(Palette.CardBorder, 1F))
            {
                g.FillPath(brush, path);
                g.DrawPath(pen, path);
            }

            if (Focused && Enabled)
            {
                using (var path = RoundedRect(Rectangle.Inflate(rect, -1, -1), Math.Max(2, radius - 1)))
                using (var pen = new Pen(Palette.Accent, Math.Max(1, MainForm.ScaleFor(2, DpiScale))))
                {
                    g.DrawPath(pen, path);
                }
            }

            var textColor = Enabled ? Palette.TitleText : Palette.SubText;
            TextRenderer.DrawText(
                g,
                Text,
                Font,
                rect,
                textColor,
                TextFormatFlags.HorizontalCenter
                | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine
                | TextFormatFlags.NoPrefix
                | TextFormatFlags.NoPadding);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            _pressed = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _pressed = true;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _pressed = false;
            Invalidate();
        }

        protected override void OnGotFocus(EventArgs e)
        {
            base.OnGotFocus(e);
            Invalidate();
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            Invalidate();
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Cursor = Enabled ? Cursors.Hand : Cursors.Default;
            Invalidate();
        }

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
    }
}
