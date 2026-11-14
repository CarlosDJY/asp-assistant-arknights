// Added for ASP Assistant Regulation on 2026-11-14; see NOTICE.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Shell;
using System.Windows.Threading;
using ASPAssistant.Core;
using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Services;

namespace ASPAssistant.App.Windows;

/// <summary>
/// 贴边自动隐藏（游戏全屏或未找到游戏时）。窗口本身不动（位置、记忆位置、截图逻辑都不受影响）：用窗口区域只露出靠边的一条，
/// 内容整体平移，看起来就是滑出屏幕。被裁掉的部分不接收鼠标，点击直接落到游戏上。
/// </summary>
public partial class SidePanelWindow
{
    /// <summary>Width of the strip left showing when tucked; wide enough to see and to rest the cursor on.</summary>
    private const double EdgePeekDip = 12;
    private const double EdgeNearMarginDip = 16;
    // Frame rate while sliding; a slower poll while still is plenty for the 250 / 500 ms delays and keeps idle CPU low.
    private static readonly TimeSpan EdgeSlideInterval = TimeSpan.FromMilliseconds(16);
    private static readonly TimeSpan EdgeIdleInterval = TimeSpan.FromMilliseconds(50);
    private readonly EdgeAutoHide _edgeHide = new();
    private DispatcherTimer? _edgeTimer;
    private DockEdge _edge = DockEdge.None;
    private int _appliedOffset;
    private int _appliedWidth;
    private bool _regionApplied;
    private bool _settingRegion;
    private bool _regionDirty;
    private Thickness? _resizeBorder;
    private Border? _edgeHandle;
    private string? _lastEdgeLog;
    private string? _lastHoldLog;
    private string? _lastTraceLog;
    private bool _loggedTucked;

    private void InitEdgeAutoHide()
    {
        EdgeAutoHideToggle.IsChecked = _positionSettings.AutoHideAtEdge;
        EdgeAutoHideToggle.Checked += OnEdgeAutoHideChanged;
        EdgeAutoHideToggle.Unchecked += OnEdgeAutoHideChanged;
        CreateEdgeHandle();
        _edgeTimer = new DispatcherTimer(DispatcherPriority.Render, Dispatcher) { Interval = EdgeIdleInterval };
        _edgeTimer.Tick += (_, _) => TickEdgeAutoHide();
        _edgeTimer.Start();
        Closed += (_, _) => _edgeTimer.Stop();
    }

    private async void OnEdgeAutoHideChanged(object sender, RoutedEventArgs e)
    {
        var enabled = EdgeAutoHideToggle.IsChecked == true;
        if (enabled == _positionSettings.AutoHideAtEdge) return;
        _positionSettings = _positionSettings with { AutoHideAtEdge = enabled };
        if (!enabled) RevealEdgePanel();
        await SavePreferenceAsync(() => _settingsManager.SaveSidePanelPositionAsync(_positionSettings), EdgeAutoHideToggle);
    }

    private void TickEdgeAutoHide()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        // Hidden for a screenshot or minimised: keep the current state, it is put back as it was.
        if (hwnd == IntPtr.Zero || !IsVisible || WindowState != WindowState.Normal) return;
        var edge = CurrentDockEdge(hwnd, out var why);
        LogEdge(edge == DockEdge.None ? $"off: {why}" : $"docked {edge}: {why}");
        if (edge == DockEdge.None)
        {
            if (_edge != DockEdge.None || _regionApplied) RevealEdgePanel();
            return;
        }
        if (edge != _edge)
        {
            RevealEdgePanel();
            _edge = edge;
        }

        var window = User32.ReadWindowRect(hwnd);
        var scale = User32.GetWindowDpiScale(hwnd);
        User32.GetCursorPos(out var cursor);
        var (visibleLeft, visibleRight) = EdgeAutoHide.VisibleSpan(edge, window.Width, _appliedOffset);
        bool onVisible = cursor.Y >= window.Top && cursor.Y < window.Bottom
            && cursor.X >= window.Left + visibleLeft && cursor.X < window.Left + visibleRight;
        var margin = (int)Math.Round(EdgeNearMarginDip * scale);
        bool near = cursor.Y >= window.Top - margin && cursor.Y < window.Bottom + margin
            && cursor.X >= window.Left - margin && cursor.X < window.Right + margin;

        var keep = StayOpenReason();
        var changed = _edgeHide.Tick(DateTime.UtcNow, onVisible, near, keep is not null);
        var trace = $"near={near} strip={onVisible} hold={keep ?? "-"} heading={(_edgeHide.WantsShown ? "out" : "in")}";
        if (trace != _lastTraceLog)
        {
            _lastTraceLog = trace;
            AppLogger.InfoAlways("EdgeHide", $"{trace} cursor={cursor.X},{cursor.Y} window={window.Left},{window.Top},{window.Right},{window.Bottom}");
        }
        if (changed || _appliedOffset != 0 && window.Width != _appliedWidth)
            ApplyEdgeOffset(hwnd, window, scale);
        else if (_regionDirty && _appliedOffset != 0)
            SetEdgeRegion(hwnd, window);
        SetEdgeTimerInterval(sliding: _edgeHide.WantsShown ? _edgeHide.Shown < 1 : _edgeHide.Shown > 0);
        if (_edgeHide.IsTucked != _loggedTucked)
        {
            _loggedTucked = _edgeHide.IsTucked;
            AppLogger.InfoAlways("EdgeHide", _loggedTucked ? "tucked" : $"revealed (cursor {cursor.X},{cursor.Y})");
        }
        var hold = !near && _edgeHide.Shown >= 1 ? StayOpenReason() : null;
        if (hold != _lastHoldLog)
        {
            _lastHoldLog = hold;
            if (hold is not null) AppLogger.InfoAlways("EdgeHide", $"held open: {hold}");
        }
    }

    /// <summary>The edge the panel is docked to while auto-hide applies, else <see cref="DockEdge.None"/>.</summary>
    private DockEdge CurrentDockEdge(IntPtr hwnd, out string why)
    {
        // Fullscreen game, or no game found at all (trying it out, or a client not detected).
        // A windowed game leaves the panel as it was: it sits beside the game, not over it.
        if (EdgeAutoHide.OffReason(_positionSettings.AutoHideAtEdge, _lastGameRect is not null, _wasFullscreen) is { } off)
        {
            why = off;
            return DockEdge.None;
        }
        var window = User32.ReadWindowRect(hwnd);
        var work = User32.GetMonitorGeometry(window.Left + window.Width / 2, window.Top + window.Height / 2).WorkArea;
        var tolerance = (int)Math.Ceiling(2 * User32.GetWindowDpiScale(hwnd));
        var edge = EdgeAutoHide.EdgeOf(window, work, tolerance);
        why = $"{(_lastGameRect is null ? "no game window" : "game fullscreen")}, panel L={window.Left} R={window.Right}, work L={work.Left} R={work.Right}";
        if (edge == DockEdge.None) why = "not against a screen edge (" + why + ")";
        return edge;
    }

    /// <summary>Logs eligibility once per change, so a panel that never tucks can be explained from the log.</summary>
    private void LogEdge(string state)
    {
        if (state == _lastEdgeLog) return;
        _lastEdgeLog = state;
        AppLogger.InfoAlways("EdgeHide", state);
    }

    /// <summary>Anything the player is in the middle of keeps the panel out.</summary>
    private bool EdgePanelMustStayOpen() => StayOpenReason() is not null;

    private string? StayOpenReason() =>
        _isUserMovingOrSizing ? "moving or resizing"
        : OwnedWindows.Cast<Window>().Any(w => w.IsVisible) ? "a dialog is open"            // editor, pickers, confirms
        : Mouse.Captured is not null ? $"mouse captured by {Mouse.Captured.GetType().Name}"  // open menu, dropdown, scrollbar drag
        : IsActive && Keyboard.FocusedElement is TextBoxBase ? "typing in a text box"
        : BanProcessingMask.Visibility == Visibility.Visible ? "Ban recognition running"
        : null;
    // The update / fullscreen popovers open by themselves; they only inform, so they tuck away with the panel.

    private void ApplyEdgeOffset(IntPtr hwnd, RECT window, double scale)
    {
        var peek = (int)Math.Round(EdgePeekDip * scale);
        var offset = EdgeAutoHide.TuckOffset(window.Width, peek, EdgeAutoHide.Ease(_edgeHide.Shown));
        _appliedOffset = offset;
        _appliedWidth = window.Width;
        var content = (UIElement)Content;
        SetResizeBorder(tucking: offset != 0);
        UpdateEdgeHandle(1 - EdgeAutoHide.Ease(_edgeHide.Shown));
        if (offset == 0)
        {
            content.RenderTransform = Transform.Identity;
            ClearEdgeRegion(hwnd);
            return;
        }
        var shift = (_edge == DockEdge.Right ? offset : -offset) / scale;
        if (content.RenderTransform is TranslateTransform translate) translate.X = shift;
        else content.RenderTransform = new TranslateTransform(shift, 0);
        SetEdgeRegion(hwnd, window);
    }

    private void SetEdgeRegion(IntPtr hwnd, RECT window)
    {
        var (left, right) = EdgeAutoHide.VisibleSpan(_edge, window.Width, _appliedOffset);
        var region = User32.CreateRectRgn(left, 0, right, window.Height);
        _settingRegion = true;
        try
        {
            if (User32.SetWindowRgn(hwnd, region, true) == 0) User32.DeleteObject(region);
            else _regionApplied = true;
        }
        finally { _settingRegion = false; }
        _regionDirty = false;
    }

    private void ClearEdgeRegion(IntPtr hwnd)
    {
        if (!_regionApplied) return;
        _settingRegion = true;
        try { User32.SetWindowRgn(hwnd, IntPtr.Zero, true); }
        finally { _settingRegion = false; }
        _regionApplied = false;
        _regionDirty = false;
    }

    private void SetEdgeTimerInterval(bool sliding)
    {
        var wanted = sliding ? EdgeSlideInterval : EdgeIdleInterval;
        if (_edgeTimer is not null && _edgeTimer.Interval != wanted) _edgeTimer.Interval = wanted;
    }

    /// <summary>Fully out, no region: leaving the edge, leaving fullscreen, or turning the setting off.</summary>
    private void RevealEdgePanel()
    {
        _edgeHide.Reset();
        SetEdgeTimerInterval(sliding: false);
        _edge = DockEdge.None;
        _appliedOffset = 0;
        if (Content is UIElement content) content.RenderTransform = Transform.Identity;
        SetResizeBorder(tucking: false);
        UpdateEdgeHandle(0);
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd != IntPtr.Zero) ClearEdgeRegion(hwnd);
    }

    /// <summary>
    /// The strip left showing is the window's own edge, inside WindowChrome's resize border: without this the
    /// cursor turns into a resize arrow there and a click starts resizing instead of revealing.
    /// </summary>
    private void SetResizeBorder(bool tucking)
    {
        if (WindowChrome.GetWindowChrome(this) is not { IsFrozen: false } chrome) return;
        _resizeBorder ??= chrome.ResizeBorderThickness;
        var wanted = tucking ? new Thickness(0) : _resizeBorder.Value;
        if (chrome.ResizeBorderThickness != wanted) chrome.ResizeBorderThickness = wanted;
    }

    /// <summary>
    /// A move, resize or show/hide may make WindowChrome (GlassFrameThickness 0) put back its own full-window
    /// region. Only mark it here; the next tick re-applies ours. SetWindowRgn itself sends WM_WINDOWPOSCHANGED,
    /// so the region calls made here are ignored: re-applying from this message directly loops forever and
    /// starves the animation timer (the panel froze half-way and kept a core busy).
    /// </summary>
    private void OnEdgeWindowPosChanged()
    {
        if (_settingRegion || !_regionApplied) return;
        _regionDirty = true;
    }

    /// <summary>
    /// A themed grip drawn at the panel's outer side edge (the part left showing when tucked), so the strip
    /// reads as a handle over the game instead of a sliver of background. Fades in as the panel tucks.
    /// </summary>
    private void CreateEdgeHandle()
    {
        if (Content is not Border root || root.Child is not UIElement body) return;
        var grip = new Border
        {
            Width = 3, Height = 48, CornerRadius = new CornerRadius(1.5),
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
        };
        grip.SetResourceReference(Border.BackgroundProperty, "PanelBackgroundBrush");
        _edgeHandle = new Border
        {
            Width = EdgePeekDip, Opacity = 0, IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left, Child = grip,
        };
        _edgeHandle.SetResourceReference(Border.BackgroundProperty, "AccentSolidBrush");
        root.Child = null;
        var layers = new Grid();
        layers.Children.Add(body);
        layers.Children.Add(_edgeHandle);
        root.Child = layers;
    }

    private void UpdateEdgeHandle(double tucked)
    {
        if (_edgeHandle is null) return;
        _edgeHandle.HorizontalAlignment = _edge == DockEdge.Left ? HorizontalAlignment.Right : HorizontalAlignment.Left;
        _edgeHandle.Opacity = Math.Clamp(tucked, 0, 1);
    }
}
