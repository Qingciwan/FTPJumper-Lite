using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using FtpJumperLite.Models;
using FtpJumperLite.Services;

namespace FtpJumperLite.Ui
{
    /// <summary>
    /// 主窗体：科目选择界面（单列列表，默认正好 10 行，投影友好）。
    /// 单击科目 = 拼 ftp:// 网址交给文件资源管理器打开；界面本身不提供科目增删改（由教师手工编辑 subjects.json）。
    ///
    /// 布局三条硬约束（都是为了高 DPI，尤其 2K@200%）：
    /// 1) 所有尺寸常量都是 <b>96 DPI 逻辑像素</b>，用前一律经 <see cref="Scale"/> 换算；
    ///    字号是“点”（随 DPI 自动放大），常量若不同步放大，就会出现“行高小于文字所需高度”。
    /// 2) 行高永远不小于“最小字号下两行文本 + 内边距 + 行间距”所需的高度（内容下限），
    ///    否则 TableLayoutPanel 会自行重排：把多余空间塞给最后一行（表现为最后一个按钮被撑开）。
    /// 3) 科目表末尾**永远**保留一个吸收行，滚不动时由它吃掉剩余像素，绝不让某个科目行被拉伸。
    /// </summary>
    public sealed class MainForm : Form
    {
        private const int MaxVisibleRows = 10;

        // ---- 96 DPI 逻辑常量（实际使用前必须经过 Scale()）----
        private const int MinRowHeightLogical = 64;
        private const int MaxRowHeightLogical = 152;
        private const int DefaultRowHeightLogical = 92;
        private const int RowMarginLogical = 8;
        private const int BottomBarHeightLogical = 68;
        private const int MinFormWidthLogical = 470;
        private const int MinFormHeightLogical = 520;
        private const int ButtonMinWidthLogical = 120;
        private const int ButtonMinHeightLogical = 40;
        private const int TablePadLeftLogical = 14;
        private const int TablePadTopLogical = 12;
        private const int TablePadRightLogical = 14;
        private const int TablePadBottomLogical = 8;
        private const int BottomPadLeftLogical = 12;
        private const int BottomPadTopLogical = 8;
        private const int BottomPadRightLogical = 12;
        private const int BottomPadBottomLogical = 10;
        private const int DefaultFormMarginLogical = 40;
        private const int DefaultFormSideMarginLogical = 80;
        private const int DefaultFormMinWidthLogical = 430;
        private const int DefaultFormMaxWidthLogical = 760;
        private const float MinSubjectNameFontSize = 12F;
        private const float MaxSubjectNameFontSize = 17F;

        /// <summary>Button 默认 Padding=3，文字区比按钮本身窄/矮 6px。</summary>
        private const int ButtonPadding = 6;

        /// <summary>
        /// 字号拟合时预留的安全余量（逻辑像素）：主题按钮的文字区除 Padding 外还有一点内缩，
        /// 若按“测得高度 == 可用高度”卡边界，第二行会被裁掉。留一点余量最稳。
        /// </summary>
        private const int TextSafetyLogical = 8;

        private readonly string _configPath;
        private readonly ConfigStore _configStore;
        private readonly TableLayoutPanel _root;
        private readonly TableLayoutPanel _bottom;
        private readonly TableLayoutPanel _subjectTable;
        private readonly Button _btnRefresh;
        private readonly Button _btnOpenConfig;
        private readonly Button _btnQuit;
        private readonly ToolStripStatusLabel _statusLabel;
        private readonly ToolTip _toolTip = new ToolTip();
        private readonly List<Button> _subjectRows = new List<Button>();
        private List<Subject> _subjects = new List<Subject>();
        private RowStyle? _spacerRowStyle;
        private float _dpiScale = 1F;
        /// <summary>构造函数是否已跑完；在此之前任何字段都可能还是 null，禁止按 DPI 缩放。</summary>
        private bool _layoutReady;
        private bool _inRowLayout;

        public MainForm(string? configPath)
        {
            _configPath = string.IsNullOrWhiteSpace(configPath) ? ConfigStore.DefaultConfigPath : configPath!;
            _configStore = new ConfigStore(_configPath);

            // 自己按 DPI 缩放全部尺寸，禁用 WinForms 的字体自动缩放，避免“双重缩放 / 尺寸不可预期”。
            AutoScaleMode = AutoScaleMode.None;
            _dpiScale = CurrentDpiScale();

            Font = new Font("Microsoft YaHei UI", 11F);
            Text = "课堂资料助手 Lite · FtpJumper";
            StartPosition = FormStartPosition.CenterScreen;

            var appIcon = AppIcon.Value;
            if (appIcon != null)
            {
                Icon = appIcon;
            }

            // 默认尺寸：单列刚好放下 10 个科目，并尽量占满屏幕可用高度。
            // workArea 已经是当前 DPI 下的像素，只有“下限/上限”这类常量需要 Scale。
            var workArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
            var defaultHeight = Math.Max(Scale(MinFormHeightLogical), workArea.Height - Scale(DefaultFormMarginLogical));
            var defaultWidth = (int)Math.Round(defaultHeight * 0.72);
            defaultWidth = Math.Min(defaultWidth, workArea.Width - Scale(DefaultFormSideMarginLogical));
            defaultWidth = Math.Min(Scale(DefaultFormMaxWidthLogical), Math.Max(Scale(DefaultFormMinWidthLogical), defaultWidth));
            Size = new Size(defaultWidth, defaultHeight);
            // 最小宽度必须能完整容纳底部三个按钮（各 120 + 间距 + 边距），否则会被裁到窗口外。
            MinimumSize = new Size(Scale(MinFormWidthLogical), Scale(MinFormHeightLogical));

            _root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
            // 列必须固定占满整宽：不设 ColumnStyle 时列会按内容 AutoSize，
            // 结果是被科目按钮的文字撑得比窗口还宽，整行向右溢出、左右留白不再对称。
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, Scale(BottomBarHeightLogical)));
            _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            _subjectTable = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 1,
                AutoScroll = true,
                Padding = new Padding(
                    Scale(TablePadLeftLogical),
                    Scale(TablePadTopLogical),
                    Scale(TablePadRightLogical),
                    Scale(TablePadBottomLogical)),
                BackColor = SystemColors.Control
            };
            // 列固定占满整宽：不设 ColumnStyle 时列按内容 AutoSize，文字一长就会把列撑得比表格还宽，
            // 结果是整行偏左、右侧留白被吃掉（高 DPI 下文字变宽时尤其明显）。
            _subjectTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            // 文本已按像素宽度截断，横向永远不需要滚动。
            _subjectTable.HorizontalScroll.Enabled = false;
            _subjectTable.Resize += (_, _) => ApplyRowLayout();
            _subjectTable.Layout += (_, _) => ApplyRowLayout();
            _root.Controls.Add(_subjectTable, 0, 0);

            _btnRefresh = MakeBottomButton("刷新配置", (_, _) => ReloadSubjects());
            _btnOpenConfig = MakeBottomButton("打开配置目录", OpenConfigDirectory);
            _btnQuit = MakeBottomButton("退出", (_, _) => Close());

            // 底部工具条：左侧弹性空白 + 右侧按钮，保证按钮永远完整落在窗口内。
            _bottom = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                Padding = new Padding(
                    Scale(BottomPadLeftLogical),
                    Scale(BottomPadTopLogical),
                    Scale(BottomPadRightLogical),
                    Scale(BottomPadBottomLogical))
            };
            _bottom.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _bottom.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _bottom.Controls.Add(_btnRefresh, 1, 0);
            _bottom.Controls.Add(_btnOpenConfig, 2, 0);
            _bottom.Controls.Add(_btnQuit, 3, 0);
            _root.Controls.Add(_bottom, 0, 1);

            var statusStrip = new StatusStrip();
            _statusLabel = new ToolStripStatusLabel { Spring = false };
            statusStrip.Items.Add(_statusLabel);
            _root.Controls.Add(statusStrip, 0, 2);

            Controls.Add(_root);

            _statusLabel.Text = "就绪：单击科目 = 直接打开该科目的 FTP 网址；修改配置后请点「刷新配置」。";
            ReloadSubjects();

            // 字段全部就绪：从这里开始才允许按真实 DPI 重新缩放（见 TryApplyDpiScaling）。
            _layoutReady = true;
        }

        // ---------- DPI ----------

        /// <summary>当前 DPI 相对 96 的倍数（1.0 = 100%，2.0 = 200%）。</summary>
        public float DpiScale => _dpiScale;

        /// <summary>把 96 DPI 逻辑像素换算成当前 DPI 的像素。</summary>
        public static int ScaleFor(int logicalPixels, float dpiScale)
        {
            if (dpiScale <= 0F)
            {
                dpiScale = 1F;
            }

            return (int)Math.Round(logicalPixels * dpiScale, MidpointRounding.AwayFromZero);
        }

        private int Scale(int logicalPixels) => ScaleFor(logicalPixels, _dpiScale);

        private float CurrentDpiScale()
        {
            var dpi = 0F;

            // 1) 首选 WinForms 的 DeviceDpi（需要 app.config 里声明 DpiAwareness=PerMonitorV2 才是真实值）。
            try
            {
                var device = DeviceDpi;
                if (device > 0)
                {
                    dpi = device;
                }
            }
            catch
            {
                // 忽略，走下面的兜底。
            }

            // 2) 兜底：直接问窗口/设备上下文的真实 DPI。
            //    没有 app.config 声明时 WinForms 会固定返回 96，而 GDI 仍按真实 DPI 画字，
            //    这里必须把真实 DPI 拿回来，否则 175%/200% 下“字号变大、行高不变”，布局必然错乱。
            if (dpi <= 96F)
            {
                var real = RealDpi();
                if (real > dpi)
                {
                    dpi = real;
                }
            }

            return dpi <= 0F ? 1F : dpi / 96F;
        }

        private float RealDpi()
        {
            try
            {
                if (IsHandleCreated)
                {
                    var dpi = GetDpiForWindow(Handle);
                    if (dpi > 0)
                    {
                        return dpi;
                    }
                }
            }
            catch
            {
                // 老系统没有 GetDpiForWindow，走下面的 Graphics 兜底。
            }

            try
            {
                using (var graphics = CreateGraphics())
                {
                    if (graphics.DpiX > 0)
                    {
                        return graphics.DpiX;
                    }
                }
            }
            catch
            {
                // 拿不到就按 96 处理。
            }

            return 96F;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            TryApplyDpiScaling();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            // 构造完成后再校正一次：句柄可能在构造期间就创建了（那时字段还没赋值，不能缩放）。
            TryApplyDpiScaling();
        }

        protected override void OnDpiChangedAfterParent(EventArgs e)
        {
            base.OnDpiChangedAfterParent(e);
            TryApplyDpiScaling();
        }

        protected override void OnDpiChanged(DpiChangedEventArgs e)
        {
            base.OnDpiChanged(e);
            TryApplyDpiScaling();
        }

        /// <summary>
        /// 按真实 DPI 重新套用尺寸。**必须等构造函数跑完**：
        /// 窗体的句柄可能在构造期间就被创建（例如把 StatusStrip/表格加进 Controls 时），
        /// 那时 _root / _bottom / 底部按钮等字段都还是 null，直接缩放会 NullReferenceException。
        /// </summary>
        private void TryApplyDpiScaling()
        {
            if (!_layoutReady)
            {
                return;
            }

            var scale = CurrentDpiScale();
            AppLogger.Info("DPI：scale=" + scale + " DeviceDpi=" + DeviceDpi
                           + " 窗口=" + Size + " 表格=" + _subjectTable.ClientSize);
            if (Math.Abs(scale - _dpiScale) <= 0.01F)
            {
                return;
            }

            _dpiScale = scale;
            ApplyDpiScaling();
        }

        /// <summary>DPI 变化后重新套用所有与 DPI 相关的尺寸。</summary>
        private void ApplyDpiScaling()
        {
            _root.RowStyles[1].Height = Scale(BottomBarHeightLogical);
            _subjectTable.Padding = new Padding(
                Scale(TablePadLeftLogical),
                Scale(TablePadTopLogical),
                Scale(TablePadRightLogical),
                Scale(TablePadBottomLogical));
            _bottom.Padding = new Padding(
                Scale(BottomPadLeftLogical),
                Scale(BottomPadTopLogical),
                Scale(BottomPadRightLogical),
                Scale(BottomPadBottomLogical));
            foreach (var button in new[] { _btnRefresh, _btnOpenConfig, _btnQuit })
            {
                button.MinimumSize = new Size(Scale(ButtonMinWidthLogical), Scale(ButtonMinHeightLogical));
                button.Margin = new Padding(Scale(RowMarginLogical), 0, 0, 0);
            }

            MinimumSize = new Size(Scale(MinFormWidthLogical), Scale(MinFormHeightLogical));
            RebuildSubjectGrid();
        }

        private Button MakeBottomButton(string text, EventHandler onClick)
        {
            var b = new Button
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(Scale(ButtonMinWidthLogical), Scale(ButtonMinHeightLogical)),
                Margin = new Padding(Scale(RowMarginLogical), 0, 0, 0),
                Font = new Font("Microsoft YaHei UI", 12F)
            };
            b.Click += onClick;
            return b;
        }

        // ---------- 配置装载 ----------

        private void ReloadSubjects()
        {
            if (!_configStore.TryLoad(out var config, out var error))
            {
                _subjects = new List<Subject>();
                RebuildSubjectGrid();
                MessageBox.Show(
                    this,
                    error,
                    "配置读取失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            _subjects = config!.Subjects;
            RebuildSubjectGrid();
            AppLogger.Info("已刷新配置，科目数：" + _subjects.Count);
        }

        private void RebuildSubjectGrid()
        {
            _subjectTable.SuspendLayout();
            _subjectTable.Controls.Clear();
            _subjectTable.RowStyles.Clear();
            _subjectRows.Clear();
            _spacerRowStyle = null;

            if (_subjects.Count == 0)
            {
                _subjectTable.RowCount = 1;
                _subjectTable.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
                _subjectTable.Controls.Add(CreateHintLabel(), 0, 0);
            }
            else
            {
                // 末尾**永远**留一个吸收行：即使科目超过一屏（滚动）也留着，
                // 这样“剩余像素”只会进吸收行，绝不会把最后一个科目行撑开。
                _subjectTable.RowCount = _subjects.Count + 1;

                var height = ComputeRowHeightPx(
                    _subjectTable.ClientSize.Height - _subjectTable.Padding.Vertical,
                    _subjects.Count,
                    _dpiScale,
                    ComputeContentFloorPx());

                for (var i = 0; i < _subjects.Count; i++)
                {
                    _subjectTable.RowStyles.Add(new RowStyle(SizeType.Absolute, height));
                }

                _spacerRowStyle = new RowStyle(SizeType.Absolute, 0F);
                _subjectTable.RowStyles.Add(_spacerRowStyle);

                for (var i = 0; i < _subjects.Count; i++)
                {
                    var row = CreateSubjectButton(_subjects[i], height);
                    _subjectRows.Add(row);
                    _subjectTable.Controls.Add(row, 0, i);
                }
            }

            _subjectTable.ResumeLayout();
            ApplyRowLayout();
            SetInteractionEnabled(true);
        }

        private Label CreateHintLabel()
        {
            return new Label
            {
                Text = "尚未加载到任何科目。\r\n\r\n请点击下方「打开配置目录」，"
                       + "用记事本编辑 subjects.json（参考随附示例）。\r\n保存后点击「刷新配置」。",
                Dock = DockStyle.Fill,
                Font = new Font("Microsoft YaHei UI", 13F),
                ForeColor = Color.DimGray
            };
        }

        // ---------- 科目行 ----------

        /// <summary>
        /// 科目行：系统按钮外观，两行文本（科目名 + 主机·远端路径），整体在按钮内**水平与垂直居中**
        /// （TextAlign = MiddleCenter）。
        /// </summary>
        private Button CreateSubjectButton(Subject subject, int rowHeight)
        {
            var margin = Scale(RowMarginLogical);
            var buttonHeight = Math.Max(0, rowHeight - margin);

            var button = new Button
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, margin / 2, 0, margin / 2),
                TextAlign = ContentAlignment.MiddleCenter,
                UseMnemonic = false,
                Cursor = Cursors.Hand
            };

            ApplyRowTextAndFont(button, subject, buttonHeight);

            _toolTip.SetToolTip(button, BuildTooltip(subject));
            button.Click += (_, _) => OpenSubject(subject);
            return button;
        }

        /// <summary>
        /// 给按钮设置“两行文本 + 与之匹配的字号”：
        /// 每行先按**像素宽度**截断（超出视宽就加省略号），保证文本永不换行、永不超出按钮宽度
        /// ——否则按钮的“首选宽度”会超过视口，表格底部就会冒出横向滚动条；
        /// 再从大到小挑一个能把两行放下的字号。
        /// </summary>
        private void ApplyRowTextAndFont(Button button, Subject subject, int buttonHeight)
        {
            var maxWidth = AvailableRowWidth();
            // 预留安全余量：主题按钮的文字区在 Padding 之外还有一点内缩，卡边界会把第二行裁掉。
            var maxTextHeight = Math.Max(1, buttonHeight - ButtonPadding - Scale(TextSafetyLogical));

            for (var size = MaxSubjectNameFontSize; size > MinSubjectNameFontSize; size -= 0.5F)
            {
                using (var probe = new Font("Microsoft YaHei UI", size, FontStyle.Bold))
                {
                    var text = Ellipsize(subject.Name, probe, maxWidth)
                               + "\r\n"
                               + Ellipsize(subject.DisplayLine, probe, maxWidth);

                    if (MeasureTwoLineHeight(text, size, maxWidth) <= maxTextHeight)
                    {
                        SetRowText(button, text, size);
                        return;
                    }
                }
            }

            using (var fallback = new Font("Microsoft YaHei UI", MinSubjectNameFontSize, FontStyle.Bold))
            {
                var text = Ellipsize(subject.Name, fallback, maxWidth)
                           + "\r\n"
                           + Ellipsize(subject.DisplayLine, fallback, maxWidth);
                SetRowText(button, text, MinSubjectNameFontSize);
            }
        }

        private static void SetRowText(Button button, string text, float fontSize)
        {
            button.Text = text;
            if (Math.Abs(button.Font.Size - fontSize) > 0.01F)
            {
                var old = button.Font;
                button.Font = new Font("Microsoft YaHei UI", fontSize, FontStyle.Bold);
                if (old != null && !ReferenceEquals(old, Control.DefaultFont))
                {
                    old.Dispose();
                }
            }
        }

        /// <summary>把一行文字按像素宽度截断（必要时加省略号），保证它一定能放进 maxWidth。</summary>
        public static string Ellipsize(string text, Font font, int maxWidth)
        {
            if (string.IsNullOrEmpty(text))
            {
                return "";
            }

            var flags = TextFormatFlags.NoPrefix | TextFormatFlags.SingleLine;
            var probe = new Size(10000, 10000);
            if (TextRenderer.MeasureText(text, font, probe, flags).Width <= maxWidth)
            {
                return text;
            }

            const string Ellipsis = "…";
            var low = 0;
            var high = text.Length;
            while (low < high)
            {
                var mid = (low + high + 1) / 2;
                var candidate = text.Substring(0, mid) + Ellipsis;
                if (TextRenderer.MeasureText(candidate, font, probe, flags).Width <= maxWidth)
                {
                    low = mid;
                }
                else
                {
                    high = mid - 1;
                }
            }

            return low <= 0 ? Ellipsis : text.Substring(0, low) + Ellipsis;
        }

        /// <summary>行的可用文字宽度（扣掉表格内边距、可能的滚动条与按钮内边距）。</summary>
        private int AvailableRowWidth()
        {
            var width = _subjectTable.ClientSize.Width - _subjectTable.Padding.Horizontal;
            if (width <= 0)
            {
                width = ClientSize.Width - _subjectTable.Padding.Horizontal;
            }

            if (_subjectTable.VerticalScroll.Visible)
            {
                width -= SystemInformation.VerticalScrollBarWidth;
            }

            return Math.Max(1, width - ButtonPadding);
        }

        /// <summary>
        /// 内容下限：所有科目里“最小字号下两行文本”最高的那个所需高度，加上按钮内边距、安全余量与行间距。
        /// 行高一旦低于它，TableLayoutPanel 就会自己重排并把空间塞给最后一行（高 DPI 下的典型症状）。
        /// </summary>
        private int ComputeContentFloorPx()
        {
            if (_subjects.Count == 0)
            {
                return Scale(MinRowHeightLogical);
            }

            var width = AvailableRowWidth();
            var worst = 0;
            using (var font = new Font("Microsoft YaHei UI", MinSubjectNameFontSize, FontStyle.Bold))
            {
                foreach (var subject in _subjects)
                {
                    var text = Ellipsize(subject.Name, font, width)
                               + "\r\n"
                               + Ellipsize(subject.DisplayLine, font, width);
                    worst = Math.Max(worst, MeasureTwoLineHeight(text, MinSubjectNameFontSize, width));
                }
            }

            return worst + ButtonPadding + Scale(TextSafetyLogical) + Scale(RowMarginLogical);
        }

        /// <summary>
        /// 统一行高（像素）：一屏内按 available/可见行数 平分（并扣掉行间距），
        /// 再用 [MinRowHeight, MaxRowHeight]（已按 DPI 缩放）夹紧，最后取内容下限。
        /// 返回值保证：**任何一行的按钮都不会比它需要的更矮**。
        /// </summary>
        public static int ComputeRowHeightPx(int availableHeightPx, int subjectCount, float dpiScale, int contentFloorPx)
        {
            if (subjectCount <= 0)
            {
                return ScaleFor(DefaultRowHeightLogical, dpiScale);
            }

            var visible = Math.Min(subjectCount, MaxVisibleRows);
            var min = ScaleFor(MinRowHeightLogical, dpiScale);
            var max = ScaleFor(MaxRowHeightLogical, dpiScale);

            // 注意：行高**就是该行占用的总高度**（按钮的上下 Margin 是在行内扣减的），所以这里不减 Margin。
            var height = availableHeightPx > 0
                ? availableHeightPx / visible
                : ScaleFor(DefaultRowHeightLogical, dpiScale);

            if (height < min)
            {
                height = min;
            }
            else if (height > max)
            {
                height = max;
            }

            return Math.Max(height, contentFloorPx);
        }

        /// <summary>
        /// 重算行高（含吸收行）并同步刷新每行字号与截断文本。
        /// 会在 Resize 与 Layout 两个时机被调用：只靠 Resize 时，底部工具条/状态栏尚未参与布局，
        /// 表格可能还是“更高”的旧尺寸，据此算出的行高会偏大、窗口下方留出空隙。
        /// 带重入保护：本方法内部的赋值（字号/文本/AutoScrollMinSize）又会触发布局。
        /// </summary>
        private void ApplyRowLayout()
        {
            if (_inRowLayout)
            {
                return;
            }

            if (_subjects.Count == 0 || _subjectRows.Count == 0)
            {
                return;
            }

            if (_subjectTable.RowStyles.Count < _subjectRows.Count + 1)
            {
                return; // 还没建好（吸收行缺失）时不动作
            }

            _inRowLayout = true;
            try
            {
                ApplyRowLayoutCore();
            }
            finally
            {
                _inRowLayout = false;
            }
        }

        private void ApplyRowLayoutCore()
        {
            var margin = Scale(RowMarginLogical);
            var available = _subjectTable.ClientSize.Height - _subjectTable.Padding.Vertical;
            var max = Scale(MaxRowHeightLogical);
            var target = ComputeRowHeightPx(available, _subjects.Count, _dpiScale, ComputeContentFloorPx());

            // 一屏放得下时，把剩余空间尽量分给各行（不超过上限），让列表填满窗口而不是在下面留一条空隙。
            var remainder = 0;
            if (available > 0 && _subjects.Count <= MaxVisibleRows)
            {
                var share = available / _subjects.Count;
                if (share > target)
                {
                    target = Math.Min(max, share);
                }

                // 行高就是该行占用的总高度（按钮的上下 Margin 在行内扣减），所以这里不带 margin。
                var left = available - (_subjects.Count * target);
                if (left > 0 && left < _subjects.Count)
                {
                    remainder = left; // 余数逐行 +1px，做到像素级填满
                }
            }

            for (var i = 0; i < _subjectRows.Count; i++)
            {
                var height = target + (i < remainder ? 1 : 0);
                _subjectTable.RowStyles[i].Height = height;

                // 行高变化时同步重算“文本截断 + 字号”，保证文字始终放得下且不超宽。
                ApplyRowTextAndFont(_subjectRows[i], _subjects[i], Math.Max(0, height - margin));
            }

            // 剩余像素只交给吸收行；滚动时吸收行高度为 0。
            var rowSpace = (_subjectRows.Count * target) + remainder;
            SpacerAbsorbs(available > 0 && available - rowSpace > 0);

            // 明确滚动范围：宽度固定 0（表格只允许纵向滚动，否则按钮文字的“首选宽度”
            // 会把滚动区域撑宽，底部冒出一条横向滚动条）。
            _subjectTable.AutoScrollMinSize = new Size(0, Math.Max(0, rowSpace));
        }

        private void SpacerAbsorbs(bool absorb)
        {
            if (_spacerRowStyle == null)
            {
                return;
            }

            _spacerRowStyle.SizeType = absorb ? SizeType.Percent : SizeType.Absolute;
            _spacerRowStyle.Height = absorb ? 100F : 0F;
        }

        /// <summary>
        /// 由“文本 + 可用宽高”反推最大可容纳字号：从上限开始每 0.5pt 递减，
        /// 直到两行文本放得下为止。
        /// </summary>
        public static float ComputeFittingFontSize(string text, int availableWidth, int rowHeight)
        {
            var maxTextHeight = Math.Max(1, rowHeight - ButtonPadding);
            var maxTextWidth = Math.Max(1, availableWidth);

            for (var size = MaxSubjectNameFontSize; size > MinSubjectNameFontSize; size -= 0.5F)
            {
                if (MeasureTwoLineHeight(text, size, maxTextWidth) <= maxTextHeight)
                {
                    return size;
                }
            }

            return MinSubjectNameFontSize;
        }

        /// <summary>量测给定字号下两行文本的高度（只量高度，宽度用于换行判断）。</summary>
        public static int MeasureTwoLineHeight(string text, float fontSize, int maxWidth)
        {
            using (var probe = new Font("Microsoft YaHei UI", fontSize, FontStyle.Bold))
            {
                return TextRenderer.MeasureText(
                    text,
                    probe,
                    new Size(Math.Max(1, maxWidth), 10000),
                    TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
            }
        }

        private static string BuildTooltip(Subject subject)
        {
            string url;
            try
            {
                url = FtpUrlBuilder.BuildMasked(subject, null);
            }
            catch (FtpUrlException ex)
            {
                url = "（配置有误：" + ex.Message + "）";
            }

            return subject.Name + "\n" + subject.DisplayLine
                   + "\n将打开：" + url
                   + "\n凭据：" + (subject.IsInlineCredential)
                   + (string.IsNullOrWhiteSpace(subject.LastSyncAt) ? "" : "\n最近打开：" + subject.LastSyncAt);
        }

        // ---------- 打开网址（lite 版核心行为） ----------

        private void OpenSubject(Subject subject)
        {
            string url;
            string masked;
            try
            {
                url = FtpUrlBuilder.Build(subject, _configPath);
                masked = FtpUrlBuilder.BuildMasked(subject, _configPath);
            }
            catch (FtpUrlException ex)
            {
                AppLogger.Warn("科目「" + subject.Name + "」配置有误：" + ex.Message);
                _statusLabel.Text = "「" + subject.Name + "」配置有误：" + ex.Message;
                MessageBox.Show(
                    this,
                    "无法为「" + subject.Name + "」生成 FTP 网址：\r\n" + ex.Message,
                    "配置有误",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (!FtpUrlLauncher.TryOpen(url, masked, out var error))
            {
                _statusLabel.Text = "「" + subject.Name + "」打开失败。";
                MessageBox.Show(this, error, "打开失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            _configStore.UpdateLastOpenUtc(subject.Id, DateTime.Now);
            _statusLabel.Text = "已打开「" + subject.Name + "」：" + masked;
        }

        // ---------- 其它 ----------

        private void OpenConfigDirectory(object? sender, EventArgs e)
        {
            try
            {
                var dir = Path.GetDirectoryName(_configPath);
                if (string.IsNullOrEmpty(dir))
                {
                    dir = ".";
                }

                Directory.CreateDirectory(dir);
                var args = File.Exists(_configPath)
                    ? "/select,\"" + _configPath + "\""
                    : "\"" + dir + "\"";
                using (Process.Start(new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = args,
                    UseShellExecute = true
                }))
                {
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "无法打开配置目录：" + ex.Message, "FtpJumper Lite", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void SetInteractionEnabled(bool enabled)
        {
            foreach (var row in _subjectRows)
            {
                row.Enabled = enabled;
            }

            _btnRefresh.Enabled = enabled;
            _btnOpenConfig.Enabled = enabled;
            _btnQuit.Enabled = enabled;
        }
    }
}
