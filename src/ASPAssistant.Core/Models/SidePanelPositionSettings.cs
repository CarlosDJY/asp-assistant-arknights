// Modified for ASP Assistant Arknights on 2026-11-14; see NOTICE.
namespace ASPAssistant.Core.Models;

/// <summary>Left/Top 是物理桌面坐标，宽高使用 WPF DIP。</summary>
public sealed record SidePanelPlacement(double Left, double Top, double WidthDip, double HeightDip);

/// <summary>相对游戏右上角的偏移，使用侧栏所在屏幕的 WPF DIP。</summary>
public sealed record SidePanelOffset(double X, double Y);

public sealed record SidePanelPositionSettings
{
    /// <summary>
    /// On by default: the panel is docked beside the game once (no <see cref="Placement"/> yet), then stays
    /// where the player leaves it.
    /// </summary>
    public bool FreeMovement { get; init; } = true;
    public SidePanelPlacement? Placement { get; init; }
    public SidePanelOffset? FollowOffsetDip { get; init; }

    /// <summary>
    /// On by default: while the panel sits against a screen edge it tucks into a thin strip and slides out when
    /// the cursor rests on that strip. Applies with the game fullscreen or not found; a windowed game is not affected.
    /// </summary>
    public bool AutoHideAtEdge { get; init; } = true;
}
