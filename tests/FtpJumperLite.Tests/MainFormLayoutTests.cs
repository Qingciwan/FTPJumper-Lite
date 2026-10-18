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
    /// 1) 底部按钮完整落在客户区内（防止"按钮半个在窗口外"）；
    /// 2) 单列布局下 10 个科目刚好放下、不溢出，且"放得下时不得出现滚动条"。
    /// 科目区自阶段二起是自绘控件 <see cref="SubjectListControl"/>，几何断言走它自己的 API
    /// （RowHeightPx / GetRowBounds / ContentHeightPx / MaxScrollPx / ScrollOffsetPx）。
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
        /// 于是"高 DPI 布局"这条路径永远测不到。
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
        /// 科目行的几何不变式：**所有行等高、左右留白对称、行内文本放得下**。
        /// 覆盖 2 / 9 / 10 / 11 / 12 个科目（越过 MaxVisibleRows=10 的分界）以及
        /// "不设尺寸 = 程序启动时的默认尺寸"这一场景。
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

                var list = GetList(form);
                var dpi = list.DpiScale;
                var padLeft = MainForm.ScaleFor(list.Metrics.PadLeftLogical, dpi);
                var padRight = MainForm.ScaleFor(list.Metrics.PadRightLogical, dpi);
                var padTop = MainForm.ScaleFor(list.Metrics.PadTopLogical, dpi);
                var gap = MainForm.ScaleFor(list.Metrics.RowGapLogical, dpi);
                var cardPadX = MainForm.ScaleFor(list.Metrics.CardPadXLogical, dpi);
                var cardPadY = MainForm.ScaleFor(list.Metrics.CardPadYLogical, dpi);
                var safety = MainForm.ScaleFor(list.Metrics.TextSafetyLogical, dpi);
                var scrollReserve = list.MaxScrollPx > 0
                    ? MainForm.ScaleFor(list.Metrics.ScrollBarWidthLogical, dpi) + MainForm.ScaleFor(4, dpi)
                    : 0;

                var count = list.Subjects.Count;
                var info = "行数=" + count + " 行高=" + list.RowHeightPx + " 内容高=" + list.ContentHeightPx
                           + " 可滚动=" + list.MaxScrollPx + " 偏移=" + list.ScrollOffsetPx
                           + " 科目区=" + list.ClientSize;

                Assert.True(count > 0, "应有科目行：" + info);
                Assert.True(list.RowHeightPx > 0, "行高应为正：" + info);

                // 1) 所有行等高、等宽、左边界一致（行高由可用高度平分而来，不允许某一行被拉伸）
                var first = list.GetRowBounds(0);
                for (var i = 0; i < count; i++)
                {
                    var bounds = list.GetRowBounds(i);
                    Assert.Equal(first.Height, bounds.Height);
                    Assert.Equal(first.Width, bounds.Width);
                    Assert.Equal(first.X, bounds.X);
                    Assert.True(bounds.Right <= list.ClientSize.Width, "科目行右侧越界：" + bounds + " | " + info);
                }

                // 2) 左右留白对称（出现滚动条时右侧多出滚动条保留宽度）
                Assert.Equal(padLeft, first.X);
                Assert.Equal(padRight + scrollReserve, list.ClientSize.Width - first.Right);

                // 3) 不滚动时首行顶部正好是内边距；内容放得下时绝不允许出现滚动范围
                if (list.MaxScrollPx == 0)
                {
                    Assert.Equal(padTop, first.Top);
                }

                // 4) 每行的两行文本都必须放得下（字号拟合的根因不变式：行高不足会直接裁字）
                var textWidth = Math.Max(1, first.Width - (2 * cardPadX));
                var cardHeight = list.RowHeightPx - gap;
                var budget = Math.Max(1, cardHeight - (2 * cardPadY) - safety);
                var measured = MainForm.MeasureTwoLineHeight("国\r\n国", list.TitleFontSize, textWidth);
                Assert.True(
                    measured <= budget,
                    "行内容放不下（会裁字）：文本高 " + measured + " > 预算 " + budget
                    + "（字号 " + list.TitleFontSize + "）| " + info);
                Assert.InRange(list.TitleFontSize, MainForm.MinSubjectNameFontSize, MainForm.MaxSubjectNameFontSize);
            }
        }

        /// <summary>
        /// 行高必须与 DPI **等比** 缩放：字号是"点"（随 DPI 变大），行高常量是像素，
        /// 两者不同步就是 2K@200% 下那两类布局问题的根源。
        /// </summary>
        [Fact]
        public void RowHeight_ScalesWithDpi_AndHonoursFloor()
        {
            Assert.Equal(64, MainForm.ScaleFor(64, 1F));
            Assert.Equal(128, MainForm.ScaleFor(64, 2F));
            Assert.Equal(96, MainForm.ScaleFor(64, 1.5F));

            // 同一"逻辑"可用高度下，200% 的行高必须是 100% 的两倍
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

            // 11 个科目（超过一屏）时，行高按"可见 10 行"平分并滚动的下限计算，绝不因为多一行而变矮
            Assert.Equal(
                MainForm.ComputeRowHeightPx(460, 10, 1F, 0),
                MainForm.ComputeRowHeightPx(460, 11, 1F, 0));
        }

        /// <summary>
        /// 把窗口尺寸按网格扫一遍，收集所有几何违规：行高不一致 / 左右留白不对称 /
        /// 内容放得下却可滚动 / 首行被裁 / 行内文本放不下。
        /// </summary>
        [Fact]
        public void Sweep_FindGeometryViolations()
        {
            var widths = new[] { 420, 455, 500, 560, 620, 700, 800, 1000, 1200 };
            var heights = new[] { 420, 480, 520, 560, 600, 632, 680, 760, 860, 1000 };
            var problems = new List<string>();

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

                                    var list = GetList(form);
                                    var dpi = list.DpiScale;
                                    var padLeft = MainForm.ScaleFor(list.Metrics.PadLeftLogical, dpi);
                                    var padRight = MainForm.ScaleFor(list.Metrics.PadRightLogical, dpi);
                                    var padTop = MainForm.ScaleFor(list.Metrics.PadTopLogical, dpi);
                                    var padBottom = MainForm.ScaleFor(list.Metrics.PadBottomLogical, dpi);
                                    var gap = MainForm.ScaleFor(list.Metrics.RowGapLogical, dpi);
                                    var cardPadX = MainForm.ScaleFor(list.Metrics.CardPadXLogical, dpi);
                                    var cardPadY = MainForm.ScaleFor(list.Metrics.CardPadYLogical, dpi);
                                    var safety = MainForm.ScaleFor(list.Metrics.TextSafetyLogical, dpi);
                                    var scrollReserve = list.MaxScrollPx > 0
                                        ? MainForm.ScaleFor(list.Metrics.ScrollBarWidthLogical, dpi) + MainForm.ScaleFor(4, dpi)
                                        : 0;

                                    var avail = list.ClientSize.Height - padTop - padBottom;
                                    var fits = list.ContentHeightPx <= avail;
                                    var first = list.GetRowBounds(0);

                                    if (fits && list.MaxScrollPx != 0)
                                    {
                                        problems.Add(string.Format(
                                            "内容放得下却可滚动 count={0} {1}x{2}: 内容={3} 可用={4} 行高={5}",
                                            count, w, h, list.ContentHeightPx, avail, list.RowHeightPx));
                                    }

                                    if (fits && first.Top != padTop)
                                    {
                                        problems.Add(string.Format(
                                            "首行被裁 count={0} {1}x{2}: top={3} 期望={4}",
                                            count, w, h, first.Top, padTop));
                                    }

                                    if (first.X != padLeft || list.ClientSize.Width - first.Right != padRight + scrollReserve)
                                    {
                                        problems.Add(string.Format(
                                            "留白不对称 count={0} {1}x{2}: 左={3} 右={4} 期望左={5} 期望右={6}",
                                            count, w, h, first.X, list.ClientSize.Width - first.Right, padLeft, padRight + scrollReserve));
                                    }

                                    var textWidth = Math.Max(1, first.Width - (2 * cardPadX));
                                    var budget = Math.Max(1, list.RowHeightPx - gap - (2 * cardPadY) - safety);
                                    var measured = MainForm.MeasureTwoLineHeight("国\r\n国", list.TitleFontSize, textWidth);
                                    if (measured > budget)
                                    {
                                        problems.Add(string.Format(
                                            "文本放不下 count={0} {1}x{2}: {3} > {4}（字号 {5}）",
                                            count, w, h, measured, budget, list.TitleFontSize));
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

        /// <summary>科目名与远端路径都用"较长文本"，模拟真实配置（如 ftp.example.com · /pub/yuwen）。</summary>
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

                // 2) 科目区：10 行都在、行高为正、行不越界（放得下时全部落在可视区内）。
                var list = GetList(form);
                Assert.Equal(10, list.Subjects.Count);
                Assert.True(list.RowHeightPx > 0, "科目行高应为正。");

                for (var i = 0; i < list.Subjects.Count; i++)
                {
                    var bounds = list.GetRowBounds(i);
                    Assert.True(bounds.Right <= list.ClientSize.Width, "科目行右侧越界：" + bounds);
                    if (list.MaxScrollPx == 0)
                    {
                        Assert.True(bounds.Bottom <= list.ClientSize.Height, "科目行底部越界：" + bounds);
                    }
                }
            }
        }

        /// <summary>
        /// 窗口落位（纯函数）：目标机现场是 200% 下默认窗口 1428x1984 落在屏幕左上角、
        /// 下沿压住任务栏；这里把"工作区内居中 + 绝不越界"钉死。
        /// </summary>
        [Fact]
        public void ComputeCenteredLocation_CentersAndStaysInsideWorkArea()
        {
            var work = new Rectangle(0, 0, 3840, 2064);   // 4K@200% 的工作区（任务栏 96px）
            var size = new Size(1428, 1984);
            var p = MainForm.ComputeCenteredLocation(size, work);

            Assert.Equal(new Point(1206, 40), p);
            Assert.True(work.Contains(new Rectangle(p, size)), "窗口应完整落在工作区内：" + p);

            // 比工作区还大：贴左上角，绝不出现负坐标（负坐标会跑到屏幕外或压住任务栏）。
            Assert.Equal(new Point(0, 0), MainForm.ComputeCenteredLocation(new Size(4000, 3000), work));

            // 工作区不在原点（多屏）时也要夹在它里面。
            var second = new Rectangle(1920, 0, 1920, 1040);
            var q = MainForm.ComputeCenteredLocation(new Size(1428, 1000), second);
            Assert.True(
                second.Contains(new Rectangle(q, new Size(1428, 1000))),
                "多屏情况下也应完整落在该屏工作区内：" + q);
            Assert.True(q.X >= second.Left, "不应跑到左边那块屏幕上：" + q);

            // 比工作区更高（小屏/任务栏很高）：贴顶部，不能出现负 y。
            Assert.Equal(second.Top, MainForm.ComputeCenteredLocation(new Size(1428, 1984), second).Y);
        }

        /// <summary>
        /// 目标机缺陷回归：内容放得下时**不允许**出现滚动范围，且第一行必须完整可见。
        /// 现场表现是"表格比可视区高 31px → 出滚动条 → 首行被 AutoScroll 滚出去、顶部被切"。
        /// </summary>
        [Fact]
        public void RowsFit_ThenNoScrollbarAndFirstRowIsNotClipped()
        {
            var configPath = Path.Combine(_dir, "fit.json");
            File.WriteAllText(configPath, BuildConfigJson(10));

            var sawFitCase = false;
            foreach (var size in new[] { new Size(740, 1000), new Size(697, 680), new Size(660, 620) })
            {
                Exception? failure = null;
                var fit = false;
                var thread = new Thread(() =>
                {
                    MakeThreadDpiAware();
                    try
                    {
                        fit = AssertNoScrollAndFirstRowIntact(configPath, size);
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
                sawFitCase |= fit;
            }

            Assert.True(sawFitCase, "至少要有一个尺寸能放下 10 行，否则这条测试没验到“放得下就不该有滚动条”。");
        }

        /// <summary>
        /// 返回值表示该尺寸下内容是否放得下。与是否溢出无关的硬约束：首行完整、滚动位置为 0。
        /// </summary>
        private static bool AssertNoScrollAndFirstRowIntact(string configPath, Size size)
        {
            using (var form = new MainForm(configPath))
            {
                form.Size = size;
                form.CreateControl();
                form.PerformLayout();

                var list = GetList(form);
                var dpi = list.DpiScale;
                var padTop = MainForm.ScaleFor(list.Metrics.PadTopLogical, dpi);
                var padBottom = MainForm.ScaleFor(list.Metrics.PadBottomLogical, dpi);
                var avail = list.ClientSize.Height - padTop - padBottom;
                var fits = list.ContentHeightPx <= avail;
                var info = "内容高=" + list.ContentHeightPx + " 可用=" + avail
                           + " 行高=" + list.RowHeightPx + " 可滚动=" + list.MaxScrollPx;

                Assert.True(list.GetRowBounds(0).Bottom <= list.ClientSize.Height, "首行底部越界：" + info);
                Assert.Equal(0, list.ScrollOffsetPx);

                if (fits)
                {
                    Assert.Equal(0, list.MaxScrollPx);
                    Assert.Equal(padTop, list.GetRowBounds(0).Top);
                }

                return fits;
            }
        }

        /// <summary>
        /// 科目区的滚动与命中测试：科目多于可见行时能滚、能滚到底、能回顶；
        /// 键盘"当前行"会把自己滚进可视区并夹在有效范围内。
        /// </summary>
        [Fact]
        public void SubjectList_Scrolls_HitTests_AndClampsCurrentRow()
        {
            var configPath = Path.Combine(_dir, "many.json");
            File.WriteAllText(configPath, BuildLongConfigJson(15));

            Exception? failure = null;
            var thread = new Thread(() =>
            {
                MakeThreadDpiAware();
                try
                {
                    using (var form = new MainForm(configPath))
                    {
                        form.Size = new Size(660, 620);
                        form.CreateControl();
                        form.PerformLayout();

                        var list = GetList(form);
                        Assert.Equal(15, list.Subjects.Count);
                        Assert.True(list.MaxScrollPx > 0, "15 个科目在矮窗口下应当可以滚动。");
                        Assert.Equal(0, list.ScrollOffsetPx);

                        // 命中测试：第一行中心命中第 0 行；右侧内边距区域不算命中
                        var first = list.GetRowBounds(0);
                        Assert.Equal(0, list.GetRowIndexAt(new Point(first.Left + 5, first.Top + (first.Height / 2))));
                        Assert.Equal(-1, list.GetRowIndexAt(new Point(list.ClientSize.Width - 1, first.Top + 1)));

                        // 滚到底：偏移正好等于 MaxScrollPx，最后一行完整可见
                        list.ScrollBy(100000);
                        Assert.Equal(list.MaxScrollPx, list.ScrollOffsetPx);
                        Assert.True(list.GetRowBounds(14).Bottom <= list.ClientSize.Height, "最后一行应完整可见。");

                        // 回顶：偏移归零、首行回到内边距位置
                        list.ScrollToTop();
                        Assert.Equal(0, list.ScrollOffsetPx);
                        Assert.Equal(MainForm.ScaleFor(list.Metrics.PadTopLogical, list.DpiScale), list.GetRowBounds(0).Top);

                        // 当前行会自己滚进可视区，并夹在 [0, 行数-1]
                        list.CurrentRowIndex = 12;
                        Assert.Equal(12, list.CurrentRowIndex);
                        Assert.True(list.ScrollOffsetPx > 0, "把第 13 行设为当前行后应当自动滚动。");

                        list.CurrentRowIndex = 999;
                        Assert.Equal(14, list.CurrentRowIndex);
                    }
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            Assert.True(failure == null, "科目区滚动/命中测试失败：" + failure);
        }

        /// <summary>空科目列表：布局安全、无滚动、命中测试返回 -1（不抛异常）。</summary>
        [Fact]
        public void EmptySubjects_LayoutIsSafe()
        {
            var configPath = Path.Combine(_dir, "empty.json");
            File.WriteAllText(configPath, JsonConvert.SerializeObject(new AppConfig { Version = 2 }, Formatting.Indented));

            Exception? failure = null;
            var thread = new Thread(() =>
            {
                MakeThreadDpiAware();
                try
                {
                    using (var form = new MainForm(configPath))
                    {
                        form.Size = new Size(660, 620);
                        form.CreateControl();
                        form.PerformLayout();

                        var list = GetList(form);
                        Assert.Empty(list.Subjects);
                        Assert.Equal(0, list.RowHeightPx);
                        Assert.Equal(0, list.ContentHeightPx);
                        Assert.Equal(0, list.MaxScrollPx);
                        Assert.Equal(-1, list.GetRowIndexAt(new Point(10, 10)));
                        Assert.Equal(-1, list.CurrentRowIndex);

                        list.ScrollToTop();
                        list.ScrollBy(100);
                        Assert.Equal(0, list.ScrollOffsetPx);
                    }
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            Assert.True(failure == null, "空科目列表布局失败：" + failure);
        }

        [Fact]
        public void AppIcon_LoadsLargeFrameFromBuiltExe()
        {
            // 测试输出目录里就有被测 exe（由 csproj 的 ApplicationIcon 嵌入了 Report1.ico）。
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
                Assert.InRange(fontSize, MainForm.MinSubjectNameFontSize, MainForm.MaxSubjectNameFontSize);
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

        private static SubjectListControl GetList(MainForm form)
        {
            return GetPrivateField<SubjectListControl>(form, "_subjectList");
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
