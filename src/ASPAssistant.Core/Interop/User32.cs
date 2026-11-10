// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
using System.Runtime.InteropServices;

namespace ASPAssistant.Core.Interop;

[StructLayout(LayoutKind.Sequential)]
public struct RECT
{
    public int Left;
    public int Top;
    public int Right;
    public int Bottom;

    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

public static class User32
{
    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool IsIconic(IntPtr hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool PrintWindow(IntPtr hwnd, IntPtr hdcBlt, uint nFlags);

    public const uint PW_RENDERFULLCONTENT = 2;

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    public static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ClientToScreen(IntPtr hWnd, ref POINT lpPoint);

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    public const int GWL_EXSTYLE = -20;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_TOOLWINDOW = 0x00000080;
    public const int WS_EX_NOACTIVATE = 0x08000000;

    [DllImport("user32.dll")]
    public static extern IntPtr SendMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetCursorPos(out POINT lpPoint);

    public const uint WM_MOUSEWHEEL = 0x020A;

    public static IntPtr FindArknightsWindow()
    {
        return FindWindow(null, "明日方舟");
    }

    public static RECT? GetClientRectScreen(IntPtr hWnd)
    {
        if (!GetClientRect(hWnd, out var clientRect))
            return null;

        var topLeft = new POINT { X = 0, Y = 0 };
        if (!ClientToScreen(hWnd, ref topLeft))
            return null;

        return new RECT
        {
            Left = topLeft.X,
            Top = topLeft.Y,
            Right = topLeft.X + clientRect.Width,
            Bottom = topLeft.Y + clientRect.Height
        };
    }

    // Monitor APIs

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromPoint(POINT pt, uint dwFlags);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MONITORINFO
    {
        public int    cbSize;
        public RECT   rcMonitor;
        public RECT   rcWork;
        public uint   dwFlags;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetMonitorInfo(IntPtr hMonitor, ref MONITORINFO lpmi);

    private const uint MonitorDefaultToNearest = 2;

    // Global hotkey APIs

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT   = 0x0004;
    public const uint VK_OEM_2    = 0xBF;  // '/' key
    public const int  WM_HOTKEY   = 0x0312;

    /// <summary>
    /// Sends WM_MOUSEWHEEL to the game window to scroll it vertically.
    /// Positive <paramref name="clicks"/> scrolls up; negative scrolls down.
    /// One click equals one WHEEL_DELTA (120 units).
    /// The message is targeted at the window's center so the game receives it
    /// regardless of where the real mouse cursor is.
    /// </summary>
    public static void ScrollWindow(IntPtr hwnd, int clicks)
    {
        var clientRect = GetClientRectScreen(hwnd);
        if (clientRect == null) return;

        int cx = clientRect.Value.Left + clientRect.Value.Width  / 2;
        int cy = clientRect.Value.Top  + clientRect.Value.Height / 2;

        // wParam high-word = delta; lParam = screen coords of cursor position.
        int delta = clicks * 120; // WHEEL_DELTA = 120
        IntPtr wParam = (IntPtr)((uint)(delta << 16));
        IntPtr lParam = (IntPtr)((cy << 16) | (cx & 0xFFFF));
        SendMessage(hwnd, WM_MOUSEWHEEL, wParam, lParam);
    }

    /// <summary>
    /// Returns the bounding rectangle (in physical pixels) of the monitor
    /// that contains the given physical-pixel point, or the nearest monitor
    /// if the point lies outside all monitors.
    /// </summary>
    public static RECT GetMonitorRect(int x, int y)
    {
        var pt      = new POINT { X = x, Y = y };
        var hMon    = MonitorFromPoint(pt, MonitorDefaultToNearest);
        var info    = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        GetMonitorInfo(hMon, ref info);
        return info.rcMonitor;
    }
}
