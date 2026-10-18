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
    /// 主窗体：科目选择界面（单列卡片列表，默认正好 10 行，投影友好）。
    /// 单击科目 = 拼 ftp:// 网址交给文件资源管理器打开；界面本身不提供科目增删改（由教师手工编辑 subjects.json）。
    ///
    /// 布局三条硬约束（都是为了高 DPI，尤其 2K@200%）：
    /// 1) 所有尺寸常量都是 <b>96 DPI 逻辑像素</b>，用前一律经 <see cref="Scale"/> 换算；
    ///    字号是"点"（随 DPI 自动放大），常量若不同步放大，就会出现"行高小于文字所需高度"。
    /// 2) 行高由可见高度平分，夹在 [MinRowHeight, MaxRowHeight] 之间，且**永远不小于**内容下限
    ///    （最小字号两行文本 + 卡片内边距 + 安全余量），否则文字会被裁。
    /// 3) 科目区是自绘控件（<see cref="SubjectListControl"/>），滚动条也由它自己按像素判断，
    ///    不再依赖 WinForms 的 AutoScroll —— 从根上消除"内容放得下却冒出滚动条、首行被滚走"那类反馈环。
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

        // ---- 顶部工具条（齿轮）----
        private const int HeaderHeightLogical = 44;
        private const int HeaderPadRightLogical = 12;
        private const int HeaderPadTopLogical = 2;
        private const int GearSizeLogical = 30;
        private const int MinFormWidthLogical = 470;
        private const int MinFormHeightLogical = 520;
        private const int ButtonMinWidthLogical = 120;
        private const int ButtonMinHeightLogical = 40;
        private const int TablePadLeftLogical = 14;
        private const int TablePadTopLogical = 12;
        private const int TablePadRightLogical = 14;
        private const int TablePadBottomLogical = 12;
        private const int BottomPadLeftLogical = 12;
        private const int BottomPadTopLogical = 8;
        private const int BottomPadRightLogical = 12;
        private const int BottomPadBottomLogical = 10;
        private const int DefaultFormMarginLogical = 40;
        private const int DefaultFormSideMarginLogical = 80;
        private const int DefaultFormMinWidthLogical = 430;

        /// <summary>默认窗口宽度的上限（逻辑像素）。"折中档"比原来的 760 更窄，观感更接近 0919。</summary>
        private const int DefaultFormMaxWidthLogical = 620;

        /// <summary>
        /// 默认宽度 = 高度 × 该比例。原来是 0.72（宽高比偏"方"），0.58 更接近 0919 那版的窄条比例；
        /// 再窄就会挤压底部三个按钮（各 120 逻辑像素 + 间距），所以停在 0.58。
        /// </summary>
        private const double DefaultFormAspectRatio = 0.58;

        public const float MinSubjectNameFontSize = 12F;

        /// <summary>
        /// 科目名字号上限。"折中档"从 17 降到 15：0919 在 200% 机器上的观感是"文字比行高小一圈"，
        /// 17pt 会把行塞满；15pt 在同一行高下留出余量，更接近那版的呼吸感。
        /// </summary>
        public const float MaxSubjectNameFontSize = 15F;

        /// <summary>Button 默认 Padding=3，文字区比按钮本身窄/矮 6px（也是字号拟合的默认预留）。</summary>
        private const int ButtonPadding = 6;

        private readonly string _configPath;
        private readonly ConfigStore _configStore;
        private readonly SettingsStore _settingsStore;
        private readonly Timer _settingsReloadTimer;
        private UiSettings _settings;
        private FileSystemWatcher? _settingsWatcher;
        private bool _isDark;
        private readonly TableLayoutPanel _root;
        private readonly TableLayoutPanel _header;
        private readonly Win11Button _addSubjectButton;
        private readonly Win11Button _gear;
        private readonly TableLayoutPanel _bottom;
        private readonly SubjectListControl _subjectList;
        private readonly Win11Button _btnRefresh;
        private readonly Win11Button _btnOpenConfig;
        private readonly Win11Button _btnQuit;
        private readonly ToolStripStatusLabel _statusLabel;
        private UiPalette _palette = UiPalette.Light();
        private List<Subject> _subjects = new List<Subject>();
        private float _dpiScale = 1F;

        /// <summary>构造函数是否已跑完；在此之前任何字段都可能还是 null，禁止按 DPI 缩放。</summary>
        private bool _layoutReady;

        public MainForm(string? configPath)
        {
            _configPath = string.IsNullOrWhiteSpace(configPath) ? ConfigStore.DefaultConfigPath : configPath!;
            _configStore = new ConfigStore(_configPath);

            // 个性化设置：与 subjects.json 同目录的 settings.json；缺失时用默认值（主题跟随系统）。
            _settingsStore = new SettingsStore(SettingsStore.PathForConfigFile(_configPath));
            _settings = LoadSettings();
            _settingsReloadTimer = new Timer { Interval = 300 };
            _settingsReloadTimer.Tick += (_, _) =>
            {
                _settingsReloadTimer.Stop();
                ReloadSettings();
            };

            // 自己按 DPI 缩放全部尺寸，禁用 WinForms 的字体自动缩放，避免"双重缩放 / 尺寸不可预期"。
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

            _palette = ResolvePalette(out _isDark);

            // 默认尺寸：单列刚好放下 10 个科目，并尽量占满屏幕可用高度。
            // workArea 已经是当前 DPI 下的像素，只有"下限/上限"这类常量需要 Scale。
            var workArea = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
            var defaultHeight = Math.Max(Scale(MinFormHeightLogical), workArea.Height - Scale(DefaultFormMarginLogical));
            var defaultWidth = (int)Math.Round(defaultHeight * DefaultFormAspectRatio);
            defaultWidth = Math.Min(defaultWidth, workArea.Width - Scale(DefaultFormSideMarginLogical));
            defaultWidth = Math.Min(Scale(DefaultFormMaxWidthLogical), Math.Max(Scale(DefaultFormMinWidthLogical), defaultWidth));
            Size = new Size(defaultWidth, defaultHeight);
            // 最小宽度必须能完整容纳底部三个按钮（各 120 + 间距 + 边距），否则会被裁到窗口外。
            MinimumSize = new Size(Scale(MinFormWidthLogical), Scale(MinFormHeightLogical));

            _root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 4 };
            // 列必须固定占满整宽，否则列会按内容 AutoSize、把整行撑得比窗口还宽。
            _root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, Scale(HeaderHeightLogical) * FontScaleFactor));
            _root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            _root.RowStyles.Add(new RowStyle(SizeType.Absolute, Scale(BottomBarHeightLogical) * FontScaleFactor));
            _root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            // 顶部细工具条：右侧一个齿轮（主题/字号设置入口）。Win11 的做法就是标题区右上角放设置。
            _header = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                BackColor = _palette.FormBack,
                Padding = new Padding(Scale(HeaderPadRightLogical), Scale(HeaderPadTopLogical), Scale(HeaderPadRightLogical), 0)
            };
            _header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            _header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _header.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            _addSubjectButton = new Win11Button
            {
                Text = "+",
                AutoSize = false,
                Size = new Size(Scale(GearSizeLogical), Scale(GearSizeLogical)),
                MinimumSize = new Size(Scale(GearSizeLogical), Scale(GearSizeLogical)),
                Margin = new Padding(0, 0, Scale(6), 0),
                Font = new Font("Segoe UI Symbol", 11F),
                Palette = _palette,
                DpiScale = _dpiScale
            };
            _addSubjectButton.Click += (_, _) => AddSubject();
            new ToolTip().SetToolTip(_addSubjectButton, "新增科目（写入 subjects.json）");
            _header.Controls.Add(_addSubjectButton, 1, 0);
            _gear = new Win11Button
            {
                Text = "⚙",
                AutoSize = false,
                Size = new Size(Scale(GearSizeLogical), Scale(GearSizeLogical)),
                MinimumSize = new Size(Scale(GearSizeLogical), Scale(GearSizeLogical)),
                Margin = new Padding(0),
                Font = new Font("Segoe UI Symbol", 11F),
                Palette = _palette,
                DpiScale = _dpiScale
            };
            _gear.Click += (_, _) => OpenSettings();
            new ToolTip().SetToolTip(_gear, "设置（主题 / 字号）");
            _header.Controls.Add(_gear, 2, 0);
            _root.Controls.Add(_header, 0, 0);

            _subjectList = new SubjectListControl
            {
                Dock = DockStyle.Fill,
                DpiScale = _dpiScale,
                Metrics = CreateRowMetrics(),
                Palette = _palette,
                TooltipProvider = BuildTooltip
            };
            _subjectList.SubjectActivated += (_, e) => OpenSubject(e.Subject);
            _root.Controls.Add(_subjectList, 0, 1);

            _btnRefresh = MakeBottomButton("刷新配置", (_, _) => ReloadSubjects());
            _btnOpenConfig = MakeBottomButton("打开配置目录", OpenConfigDirectory);
            _btnQuit = MakeBottomButton("退出", (_, _) => Close());

            // 底部工具条：左侧弹性空白 + 右侧按钮，保证按钮永远完整落在窗口内。
            _bottom = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 4,
                RowCount = 1,
                BackColor = _palette.FormBack,
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
            _root.Controls.Add(_bottom, 0, 2);

            var statusStrip = new StatusStrip
            {
                BackColor = _palette.FormBack,
                ForeColor = _palette.SubText,
                SizingGrip = false,
                RenderMode = ToolStripRenderMode.System
            };
            _statusLabel = new ToolStripStatusLabel { Spring = false };
            statusStrip.Items.Add(_statusLabel);
            _root.Controls.Add(statusStrip, 0, 3);

            Controls.Add(_root);
            BackColor = _palette.FormBack;

            _statusLabel.Text = "就绪：单击科目 = 直接打开该科目的 FTP 网址；修改配置后请点「刷新配置」。";
            ReloadSubjects();

            // 字段全部就绪：从这里开始才允许按真实 DPI 重新缩放（见 TryApplyDpiScaling）。
            _layoutReady = true;
            HookSettingsWatcher();
        }

        // ---------- DPI ----------

        /// <summary>当前 DPI 相对 96 的倍数（1.0 = 100%，2.0 = 200%）。</summary>
        public float DpiScale => _dpiScale;

        /// <summary>当前生效的配色（由 settings.json + 系统深浅色解析而来）。</summary>
        public UiPalette Palette
        {
            get => _palette;
            set
            {
                _palette = value ?? UiPalette.Light();
                BackColor = _palette.FormBack;
                _bottom.BackColor = _palette.FormBack;
                foreach (var button in new[] { _btnRefresh, _btnOpenConfig, _btnQuit })
                {
                    button.Palette = _palette;
                    button.Invalidate();
                }

                if (_statusLabel.Owner is StatusStrip strip)
                {
                    strip.BackColor = _palette.FormBack;
                    strip.ForeColor = _palette.SubText;
                }

                _statusLabel.ForeColor = _palette.SubText;
                _subjectList.Palette = _palette;
                if (_header != null)
                {
                    _header.BackColor = _palette.FormBack;
                }

                if (_gear != null)
                {
                    _gear.Palette = _palette;
                    _gear.Invalidate();
                }

                Invalidate(true);
            }
        }

        /// <summary>新增科目：字段级表单 → 整份校验 → 写回 subjects.json → 重新加载。</summary>
        private void AddSubject()
        {
            using (var dialog = new SubjectEditorForm(_palette, _dpiScale))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                var subject = dialog.Result;
                if (!_configStore.TryAddSubject(subject, out var error))
                {
                    MessageBox.Show(this, error, "新增科目失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                ReloadSubjects();
                _statusLabel.Text = "已新增科目「" + subject.Name + "」，已写回 subjects.json。";
                AppLogger.Info("已新增科目：" + subject.Id + "（" + subject.Name + "）");
            }
        }

        /// <summary>打开设置窗口（主题 / 字号）：确定后写回 settings.json 并立即生效，不需要重启。</summary>
        private void OpenSettings()
        {
            using (var dialog = new SettingsForm(_settings, _palette, _dpiScale))
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                _settings = dialog.Result;
                if (!_settingsStore.TrySave(_settings, out var error))
                {
                    MessageBox.Show(this, error, "设置保存失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }

                ApplySettings();
                _statusLabel.Text = "设置已更新：主题=" + _settings.Theme + "，字号=" + _settings.FontScale + "%。";
                AppLogger.Info("设置已保存：theme=" + _settings.Theme + " fontScale=" + _settings.FontScale);
            }
        }

        // ---------- 设置与主题 ----------

        private UiSettings LoadSettings()
        {
            if (_settingsStore.TryLoad(out var settings, out var error))
            {
                AppLogger.Info("已加载设置：theme=" + settings.Theme
                               + " fontScale=" + settings.FontScale
                               + " 自定义调色板=" + (settings.Palette?.Custom != null && !settings.Palette.Custom.IsEmpty));
                return settings;
            }

            AppLogger.Warn("设置读取失败，先用默认设置：" + error);
            return UiSettings.CreateDefault();
        }

        /// <summary>按当前的 settings + 系统深浅色解析出调色板，并记录最终选了哪一侧。</summary>
        private UiPalette ResolvePalette(out bool isDark)
        {
            var systemDark = ThemeResolver.IsSystemDarkTheme();
            var palette = ThemeResolver.Resolve(_settings, systemDark, out isDark, out var note);
            if (!string.IsNullOrEmpty(note))
            {
                AppLogger.Warn("主题解析：" + note);
            }

            return palette;
        }

        /// <summary>settings.json 被外部改动后重新加载并立即生效（不重启）。</summary>
        private void ReloadSettings()
        {
            if (IsDisposed)
            {
                return;
            }

            _settings = LoadSettings();
            ApplySettings();
        }

        /// <summary>把当前设置整体套到界面上（配色 + 字号 + 标题栏）。</summary>
        private void ApplySettings()
        {
            Palette = ResolvePalette(out _isDark);
            ApplyFontScale();
            ApplyTitleBarTheme();
            AppLogger.Info("设置已生效：theme=" + _settings.Theme + " fontScale=" + _settings.FontScale
                           + " → " + (_isDark ? "深色" : "浅色"));
        }

        /// <summary>字号缩放：科目区（含行高拟合）与底部按钮一起缩放，底栏高度同步变高，避免文字被裁。</summary>
        private void ApplyFontScale()
        {
            var factor = FontScaleFactor;

            _root.RowStyles[0].Height = Scale(HeaderHeightLogical) * factor;
            _root.RowStyles[2].Height = Scale(BottomBarHeightLogical) * factor;
            var gearSize = new Size(
                (int)Math.Round(Scale(GearSizeLogical) * Math.Min(1.5F, factor)),
                (int)Math.Round(Scale(GearSizeLogical) * Math.Min(1.5F, factor)));
            _gear.Size = gearSize;
            _gear.MinimumSize = gearSize;
            _gear.Font = new Font("Segoe UI Symbol", 11F * Math.Min(1.5F, factor));
            _gear.DpiScale = _dpiScale;
            _gear.Palette = _palette;
            foreach (var button in new[] { _btnRefresh, _btnOpenConfig, _btnQuit })
            {
                var size = 12F * factor;
                if (Math.Abs(button.Font.Size - size) > 0.01F)
                {
                    var old = button.Font;
                    button.Font = new Font("Microsoft YaHei UI", size);
                    if (old != null && !ReferenceEquals(old, Control.DefaultFont))
                    {
                        old.Dispose();
                    }
                }

                button.MinimumSize = new Size(
                    (int)Math.Round(Scale(ButtonMinWidthLogical) * factor),
                    (int)Math.Round(Scale(ButtonMinHeightLogical) * factor));
            }

            // Metrics 里的 FontScale 会驱动科目区重算字号与内容下限。
            _subjectList.Metrics = CreateRowMetrics();
        }

        /// <summary>标题栏深浅色（Win10 2004+/Win11）。dwmapi 不可用或系统过老时静默跳过。</summary>
        private void ApplyTitleBarTheme()
        {
            if (!IsHandleCreated)
            {
                return;
            }

            try
            {
                var useDark = _isDark ? 1 : 0;
                var hr = DwmSetWindowAttribute(Handle, 20 /*DWMWA_USE_IMMERSIVE_DARK_MODE*/, ref useDark, sizeof(int));
                if (hr != 0)
                {
                    // Win10 1809~1909 用的是 19。
                    DwmSetWindowAttribute(Handle, 19, ref useDark, sizeof(int));
                }

                var corner = 2; // DWMWA_WINDOW_CORNER_PREFERENCE = ROUND
                DwmSetWindowAttribute(Handle, 33, ref corner, sizeof(int));
            }
            catch
            {
                // 没有 DWM（远程会话/老系统）就算了，主体界面仍然是自绘的。
            }
        }

        [System.Runtime.InteropServices.DllImport("dwmapi.dll", PreserveSig = true)]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

        /// <summary>监视 settings.json：外部编辑保存后自动重新加载（连发事件用计时器合并）。</summary>
        private void HookSettingsWatcher()
        {
            try
            {
                var dir = Path.GetDirectoryName(_settingsStore.Path);
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                {
                    return;
                }

                _settingsWatcher = new FileSystemWatcher(dir, "settings.json")
                {
                    NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size
                };
                _settingsWatcher.Changed += (_, _) => ScheduleSettingsReload();
                _settingsWatcher.Created += (_, _) => ScheduleSettingsReload();
                _settingsWatcher.Renamed += (_, _) => ScheduleSettingsReload();
                _settingsWatcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                AppLogger.Warn("无法监视 settings.json（改动后需重启才生效）：" + ex.Message);
            }
        }

        private void ScheduleSettingsReload()
        {
            try
            {
                if (IsDisposed || !IsHandleCreated)
                {
                    return;
                }

                BeginInvoke(new Action(() =>
                {
                    _settingsReloadTimer.Stop();
                    _settingsReloadTimer.Start();
                }));
            }
            catch
            {
                // 窗体正在销毁时忽略。
            }
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            const int WmSettingChange = 0x001A;
            const int WmThemeChanged = 0x031A;
            if ((m.Msg == WmSettingChange || m.Msg == WmThemeChanged) && _layoutReady)
            {
                // 系统深浅色变了：只有"跟随系统"的模式需要立刻重涂（强制浅/深时保持用户的选择）。
                var mode = (_settings.Theme ?? "system").Trim().ToLowerInvariant();
                if (mode == "system" || mode == "custom")
                {
                    ApplySettings();
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _settingsWatcher?.Dispose();
                _settingsWatcher = null;
                _settingsReloadTimer?.Dispose();
            }

            base.Dispose(disposing);
        }

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

        /// <summary>把窗体常量装进科目区的度量对象（阶段三调档位只改这里）。</summary>
        private RowMetrics CreateRowMetrics()
        {
            return new RowMetrics
            {
                MinRowHeightLogical = MinRowHeightLogical,
                MaxRowHeightLogical = MaxRowHeightLogical,
                DefaultRowHeightLogical = DefaultRowHeightLogical,
                MaxVisibleRows = MaxVisibleRows,
                RowGapLogical = RowMarginLogical,
                PadLeftLogical = TablePadLeftLogical,
                PadRightLogical = TablePadRightLogical,
                PadTopLogical = TablePadTopLogical,
                PadBottomLogical = TablePadBottomLogical,
                FontScale = FontScaleFactor
            };
        }

        /// <summary>全局字号缩放倍数（settings.json 的 fontScale/100，夹在 60%~200%）。</summary>
        private float FontScaleFactor => Math.Max(0.6F, Math.Min(2.0F, _settings.FontScale / 100F));

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
            //    这里必须把真实 DPI 拿回来，否则 175%/200% 下"字号变大、行高不变"，布局必然错乱。
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
            ApplyTitleBarTheme();
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            // 构造完成后再校正一次：句柄可能在构造期间就创建了（那时字段还没赋值，不能缩放）。
            TryApplyDpiScaling();

            // 尺寸/DPI 都定下来之后自己摆位置。不能只靠 StartPosition=CenterScreen：
            // 构造函数里 RealDpi() 会通过 CreateGraphics() 提前创建句柄，StartPosition 在那一刻就按
            // "还没有尺寸的窗体"生效（或被系统默认级联位置顶掉），于是 200% 机器上窗口落在屏幕左上角、
            // 下沿压住任务栏。这里显式居中并夹进工作区，位置只由最终尺寸决定。
            CenterOnPrimaryWorkArea();

            // 句柄可能在着色板之前就建好了（RealDpi），这里按最终的主题再刷一次标题栏。
            ApplyTitleBarTheme();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            // 科目区的滚动位置本来就该是 0（内容放得下时没有滚动范围）。
            _subjectList.ScrollToTop();

            // 现场排障用：把最终落位与行高写进日志（目标机上一条即可判定）。
            AppLogger.Info("窗口已显示：Bounds=" + Bounds
                           + " 科目区=" + _subjectList.ClientSize
                           + " 行高=" + _subjectList.RowHeightPx
                           + " 内容高=" + _subjectList.ContentHeightPx
                           + " 可滚动=" + _subjectList.MaxScrollPx
                           + " 滚动偏移=" + _subjectList.ScrollOffsetPx);
        }

        /// <summary>把窗口在<b>主屏工作区</b>内居中摆放，并保证整个窗口不越出工作区（含任务栏区域）。</summary>
        private void CenterOnPrimaryWorkArea()
        {
            var work = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1280, 800);
            var target = ComputeCenteredLocation(Size, work);

            // 交给系统兜底不如自己算：设为 Manual 后不会再被 StartPosition / 系统默认位置改写。
            StartPosition = FormStartPosition.Manual;
            Location = target;

            AppLogger.Info("窗口定位：工作区=" + work + " 窗口=" + Size + " → 位置=" + Location);
        }

        /// <summary>
        /// 居中位置（纯函数，便于测试）：窗口比工作区小就居中，比工作区大就贴工作区左上角，
        /// 结果永远保证窗口完整落在工作区内。
        /// </summary>
        public static Point ComputeCenteredLocation(Size windowSize, Rectangle workArea)
        {
            var x = workArea.Left + ((workArea.Width - windowSize.Width) / 2);
            var y = workArea.Top + ((workArea.Height - windowSize.Height) / 2);

            x = windowSize.Width >= workArea.Width
                ? workArea.Left
                : Math.Max(workArea.Left, Math.Min(x, workArea.Right - windowSize.Width));
            y = windowSize.Height >= workArea.Height
                ? workArea.Top
                : Math.Max(workArea.Top, Math.Min(y, workArea.Bottom - windowSize.Height));

            return new Point(x, y);
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
                           + " 窗口=" + Size + " 科目区=" + _subjectList.ClientSize);
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
            var factor = FontScaleFactor;
            _root.RowStyles[0].Height = Scale(HeaderHeightLogical) * factor;
            _root.RowStyles[2].Height = Scale(BottomBarHeightLogical) * factor;
            _bottom.Padding = new Padding(
                Scale(BottomPadLeftLogical),
                Scale(BottomPadTopLogical),
                Scale(BottomPadRightLogical),
                Scale(BottomPadBottomLogical));
            foreach (var button in new[] { _btnRefresh, _btnOpenConfig, _btnQuit })
            {
                button.MinimumSize = new Size(Scale(ButtonMinWidthLogical), Scale(ButtonMinHeightLogical));
                button.Margin = new Padding(Scale(RowMarginLogical), 0, 0, 0);
                button.DpiScale = _dpiScale;
            }

            MinimumSize = new Size(Scale(MinFormWidthLogical), Scale(MinFormHeightLogical));

            // 科目区自己按 DPI 重算行高/字号/卡片尺寸（先给 Metrics 再给 DpiScale，避免算两遍）。
            _subjectList.Metrics = CreateRowMetrics();
            _subjectList.DpiScale = _dpiScale;
        }

        private Win11Button MakeBottomButton(string text, EventHandler onClick)
        {
            var factor = FontScaleFactor;
            var b = new Win11Button
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                MinimumSize = new Size(
                    (int)Math.Round(Scale(ButtonMinWidthLogical) * factor),
                    (int)Math.Round(Scale(ButtonMinHeightLogical) * factor)),
                Margin = new Padding(Scale(RowMarginLogical), 0, 0, 0),
                Font = new Font("Microsoft YaHei UI", 12F * factor),
                Palette = _palette,
                DpiScale = _dpiScale
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
                BindSubjects();
                MessageBox.Show(
                    this,
                    error,
                    "配置读取失败",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            _subjects = config!.Subjects;
            BindSubjects();
            AppLogger.Info("已刷新配置，科目数：" + _subjects.Count);
        }

        /// <summary>把当前科目喂给自绘列表（列表自己算行高，不需要重建控件）。</summary>
        private void BindSubjects()
        {
            _subjectList.Subjects = _subjects;
            SetInteractionEnabled(true);
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

        /// <summary>
        /// 统一行高（像素）：一屏内按 available/可见行数 平分，
        /// 再用 [MinRowHeight, MaxRowHeight]（已按 DPI 缩放）夹紧，最后取内容下限。
        /// 返回值保证：**任何一行的卡片都不会比它需要的更矮**。
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
        /// 由"文本 + 可用宽高"反推最大可容纳字号：从上限开始每 0.5pt 递减，
        /// 直到两行文本放得下为止。paddingPx 是拟合时预留的垂直内缩（默认按按钮的 6px）；
        /// min/maxFontSize 允许调用方按全局字号缩放（settings.json 的 fontScale）改写上下限。
        /// </summary>
        public static float ComputeFittingFontSize(
            string text,
            int availableWidth,
            int rowHeight,
            int paddingPx = ButtonPadding,
            float minFontSize = MinSubjectNameFontSize,
            float maxFontSize = MaxSubjectNameFontSize)
        {
            var maxTextHeight = Math.Max(1, rowHeight - paddingPx);
            var maxTextWidth = Math.Max(1, availableWidth);
            var min = Math.Max(6F, minFontSize);
            var max = Math.Max(min + 0.5F, maxFontSize);

            for (var size = max; size > min; size -= 0.5F)
            {
                if (MeasureTwoLineHeight(text, size, maxTextWidth) <= maxTextHeight)
                {
                    return size;
                }
            }

            return min;
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
            _subjectList.Enabled = enabled;
            _btnRefresh.Enabled = enabled;
            _btnOpenConfig.Enabled = enabled;
            _btnQuit.Enabled = enabled;
        }
    }
}
