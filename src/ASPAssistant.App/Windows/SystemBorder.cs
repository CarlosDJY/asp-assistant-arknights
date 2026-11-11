// Modified for ASP Assistant Arknights on 2026-11-12; see NOTICE.
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace ASPAssistant.App.Windows;

/// <summary>
/// Windows 11 draws its own thin (light) frame and rounds the corners of borderless windows. Every window here draws
/// its own border in the theme's colors, so the system frame is turned off. No effect on Windows 10.
/// </summary>
public static class SystemBorder
{
    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    private const int DwmwaWindowCornerPreference = 33, DwmwaBorderColor = 34;
    private const int DwmwcpDoNotRound = 1;
    private const int DwmwaColorNone = unchecked((int)0xFFFFFFFE);

    /// <summary>Applies to every window as soon as it has a handle.</summary>
    public static void RegisterForAllWindows() =>
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) =>
            {
                if (sender is Window { WindowStyle: WindowStyle.None } w) Hide(new WindowInteropHelper(w).Handle);
            }));

    public static void Hide(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero) return;
        try
        {
            var none = DwmwaColorNone;
            DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref none, sizeof(int));
            var corners = DwmwcpDoNotRound;
            DwmSetWindowAttribute(hwnd, DwmwaWindowCornerPreference, ref corners, sizeof(int));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }
    }
}
