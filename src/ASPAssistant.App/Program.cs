using System.Runtime.InteropServices;

namespace ASPAssistant.App;

public static class Program
{
    // Use Win32 MessageBox directly — PresentationFramework is not loaded yet at this point.
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(nint hWnd, string text, string caption, uint type);

    private const uint MB_OK = 0x0;
    private const uint MB_ICONERROR = 0x10;
    private const uint MB_ICONWARNING = 0x30;

    [STAThread]
    public static void Main()
    {
        // Single instance: every process runs its own 500 ms screen scan loop, so two
        // copies both detect the ban screen, both scroll the game and both screenshot —
        // the ban analysis then fires twice and the two recognitions corrupt each other.
        using var singleInstance = new Mutex(
            initiallyOwned: true, "ASPAssistant.App.SingleInstance", out bool isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBoxW(
                nint.Zero,
                "鸿雪·卫戍助手已经在运行。\n\n同时运行两个实例会让 ban 位识别重复触发、互相抢滚动和截图，请先关闭已运行的窗口。",
                "鸿雪·卫戍助手已在运行",
                MB_OK | MB_ICONWARNING);
            return;
        }

        // .NET 8 WPF requires Windows 10 build 14393 (version 1607) or later.
        // Check BEFORE loading any WPF assembly so a failed load doesn't silently terminate the process.
        // OperatingSystem.IsWindowsVersionAtLeast reads from ntdll RtlGetVersion — reflects the real OS version.
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 14393))
        {
            var v = Environment.OSVersion.Version;
            MessageBoxW(
                nint.Zero,
                $"鸿雪·卫戍助手需要 Windows 10 1607（版本 14393）或更高版本才能运行。\n\n" +
                $"当前系统版本：{v.Major}.{v.Minor}.{v.Build}\n\n" +
                $"请将 Windows 升级至 1607 或更高版本后重试。",
                "系统版本不兼容 — 鸿雪·卫戍助手",
                MB_OK | MB_ICONERROR);
            return;
        }

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
