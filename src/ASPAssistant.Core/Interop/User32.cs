// Modified for ASP Assistant Regulation on 2026-11-13; see NOTICE.
using System.Runtime.InteropServices;
using System.ComponentModel;
using System.Text;

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
    private delegate bool EnumWindowProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern bool EnumChildWindows(IntPtr parent, EnumWindowProc callback, IntPtr lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(IntPtr hwnd, StringBuilder text, int count);

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("user32.dll")]
    public static extern bool IsWindowVisible(IntPtr hwnd);

    [DllImport("user32.dll")]
    public static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    public static string GetWindowTitle(IntPtr hwnd)
    {
        var text = new StringBuilder(512);
        GetWindowText(hwnd, text, text.Capacity);
        return text.ToString();
    }

    public static IntPtr FindChildWindow(IntPtr parent, string title)
    {
        var found = IntPtr.Zero;
        EnumChildWindows(parent, (hwnd, _) =>
        {
            if (GetWindowTitle(hwnd) != title) return true;
            found = hwnd;
            return false;
        }, IntPtr.Zero);
        return found;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    public static extern IntPtr FindWindow(string? lpClassName, string lpWindowName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll")]
    public static extern uint GetDpiForWindow(IntPtr hWnd);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int width, int height, uint flags);

    public static RECT ReadWindowRect(IntPtr hwnd)
    {
        if (!GetWindowRect(hwnd, out var rect))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return rect;
    }

    public static double GetWindowDpiScale(IntPtr hwnd)
    {
        var dpi = GetDpiForWindow(hwnd);
        if (dpi == 0)
            throw new InvalidOperationException("无法读取窗口 DPI");
        return dpi / 96.0;
    }

    /// <summary>顶层窗口的位置和尺寸均为物理像素，不改变焦点或 Z 序。</summary>
    public static void SetWindowBounds(IntPtr hwnd, RECT bounds)
    {
        var current = ReadWindowRect(hwnd);
        if (current.Left == bounds.Left && current.Top == bounds.Top
            && current.Right == bounds.Right && current.Bottom == bounds.Bottom)
            return;
        if (!SetWindowPos(hwnd, IntPtr.Zero, bounds.Left, bounds.Top, bounds.Width, bounds.Height, 0x0004 | 0x0010))
            throw new Win32Exception(Marshal.GetLastWin32Error());
    }

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

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
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

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    /// <summary>Restore without taking focus from the game.</summary>
    public const int SW_SHOWNOACTIVATE = 4;
    /// <summary>Holding the keys down does not repeat the hotkey.</summary>
    public const uint MOD_NOREPEAT = 0x4000;
    public const uint MOD_CONTROL = 0x0002;
    public const uint MOD_SHIFT   = 0x0004;
    public const uint VK_OEM_2    = 0xBF;  // '/' key
    public const int  WM_HOTKEY   = 0x0312;
    public const int WM_ENTERSIZEMOVE = 0x0231;
    public const int WM_EXITSIZEMOVE = 0x0232;

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
        => GetMonitorGeometry(x, y).Bounds;

    public static (RECT Bounds, RECT WorkArea) GetMonitorGeometry(int x, int y)
    {
        var pt      = new POINT { X = x, Y = y };
        var hMon    = MonitorFromPoint(pt, MonitorDefaultToNearest);
        var info    = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        if (!GetMonitorInfo(hMon, ref info))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        return (info.rcMonitor, info.rcWork);
    }
}
