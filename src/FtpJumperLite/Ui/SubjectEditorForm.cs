using System;
using System.Drawing;
using System.Windows.Forms;
using FtpJumperLite.Models;

namespace FtpJumperLite.Ui
{
    /// <summary>
    /// 科目编辑器（新增 / 修改同一套表单，字段级，不做自由 JSON 编辑）。
    /// 高 DPI 约定：
    ///  * 所有尺寸都按 dpiScale 换算（不要用固定像素）；
    ///  * 标签/单选钮一律 AutoSize —— 它们的默认高度是硬编码像素，高 DPI 下会把文字裁掉；
    ///  * 窗口高度按"内容实际高度"算，并夹到屏幕可用区内；内容放不下时内容区可滚动，
    ///    按钮始终固定在客户区底部（不会像以前那样被顶出去）。
    /// 保存路径由调用方交给 ConfigStore.TryAddSubject / TryUpdateSubject：整份校验通过才落盘。
    /// </summary>
    public sealed class SubjectEditorForm : Form
    {
        private const int RowHeight = 42;
        private const int DialogWidth = 430;
        private const int FieldWidth = 374;
        private const int ButtonBarHeight = 52;

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

        public SubjectEditorForm(UiPalette palette, float dpiScale, UiFontSet? fonts, Subject? existing = null)
        {
            _palette = palette;
            _dpiScale = dpiScale <= 0F ? 1F : dpiScale;
            _existing = existing;

            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Text = existing == null ? "新增科目" : "编辑科目";
            BackColor = palette.FormBack;
            ForeColor = palette.TitleText;
            Font = (fonts ?? UiFontSet.Default).Create(10F);

            // 先给一个临时尺寸，用来量出标题栏/边框占多少，再按内容算最终高度。
            ClientSize = new Size(S(DialogWidth), S(400));
            MinimumSize = new Size(S(DialogWidth) + S(16), S(320));

            var content = new Panel
            {
                BackColor = palette.FormBack,
                AutoScroll = true,
                Location = Point.Empty
            };

            var y = S(12);
            _id = AddField(content, "标识（英文/数字，用于日志与本地目录名）", y, existing?.Id ?? "s" + DateTime.Now.ToString("yyyyMMddHHmmss"));
            y += S(RowHeight);
            _name = AddField(content, "科目名称（显示在卡片上）", y, existing?.Name ?? "");
            y += S(RowHeight);
            _host = AddField(content, "FTP 主机", y, existing?.Host ?? "ftp.example.com");
            y += S(RowHeight);

            content.Controls.Add(MakeLabel("端口", y));
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
            content.Controls.Add(_port);

            // 同一行右侧：标签必须显式给左边距，否则会压到"端口"上。
            content.Controls.Add(MakeLabel("凭据方式", y, S(132)));
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
            content.Controls.Add(_credentialMode);
            y += S(RowHeight);

            _user = AddField(content, "账号", y, existing?.User ?? "");
            y += S(RowHeight);
            _password = AddField(content, "密码（明文保存在 subjects.json，与现状一致）", y, existing?.Password ?? "");
            y += S(RowHeight);
            _remotePath = AddField(content, "远端路径（留空 = 站点根目录）", y, existing?.RemotePath ?? "/");
            y += S(RowHeight);

            content.Controls.Add(MakeLabel("强调色（左侧色条 + 极淡同色底；留空 = 跟随主题）", y));
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
            content.Controls.Add(_color);

            content.Controls.Add(MakeLabel("底纹", y, S(236)));
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
            content.Controls.Add(_texture);
            y += S(56);

            var note = new Label
            {
                Left = S(18),
                Top = y,
                AutoSize = true,
                MaximumSize = new Size(S(FieldWidth), 0),
                ForeColor = _palette.SubText,
                BackColor = _palette.FormBack,
                Text = "保存后会整份校验通过才写回 subjects.json；写回前自动备份为 .bak。"
            };
            content.Controls.Add(note);

            var okButton = new Win11Button
            {
                Text = "保存",
                Width = S(90),
                Height = S(32),
                Palette = palette,
                DpiScale = _dpiScale
            };
            var cancelButton = new Win11Button
            {
                Text = "取消",
                Width = S(90),
                Height = S(32),
                Palette = palette,
                DpiScale = _dpiScale,
                DialogResult = DialogResult.Cancel
            };
            okButton.Click += (_, _) => TryAccept();

            Controls.Add(content);
            Controls.Add(okButton);
            Controls.Add(cancelButton);
            AcceptButton = okButton;
            CancelButton = cancelButton;

            // 高度 = 内容 + 按钮栏；再夹到屏幕可用区（内容超高时内容区自己滚，按钮不动）。
            var contentHeight = note.Bottom + S(12);
            var desiredHeight = contentHeight + S(ButtonBarHeight) + S(8);
            ClientSize = new Size(S(DialogWidth), Math.Min(desiredHeight, MaxClientHeight()));

            LayoutChrome(content, okButton, cancelButton);
            Resize += (_, _) => LayoutChrome(content, okButton, cancelButton);
        }

        /// <summary>通过校验后的科目标（只在 DialogResult=OK 时有效）。</summary>
        public Subject Result { get; private set; } = new Subject();

        /// <summary>客户区最多能有多高（扣掉标题栏/边框与一点边距，保证窗口能完整显示）。</summary>
        private int MaxClientHeight()
        {
            var work = Screen.PrimaryScreen?.WorkingArea.Height ?? 1080;
            var nonClient = Height - ClientSize.Height;
            return Math.Max(S(260), work - nonClient - S(24));
        }

        /// <summary>内容面板占满按钮栏以上的区域，两个按钮贴右下（尺寸随窗口变）。</summary>
        private void LayoutChrome(Panel content, Win11Button ok, Win11Button cancel)
        {
            var barHeight = S(ButtonBarHeight);
            var clientWidth = ClientSize.Width;
            var clientHeight = ClientSize.Height;

            content.Bounds = new Rectangle(0, 0, clientWidth, Math.Max(S(120), clientHeight - barHeight));

            var buttonTop = Math.Max(content.Bottom + S(10), clientHeight - barHeight + S(10));
            var rightMargin = S(30);
            cancel.Left = clientWidth - rightMargin - cancel.Width;
            ok.Left = cancel.Left - S(10) - ok.Width;
            ok.Top = buttonTop;
            cancel.Top = buttonTop;
        }

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

        private TextBox AddField(Control parent, string label, int top, string initial)
        {
            parent.Controls.Add(MakeLabel(label, top));
            var box = new TextBox
            {
                Left = S(18),
                Top = top + S(20),
                Width = S(FieldWidth),
                Text = initial,
                BackColor = _palette.CardBack,
                ForeColor = _palette.TitleText,
                BorderStyle = BorderStyle.FixedSingle
            };
            parent.Controls.Add(box);
            return box;
        }

        private Label MakeLabel(string text, int top, int? left = null)
        {
            // AutoSize + 宽度上限：Label 默认高度是硬编码 23px，高 DPI 下字体更高会把文字裁掉；
            // 让它跟着字体长高、宽度到边自动换行。
            return new Label
            {
                Text = text,
                Left = left ?? S(18),
                Top = top,
                AutoSize = true,
                MaximumSize = new Size(S(FieldWidth), 0),
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
