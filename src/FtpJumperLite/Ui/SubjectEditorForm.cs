using System;
using System.Drawing;
using System.Windows.Forms;
using FtpJumperLite.Models;

namespace FtpJumperLite.Ui
{
    /// <summary>
    /// 新增科目窗口（字段级表单，不做自由 JSON 编辑）。
    /// 只收集"能打开这个科目"所需的字段；颜色/纹理暂时仍写在 subjects.json 里（按既定计划）。
    /// 保存路径：由调用方交给 ConfigStore.TryAddSubject，整份校验通过才落盘。
    /// </summary>
    public sealed class SubjectEditorForm : Form
    {
        private readonly UiPalette _palette;
        private readonly float _dpiScale;
        private readonly TextBox _id;
        private readonly TextBox _name;
        private readonly TextBox _host;
        private readonly NumericUpDown _port;
        private readonly TextBox _user;
        private readonly TextBox _password;
        private readonly TextBox _remotePath;
        private readonly ComboBox _credentialMode;
        private readonly Label _hint;

        public SubjectEditorForm(UiPalette palette, float dpiScale)
        {
            _palette = palette;
            _dpiScale = dpiScale <= 0F ? 1F : dpiScale;

            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Text = "新增科目";
            BackColor = palette.FormBack;
            ForeColor = palette.TitleText;
            Font = new Font("Microsoft YaHei UI", 10F);
            ClientSize = new Size(S(430), S(392));

            var y = S(14);
            _id = AddField("标识（英文/数字，鼠标悬停提示用）", y, "s" + DateTime.Now.ToString("yyyyMMddHHmmss"));
            y += S(44);
            _name = AddField("科目名称（显示在卡片上）", y, "");
            y += S(44);
            _host = AddField("FTP 主机", y, "ftp.example.com");
            y += S(44);

            Controls.Add(MakeLabel("端口", y));
            _port = new NumericUpDown
            {
                Left = S(18),
                Top = y + S(20),
                Width = S(90),
                Minimum = 1,
                Maximum = 65535,
                Value = 21,
                BackColor = _palette.CardBack,
                ForeColor = _palette.TitleText,
                BorderStyle = BorderStyle.FixedSingle
            };
            Controls.Add(_port);
            y += S(44);

            _user = AddField("账号", y, "");
            y += S(44);
            _password = AddField("密码（明文保存在 subjects.json，与现状一致）", y, "");
            y += S(44);

            _remotePath = AddField("远端路径（留空 = 站点根目录）", y, "/");
            y += S(44);

            Controls.Add(MakeLabel("凭据方式", y));
            _credentialMode = new ComboBox
            {
                Left = S(18),
                Top = y + S(20),
                Width = S(200),
                DropDownStyle = ComboBoxStyle.DropDownList,
                BackColor = _palette.CardBack,
                ForeColor = _palette.TitleText
            };
            _credentialMode.Items.AddRange(new object[] { "inline", "prompt" });
            _credentialMode.SelectedIndex = 0;
            Controls.Add(_credentialMode);
            y += S(50);

            _hint = new Label
            {
                Left = S(18),
                Top = y,
                Width = S(394),
                Height = S(34),
                ForeColor = _palette.SubText,
                BackColor = _palette.FormBack,
                Text = "颜色/底纹请保存后在 subjects.json 里加 \"color\"/\"texture\"。"
            };
            Controls.Add(_hint);

            var okButton = new Win11Button
            {
                Text = "保存",
                Left = S(222),
                Top = S(342),
                Width = S(90),
                Height = S(32),
                Palette = palette,
                DpiScale = _dpiScale
            };
            var cancelButton = new Win11Button
            {
                Text = "取消",
                Left = S(322),
                Top = S(342),
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
                LastSyncAt = ""
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

            Result = result;
            DialogResult = DialogResult.OK;
            Close();
        }

        private void Warn(string message)
        {
            MessageBox.Show(this, message, "请检查填写内容", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            return new Label
            {
                Text = text,
                Left = S(18),
                Top = top,
                Width = S(394),
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
