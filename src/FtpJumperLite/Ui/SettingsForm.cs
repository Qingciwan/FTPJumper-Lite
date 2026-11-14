using System;
using System.Drawing;
using System.Windows.Forms;
using FtpJumperLite.Models;

namespace FtpJumperLite.Ui
{
    /// <summary>
    /// 设置窗口：主题模式 + 全局字号 + 界面字体。
    /// 每科颜色/纹理仍在 subjects.json 里手工配置；"简易 JSON 编辑器"是后续阶段的事。
    /// 尺寸全部按传入的 DPI 缩放手工排布 —— 与主窗体一致的"逻辑像素 + Scale"路子。
    /// </summary>
    public sealed class SettingsForm : Form
    {
        private readonly RadioButton _system;
        private readonly RadioButton _light;
        private readonly RadioButton _dark;
        private readonly RadioButton _custom;
        private readonly NumericUpDown _fontScale;
        private readonly UiSettings _original;
        private readonly float _dpiScale;
        private readonly string? _baseDirectory;
        private readonly Label _fontLabel;
        private string? _pendingFontFile;
        private string? _pendingFontFamily;

        public SettingsForm(UiSettings? current, UiPalette palette, float dpiScale, UiFontSet fonts, string? configDirectory)
        {
            _original = current ?? UiSettings.CreateDefault();
            _dpiScale = dpiScale <= 0F ? 1F : dpiScale;
            _baseDirectory = configDirectory;
            _pendingFontFile = _original.FontFile;
            _pendingFontFamily = _original.FontFamily;

            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Text = "设置";
            BackColor = palette.FormBack;
            ForeColor = palette.TitleText;
            Font = fonts.Create(10F);
            ClientSize = new Size(S(360), S(320));

            var y = S(16);
            Controls.Add(MakeLabel("主题", y));
            y += S(28);

            _system = MakeRadio("跟随系统", y);
            y += S(28);
            _light = MakeRadio("浅色", y);
            y += S(28);
            _dark = MakeRadio("深色", y);
            y += S(28);
            _custom = MakeRadio("自定义调色板（写在 palette.custom）", y);
            y += S(40);

            // 标签单独一行、输入框在它下面 —— 之前两者用了同一个 Left/Top，会叠在一起。
            Controls.Add(MakeLabel("字号（60~200，100 = 默认）", y));
            y += S(20);

            _fontScale = new NumericUpDown
            {
                Left = S(18),
                Top = y,
                Width = S(90),
                Minimum = 60,
                Maximum = 200,
                Value = Math.Max(60, Math.Min(200, _original.FontScale)),
                BackColor = palette.CardBack,
                ForeColor = palette.TitleText,
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(_fontScale);
            y += S(52);

            // 界面字体：默认是微软雅黑 UI；可以选一个未安装的 .ttf/.otf 文件（随程序分发即可）。
            Controls.Add(MakeLabel("界面字体（可选未安装的字体文件）", y));
            y += S(24);

            _fontLabel = new Label
            {
                Left = S(18),
                Top = y,
                AutoSize = true,
                MaximumSize = new Size(S(324), 0),
                ForeColor = palette.SubText,
                BackColor = palette.FormBack,
                Text = DescribeCurrent(fonts)
            };
            Controls.Add(_fontLabel);
            y = _fontLabel.Bottom + S(10);

            var chooseFont = new Win11Button
            {
                Text = "选择字体文件…",
                Left = S(18),
                Top = y,
                Width = S(140),
                Height = S(32),
                Palette = palette,
                DpiScale = _dpiScale
            };
            chooseFont.Click += (_, _) => ChooseFontFile();

            var resetFont = new Win11Button
            {
                Text = "恢复默认",
                Left = S(166),
                Top = y,
                Width = S(100),
                Height = S(32),
                Palette = palette,
                DpiScale = _dpiScale
            };
            resetFont.Click += (_, _) => ResetFontToDefault();

            Controls.Add(chooseFont);
            Controls.Add(resetFont);
            y += S(44);

            var ok = new Win11Button
            {
                Text = "确定",
                Left = S(160),
                Top = y,
                Width = S(88),
                Height = S(32),
                Palette = palette,
                DpiScale = _dpiScale,
                DialogResult = DialogResult.OK
            };
            var cancel = new Win11Button
            {
                Text = "取消",
                Left = S(256),
                Top = y,
                Width = S(88),
                Height = S(32),
                Palette = palette,
                DpiScale = _dpiScale,
                DialogResult = DialogResult.Cancel
            };
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;

            // 高度按内容算（高 DPI 下标签可能换行），再夹到屏幕可用区；不写死高度，
            // 否则内容一长就会把底部按钮顶出客户区。
            var desiredHeight = y + S(32) + S(16);
            var work = Screen.PrimaryScreen?.WorkingArea.Height ?? 1080;
            var nonClient = Height - ClientSize.Height;
            var maxClient = Math.Max(S(220), work - nonClient - S(24));
            ClientSize = new Size(S(360), Math.Min(desiredHeight, maxClient));

            SelectCurrentTheme();
        }

        /// <summary>确定后用户的选择（调色板覆盖原样保留，本窗口不改它）。</summary>
        public UiSettings Result
        {
            get
            {
                var theme = _light.Checked
                    ? "light"
                    : _dark.Checked
                        ? "dark"
                        : _custom.Checked
                            ? "custom"
                            : "system";

                return new UiSettings
                {
                    Version = _original.Version,
                    Theme = theme,
                    FontScale = (int)_fontScale.Value,
                    FontFamily = _pendingFontFamily,
                    FontFile = _pendingFontFile,
                    Palette = _original.Palette
                };
            }
        }

        private string DescribeCurrent(UiFontSet fonts)
        {
            if (!string.IsNullOrEmpty(_pendingFontFile))
            {
                return fonts.DisplayName + "  ← " + _pendingFontFile;
            }

            return fonts.DisplayName;
        }

        private void ChooseFontFile()
        {
            using (var dialog = new OpenFileDialog
                   {
                       Title = "选择界面字体文件",
                       Filter = "字体文件 (*.ttf;*.otf;*.ttc)|*.ttf;*.otf;*.ttc|所有文件 (*.*)|*.*",
                       CheckFileExists = true
                   })
            {
                if (dialog.ShowDialog(this) != DialogResult.OK)
                {
                    return;
                }

                UiFontSet loaded;
                try
                {
                    loaded = UiFontSet.LoadFile(dialog.FileName);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(
                        this,
                        "这个字体文件加载失败：\r\n" + ex.Message,
                        "字体不可用",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                _pendingFontFile = UiFontSet.MakeStorablePath(dialog.FileName, _baseDirectory);
                _pendingFontFamily = loaded.FamilyName;
                ApplyPreviewFont(loaded);
                _fontLabel.Text = loaded.DisplayName + "  ← " + _pendingFontFile;
            }
        }

        private void ResetFontToDefault()
        {
            _pendingFontFile = null;
            _pendingFontFamily = null;
            ApplyPreviewFont(UiFontSet.Default);
            _fontLabel.Text = UiFontSet.Default.DisplayName;
        }

        /// <summary>立刻用选中的字体重绘本窗口，让老师先看一眼效果。</summary>
        private void ApplyPreviewFont(UiFontSet set)
        {
            if (IsDisposed)
            {
                return;
            }

            var old = Font;
            Font = set.Create(10F);
            if (old != null && !ReferenceEquals(old, Control.DefaultFont))
            {
                old.Dispose();
            }

            PerformLayout();
            Invalidate(true);
        }

        private void SelectCurrentTheme()
        {
            var mode = (_original.Theme ?? "system").Trim().ToLowerInvariant();
            switch (mode)
            {
                case "light":
                    _light.Checked = true;
                    break;
                case "dark":
                    _dark.Checked = true;
                    break;
                case "custom":
                    _custom.Checked = true;
                    break;
                default:
                    _system.Checked = true;
                    break;
            }
        }

        private Label MakeLabel(string text, int top)
        {
            // AutoSize：Label 的默认高度是硬编码 23px，不随 DPI 变；
            // 高 DPI 下字体会长到 27px+，固定高度会把文字裁掉。MaximumSize 只限宽度，到边自动换行。
            return new Label
            {
                Text = text,
                Left = S(18),
                Top = top,
                AutoSize = true,
                MaximumSize = new Size(S(330), 0),
                ForeColor = ForeColor,
                BackColor = BackColor
            };
        }

        private RadioButton MakeRadio(string text, int top)
        {
            // 同上：RadioButton 默认尺寸也是硬编码（104×24），必须让它自己跟着字体长高。
            var radio = new RadioButton
            {
                Text = text,
                Left = S(24),
                Top = top,
                AutoSize = true,
                MaximumSize = new Size(S(320), 0),
                ForeColor = ForeColor,
                BackColor = BackColor
            };
            Controls.Add(radio);
            return radio;
        }

        private int S(int logicalPixels)
        {
            return MainForm.ScaleFor(logicalPixels, _dpiScale);
        }
    }
}
