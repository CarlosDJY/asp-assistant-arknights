// Modified for ASP Assistant Arknights on 2026-11-12; see NOTICE.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace ASPAssistant.App.Controls;

/// <summary>
/// Detail pop-ups (operator / equipment / covenant details: ToolTips with <c>Placement="Left"</c>) open just outside
/// the side panel, level with the hovered item, instead of next to the item itself. With two-column lists the
/// item-relative position covered the other column. Falls back to the right of the panel when there is no room
/// on the left. Plain text tooltips are left alone.
/// </summary>
public static class PanelToolTip
{
    private const double Gap = 8;

    public static void RegisterForAllElements() =>
        EventManager.RegisterClassHandler(typeof(FrameworkElement), ToolTipService.ToolTipOpeningEvent,
            new ToolTipEventHandler(OnToolTipOpening));

    private static void OnToolTipOpening(object sender, ToolTipEventArgs e)
    {
        if (sender is FrameworkElement owner && owner.ToolTip is ToolTip tip) Configure(tip, owner);
    }

    public static void Configure(ToolTip tip, FrameworkElement owner)
    {
        if (tip.Placement is not (PlacementMode.Left or PlacementMode.Custom)) return;
        if (Window.GetWindow(owner) is not { } window || window.GetType().Name != "SidePanelWindow") return;
        tip.Placement = PlacementMode.Custom;
        tip.HorizontalOffset = 0;
        tip.VerticalOffset = 0;
        tip.CustomPopupPlacementCallback = (popupSize, targetSize, _) =>
        {
            var target = tip.PlacementTarget as FrameworkElement ?? owner;
            if (target.ActualWidth <= 0 || !target.IsVisible)
                return [new CustomPopupPlacement(new Point(-popupSize.Width - Gap, 0), PopupPrimaryAxis.Horizontal)];
            // Sizes arrive in device pixels; element geometry is in DIPs.
            var scale = targetSize.Width / target.ActualWidth;
            var origin = target.TranslatePoint(new Point(0, 0), window);
            var left = (-origin.X - Gap) * scale - popupSize.Width;
            var right = (window.ActualWidth - origin.X + Gap) * scale;
            return
            [
                new CustomPopupPlacement(new Point(left, 0), PopupPrimaryAxis.Horizontal),
                new CustomPopupPlacement(new Point(right, 0), PopupPrimaryAxis.Horizontal)
            ];
        };
    }
}
