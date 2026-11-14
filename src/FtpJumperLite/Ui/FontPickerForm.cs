using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace FtpJumperLite.Ui
{
    /// <summary>
    /// 内置字体选择器：只做"列目录 + 选字体文件"这一件事。
    ///
    /// 为什么不用系统自带的 OpenFileDialog：
    /// 某些机器（例如装了 Nahimic / Alienware Sound Center 的戴尔、外星人机型）里，
    /// 那个对话框会让注入的 NahimicOSD.dll 直接崩掉我们的进程（实测 0xC0000005，日志里连退出记录都没有）。
    /// 这里只用纯文件 API + 普通控件，不碰外壳扩展、不碰 OLE 拖放，因此在那种机器上也稳。
    /// </summary>
    public sealed class FontPickerForm : Form
    {
        private readonly UiPalette _palette;
        private readonly float _dpiScale;
        private readonly ListBox _list;
        private readonly Label _path;
        private readonly Win11Button _ok;
        private string _directory;

        public FontPickerForm(UiPalette palette, float dpiScale, string? startDirectory)
        {
            _palette = palette;
            _dpiScale = dpiScale <= 0F ? 1F : dpiScale;
            _directory = PickStartDirectory(startDirectory);

            AutoScaleMode = AutoScaleMode.None;
            FormBorderStyle = FormBorderStyle.Sizable;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Text = "选择字体文件";
            BackColor = palette.FormBack;
            ForeColor = palette.TitleText;
            Font = new Font("Microsoft YaHei UI", 10F);
            ClientSize = new Size(S(560), S(440));
            MinimumSize = new Size(S(470), S(300));

            _path = new Label
            {
                Left = S(12),
                Top = S(10),
                AutoSize = false,
                Height = S(20),
                Width = S(496),
                AutoEllipsis = true,
                ForeColor = palette.SubText,
                BackColor = palette.FormBack
            };
            Controls.Add(_path);

            _list = new ListBox
            {
                Left = S(12),
                Top = S(36),
                Width = S(496),
                Height = S(300),
                IntegralHeight = false,
                BackColor = palette.CardBack,
                ForeColor = palette.TitleText,
                BorderStyle = BorderStyle.FixedSingle
            };
            _list.DoubleClick += (_, _) => ActivateSelection();
            // 选中字体文件才允许点「确定」：之前这里漏了，导致确定按钮一直是灰的。
            _list.SelectedIndexChanged += (_, _) => UpdateOkState();
            // 回车 = 双击（选中目录就进去、选中字体就确定）。
            _list.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    ActivateSelection();
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            };
            Controls.Add(_list);

            var up = MakeButton("上一级", S(12), S(344), S(90));
            up.Click += (_, _) => NavigateTo(Path.GetDirectoryName(_directory));

            var systemFonts = MakeButton("系统字体目录", S(110), S(344), S(130));
            systemFonts.Click += (_, _) => NavigateTo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "Fonts"));

            _ok = MakeButton("确定", S(330), S(344), S(84));
            _ok.Click += (_, _) => ActivateSelection();

            var cancel = MakeButton("取消", S(424), S(344), S(84));
            cancel.DialogResult = DialogResult.Cancel;

            Controls.Add(up);
            Controls.Add(systemFonts);
            Controls.Add(_ok);
            Controls.Add(cancel);
            AcceptButton = _ok;
            CancelButton = cancel;

            Resize += (_, _) => LayoutChildren();
            LayoutChildren();
            NavigateTo(_directory);
        }

        /// <summary>选中字体的完整路径（只在 DialogResult = OK 时有效）。</summary>
        public string? SelectedPath { get; private set; }

        /// <summary>列出某个目录下的字体文件（按文件名排序，不递归）。</summary>
        public static List<string> ListFontFiles(string directory)
        {
            var result = new List<string>();
            try
            {
                foreach (var file in Directory.GetFiles(directory))
                {
                    if (IsFontFile(file))
                    {
                        result.Add(file);
                    }
                }

                result.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                // 没有权限的目录就当空的，界面上会显示"（没有字体文件）"。
            }

            return result;
        }

        public static bool IsFontFile(string path)
        {
            var extension = Path.GetExtension(path) ?? string.Empty;
            return extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".otf", StringComparison.OrdinalIgnoreCase)
                   || extension.Equals(".ttc", StringComparison.OrdinalIgnoreCase);
        }

        private static string PickStartDirectory(string? startDirectory)
        {
            if (!string.IsNullOrEmpty(startDirectory) && Directory.Exists(startDirectory))
            {
                return startDirectory!;
            }

            return AppDomain.CurrentDomain.BaseDirectory;
        }

        private void LayoutChildren()
        {
            var width = Math.Max(S(200), ClientSize.Width - S(24));
            var height = Math.Max(S(120), ClientSize.Height - S(120));
            _path.Width = width;
            _list.Width = width;
            _list.Height = height;

            var top = _list.Bottom + S(10);
            MoveChild("上一级", S(12));
            MoveChild("系统字体目录", S(110));
            MoveChild("取消", ClientSize.Width - S(96));
            MoveChild("确定", ClientSize.Width - S(190));

            void MoveChild(string text, int left)
            {
                foreach (Control child in Controls)
                {
                    if (child is Win11Button button && button.Text == text)
                    {
                        button.Top = top;
                        button.Left = left;
                    }
                }
            }
        }

        private Win11Button MakeButton(string text, int left, int top, int width)
        {
            return new Win11Button
            {
                Text = text,
                Left = left,
                Top = top,
                Width = width,
                Height = S(32),
                Palette = _palette,
                DpiScale = _dpiScale
            };
        }

        private void NavigateTo(string? directory)
        {
            if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory))
            {
                return;
            }

            _directory = directory!;
            _path.Text = _directory;
            _list.Items.Clear();

            try
            {
                var parent = Path.GetDirectoryName(_directory);
                if (!string.IsNullOrEmpty(parent))
                {
                    _list.Items.Add(new Entry("..\\", parent, false));
                }

                foreach (var child in Directory.GetDirectories(_directory))
                {
                    _list.Items.Add(new Entry(Path.GetFileName(child) + "\\", child, false));
                }
            }
            catch
            {
                // 读不了子目录就只列文件。
            }

            foreach (var file in ListFontFiles(_directory))
            {
                _list.Items.Add(new Entry(Path.GetFileName(file), file, true));
            }

            if (_list.Items.Count == 0)
            {
                _list.Items.Add(new Entry("（这个目录里没有字体文件）", null, false));
            }

            UpdateOkState();
        }

        /// <summary>只有选中"字体文件"时「确定」才可用；选中目录/占位项时不可用。</summary>
        private void UpdateOkState()
        {
            _ok.Enabled = _list.SelectedItem is Entry entry && entry.IsFont;
        }

        private void ActivateSelection()
        {
            if (!(_list.SelectedItem is Entry entry) || entry.FullPath == null)
            {
                return;
            }

            if (!entry.IsFont)
            {
                NavigateTo(entry.FullPath);
                return;
            }

            SelectedPath = entry.FullPath;
            DialogResult = DialogResult.OK;
            Close();
        }

        private int S(int logicalPixels)
        {
            return MainForm.ScaleFor(logicalPixels, _dpiScale);
        }

        private sealed class Entry
        {
            public Entry(string text, string? fullPath, bool isFont)
            {
                Text = text;
                FullPath = fullPath;
                IsFont = isFont;
            }

            public string Text { get; }

            public string? FullPath { get; }

            public bool IsFont { get; }

            public override string ToString()
            {
                return Text;
            }
        }
    }
}
