// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

public class WindowTrackerServiceTests
{
    private static RECT R(int l, int t, int r, int b) =>
        new() { Left = l, Top = t, Right = r, Bottom = b };

    // 首帧：previous 为 null，视为"变化"
    [Fact]
    public void IsGameWindowChanged_WhenPreviousIsNull_ReturnsTrue()
    {
        WindowTrackerService.IsGameWindowChanged(null, false, 96, R(0, 0, 1920, 1080), false, 96)
            .Should().BeTrue();
    }

    // RECT、DPI 和全屏状态不变 → 稳定心跳
    [Fact]
    public void IsGameWindowChanged_WhenRectAndAttachSame_ReturnsFalse()
    {
        var rect = R(100, 50, 1820, 1080);
        WindowTrackerService.IsGameWindowChanged(rect, false, 120, rect, false, 120)
            .Should().BeFalse();
    }

    // RECT 变化 → 真实移动
    [Fact]
    public void IsGameWindowChanged_WhenRectChanged_ReturnsTrue()
    {
        WindowTrackerService.IsGameWindowChanged(R(0, 0, 100, 100), false, 96, R(10, 0, 110, 100), false, 96)
            .Should().BeTrue();
    }

    // 用户调整缩放但游戏物理矩形不变时，也必须重新定位。
    [Fact]
    public void IsGameWindowChanged_WhenOnlyDpiChanged_ReturnsTrue()
    {
        var rect = R(0, 0, 1920, 1080);
        WindowTrackerService.IsGameWindowChanged(rect, false, 120, rect, false, 168)
            .Should().BeTrue();
    }

    [Fact]
    public void IsGameWindowChanged_WhenOnlyFullscreenChanged_ReturnsTrue()
    {
        var rect = R(0, 0, 1920, 1080);
        WindowTrackerService.IsGameWindowChanged(rect, false, 96, rect, true, 96)
            .Should().BeTrue();
    }
}
