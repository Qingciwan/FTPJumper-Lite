using System;
using System.Drawing;
using System.Windows.Forms;
using FtpJumperLite.Models;
using FtpJumperLite.Services;

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
        private readonly UiPalette _palette;
        private readonly float _dpiScale;
        private readonly string? _baseDirectory;
        private readonly Label _fontLabel;
        private string? _pendingFontFile;
        private string? _pendingFontFamily;

        public SettingsForm(UiSettings? current, UiPalette palette, float dpiScale, UiFontSet fonts, string? configDirectory)
        {
            _original = current ?? UiSettings.CreateDefault();
            _palette = palette;
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
            // 不用系统文件对话框：装了 Nahimic / Alienware Sound Center 的机器上，
            // 那个对话框会让注入的 NahimicOSD.dll 把进程直接搞崩（实测 0xC0000005、无日志）。
            // 内置选择器只走纯文件 API，不碰外壳扩展与 OLE。
            AppLogger.Info("界面字体：打开内置字体选择器…");

            using (var picker = new FontPickerForm(_palette, _dpiScale, _baseDirectory))
            {
                if (picker.ShowDialog(this) != DialogResult.OK || string.IsNullOrEmpty(picker.SelectedPath))
                {
                    AppLogger.Info("界面字体：取消选择");
                    return;
                }

                TryApplyFontFile(picker.SelectedPath!);
            }
        }

        /// <summary>
        /// 加载并预览一个字体文件。任何失败都只影响这一次选择：窗口保持原样、pending 状态不变。
        /// 走得通的两条入口：文件对话框（选择字体文件…）和把字体文件拖进窗口。
        /// </summary>
        private void TryApplyFontFile(string fileName)
        {
            try
            {
                TryApplyFontFileCore(fileName);
            }
            catch (Exception ex)
            {
                // 最后一道兜底：任何意外都只弹提示，不让整个程序崩掉。
                AppLogger.Error("界面字体：处理 " + fileName + " 时出错 → " + ex);
                MessageBox.Show(
                    this,
                    "换字体时出错，已保留原来的字体：\r\n\r\n" + ex.Message,
                    "字体不可用",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        private void TryApplyFontFileCore(string fileName)
        {
            AppLogger.Info("界面字体：用户选择 " + fileName);

            UiFontSet loaded;
            try
            {
                // LoadFile 内部先做结构校验（非字体/截断文件在这一步就会被挡住），
                // 只有通过校验才会真正调用 GDI+ 的 AddFontFile。
                loaded = UiFontSet.LoadFile(fileName);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("界面字体：这个文件用不了 " + fileName + " → " + ex.Message);
                MessageBox.Show(
                    this,
                    "这个字体文件用不了：\r\n\r\n" + ex.Message,
                    "字体不可用",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var storable = UiFontSet.MakeStorablePath(fileName, _baseDirectory);

            // 先预览，成功后才改 pending 状态：失败时窗口保持原样，不会留下半套配置。
            try
            {
                ApplyPreviewFont(loaded);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("界面字体：预览失败 " + fileName + " → " + ex.Message);
                MessageBox.Show(
                    this,
                    "预览这个字体时出错，已保留原来的字体：\r\n\r\n" + ex.Message,
                    "字体不可用",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            _pendingFontFile = storable;
            _pendingFontFamily = loaded.FamilyName;
            _fontLabel.Text = loaded.DisplayName + "  ← " + storable;
        }

        private void ResetFontToDefault()
        {
            _pendingFontFile = null;
            _pendingFontFamily = null;
            try
            {
                ApplyPreviewFont(UiFontSet.Default);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("界面字体：恢复默认字体预览失败 → " + ex.Message);
            }

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
