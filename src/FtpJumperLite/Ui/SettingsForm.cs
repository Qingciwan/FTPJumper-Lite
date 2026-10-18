using System;
using System.Drawing;
using System.Windows.Forms;
using FtpJumperLite.Models;

namespace FtpJumperLite.Ui
{
    /// <summary>
    /// 设置窗口（阶段四首版：主题模式 + 全局字号）。
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

        public SettingsForm(UiSettings? current, UiPalette palette, float dpiScale)
        {
            _original = current ?? UiSettings.CreateDefault();
            _dpiScale = dpiScale <= 0F ? 1F : dpiScale;

            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Text = "设置";
            BackColor = palette.FormBack;
            ForeColor = palette.TitleText;
            Font = new Font("Microsoft YaHei UI", 10F);
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
            _custom = MakeRadio("自定义调色板（写在 settings.json 的 palette.custom）", y);
            y += S(40);

            Controls.Add(MakeLabel("字号（60~200，100 = 默认）", y + S(4)));

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
                    Palette = _original.Palette
                };
            }
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
            var label = new Label
            {
                Text = text,
                Left = S(18),
                Top = top,
                Width = S(330),
                ForeColor = ForeColor,
                BackColor = BackColor
            };
            return label;
        }

        private RadioButton MakeRadio(string text, int top)
        {
            var radio = new RadioButton
            {
                Text = text,
                Left = S(24),
                Top = top,
                Width = S(320),
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
