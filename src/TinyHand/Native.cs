using System.Runtime.InteropServices;

namespace TinyHand;

/// <summary>The Win32 calls TinyHand needs that WinForms does not wrap.</summary>
internal static class Native
{
    public const int WH_MOUSE_LL = 14;
    public const int WM_QUIT = 0x0012;
    public const int WM_HOTKEY = 0x0312;
    public const int WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202;
    public const int WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205;
    public const int WM_MBUTTONDOWN = 0x0207, WM_MBUTTONUP = 0x0208;
    public const int WM_XBUTTONDOWN = 0x020B, WM_XBUTTONUP = 0x020C;

    public const int WS_POPUP = unchecked((int)0x80000000);
    public const int WS_EX_TOPMOST = 0x00000008;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_NOACTIVATE = 0x08000000;

    public const int SW_HIDE = 0;
    public const int SW_SHOWNOACTIVATE = 4;
    public static readonly IntPtr HWND_TOPMOST = new(-1);
    public static readonly IntPtr HWND_BROADCAST = new(0xffff);
    public const uint SWP_NOSIZE = 0x0001, SWP_NOMOVE = 0x0002, SWP_NOACTIVATE = 0x0010;

    public const byte AC_SRC_OVER = 0, AC_SRC_ALPHA = 1;
    public const uint ULW_ALPHA = 2;

    public const int VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12, VK_ESCAPE = 0x1B;
    public const int VK_LWIN = 0x5B, VK_RWIN = 0x5C;
    public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_NOREPEAT = 0x4000;

    public const uint SPI_SETCURSORS = 0x0057;
    public const uint GA_ROOT = 2;
    public const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT { public int X, Y; public POINT(int x, int y) { X = x; Y = y; } }

    [StructLayout(LayoutKind.Sequential)]
    public struct SIZE { public int Cx, Cy; public SIZE(int cx, int cy) { Cx = cx; Cy = cy; } }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSLLHOOKSTRUCT
    {
        public POINT Pt;
        public uint MouseData, Flags, Time;
        public IntPtr ExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MSG
    {
        public IntPtr Hwnd;
        public uint Message;
        public IntPtr WParam, LParam;
        public uint Time;
        public POINT Pt;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BLENDFUNCTION
    {
        public byte BlendOp, BlendFlags, SourceConstantAlpha, AlphaFormat;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFOHEADER
    {
        public uint Size;
        public int Width, Height;
        public ushort Planes, BitCount;
        public uint Compression, SizeImage;
        public int XPelsPerMeter, YPelsPerMeter;
        public uint ClrUsed, ClrImportant;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct MONITORINFO
    {
        public int Size;
        public RECT Monitor, Work;
        public uint Flags;
    }

    public delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint threadId);
    [DllImport("user32.dll")]
    public static extern bool UnhookWindowsHookEx(IntPtr hhk);
    [DllImport("user32.dll")]
    public static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    public static extern IntPtr GetModuleHandle(string? name);
    [DllImport("kernel32.dll")]
    public static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")]
    public static extern int GetMessage(out MSG msg, IntPtr hwnd, uint min, uint max);
    [DllImport("user32.dll")]
    public static extern bool PostThreadMessage(uint threadId, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")]
    public static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    public static extern short GetAsyncKeyState(int vk);
    [DllImport("user32.dll")]
    public static extern bool GetCursorPos(out POINT pt);
    [DllImport("user32.dll")]
    public static extern uint GetDoubleClickTime();

    [DllImport("user32.dll")]
    public static extern IntPtr WindowFromPoint(POINT pt);
    [DllImport("user32.dll")]
    public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);
    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);

    [DllImport("user32.dll")]
    public static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetMonitorInfo(IntPtr monitor, ref MONITORINFO info);
    [DllImport("shcore.dll")]
    public static extern int GetDpiForMonitor(IntPtr monitor, int type, out uint dpiX, out uint dpiY);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, ref POINT pptDst, ref SIZE psize,
        IntPtr hdcSrc, ref POINT pptSrc, uint crKey, ref BLENDFUNCTION pblend, uint flags);
    [DllImport("user32.dll")]
    public static extern bool ShowWindow(IntPtr hwnd, int cmd);
    [DllImport("user32.dll")]
    public static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    [DllImport("user32.dll")]
    public static extern IntPtr GetDC(IntPtr hwnd);
    [DllImport("user32.dll")]
    public static extern int ReleaseDC(IntPtr hwnd, IntPtr hdc);
    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateCompatibleDC(IntPtr hdc);
    [DllImport("gdi32.dll")]
    public static extern bool DeleteDC(IntPtr hdc);
    [DllImport("gdi32.dll")]
    public static extern IntPtr SelectObject(IntPtr hdc, IntPtr obj);
    [DllImport("gdi32.dll")]
    public static extern bool DeleteObject(IntPtr obj);
    [DllImport("gdi32.dll")]
    public static extern IntPtr CreateDIBSection(IntPtr hdc, ref BITMAPINFOHEADER bmi, uint usage,
        out IntPtr bits, IntPtr section, uint offset);

    [DllImport("user32.dll")]
    public static extern IntPtr CreateCursor(IntPtr hInst, int xHot, int yHot, int w, int h, byte[] and, byte[] xor);
    [DllImport("user32.dll")]
    public static extern bool SetSystemCursor(IntPtr cursor, uint id);
    [DllImport("user32.dll")]
    public static extern bool SystemParametersInfo(uint action, uint param, IntPtr pv, uint winIni);

    [DllImport("user32.dll")]
    public static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")]
    public static extern bool UnregisterHotKey(IntPtr hwnd, int id);

    [DllImport("winmm.dll")]
    public static extern uint timeBeginPeriod(uint ms);
    [DllImport("winmm.dll")]
    public static extern uint timeEndPeriod(uint ms);

    public static bool KeyDown(int vk) => (GetAsyncKeyState(vk) & 0x8000) != 0;

    /// <summary>The monitor's work area (the screen minus the taskbar), in physical pixels.</summary>
    public static Rectangle WorkAreaAt(int x, int y)
    {
        var info = new MONITORINFO { Size = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(MonitorFromPoint(new POINT(x, y), MONITOR_DEFAULTTONEAREST), ref info);
        var w = info.Work;
        return Rectangle.FromLTRB(w.Left, w.Top, w.Right, w.Bottom);
    }

    public static double ScaleAt(int x, int y)
    {
        try
        {
            var mon = MonitorFromPoint(new POINT(x, y), MONITOR_DEFAULTTONEAREST);
            if (GetDpiForMonitor(mon, 0, out var dpi, out _) == 0 && dpi > 0)
                return dpi / 96.0;
        }
        catch (DllNotFoundException) { }
        return 1;
    }

    /// <summary>True when the window under (x, y) is one of this process's own normal windows (a menu, the settings window).</summary>
    public static bool OwnWindowAt(int x, int y)
    {
        var hwnd = WindowFromPoint(new POINT(x, y));
        if (hwnd == IntPtr.Zero)
            return false;
        GetWindowThreadProcessId(GetAncestor(hwnd, GA_ROOT), out var pid);
        return pid == (uint)Environment.ProcessId;
    }
}
