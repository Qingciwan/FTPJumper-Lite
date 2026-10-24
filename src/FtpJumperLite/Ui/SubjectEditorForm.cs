using System;
using System.Drawing;
using System.Windows.Forms;
using FtpJumperLite.Models;

namespace FtpJumperLite.Ui
{
    /// <summary>
    /// 科目编辑器（新增 / 修改同一套表单，字段级，不做自由 JSON 编辑）。
    /// 保存路径由调用方交给 ConfigStore.TryAddSubject / TryUpdateSubject：整份校验通过才落盘。
    /// </summary>
    public sealed class SubjectEditorForm : Form
    {
        private const int RowHeight = 42;

        private readonly UiPalette _palette;
        private readonly float _dpiScale;
        private readonly Subject? _existing;
        private readonly TextBox _id;
        private readonly TextBox _name;
        private readonly TextBox _host;
        private readonly NumericUpDown _port;
        private readonly TextBox _user;
        private readonly TextBox _password;
        private readonly TextBox _remotePath;
        private readonly ComboBox _credentialMode;
        private readonly ComboBox _color;
        private readonly ComboBox _texture;

        public SubjectEditorForm(UiPalette palette, float dpiScale, Subject? existing = null)
        {
            _palette = palette;
            _dpiScale = dpiScale <= 0F ? 1F : dpiScale;
            _existing = existing;

            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Text = existing == null ? "新增科目" : "编辑科目";
            BackColor = palette.FormBack;
            ForeColor = palette.TitleText;
            Font = new Font("Microsoft YaHei UI", 10F);
            ClientSize = new Size(S(430), S(552));

            var y = S(12);
            _id = AddField("标识（英文/数字，用于日志与本地目录名）", y, existing?.Id ?? "s" + DateTime.Now.ToString("yyyyMMddHHmmss"));
            y += S(RowHeight);
            _name = AddField("科目名称（显示在卡片上）", y, existing?.Name ?? "");
            y += S(RowHeight);
            _host = AddField("FTP 主机", y, existing?.Host ?? "ftp.example.com");
            y += S(RowHeight);

            Controls.Add(MakeLabel("端口", y));
            _port = new NumericUpDown
            {
                Left = S(18),
                Top = y + S(20),
                Width = S(90),
                Minimum = 1,
                Maximum = 65535,
                Value = Math.Max(1, Math.Min(65535, existing?.Port ?? 21)),
                BackColor = _palette.CardBack,
                ForeColor = _palette.TitleText,
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(_port);

            Controls.Add(MakeLabel("凭据方式", y));
            _credentialMode = new ComboBox
            {
                Left = S(132),
                Top = y + S(20),
                Width = S(200),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = _palette.CardBack,
                ForeColor = _palette.TitleText
            };
            _credentialMode.Items.AddRange(new object[] { "inline", "prompt" });
            _credentialMode.SelectedItem = string.Equals(existing?.CredentialMode, "prompt", StringComparison.OrdinalIgnoreCase)
                ? "prompt"
                : "inline";
            Controls.Add(_credentialMode);
            y += S(RowHeight);

            _user = AddField("账号", y, existing?.User ?? "");
            y += S(RowHeight);
            _password = AddField("密码（明文保存在 subjects.json，与现状一致）", y, existing?.Password ?? "");
            y += S(RowHeight);
            _remotePath = AddField("远端路径（留空 = 站点根目录）", y, existing?.RemotePath ?? "/");
            y += S(RowHeight);

            Controls.Add(MakeLabel("强调色（左侧色条 + 极淡同色底；留空 = 跟随主题）", y));
            _color = new ComboBox
            {
                Left = S(18),
                Top = y + S(20),
                Width = S(200),
                DropDownStyle = ComboBoxStyle.DropDown,
                BackColor = _palette.CardBack,
                ForeColor = _palette.TitleText
            };
            _color.Items.AddRange(new object[] { "", "红", "橙", "黄", "绿", "青", "蓝", "紫", "粉", "棕", "灰" });
            _color.Text = existing?.ColorHex ?? "";
            Controls.Add(_color);

            Controls.Add(MakeLabel("底纹", y));
            _texture = new ComboBox
            {
                Left = S(236),
                Top = y + S(20),
                Width = S(176),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = _palette.CardBack,
                ForeColor = _palette.TitleText
            };
            _texture.Items.AddRange(new object[] { "无", "stripe（斜纹）", "dots（点阵）", "grid（网格）", "gradient（渐变）" });
            _texture.SelectedIndex = TextureToIndex(existing?.Texture);
            Controls.Add(_texture);
            y += S(56);

            var note = new Label
            {
                Left = S(18),
                Top = y,
                AutoSize = true,
                MaximumSize = new Size(S(394), 0),
                ForeColor = _palette.SubText,
                BackColor = _palette.FormBack,
                Text = "保存后会整份校验通过才写回 subjects.json；写回前自动备份为 .bak。"
            };
            Controls.Add(note);

            var okButton = new Win11Button
            {
                Text = "保存",
                Left = S(222),
                Top = S(506),
                Width = S(90),
                Height = S(32),
                Palette = palette,
                DpiScale = _dpiScale
            };
            var cancelButton = new Win11Button
            {
                Text = "取消",
                Left = S(322),
                Top = S(506),
                Width = S(90),
                Height = S(32),
                Palette = palette,
                DpiScale = _dpiScale,
                DialogResult = DialogResult.Cancel
            };
            okButton.Click += (_, _) => TryAccept();
            Controls.Add(okButton);
            Controls.Add(cancelButton);
            AcceptButton = okButton;
            CancelButton = cancelButton;
        }

        /// <summary>通过校验后的科目标（只在 DialogResult=OK 时有效）。</summary>
        public Subject Result { get; private set; } = new Subject();

        private void TryAccept()
        {
            var result = new Subject
            {
                Id = (_id.Text ?? "").Trim(),
                Name = (_name.Text ?? "").Trim(),
                Host = (_host.Text ?? "").Trim(),
                Port = (int)_port.Value,
                User = (_user.Text ?? "").Trim(),
                Password = _password.Text ?? "",
                RemotePath = (_remotePath.Text ?? "").Trim(),
                CredentialMode = (string)(_credentialMode.SelectedItem ?? "inline"),
                ColorHex = (_color.Text ?? "").Trim(),
                Texture = IndexToTexture(_texture.SelectedIndex),
                LastSyncAt = _existing?.LastSyncAt ?? ""
            };

            if (result.Id.Length == 0)
            {
                Warn("请填写标识（英文/数字）。");
                return;
            }

            if (result.Name.Length == 0)
            {
                Warn("请填写科目名称。");
                return;
            }

            if (result.Host.Length == 0)
            {
                Warn("请填写 FTP 主机。");
                return;
            }

            if (!SubjectStyles.TryNormalizeColor(result.ColorHex, out var color))
            {
                Warn("颜色写法不正确。可以写 #RRGGBB、#ABC，或 红/蓝/绿 这类色名。");
                return;
            }

            result.ColorHex = color;

            Result = result;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void Warn(string message)
        {
            MessageBox.Show(this, message, "请检查填写内容", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static int TextureToIndex(string? texture)
        {
            switch ((texture ?? "").Trim().ToLowerInvariant())
            {
                case "stripe":
                    return 1;
                case "dots":
                    return 2;
                case "grid":
                    return 3;
                case "gradient":
                    return 4;
                default:
                    return 0;
            }
        }

        private static string? IndexToTexture(int index)
        {
            switch (index)
            {
                case 1:
                    return "stripe";
                case 2:
                    return "dots";
                case 3:
                    return "grid";
                case 4:
                    return "gradient";
                default:
                    return null;
            }
        }

        private TextBox AddField(string label, int top, string initial)
        {
            Controls.Add(MakeLabel(label, top));
            var box = new TextBox
            {
                Left = S(18),
                Top = top + S(20),
                Width = S(394),
                Text = initial,
                BackColor = _palette.CardBack,
                ForeColor = _palette.TitleText,
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(box);
            return box;
        }

        private Label MakeLabel(string text, int top)
        {
            // AutoSize + 宽度上限：Label 默认高度是硬编码 23px，高 DPI 下字体更高会把文字裁掉；
            // 让它跟着字体长高、宽度到边自动换行。
            return new Label
            {
                Text = text,
                Left = S(18),
                Top = top,
                AutoSize = true,
                MaximumSize = new Size(S(394), 0),
                ForeColor = _palette.SubText,
                BackColor = _palette.FormBack
            };
        }

        private int S(int logicalPixels)
        {
            return MainForm.ScaleFor(logicalPixels, _dpiScale);
        }
    }
}
