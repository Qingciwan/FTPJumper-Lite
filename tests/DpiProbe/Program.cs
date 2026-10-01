using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace FtpJumperLite.DpiProbe
{
    /// <summary>
    /// 一次性探针：报告本机显示与 DPI 的真实情况，区分“DPI 非感知（被虚拟化）”与“PerMonitorV2 感知”两种视角。
    /// 用法：DpiProbe.exe [aware]
    /// </summary>
    internal static class Program
    {
        private const int DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        [DllImport("user32.dll")]
        private static extern uint GetDpiForSystem();

        [DllImport("user32.dll")]
        private static extern uint GetDpiForWindow(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDesktopWindow();

        [DllImport("gdi32.dll")]
        private static extern int GetDeviceCaps(IntPtr hdc, int index);

        [DllImport("user32.dll")]
        private static extern IntPtr GetDC(IntPtr hwnd);

        [DllImport("user32.dll")]
        private static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool EnumDisplaySettings(string? deviceName, int modeNum, ref DEVMODE devMode);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DEVMODE
        {
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmDeviceName;
            public short dmSpecVersion, dmDriverVersion, dmSize, dmDriverExtra;
            public int dmFields, dmPositionX, dmPositionY, dmDisplayOrientation, dmDisplayFixedOutput;
            public short dmColor, dmDuplex, dmYResolution, dmTTOption, dmCollate;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string dmFormName;
            public short dmLogPixels;
            public int dmBitsPerPel, dmPelsWidth, dmPelsHeight, dmDisplayFlags, dmDisplayFrequency;
            public int dmICMMethod, dmICMIntent, dmMediaType, dmDitherType, dmReserved1, dmReserved2, dmPanningWidth, dmPanningHeight;
        }

        [STAThread]
        private static int Main(string[] args)
        {
            Console.OutputEncoding = new System.Text.UTF8Encoding(false);

            var wantAware = args.Length > 0 && args[0] == "aware";
            var ok = true;
            if (wantAware)
            {
                ok = SetProcessDpiAwarenessContext(new IntPtr(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2));
            }

            Console.WriteLine("=== DpiProbe（" + (wantAware ? "已请求 PerMonitorV2 感知" : "进程默认为非感知/系统感知") + "，设置结果=" + ok + "）===");

            foreach (Screen screen in Screen.AllScreens)
            {
                Console.WriteLine("屏幕 " + screen.DeviceName
                                  + " Bounds=" + screen.Bounds
                                  + " WorkingArea=" + screen.WorkingArea
                                  + " Primary=" + screen.Primary);
            }

            Console.WriteLine("VirtualScreen=" + SystemInformation.VirtualScreen);
            Console.WriteLine("GetDpiForSystem=" + GetDpiForSystem());
            Console.WriteLine("GetDpiForWindow(desktop)=" + GetDpiForWindow(GetDesktopWindow()));

            var hdc = GetDC(IntPtr.Zero);
            if (hdc != IntPtr.Zero)
            {
                Console.WriteLine("GetDeviceCaps(LOGPIXELSX)=" + GetDeviceCaps(hdc, 88) + " LOGPIXELSY=" + GetDeviceCaps(hdc, 90));
                ReleaseDC(IntPtr.Zero, hdc);
            }

            var dm = new DEVMODE { dmDeviceName = "", dmFormName = "", dmSize = (short)Marshal.SizeOf(typeof(DEVMODE)) };
            if (EnumDisplaySettings(null, -1, ref dm))
            {
                Console.WriteLine("物理分辨率(EnumDisplaySettings)=" + dm.dmPelsWidth + "x" + dm.dmPelsHeight
                                  + " dmLogPixels=" + dm.dmLogPixels);
            }

            // 用 WinForms 实际创建一个窗体，看它拿到的 DeviceDpi 与默认尺寸
            var form = new Form { Width = 100, Height = 100 };
            Console.WriteLine("WinForms 窗体 DeviceDpi=" + form.DeviceDpi);
            form.Dispose();

            return 0;
        }
    }
}
