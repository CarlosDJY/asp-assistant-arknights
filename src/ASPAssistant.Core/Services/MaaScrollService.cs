using System.Text.Json;
using MaaFramework.Binding;

namespace ASPAssistant.Core.Services;

/// <summary>Scrolls the focused game window inside its left information panel.</summary>
public sealed class MaaScrollService
{
    public bool TryScrollAtWindowCenter(IntPtr hwnd, int dy)
    {
        if (hwnd == IntPtr.Zero || !Interop.User32.IsWindow(hwnd)) return false;
        if (Interop.User32.GetForegroundWindow() != hwnd)
        {
            Interop.User32.SetForegroundWindow(hwnd);
            if (Interop.User32.GetForegroundWindow() != hwnd)
            {
                AppLogger.Warn("InformationScroll", "PC scroll cancelled: game could not acquire foreground");
                return false;
            }
        }
        var rect = Interop.User32.GetClientRectScreen(hwnd);
        if (rect is null || !Interop.User32.GetCursorPos(out var previous)) return false;
        try
        {
            var target = new DesktopWindowInfo(hwnd, "Arknights", string.Empty,
                Win32ScreencapMethod.ScreenDC, Win32InputMethod.Seize, Win32InputMethod.Seize);
            using var controller = new MaaWin32Controller(target, LinkOption.Start, CheckStatusOption.None);
            if (!controller.SetOption_ScreenshotUseRawSize(true)) return false;
            if (controller.Screencap().Wait() != MaaJobStatus.Succeeded)
            {
                AppLogger.Warn("InformationScroll", "PC scroll cancelled: controller coordinate initialization failed");
                return false;
            }
            if (Interop.User32.GetForegroundWindow() != hwnd)
            {
                AppLogger.Warn("InformationScroll", "PC scroll cancelled after controller creation: game lost foreground");
                return false;
            }
            using var resource = new MaaResource();
            if (resource.AppendBundle(Path.Combine(AppContext.BaseDirectory, "data", "maa_resource")).Wait()
                != MaaJobStatus.Succeeded) return false;
            using var tasker = new MaaTasker(controller, resource, DisposeOptions.None);
            // Controller.Scroll(dx, dy) resets the target to (0, 0); the action accepts an explicit target.
            var status = ((IMaaTasker)tasker).AppendAction("Scroll", JsonSerializer.Serialize(new
                { target = new[] { rect.Value.Width / 2, rect.Value.Height / 2 }, dx = 0, dy })).Wait();
            if (!Interop.User32.GetCursorPos(out var atWheel)) return false;
            AppLogger.Info("InformationScroll", $"PC wheel dy={dy}, status={status}, target=({rect.Value.Left + rect.Value.Width / 2},{rect.Value.Top + rect.Value.Height / 2}), cursor=({atWheel.X},{atWheel.Y})");
            // Keep the pointer over the panel until the game has processed the wheel event.
            Thread.Sleep(250);
            return status == MaaJobStatus.Succeeded;
        }
        finally { Interop.User32.SetCursorPos(previous.X, previous.Y); }
    }
}
