using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using FtpJumperLite.Ui;

namespace FtpJumperLite.UiShot
{
    /// <summary>
    /// 一次性截图/诊断工具：把主窗体按指定尺寸真实渲染并存成 PNG，用于人工核对布局。
    /// 用法：UiShot.exe &lt;configPath&gt; &lt;width&gt; &lt;height&gt; &lt;outputPng&gt; [--diag]
    /// </summary>
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            var config = args.Length > 0 ? args[0] : "subjects.json";
            var width = args.Length > 1 ? int.Parse(args[1]) : 0;
            var height = args.Length > 2 ? int.Parse(args[2]) : 0;
            var output = args.Length > 3 ? args[3] : "ui.png";
            var diag = args.Contains("--diag");

            using (var form = new MainForm(config))
            {
                var afterCtor = form.Size;
                form.StartPosition = FormStartPosition.Manual;
                form.Location = new Point(-4000, -4000);
                if (width > 0 && height > 0)
                {
                    form.Size = new Size(width, height);
                }

                var afterSet = form.Size;
                form.Show();
                Application.DoEvents();
                for (var i = 0; i < 10; i++)
                {
                    System.Threading.Thread.Sleep(60);
                    Application.DoEvents();
                }

                Console.WriteLine("请求=" + width + "x" + height
                                  + " 构造后=" + afterCtor
                                  + " 赋值后=" + afterSet
                                  + " Show后=" + form.Size
                                  + " client=" + form.ClientSize
                                  + " dpi=" + form.DeviceDpi
                                  + " scale=" + form.DpiScale
                                  + " min=" + form.MinimumSize
                                  + " max=" + form.MaximumSize
                                  + " autoScale=" + form.AutoScaleMode);

                if (diag)
                {
                    var table = GetPrivateField<TableLayoutPanel>(form, "_subjectTable");
                    Console.WriteLine("表格 client=" + table.ClientSize + " padding=" + table.Padding
                                      + " 行样式数=" + table.RowStyles.Count + " 控件数=" + table.Controls.Count
                                      + " 滚动=" + table.VerticalScroll.Visible);
                    Console.WriteLine("  竖滚动可见=" + table.VerticalScroll.Visible
                                      + " 横滚动可见=" + table.HorizontalScroll.Visible
                                      + " 横滚动启用=" + table.HorizontalScroll.Enabled
                                      + " AutoScrollMinSize=" + table.AutoScrollMinSize
                                      + " DisplayRectangle=" + table.DisplayRectangle
                                      + " 子控件最大右边界=" + (table.Controls.Count == 0 ? 0 : table.Controls.Cast<Control>().Max(c => c.Right))
                                      + " 按钮首选宽=" + (table.Controls.Count == 0 ? 0 : table.Controls.Cast<Control>().Max(c => c.PreferredSize.Width)));

                    foreach (Control row in table.Controls)
                    {
                        var button = (Button)row;
                        var textHeight = MainForm.MeasureTwoLineHeight(
                            button.Text,
                            button.Font.Size,
                            Math.Max(1, button.ClientSize.Width - 6));
                        Console.WriteLine("  行 Y=" + row.Top + " 行高=" + row.Height
                                          + " 按钮客户高=" + button.ClientSize.Height
                                          + " 字号=" + button.Font.Size
                                          + " 文本高=" + textHeight
                                          + " 文本=" + button.Text.Replace("\r\n", " / "));
                    }
                }

                using (var bitmap = new Bitmap(form.Width, form.Height))
                {
                    if (diag && args.Contains("--print"))
                    {
                        // 真实窗口渲染（PrintWindow），比 DrawToBitmap 更忠实（后者对滚动条等渲染不准）。
                        form.Location = new Point(0, 0);
                        Application.DoEvents();
                        System.Threading.Thread.Sleep(400);
                        Application.DoEvents();
                        using (var g = Graphics.FromImage(bitmap))
                        {
                            var hdc = g.GetHdc();
                            try
                            {
                                PrintWindow(form.Handle, hdc, 1 /*PW_CLIENTONLY*/);
                            }
                            finally
                            {
                                g.ReleaseHdc(hdc);
                            }
                        }
                    }
                    else
                    {
                        form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
                    }

                    bitmap.Save(output, ImageFormat.Png);
                }

                Console.WriteLine("已保存 " + Path.GetFullPath(output));
                form.Close();
            }

            return 0;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

        private static T GetPrivateField<T>(object instance, string fieldName)
            where T : class
        {
            var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            if (field == null)
            {
                throw new InvalidOperationException("找不到字段 " + fieldName);
            }

            return (T)field.GetValue(instance)!;
        }
    }
}
