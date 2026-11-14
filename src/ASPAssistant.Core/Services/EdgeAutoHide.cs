// Added for ASP Assistant Regulation on 2026-11-14; see NOTICE.
using ASPAssistant.Core.Interop;

namespace ASPAssistant.Core.Services;

/// <summary>The screen edge a panel sits against, if any.</summary>
public enum DockEdge { None, Left, Right }

/// <summary>
/// 贴边自动隐藏：侧栏贴在屏幕边上时收成一条细边；鼠标在细边上停留一会儿才滑出（避免玩游戏时蹭到），
/// 离开侧栏一段时间后再收回。只管状态和进度，窗口怎么画由调用方决定。
/// </summary>
public sealed class EdgeAutoHide
{
    /// <summary>The cursor has to rest on the strip this long before the panel slides out.</summary>
    public static readonly TimeSpan RevealDelay = TimeSpan.FromMilliseconds(250);
    /// <summary>The panel stays out this long after the cursor leaves it.</summary>
    public static readonly TimeSpan HideDelay = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan SlideDuration = TimeSpan.FromMilliseconds(160);
    /// <summary>A retracting panel still this far out comes straight back when the cursor returns to it.</summary>
    private const double CatchBackThreshold = 0.15;
    private static readonly TimeSpan MaxStep = TimeSpan.FromMilliseconds(100);

    private bool _wantShown = true;
    private DateTime? _hoverSince;
    private DateTime? _awaySince;
    private DateTime? _lastTick;

    /// <summary>1 = fully out, 0 = tucked to the strip. Moves linearly; draw it through <see cref="Ease"/>.</summary>
    public double Shown { get; private set; } = 1;

    public bool IsTucked => Shown <= 0;

    /// <summary>Whether it is heading out (true) or in (false).</summary>
    public bool WantsShown => _wantShown;

    /// <summary>Back to fully shown, e.g. when the panel leaves the edge or the game leaves fullscreen.</summary>
    public void Reset()
    {
        Shown = 1;
        _wantShown = true;
        _hoverSince = _awaySince = _lastTick = null;
    }

    /// <param name="cursorOnVisiblePart">The cursor is over what is drawn now (the strip when tucked).</param>
    /// <param name="cursorNearPanel">The cursor is over the full panel area or just around it.</param>
    /// <param name="keepOpen">Something in the panel needs it out (a dialog, a menu, typing, a drag).</param>
    /// <returns>Whether <see cref="Shown"/> changed.</returns>
    public bool Tick(DateTime now, bool cursorOnVisiblePart, bool cursorNearPanel, bool keepOpen)
    {
        var elapsed = _lastTick is { } last ? now - last : TimeSpan.Zero;
        if (elapsed < TimeSpan.Zero) elapsed = TimeSpan.Zero;
        if (elapsed > MaxStep) elapsed = MaxStep;
        _lastTick = now;

        if (_wantShown)
        {
            _hoverSince = null;
            if (cursorNearPanel || keepOpen) _awaySince = null;
            else if (now - (_awaySince ??= now) >= HideDelay)
            {
                _wantShown = false;
                _awaySince = null;
            }
        }
        else
        {
            _awaySince = null;
            if (!cursorOnVisiblePart) _hoverSince = null;
            else if (Shown > CatchBackThreshold || now - (_hoverSince ??= now) >= RevealDelay)
            {
                _wantShown = true;
                _hoverSince = null;
            }
        }

        var before = Shown;
        var step = elapsed / SlideDuration;
        Shown = _wantShown ? Math.Min(1, Shown + step) : Math.Max(0, Shown - step);
        return Shown != before;
    }

    /// <summary>Ease-out cubic: quick start, soft landing.</summary>
    public static double Ease(double t) => 1 - Math.Pow(1 - Math.Clamp(t, 0, 1), 3);

    /// <summary>
    /// Why auto-hide is off, or null when it applies: with the game fullscreen, or with no game window found.
    /// A windowed game keeps the panel beside it, so there is nothing to tuck away from.
    /// </summary>
    public static string? OffReason(bool enabled, bool gameFound, bool gameFullscreen)
        => !enabled ? "setting off"
            : gameFound && !gameFullscreen ? "game is windowed"
            : null;

    /// <summary>Which edge of the work area the window sits against (within <paramref name="tolerance"/> pixels).</summary>
    public static DockEdge EdgeOf(RECT window, RECT workArea, int tolerance)
    {
        if (window.Width <= 0 || window.Width >= workArea.Width) return DockEdge.None;
        if (Math.Abs(window.Right - workArea.Right) <= tolerance) return DockEdge.Right;
        if (Math.Abs(window.Left - workArea.Left) <= tolerance) return DockEdge.Left;
        return DockEdge.None;
    }

    /// <summary>
    /// How far (physical pixels) the panel's content is pushed toward the edge for the eased progress,
    /// leaving <paramref name="peek"/> pixels visible when fully tucked.
    /// </summary>
    public static int TuckOffset(int width, int peek, double eased)
        => (int)Math.Round(Math.Max(0, width - peek) * (1 - Math.Clamp(eased, 0, 1)));

    /// <summary>The visible part in window coordinates (left, right) for the given offset.</summary>
    public static (int Left, int Right) VisibleSpan(DockEdge edge, int width, int offset) => edge switch
    {
        DockEdge.Right => (offset, width),
        DockEdge.Left => (0, width - offset),
        _ => (0, width),
    };
}
