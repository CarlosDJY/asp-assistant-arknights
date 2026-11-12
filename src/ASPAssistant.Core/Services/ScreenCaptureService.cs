// Modified for ASP Assistant Regulation on 2026-11-11; see NOTICE.
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using ASPAssistant.Core.Interop;

namespace ASPAssistant.Core.Services;

public class ScreenCaptureService
{
    private GameConnectionService _connection;

    public ScreenCaptureService(GameConnectionService? connection = null)
        => _connection = connection ?? new GameConnectionService();

    public GameConnectionService Connection => Volatile.Read(ref _connection);

    public void SetConnection(GameConnectionService connection)
    {
        var previous = Interlocked.Exchange(ref _connection, connection);
        previous.Dispose();
    }

    /// <summary>
    /// Maximum capture width in pixels. Screenshots wider than this are scaled down
    /// proportionally before PNG encoding, reducing both encode time and all
    /// downstream MAA decode/processing costs. Has no effect on windows narrower than this.
    /// </summary>
    public int MaxCaptureWidth { get; init; } = 1920;

    /// <summary>
    /// Captures the Arknights game window by reading pixels directly from the screen
    /// via BitBlt (Graphics.CopyFromScreen). This works with hardware-accelerated
    /// renderers (DirectX/Vulkan) that do not honour PrintWindow/GDI capture.
    /// Requires the window to be visible and not fully occluded.
    /// Returns PNG bytes, or null if the window is not found.
    /// </summary>
    public byte[]? CaptureScreen()
    {
        var connection = Connection;
        if (connection.IsAdb) return connection.CaptureAdbScreen(MaxCaptureWidth);
        var hwnd = connection.FindGameWindow();
        if (hwnd == IntPtr.Zero)
        {
            AppLogger.Warn("ScreenCapture", "Arknights window not found");
            return null;
        }

        // Skip minimized windows: ClientToScreen returns (-32000,-32000) for them,
        // which causes CopyFromScreen to throw Win32Exception(6) ERROR_INVALID_HANDLE.
        if (User32.IsIconic(hwnd))
        {
            AppLogger.Warn("ScreenCapture", "Arknights window is minimized — skipping capture");
            return null;
        }

        // CopyFromScreen cannot see through another application's foreground window.
        var foreground = User32.GetForegroundWindow();
        if (foreground != hwnd)
        {
            AppLogger.Warn("ScreenCapture", $"Foreground mismatch: game=0x{hwnd.ToInt64():X}, foreground=0x{foreground.ToInt64():X}");
            return null;
        }

        var screenRect = User32.GetClientRectScreen(hwnd);
        if (screenRect == null)
        {
            AppLogger.Warn("ScreenCapture", "GetClientRectScreen returned null");
            return null;
        }

        var rect = screenRect.Value;
        int captureWidth  = rect.Width;
        int captureHeight = rect.Height;
        if (captureWidth <= 0 || captureHeight <= 0)
        {
            AppLogger.Warn("ScreenCapture", $"Invalid client rect size: {captureWidth}x{captureHeight}");
            return null;
        }

        try
        {
            // Capture at native resolution.
            using var raw = new Bitmap(captureWidth, captureHeight, PixelFormat.Format24bppRgb);
            using (var g = Graphics.FromImage(raw))
                g.CopyFromScreen(rect.Left, rect.Top, 0, 0,
                    new System.Drawing.Size(captureWidth, captureHeight));

            // Reject a frame captured while the user switched apps or resized the game.
            var after = User32.GetClientRectScreen(hwnd);
            if (User32.GetForegroundWindow() != hwnd || after is null
                || after.Value.Left != rect.Left || after.Value.Top != rect.Top
                || after.Value.Width != rect.Width || after.Value.Height != rect.Height)
            {
                AppLogger.Warn("ScreenCapture", "Capture invalidated because foreground or client rect changed during capture");
                return null;
            }

            // Downscale if the window is wider than MaxCaptureWidth. This reduces PNG
            // encode cost here and all MAA decode/processing costs in callers.
            Bitmap output = raw;
            bool scaled = captureWidth > MaxCaptureWidth;
            if (scaled)
            {
                float scale = (float)MaxCaptureWidth / captureWidth;
                int scaledW = MaxCaptureWidth;
                int scaledH = (int)(captureHeight * scale);
                var resized = new Bitmap(scaledW, scaledH, PixelFormat.Format24bppRgb);
                using var gr = Graphics.FromImage(resized);
                gr.InterpolationMode = InterpolationMode.Bilinear;
                gr.DrawImage(raw, 0, 0, scaledW, scaledH);
                output = resized;
            }

            try
            {
                // BMP, not PNG: see ImageBytes (encode + every OCR decode become near memory copies).
                var png = ImageBytes.Encode(output);
                AppLogger.Info("ScreenCapture", $"Captured {captureWidth}x{captureHeight} ({png.Length} bytes)");
                return png;
            }
            finally
            {
                if (scaled) output.Dispose();
            }
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            // Window state changed between the geometry query and the capture
            // (e.g. minimized mid-capture). Treat as no screenshot available.
            AppLogger.Warn("ScreenCapture", $"Win32Exception during capture (code={ex.NativeErrorCode}): {ex.Message}");
            return null;
        }
    }
}
