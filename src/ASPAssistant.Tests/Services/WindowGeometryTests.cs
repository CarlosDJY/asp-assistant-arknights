// Modified for ASP Assistant Arknights on 2026-11-10; see NOTICE.
using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

public class WindowGeometryTests
{
    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(1.75)]
    [InlineData(2.0)]
    public void SidePanel_AttachesUsingItsScaledPhysicalWidth(double scale)
    {
        var game = new RECT { Left = 100, Top = 60, Right = 1600, Bottom = 960 };
        var work = new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1040 };
        var result = WindowGeometry.SidePanelBounds(game, work, scale, 310, null, false);
        result.Width.Should().Be((int)Math.Round(310 * scale));
        result.Top.Should().Be(60);
        result.Height.Should().Be(900);
        if (scale == 1)
            result.Left.Should().Be(game.Right);
        else
            result.Right.Should().Be(game.Right);
        result.Left.Should().BeGreaterThanOrEqualTo(work.Left);
        result.Right.Should().BeLessThanOrEqualTo(work.Right);
        result.Bottom.Should().BeLessThanOrEqualTo(work.Bottom);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(1.75)]
    [InlineData(2.0)]
    public void Fullscreen_PreservesPhysicalHeightRatiosAndFitsWorkArea(double scale)
    {
        var game = new RECT { Left = 1920, Top = 0, Right = 4480, Bottom = 1440 };
        var work = new RECT { Left = 1920, Top = 0, Right = 4480, Bottom = 1400 };
        var result = WindowGeometry.SidePanelBounds(game, work, scale, 310, null, true);
        result.Right.Should().Be(4480);
        result.Top.Should().Be((int)Math.Round(1440 * 0.22));
        result.Height.Should().Be((int)Math.Round(1440 * 0.46));
        result.Width.Should().Be((int)Math.Round(310 * scale));
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(1.75)]
    [InlineData(2.0)]
    public void ShopMarker_MapsToTheSameDetectedNameAcrossDpi(double scale)
    {
        var client = new RECT { Left = -2500, Top = 180, Right = -580, Bottom = 1260 };
        var marker = WindowGeometry.ShopMarkerPosition((0.4, 0.82, 0.08, 0.02), client.Width / scale, client.Height / scale, 27);
        (client.Left + (marker.X + 27) * scale).Should().BeApproximately(client.Left + 0.48 * client.Width, 0.001);
        (client.Top + (marker.Y + 29) * scale).Should().BeApproximately(client.Top + 0.82 * client.Height, 0.001);
    }

    [Fact]
    public void MixedDpiMonitors_KeepDesktopOriginsAndUseIndependentPanelScales()
    {
        var firstWork = new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1040 };
        var secondWork = new RECT { Left = 1920, Top = -200, Right = 4480, Bottom = 1200 };
        var first = WindowGeometry.SidePanelBounds(
            new RECT { Left = 100, Top = 60, Right = 1800, Bottom = 960 }, firstWork, 1.25, 310, null, false);
        var second = WindowGeometry.SidePanelBounds(
            new RECT { Left = 2020, Top = -100, Right = 4360, Bottom = 1100 }, secondWork, 1.75, 310, null, false);
        first.Right.Should().Be(1800);
        first.Width.Should().Be(388);
        second.Right.Should().Be(4360);
        second.Top.Should().Be(-100);
        second.Width.Should().Be(542);
        second.Left.Should().BeGreaterThanOrEqualTo(secondWork.Left);
    }

    [Fact]
    public void NegativeMonitorOriginAndManualHeight_AreNotScaledAsDesktopCoordinates()
    {
        var work = new RECT { Left = -2560, Top = -200, Right = 0, Bottom = 1200 };
        var game = new RECT { Left = -2400, Top = -100, Right = -100, Bottom = 1100 };
        var result = WindowGeometry.SidePanelBounds(game, work, 1.75, 310, 400, false);
        result.Right.Should().Be(-100);
        result.Top.Should().Be(-100);
        result.Height.Should().Be(700);
        result.Width.Should().Be(542);
        var moved = WindowGeometry.ClampBounds(new RECT { Left = 7000, Top = -800, Right = 7542, Bottom = -100 }, work);
        moved.Right.Should().Be(work.Right);
        moved.Top.Should().Be(work.Top);
    }

    [Fact]
    public void WorkArea_ClampsOversizedOrOffscreenPanel()
    {
        var work = new RECT { Left = 0, Top = 40, Right = 1280, Bottom = 680 };
        var game = new RECT { Left = -100, Top = -100, Right = 1400, Bottom = 1000 };
        var result = WindowGeometry.SidePanelBounds(game, work, 2, 400, 900, false);
        result.Left.Should().BeGreaterThanOrEqualTo(0);
        result.Right.Should().BeLessThanOrEqualTo(1280);
        result.Top.Should().Be(40);
        result.Bottom.Should().Be(680);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(1.25)]
    [InlineData(1.5)]
    [InlineData(1.75)]
    [InlineData(2.0)]
    public void RememberedOffset_FollowsGameTranslationAtEachDpi(double scale)
    {
        var game = new RECT { Left = 100, Top = 100, Right = 1000, Bottom = 900 };
        var placement = new ASPAssistant.Core.Models.SidePanelPlacement(300, 200, 310, 300);
        var offset = WindowGeometry.CaptureFollowOffset(game, placement, scale);
        var work = new RECT { Left = -2560, Top = -1440, Right = 4480, Bottom = 2160 };
        var result = WindowGeometry.FollowBounds(
            new RECT { Left = 130, Top = 120, Right = 1030, Bottom = 920 }, work, scale, 310, 300, false, offset);
        result.Left.Should().Be(330);
        result.Top.Should().Be(220);
        result.Width.Should().Be((int)Math.Round(310 * scale));
        result.Height.Should().Be((int)Math.Round(300 * scale));
    }

    [Fact]
    public void RememberedOffset_ChangesScreenDpiWithoutScalingDesktopOrigin()
    {
        var game = new RECT { Left = 100, Top = 100, Right = 1000, Bottom = 900 };
        var offset = WindowGeometry.CaptureFollowOffset(game,
            new ASPAssistant.Core.Models.SidePanelPlacement(1025, 150, 310, 300), 1.25);
        var moved = WindowGeometry.FollowBounds(
            new RECT { Left = 2020, Top = -100, Right = 2920, Bottom = 700 },
            new RECT { Left = 1920, Top = -200, Right = 4480, Bottom = 1200 }, 1.75, 310, 300, true, offset);
        moved.Left.Should().Be(2955);
        moved.Top.Should().Be(-30);
    }

    [Fact]
    public void RestoreFreePlacement_ClampsRemovedMonitorAndOversizedHeight()
    {
        var result = WindowGeometry.RestoreBounds(
            new ASPAssistant.Core.Models.SidePanelPlacement(-5000, 6000, 310, 1e300),
            new RECT { Left = 0, Top = 40, Right = 1920, Bottom = 1040 }, 1.75);
        result.Left.Should().Be(0);
        result.Top.Should().Be(40);
        result.Bottom.Should().Be(1040);
        result.Width.Should().Be(542);
    }
}
