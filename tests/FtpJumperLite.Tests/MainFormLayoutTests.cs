using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;
using FtpJumperLite.Models;
using FtpJumperLite.Ui;
using Newtonsoft.Json;
using Xunit;

namespace FtpJumperLite.Tests
{
    /// <summary>
    /// 主窗体布局回归测试：用 STA 线程真实创建窗体（不显示），程序化校验
    /// 1) 底部按钮完整落在客户区内（防止“按钮半个在窗口外”）；
    /// 2) 单列布局下 10 个科目刚好放下、不溢出。
    /// </summary>
    public sealed class MainFormLayoutTests : IDisposable
    {
        private readonly string _dir;

        public MainFormLayoutTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "FtpJumperLiteTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(_dir))
                {
                    Directory.Delete(_dir, true);
                }
            }
            catch
            {
                // 忽略清理失败。
            }
        }

        [Fact]
        public void BottomButtons_And_TenRows_FitInsideWindow()
        {
            // 覆盖多种窗口尺寸：默认（100% DPI）、2K@200% 缩放下的窄高比例、最小尺寸、更宽的窗口。
            foreach (var size in new[]
                     {
                         new Size(740, 1000),
                         new Size(697, 680),   // 2560x1440 @200% 的逻辑可用区域
                         new Size(660, 620),   // 更矮的教室机
                         new Size(470, 520)    // 最小尺寸
                     })
            {
                AssertLayoutAt(size);
            }
        }

        /// <summary>
        /// 让当前线程按 PerMonitorV2 感知 DPI：否则测试宿主是非感知进程，窗口会被虚拟化成 96 DPI，
        /// 于是“高 DPI 布局”这条路径永远测不到（本机实际是 175%，DeviceDpi=168）。
        /// </summary>
        private static void MakeThreadDpiAware()
        {
            try
            {
                SetThreadDpiAwarenessContext(new IntPtr(-4)); // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
            }
            catch
            {
                // 老系统没有该 API，忽略（测试仍会在 96 DPI 下运行）。
            }
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr value);

        private void AssertLayoutAt(Size size)
        {
            var configPath = Path.Combine(_dir, "subjects.json");
            File.WriteAllText(configPath, BuildConfigJson(10));

            Exception? failure = null;
            var thread = new Thread(() =>
            {
                MakeThreadDpiAware();
                try
                {
                    AssertLayout(configPath, size);
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            Assert.True(failure == null, "窗口 " + size + " 下布局校验失败：" + failure);
        }

        /// <summary>
        /// 科目行的几何不变式：**所有行等高等宽、左右留白对称、最后一行不得被拉伸**。
        /// 覆盖 2 / 9 / 10 / 11 / 12 个科目（越过 MaxVisibleRows=10 的分界）以及
        /// “不设尺寸 = 程序启动时的默认尺寸”这一场景。
        /// </summary>
        [Theory]
        [InlineData(2)]
        [InlineData(9)]
        [InlineData(10)]
        [InlineData(11)]
        [InlineData(12)]
        public void RowsAreUniformAndSymmetric(int subjectCount)
        {
            var configPath = Path.Combine(_dir, "subjects-" + subjectCount + ".json");
            File.WriteAllText(configPath, BuildConfigJson(subjectCount));

            var sizes = new[]
            {
                (Size?)null,              // 启动时的默认尺寸
                new Size(455, 632),       // 2560x1440 @200% 的逻辑默认尺寸
                new Size(490, 680),
                new Size(697, 680),       // 2560x1440 @200% 的最大可用区域
                new Size(740, 1000),      // 1080p @100%
                new Size(470, 520)        // 最小尺寸
            };

            foreach (var size in sizes)
            {
                Exception? failure = null;
                var thread = new Thread(() =>
                {
                    MakeThreadDpiAware();
                    try
                    {
                        AssertRowGeometry(configPath, size);
                    }
                    catch (Exception ex)
                    {
                        failure = ex;
                    }
                });
                thread.SetApartmentState(ApartmentState.STA);
                thread.Start();
                thread.Join();

                Assert.True(
                    failure == null,
                    subjectCount + " 个科目 @ " + (size.HasValue ? size.Value.ToString() : "启动默认尺寸") + "：" + failure);
            }
        }

        private static void AssertRowGeometry(string configPath, Size? size)
        {
            using (var form = new MainForm(configPath))
            {
                if (size.HasValue)
                {
                    form.Size = size.Value;
                }

                form.CreateControl();
                form.PerformLayout();

                var table = GetPrivateField<TableLayoutPanel>(form, "_subjectTable");
                var rows = table.Controls.Cast<Control>().ToList();
                var heights = rows.Select(r => r.Height).ToList();
                var leftGap = rows.Min(r => r.Left);
                var rightGap = rows.Min(r => table.ClientSize.Width - r.Right);
                var scrollbar = table.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0;
                var info = "行数=" + rows.Count
                           + " 行高=[" + string.Join(",", heights) + "]"
                           + " 左留白=" + leftGap + " 右留白=" + rightGap
                           + " 表client=" + table.ClientSize + " padding=" + table.Padding
                           + " 滚动=" + table.VerticalScroll.Visible;

                // 1) 所有科目行等高
                Assert.True(heights.Max() - heights.Min() <= 1, "行高不一致（最后一行被拉伸？）：" + info);

                // 2) 左右留白对称（有滚动条时右侧会多出滚动条宽度）
                Assert.True(
                    Math.Abs(leftGap - rightGap) <= scrollbar + 1,
                    "行未水平居中（左右留白不对称）：" + info);

                // 3) 行宽 + 左右留白 = 表格客户区宽（确认没有被裁切）
                Assert.Equal(table.ClientSize.Width, leftGap + rows[0].Width + rightGap);

                // 4) 每行都必须装得下自己的两行文本——这是“最后一行被撑开 / 文字被裁”的根因不变式：
                //    一旦行高小于内容所需，TableLayoutPanel 就会自行重排并把多余空间塞给最后一行。
                foreach (var row in rows)
                {
                    var button = Assert.IsType<Button>(row);
                    var textHeight = MainForm.MeasureTwoLineHeight(
                        button.Text,
                        button.Font.Size,
                        Math.Max(1, button.ClientSize.Width - 6));
                    Assert.True(
                        textHeight <= Math.Max(1, button.ClientSize.Height - 6),
                        "行内容放不下（会触发重排/裁切）：文本高 " + textHeight
                        + " > 可用高 " + Math.Max(1, button.ClientSize.Height - 6)
                        + "（字号 " + button.Font.Size + "）| " + info);
                }
            }
        }

        /// <summary>
        /// 行高必须与 DPI **等比** 缩放：字号是“点”（随 DPI 变大），行高常量是像素，
        /// 两者不同步就是 2K@200% 下那两类布局问题的根源。
        /// </summary>
        [Fact]
        public void RowHeight_ScalesWithDpi_AndHonoursFloor()
        {
            Assert.Equal(64, MainForm.ScaleFor(64, 1F));
            Assert.Equal(128, MainForm.ScaleFor(64, 2F));
            Assert.Equal(96, MainForm.ScaleFor(64, 1.5F));

            // 同一“逻辑”可用高度下，200% 的行高必须是 100% 的两倍
            foreach (var (logicalAvailable, count) in new[] { (520, 10), (900, 10), (300, 5), (460, 11), (2000, 10) })
            {
                var at100 = MainForm.ComputeRowHeightPx(logicalAvailable, count, 1F, 0);
                var at200 = MainForm.ComputeRowHeightPx(logicalAvailable * 2, count, 2F, 0);
                var at150 = MainForm.ComputeRowHeightPx((int)(logicalAvailable * 1.5), count, 1.5F, 0);

                Assert.True(at100 >= 64, "100% 下行高不应低于下限：值=" + at100);
                Assert.True(at100 <= 152, "100% 下行高不应超过上限：值=" + at100);
                Assert.Equal(at100 * 2, at200);
                Assert.True(Math.Abs(at100 * 1.5 - at150) <= 1, "150% 行高应与 100% 成正比：" + at100 + " vs " + at150);
            }

            // 内容下限优先于一切：行高低于文字所需高度时，必须以内容下限为准
            Assert.Equal(200, MainForm.ComputeRowHeightPx(520, 10, 1F, 200));
            Assert.Equal(400, MainForm.ComputeRowHeightPx(1040, 10, 2F, 400));

            // 11 个科目（超过一屏）时，行高按“可见 10 行”平分并滚动的下限计算，绝不因为多一行而变矮
            Assert.Equal(
                MainForm.ComputeRowHeightPx(460, 10, 1F, 0),
                MainForm.ComputeRowHeightPx(460, 11, 1F, 0));
        }

        /// <summary>
        /// 临时诊断：把窗口尺寸按网格扫一遍，收集所有“行高不一致 / 左右留白不对称”的组合。
        /// </summary>
        [Fact]
        public void Sweep_FindGeometryViolations()
        {
            var widths = new[] { 420, 455, 500, 560, 620, 700, 800, 1000, 1200 };
            var heights = new[] { 420, 480, 520, 560, 600, 632, 680, 760, 860, 1000 };
            var problems = new System.Collections.Generic.List<string>();

            foreach (var count in new[] { 2, 11 })
            {
                var configPath = Path.Combine(_dir, "sweep-" + count + ".json");
                File.WriteAllText(configPath, BuildLongConfigJson(count));

                foreach (var w in widths)
                {
                    foreach (var h in heights)
                    {
                        var thread = new Thread(() =>
                        {
                            MakeThreadDpiAware();
                            try
                            {
                                using (var form = new MainForm(configPath))
                                {
                                    form.Size = new Size(w, h);
                                    form.CreateControl();
                                    form.PerformLayout();

                                    var table = GetPrivateField<TableLayoutPanel>(form, "_subjectTable");
                                    var rows = table.Controls.Cast<Control>().ToList();
                                    var hs = rows.Select(r => r.Height).ToList();
                                    var left = rows.Min(r => r.Left);
                                    var right = rows.Min(r => table.ClientSize.Width - r.Right);
                                    var bar = table.VerticalScroll.Visible ? SystemInformation.VerticalScrollBarWidth : 0;

                                    if (hs.Max() - hs.Min() > 1)
                                    {
                                        problems.Add(string.Format(
                                            "行高不一致 count={0} {1}x{2}: [{3}] 表={4} 滚动={5}",
                                            count, w, h, string.Join(",", hs), table.ClientSize, table.VerticalScroll.Visible));
                                    }

                                    if (Math.Abs(left - right) > bar + 1)
                                    {
                                        problems.Add(string.Format(
                                            "留白不对称 count={0} {1}x{2}: 左={3} 右={4} 行宽={5} 表宽={6} padding={7} 滚动={8}",
                                            count, w, h, left, right, rows[0].Width, table.ClientSize.Width, table.Padding, table.VerticalScroll.Visible));
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                problems.Add(string.Format("异常 count={0} {1}x{2}: {3}", count, w, h, ex.Message));
                            }
                        });
                        thread.SetApartmentState(ApartmentState.STA);
                        thread.Start();
                        thread.Join();
                    }
                }
            }

            Assert.True(
                problems.Count == 0,
                "共 " + problems.Count + " 处违规：\r\n" + string.Join("\r\n", problems.Take(40)));
        }

        /// <summary>科目名与远端路径都用“较长文本”，模拟真实配置（如 ftp.example.com · /pub/yuwen）。</summary>
        private static string BuildLongConfigJson(int count)
        {
            var config = new AppConfig { Version = 2 };
            for (var i = 1; i <= count; i++)
            {
                config.Subjects.Add(new Subject
                {
                    Id = "s" + i,
                    Name = "语文" + i,
                    Host = "ftp.example.com",
                    Port = 21,
                    User = "u" + i,
                    Password = "p" + i,
                    RemotePath = "/pub/yuwen-" + i,
                    CredentialMode = "inline",
                    LastSyncAt = ""
                });
            }

            return JsonConvert.SerializeObject(config, Formatting.Indented);
        }

        private static void AssertLayout(string configPath, Size size)
        {
            using (var form = new MainForm(configPath))
            {
                form.Size = size;
                form.CreateControl();
                form.PerformLayout();

                var client = form.ClientSize;
                Assert.True(client.Width > 0 && client.Height > 0, "窗体客户区尺寸无效。");

                // 1) 底部三个按钮必须完整落在客户区以及各自父容器内。
                var bottomButtons = new List<Control>();
                foreach (var text in new[] { "刷新配置", "打开配置目录", "退出" })
                {
                    var button = FindByText(form, text);
                    Assert.NotNull(button);
                    bottomButtons.Add(button!);
                }

                var bottomParent = bottomButtons[0].Parent;
                Assert.NotNull(bottomParent);
                foreach (var button in bottomButtons)
                {
                    Assert.Same(bottomParent, button.Parent);
                    Assert.True(
                        bottomParent!.ClientRectangle.Contains(button.Bounds),
                        button.Text + " 超出父容器可见区域：" + button.Bounds + "，父容器 " + bottomParent.ClientRectangle);
                }

                Assert.True(
                    bottomButtons.All(b => b.Top == bottomButtons[0].Top),
                    "底部按钮不在同一行：" + string.Join(" / ", bottomButtons.Select(b => b.Text + b.Bounds)));
                for (var i = 1; i < bottomButtons.Count; i++)
                {
                    Assert.True(
                        bottomButtons[i - 1].Right <= bottomButtons[i].Left,
                        "底部按钮重叠：" + bottomButtons[i - 1].Text + " / " + bottomButtons[i].Text);
                }

                foreach (var button in bottomButtons)
                {
                    var onForm = ToFormClient(button, form);
                    Assert.True(
                        form.ClientRectangle.Contains(onForm),
                        button.Text + " 超出窗体客户区：" + onForm + "，客户区 " + form.ClientRectangle);
                }

                // 2) 单列布局：10 个科目行都存在、高度一致（≤1px 差）且精确填满科目区。
                var table = GetPrivateField<TableLayoutPanel>(form, "_subjectTable");
                Assert.Equal(10, table.Controls.Count);

                var rowHeights = table.Controls.Cast<Control>().Select(c => c.Height).ToList();
                Assert.True(
                    rowHeights.Max() - rowHeights.Min() <= 1,
                    "科目行高度不一致（最后一行被拉伸）：" + string.Join(", ", rowHeights));

                var available = table.ClientSize.Height - table.Padding.Vertical;
                var rowSpace = table.Controls.Cast<Control>().Sum(c => c.Height + c.Margin.Vertical);
                if (rowSpace > available)
                {
                    // 一屏放不下（窗口很矮或科目很多）：行高固定为下限（64，行内再扣 8px 上下边距）并允许纵向滚动。
                    Assert.True(table.AutoScroll, "科目区应允许纵向滚动。");
                    Assert.True(
                        rowSpace / 10 >= 64,
                        "滚动时科目行不应被压扁：平均行占用高度 " + (rowSpace / 10));
                }
                else
                {
                    Assert.True(
                        available - rowSpace <= 2,
                        "科目行未精确填满：行占用高度合计 " + rowSpace + "，可用高度 " + available);
                }

                foreach (var row in table.Controls.Cast<Control>())
                {
                    var rowBounds = row.Bounds;
                    Assert.True(rowBounds.Right <= table.ClientSize.Width, "科目行右侧越界：" + rowBounds);
                    if (rowSpace <= available)
                    {
                        // 不滚动时所有行都应在可视区内；滚动时后面的行本来就在可视区之外。
                        Assert.True(rowBounds.Bottom <= table.ClientSize.Height, "科目行底部越界：" + rowBounds);
                    }
                }

                // 3) 科目名必须水平+垂直居中，且两行文本完整落在按钮内（不被裁切）。
                foreach (var row in table.Controls.Cast<Control>())
                {
                    var button = Assert.IsType<Button>(row);
                    Assert.Equal(ContentAlignment.MiddleCenter, button.TextAlign);

                    // 逐行量测真实文本宽度：带 HorizontalCenter 的 MeasureText 会把文本撑满整幅宽度，
                    // 不能直接用来判断“块是否居中”（用 int.MaxValue 作宽度上限在 GDI 里也会退化成整幅宽度）。
                    var probe = new Size(10000, 10000);
                    var lineWidths = button.Text
                        .Split(new[] { "\r\n" }, StringSplitOptions.None)
                        .Select(line => TextRenderer.MeasureText(
                            line,
                            button.Font,
                            probe,
                            TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width)
                        .ToList();
                    var blockWidth = lineWidths.Max();

                    // 居中判定：左右（以及上下）留白必须相等。
                    // 注意 TextAlign=MiddleCenter 只是设定，这里按实际可用区域算出应有的留白来校验。
                    var hPad = button.ClientSize.Width - blockWidth;
                    Assert.True(
                        hPad >= 0,
                        "科目名超出按钮宽度：文本块 " + blockWidth + " > 按钮 " + button.ClientSize.Width);

                    var pad = button.Padding;
                    var boxHeight = button.ClientSize.Height - pad.Vertical;
                    var wrapped = TextRenderer.MeasureText(
                        button.Text,
                        button.Font,
                        new Size(Math.Max(1, hPad / 2 + blockWidth), 10000),
                        TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);

                    Assert.True(
                        wrapped.Height <= boxHeight,
                        "科目名被纵向裁切：文本高 " + wrapped.Height + " 超过可用高 " + boxHeight
                        + "（按钮 " + button.ClientSize + "，字号 " + button.Font.Size + "）");
                }
            }
        }

        [Fact]
        public void AppIcon_LoadsLargeFrameFromBuiltExe()
        {
            // 测试输出目录里就有被测 exe（由 csproj 的 ApplicationIcon 嵌入了 Report.ico）。
            var exe = Path.Combine(AppContext.BaseDirectory, "FtpJumperLite.exe");
            Assert.True(File.Exists(exe), "测试输出目录里应有被测 exe：" + exe);

            using (var icon = AppIcon.LoadFrom(exe))
            {
                Assert.NotNull(icon);
                Assert.True(
                    icon!.Width >= 48,
                    "应取到大尺寸帧以适配 2K@200%（标题栏/任务栏/Alt+Tab），实际 " + icon.Width + "x" + icon.Height);
            }

            Assert.Null(AppIcon.LoadFrom(Path.Combine(_dir, "不存在的.exe")));
            Assert.Null(AppIcon.LoadFrom(null));
        }

        [Fact]
        public void SubjectFontSize_GrowsWithRowHeight_AndNeverClipsTwoLines()
        {
            // 2K@200% 的教室机窗口更高，字号应随之变大；同时任何行高下两行中文都不能溢出。
            const string text = "科目名称示例\r\nftp.example.com · /pub/yuwen";
            var previous = 0F;
            foreach (var rowHeight in new[] { 56, 64, 80, 92, 104, 116, 124, 140, 152 })
            {
                var fontSize = MainForm.ComputeFittingFontSize(text, 640, rowHeight);
                Assert.InRange(fontSize, 12F, 17F);
                Assert.True(
                    fontSize >= previous,
                    "字号应随行高单调不减（" + rowHeight + " → " + fontSize + "）");
                previous = fontSize;

                var height = MainForm.MeasureTwoLineHeight(text, fontSize, 640);
                Assert.True(
                    height <= rowHeight - 6,
                    "行高 " + rowHeight + " 放不下 " + fontSize + "pt 的两行中文（量得 " + height + "）");
            }
        }

        private static string BuildConfigJson(int count)
        {
            var config = new AppConfig { Version = 2 };
            for (var i = 1; i <= count; i++)
            {
                config.Subjects.Add(new Subject
                {
                    Id = "s" + i,
                    Name = "科目" + i,
                    Host = "ftp.example.com",
                    Port = 21,
                    User = "u" + i,
                    Password = "p" + i,
                    RemotePath = "/pub/" + i,
                    CredentialMode = "inline",
                    LastSyncAt = ""
                });
            }

            return JsonConvert.SerializeObject(config, Formatting.Indented);
        }

        private static Rectangle ToFormClient(Control control, Form form)
        {
            var rect = control.Bounds;
            var parent = control.Parent;
            while (parent != null && !ReferenceEquals(parent, form))
            {
                rect.Offset(parent.Left, parent.Top);
                parent = parent.Parent;
            }

            return rect;
        }

        private static Control? FindByText(Control root, string text)
        {
            foreach (Control child in root.Controls)
            {
                if (child.Text == text)
                {
                    return child;
                }

                var found = FindByText(child, text);
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static T GetPrivateField<T>(object instance, string fieldName)
            where T : class
        {
            var field = instance.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(field);
            var value = field!.GetValue(instance) as T;
            Assert.NotNull(value);
            return value!;
        }
    }
}
