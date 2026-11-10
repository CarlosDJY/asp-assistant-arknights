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
        WindowTrackerService.IsGameWindowChanged(null, false, false, R(0, 0, 1920, 1080), false, false)
            .Should().BeTrue();
    }

    // RECT 无变化，attachInside 无变化 → 稳定心跳
    [Fact]
    public void IsGameWindowChanged_WhenRectAndAttachSame_ReturnsFalse()
    {
        var rect = R(100, 50, 1820, 1080);
        WindowTrackerService.IsGameWindowChanged(rect, true, false, rect, true, false)
            .Should().BeFalse();
    }

    // RECT 变化 → 真实移动
    [Fact]
    public void IsGameWindowChanged_WhenRectChanged_ReturnsTrue()
    {
        WindowTrackerService.IsGameWindowChanged(R(0, 0, 100, 100), false, false, R(10, 0, 110, 100), false, false)
            .Should().BeTrue();
    }

    // RECT 不变但 ShouldAttachInside 变化 → 也视为"变化"
    [Fact]
    public void IsGameWindowChanged_WhenOnlyAttachInsideChanged_ReturnsTrue()
    {
        var rect = R(0, 0, 1920, 1080);
        WindowTrackerService.IsGameWindowChanged(rect, false, false, rect, true, false)
            .Should().BeTrue();
    }

    [Fact]
    public void IsGameWindowChanged_WhenOnlyFullscreenChanged_ReturnsTrue()
    {
        var rect = R(0, 0, 1920, 1080);
        WindowTrackerService.IsGameWindowChanged(rect, true, false, rect, true, true)
            .Should().BeTrue();
    }
}
