// Modified for ASP Assistant Arknights on 2026-11-11; see NOTICE.
using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Models;
using MaaFramework.Binding;
using MaaFramework.Binding.Buffers;

namespace ASPAssistant.Core.Services;

/// <summary>Shares the selected screenshot source and desktop viewport with all consumers.</summary>
public sealed class GameConnectionService : IDisposable
{
    private readonly object _captureLock = new();
    private MaaAdbController? _controller;
    private DateTime _retryAfter;
    private IntPtr _window;
    private uint _windowProcessId;
    private bool _disposed;
    private string _status = "PC 窗口截图";
    private int _screenWidth;
    private int _screenHeight;

    public GameConnectionSettings Settings { get; }
    public bool IsAdb => Settings.Source == GameCaptureSource.Adb;
    public string Status => Volatile.Read(ref _status);

    public GameConnectionService(GameConnectionSettings? settings = null, IntPtr selectedWindow = default)
    {
        Settings = settings ?? new();
        _window = selectedWindow;
        if (_window != IntPtr.Zero)
            User32.GetWindowThreadProcessId(_window, out _windowProcessId);
        if (IsAdb) _status = "等待 ADB 连接";
    }

    public IntPtr FindGameWindow()
    {
        if (!IsAdb) return User32.FindArknightsWindow();
        if (_window != IntPtr.Zero && User32.IsWindow(_window))
        {
            User32.GetWindowThreadProcessId(_window, out var processId);
            if (processId == _windowProcessId && User32.GetWindowTitle(_window) == Settings.WindowTitle)
                return _window;
        }

        // Duplicate titles need an explicit selection in the connection dialog.
        // A closed instance must never silently attach the overlay to another instance.
        if (_window != IntPtr.Zero) return IntPtr.Zero;
        var matches = FindMuMuWindows().Where(w => w.Name == Settings.WindowTitle).ToList();
        if (matches.Count != 1) return IntPtr.Zero;
        _window = matches[0].Handle;
        User32.GetWindowThreadProcessId(_window, out _windowProcessId);
        return _window;
    }

    public static IReadOnlyList<DesktopWindowInfo> FindMuMuWindows()
    {
        using var windows = MaaToolkit.Shared.Desktop.Window.Find();
        return windows.Where(w => User32.IsWindowVisible(w.Handle)
                && IsMuMuGameWindow(w.Handle))
            .Select(w => new DesktopWindowInfo(w.Handle, w.Name, w.ClassName,
                w.ScreencapMethod, w.MouseMethod, w.KeyboardMethod)).ToList();
    }

    private static bool IsMuMuGameWindow(IntPtr window)
    {
        var display = User32.FindChildWindow(window, "nemudisplay");
        return display != IntPtr.Zero && User32.GetAncestor(display, 2) == window;
    }

    public RECT? GetViewport(IntPtr window)
    {
        if (window == IntPtr.Zero || User32.IsIconic(window)) return null;
        if (!IsAdb) return User32.GetClientRectScreen(window);
        var display = User32.FindChildWindow(window, "nemudisplay");
        var bounds = display == IntPtr.Zero ? null : User32.GetClientRectScreen(display);
        return bounds is { Width: > 0, Height: > 0 } rect
            ? WindowGeometry.FitAspectRatio(rect, 16, 9) : null;
    }

    /// <summary>Captures the selected ADB device; its dimensions also bound information-panel scrolling.</summary>
    public byte[]? CaptureAdbScreen(int maxWidth)
    {
        lock (_captureLock)
        {
            if (_disposed || !IsAdb || DateTime.UtcNow < _retryAfter) return null;
            try
            {
                if (_controller is null)
                {
                    if (!File.Exists(Settings.AdbPath))
                        throw new IOException("请选择有效的 adb.exe 路径");
                    if (string.IsNullOrWhiteSpace(Settings.AdbSerial))
                        throw new IOException("请填写 ADB 连接地址或选择发现的设备");
                    _status = "正在连接 ADB…";
                    // Keep portable screenshot transport separate from MuMu's desktop viewport.
                    _controller = new MaaAdbController(Settings.AdbPath, Settings.AdbSerial,
                        AdbScreencapMethods.Encode | AdbScreencapMethods.EncodeToFileAndPull
                            | AdbScreencapMethods.RawWithGzip | AdbScreencapMethods.RawByNetcat,
                        AdbInputMethods.AdbShell,
                        agentPath: Path.Combine(AppContext.BaseDirectory, "MaaAgentBinary"), link: LinkOption.None);
                    if (_controller.LinkStart().Wait() != MaaJobStatus.Succeeded)
                        throw new IOException("ADB 连接失败，请检查设备与连接地址");
                    _controller.SetOption_ScreenshotUseRawSize(true);
                }

                if (_controller.Screencap().Wait() != MaaJobStatus.Succeeded)
                    throw new IOException("ADB 截图失败，请检查模拟器与连接地址");
                using var image = new MaaImageBuffer();
                if (!_controller.GetCachedImage(image) || image.IsEmpty)
                    throw new IOException("ADB 未返回有效画面");
                var width = image.Width;
                var height = image.Height;
                _screenWidth = width;
                _screenHeight = height;
                if (!WindowGeometry.IsSupportedGameAspectRatio(width, height))
                {
                    _status = $"当前画面 {width}×{height}，请设置为横屏 16∶9";
                    return null;
                }
                if (width > maxWidth)
                    image.TryResize(maxWidth, (int)Math.Round(height * (double)maxWidth / width));
                if (!image.TryGetEncodedData(out byte[]? png) || png is null)
                    throw new IOException("ADB 画面编码失败");
                _status = $"ADB 已连接 · {width}×{height}";
                _retryAfter = default;
                return png;
            }
            catch (Exception ex)
            {
                _status = $"{ex.Message}（5 秒后重试）";
                AppLogger.Warn("AdbCapture", _status);
                _controller?.Dispose();
                _controller = null;
                _retryAfter = DateTime.UtcNow.AddSeconds(5);
                return null;
            }
        }
    }

    public void Dispose()
    {
        lock (_captureLock)
        {
            _disposed = true;
            _controller?.Dispose();
            _controller = null;
        }
    }

    public bool ScrollInformationPanelDown()
    {
        lock (_captureLock)
        {
            if (_disposed || _controller is null) return false;
            return _controller.Swipe((int)(_screenWidth * .35), (int)(_screenHeight * .72),
                (int)(_screenWidth * .35), (int)(_screenHeight * .64), 200).Wait() == MaaJobStatus.Succeeded;
        }
    }
}
