// Modified for ASP Assistant Arknights on 2026-11-10; see NOTICE.
namespace ASPAssistant.Core.Models;

/// <summary>Left/Top 是物理桌面坐标，宽高使用 WPF DIP。</summary>
public sealed record SidePanelPlacement(double Left, double Top, double WidthDip, double HeightDip);

/// <summary>相对游戏右上角的偏移，使用侧栏所在屏幕的 WPF DIP。</summary>
public sealed record SidePanelOffset(double X, double Y);

public sealed record SidePanelPositionSettings
{
    public bool FreeMovement { get; init; }
    public SidePanelPlacement? Placement { get; init; }
    public SidePanelOffset? FollowOffsetDip { get; init; }
}
