// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
using System.Timers;
using ASPAssistant.Core.Interop;
using Timer = System.Timers.Timer;

namespace ASPAssistant.Core.Services;

public class WindowTrackerService : IDisposable
{
    private IntPtr _gameWindowHandle;
    private readonly Timer _pollTimer;
    private int _polling;

    public event Action<RECT>? GameWindowMoved;
    public event Action? GameWindowLost;
    public event Action<bool>? GameWindowFocusChanged;
    public event Action<RECT>? GameWindowPolled;
    public event Action<bool>? GameWindowMinimizedChanged;

    public RECT? CurrentGameRect { get; private set; }
    public bool IsGameFocused { get; private set; }
    public bool IsGameFound => _gameWindowHandle != IntPtr.Zero
                                && User32.IsWindow(_gameWindowHandle);
    public uint CurrentGameDpi { get; private set; }
    public bool IsFullscreen { get; private set; }
    public bool IsGameMinimized { get; private set; }

    public WindowTrackerService(int pollIntervalMs = 100)
    {
        _pollTimer = new Timer(pollIntervalMs);
        _pollTimer.Elapsed += OnPollTick;
    }

    public void Start()
    {
        _gameWindowHandle = User32.FindArknightsWindow();
        _pollTimer.Start();
    }

    public void Stop()
    {
        _pollTimer.Stop();
    }

    private void OnPollTick(object? sender, ElapsedEventArgs e)
    {
        if (Interlocked.CompareExchange(ref _polling, 1, 0) != 0)
            return;
        try
        {
            if (!IsGameFound)
            {
                _gameWindowHandle = User32.FindArknightsWindow();
                if (_gameWindowHandle == IntPtr.Zero)
                {
                    if (CurrentGameRect != null)
                    {
                        CurrentGameRect = null;
                        GameWindowLost?.Invoke();
                    }
                    return;
                }
            }

            bool minimized = User32.IsIconic(_gameWindowHandle);
            bool restored = IsGameMinimized && !minimized;
            if (minimized && !IsGameMinimized)
            {
                IsGameMinimized = true;
                GameWindowMinimizedChanged?.Invoke(true);
            }
            if (minimized)
            {
                if (IsGameFocused)
                {
                    IsGameFocused = false;
                    GameWindowFocusChanged?.Invoke(false);
                }
                return;
            }
            if (restored)
                CurrentGameRect = null;

            var foreground = User32.GetForegroundWindow();
            var wasFocused = IsGameFocused;
            IsGameFocused = foreground == _gameWindowHandle;
            if (wasFocused != IsGameFocused)
                GameWindowFocusChanged?.Invoke(IsGameFocused);

            if (!User32.GetWindowRect(_gameWindowHandle, out var windowRect))
            {
                CurrentGameRect = null;
                GameWindowLost?.Invoke();
                _gameWindowHandle = IntPtr.Zero;
                return;
            }
            IsGameMinimized = false;

            var monitorRect = User32.GetMonitorRect(windowRect.Right - 1, windowRect.Top + windowRect.Height / 2);
            var isFullscreen = CheckIsFullscreen(windowRect, monitorRect);
            var dpi = User32.GetDpiForWindow(_gameWindowHandle);
            if (dpi == 0) return; // 读取窗口矩形后，游戏窗口可能已经被关闭。

            var changed = IsGameWindowChanged(CurrentGameRect, IsFullscreen, CurrentGameDpi,
                windowRect, isFullscreen, dpi);
            CurrentGameDpi = dpi;
            IsFullscreen = isFullscreen;
            CurrentGameRect = windowRect;

            if (changed)
                GameWindowMoved?.Invoke(windowRect);
            else
                GameWindowPolled?.Invoke(windowRect);

            // Restore only after both windows have received valid coordinates.
            if (restored)
                GameWindowMinimizedChanged?.Invoke(false);
        }
        finally
        {
            Interlocked.Exchange(ref _polling, 0);
        }
    }

    private static bool CheckIsFullscreen(RECT rect, RECT monitorRect)
    {
        return rect.Left <= monitorRect.Left && rect.Width >= monitorRect.Width - 10;
    }

    public static bool IsGameWindowChanged(RECT? previous, bool previousFullscreen, uint previousDpi,
        RECT current, bool currentFullscreen, uint currentDpi)
    {
        if (previous is null) return true;
        var p = previous.Value;
        return p.Left != current.Left ||
               p.Top != current.Top ||
               p.Right != current.Right ||
               p.Bottom != current.Bottom ||
               previousDpi != currentDpi ||
               previousFullscreen != currentFullscreen;
    }

    public void Dispose()
    {
        _pollTimer.Stop();
        _pollTimer.Dispose();
    }
}
