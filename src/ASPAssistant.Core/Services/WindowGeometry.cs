// Modified for ASP Assistant Arknights on 2026-11-11; see NOTICE.
using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

public static class WindowGeometry
{
    public static bool IsSupportedGameAspectRatio(int width, int height)
        => width > 0 && height > 0 && Math.Abs(width - height * 16.0 / 9) <= 2;

    /// <summary>Fits the screenshot inside a physical-pixel render surface, preserving letterboxing.</summary>
    public static RECT FitAspectRatio(RECT bounds, int imageWidth, int imageHeight)
    {
        if (bounds.Width <= 0 || bounds.Height <= 0 || imageWidth <= 0 || imageHeight <= 0)
            throw new ArgumentOutOfRangeException(nameof(bounds));
        var scale = Math.Min((double)bounds.Width / imageWidth, (double)bounds.Height / imageHeight);
        var width = (int)Math.Round(imageWidth * scale);
        var height = (int)Math.Round(imageHeight * scale);
        var left = bounds.Left + (bounds.Width - width) / 2;
        var top = bounds.Top + (bounds.Height - height) / 2;
        return new RECT { Left = left, Top = top, Right = left + width, Bottom = top + height };
    }

    /// <summary>游戏和工作区使用物理像素；侧栏宽度、手动高度使用 WPF DIP。</summary>
    public static RECT SidePanelBounds(RECT game, RECT workArea, double dpiScale,
        double widthDip, double? manualHeightDip, bool fullscreen)
    {
        var width = Math.Min((int)Math.Round(widthDip * dpiScale), workArea.Width);
        var height = (int)Math.Round(Math.Min(manualHeightDip is { } heightDip
            ? heightDip * dpiScale : game.Height * (fullscreen ? 0.46 : 1), workArea.Height));
        var left = !fullscreen && workArea.Right - game.Right >= width
            ? game.Right : game.Right - width;
        var top = (int)Math.Round(game.Top + (fullscreen ? game.Height * 0.22 : 0));
        return ClampBounds(new RECT { Left = left, Top = top, Right = left + width, Bottom = top + height }, workArea);
    }

    public static RECT ClampBounds(RECT bounds, RECT workArea)
    {
        var width = Math.Min(bounds.Width, workArea.Width);
        var height = Math.Min(bounds.Height, workArea.Height);
        var left = Math.Clamp(bounds.Left, workArea.Left, workArea.Right - width);
        var top = Math.Clamp(bounds.Top, workArea.Top, workArea.Bottom - height);
        return new RECT { Left = left, Top = top, Right = left + width, Bottom = top + height };
    }

    public static SidePanelOffset CaptureFollowOffset(RECT game, SidePanelPlacement placement, double dpiScale)
        => new((placement.Left - game.Right) / dpiScale, (placement.Top - game.Top) / dpiScale);

    public static RECT FollowBounds(RECT game, RECT workArea, double dpiScale, double widthDip,
        double? heightDip, bool fullscreen, SidePanelOffset offset)
    {
        var size = SidePanelBounds(game, workArea, dpiScale, widthDip, heightDip, fullscreen);
        var left = (int)Math.Round(Math.Clamp(game.Right + offset.X * dpiScale, workArea.Left, workArea.Right - size.Width));
        var top = (int)Math.Round(Math.Clamp(game.Top + offset.Y * dpiScale, workArea.Top, workArea.Bottom - size.Height));
        return new RECT { Left = left, Top = top, Right = left + size.Width, Bottom = top + size.Height };
    }

    public static RECT RestoreBounds(SidePanelPlacement placement, RECT workArea, double dpiScale)
    {
        var width = (int)Math.Round(Math.Min(placement.WidthDip * dpiScale, workArea.Width));
        var height = (int)Math.Round(Math.Min(placement.HeightDip * dpiScale, workArea.Height));
        var left = (int)Math.Round(Math.Clamp(placement.Left, workArea.Left, workArea.Right - width));
        var top = (int)Math.Round(Math.Clamp(placement.Top, workArea.Top, workArea.Bottom - height));
        return new RECT { Left = left, Top = top, Right = left + width, Bottom = top + height };
    }

    public static (double X, double Y) ShopMarkerPosition(
        (double X, double Y, double Width, double Height) region, double widthDip, double heightDip, double markerSizeDip)
    {
        var x = (region.X + region.Width) * widthDip - markerSizeDip;
        var y = region.Y * heightDip - markerSizeDip - 2;
        return (Math.Clamp(x, 0, Math.Max(0, widthDip - markerSizeDip)),
            Math.Clamp(y, 0, Math.Max(0, heightDip - markerSizeDip)));
    }
}
