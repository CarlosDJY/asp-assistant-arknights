// Added for ASP Assistant Regulation on 2026-11-14; see NOTICE.
using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

public class EdgeAutoHideTests
{
    private static readonly DateTime Start = new(2026, 11, 14, 12, 0, 0, DateTimeKind.Utc);

    /// <summary>Ticks every 16 ms for <paramref name="duration"/> with the same inputs; returns the end time.</summary>
    private static DateTime Run(EdgeAutoHide hide, DateTime from, TimeSpan duration,
        bool onVisible = false, bool near = false, bool keepOpen = false)
    {
        var now = from;
        hide.Tick(now, onVisible, near, keepOpen);
        while (now - from < duration)
        {
            now += TimeSpan.FromMilliseconds(16);
            hide.Tick(now, onVisible, near, keepOpen);
        }
        return now;
    }

    private static DateTime Tucked(EdgeAutoHide hide)
    {
        var now = Run(hide, Start, EdgeAutoHide.HideDelay + EdgeAutoHide.SlideDuration + TimeSpan.FromMilliseconds(50));
        hide.IsTucked.Should().BeTrue();
        return now;
    }

    [Fact]
    public void StaysOutWhileTheCursorIsNearAndTucksAfterItLeaves()
    {
        var hide = new EdgeAutoHide();
        var now = Run(hide, Start, TimeSpan.FromSeconds(2), near: true);
        hide.Shown.Should().Be(1);

        now = Run(hide, now, EdgeAutoHide.HideDelay - TimeSpan.FromMilliseconds(50));
        hide.Shown.Should().Be(1, "the leave delay has not passed yet");
        Run(hide, now, EdgeAutoHide.SlideDuration + TimeSpan.FromMilliseconds(100));
        hide.IsTucked.Should().BeTrue();
    }

    [Fact]
    public void ABriefTouchOnTheStripDoesNotRevealIt()
    {
        var hide = new EdgeAutoHide();
        var now = Tucked(hide);
        now = Run(hide, now, EdgeAutoHide.RevealDelay - TimeSpan.FromMilliseconds(60), onVisible: true);
        now = Run(hide, now, TimeSpan.FromMilliseconds(100));
        hide.IsTucked.Should().BeTrue("passing over the edge while playing must not pop the panel out");

        // The dwell restarts: another short touch is still not enough.
        Run(hide, now, EdgeAutoHide.RevealDelay - TimeSpan.FromMilliseconds(60), onVisible: true);
        hide.IsTucked.Should().BeTrue();
    }

    [Fact]
    public void RestingOnTheStripSlidesThePanelOut()
    {
        var hide = new EdgeAutoHide();
        var now = Tucked(hide);
        now = Run(hide, now, EdgeAutoHide.RevealDelay + TimeSpan.FromMilliseconds(20), onVisible: true);
        hide.Shown.Should().BeGreaterThan(0);
        Run(hide, now, EdgeAutoHide.SlideDuration + TimeSpan.FromMilliseconds(50), onVisible: true, near: true);
        hide.Shown.Should().Be(1);
    }

    [Fact]
    public void KeepOpenHoldsThePanelOutEvenWithTheCursorAway()
    {
        var hide = new EdgeAutoHide();
        var now = Run(hide, Start, TimeSpan.FromSeconds(3), keepOpen: true);
        hide.Shown.Should().Be(1);
        Run(hide, now, EdgeAutoHide.HideDelay + EdgeAutoHide.SlideDuration + TimeSpan.FromMilliseconds(50));
        hide.IsTucked.Should().BeTrue("once nothing holds it, the normal leave delay applies");
    }

    [Fact]
    public void KeepOpenDoesNotPullATuckedPanelOut()
    {
        var hide = new EdgeAutoHide();
        var now = Tucked(hide);
        Run(hide, now, TimeSpan.FromSeconds(1), keepOpen: true);
        hide.IsTucked.Should().BeTrue();
    }

    [Fact]
    public void ReturningToARetractingPanelBringsItStraightBack()
    {
        var hide = new EdgeAutoHide();
        var now = Run(hide, Start, EdgeAutoHide.HideDelay + TimeSpan.FromMilliseconds(40));
        hide.Shown.Should().BeInRange(0.15, 0.99, "it has just started to slide in");
        Run(hide, now, TimeSpan.FromMilliseconds(32), onVisible: true, near: true);
        hide.Shown.Should().BeGreaterThan(0.15);
        Run(hide, now, EdgeAutoHide.SlideDuration, onVisible: true, near: true);
        hide.Shown.Should().Be(1, "no dwell is needed while it is still mostly out");
    }

    [Fact]
    public void ResetShowsItAtOnceAndAStallDoesNotJump()
    {
        var hide = new EdgeAutoHide();
        var now = Tucked(hide);
        hide.Reset();
        hide.Shown.Should().Be(1);

        hide.Tick(now, false, false, false);
        // A stalled dispatcher: the next tick comes 5 s later, past the leave delay.
        hide.Tick(now + TimeSpan.FromSeconds(5), false, false, false);
        hide.Shown.Should().BeGreaterThan(0, "a long gap between ticks moves at most one short step, not straight to tucked");
    }

    [Fact]
    public void EdgeIsFoundOnlyAgainstTheWorkAreaSides()
    {
        var work = new RECT { Left = 0, Top = 0, Right = 1920, Bottom = 1040 };
        RECT At(int left, int width = 310) => new() { Left = left, Top = 200, Right = left + width, Bottom = 700 };
        EdgeAutoHide.EdgeOf(At(1920 - 310), work, 2).Should().Be(DockEdge.Right);
        EdgeAutoHide.EdgeOf(At(1920 - 311), work, 2).Should().Be(DockEdge.Right);
        EdgeAutoHide.EdgeOf(At(0), work, 2).Should().Be(DockEdge.Left);
        EdgeAutoHide.EdgeOf(At(800), work, 2).Should().Be(DockEdge.None);
        EdgeAutoHide.EdgeOf(At(1920 - 330), work, 2).Should().Be(DockEdge.None);
        EdgeAutoHide.EdgeOf(At(0, 1920), work, 2).Should().Be(DockEdge.None, "a panel as wide as the screen has no edge to hide behind");

        var right = new RECT { Left = 1920, Top = 0, Right = 3840, Bottom = 1080 };
        EdgeAutoHide.EdgeOf(At(3840 - 310), right, 2).Should().Be(DockEdge.Right);
        EdgeAutoHide.EdgeOf(At(1920), right, 2).Should().Be(DockEdge.Left);
    }

    [Theory]
    [InlineData(true, false, false, null)]               // no game window: works on its own at the screen edge
    [InlineData(true, true, true, null)]                 // fullscreen game
    [InlineData(true, true, false, "game is windowed")]  // windowed game: the panel sits beside it
    [InlineData(false, false, false, "setting off")]
    [InlineData(false, true, true, "setting off")]
    public void AppliesWithAFullscreenGameOrNoGameButNotBesideAWindowedOne(bool enabled, bool found, bool fullscreen, string? reason)
        => EdgeAutoHide.OffReason(enabled, found, fullscreen).Should().Be(reason);

    [Theory]
    [InlineData(0.0, 310 - 6)]
    [InlineData(1.0, 0)]
    public void TuckLeavesThePeekStripVisible(double eased, int expectedOffset)
    {
        var offset = EdgeAutoHide.TuckOffset(310, 6, eased);
        offset.Should().Be(expectedOffset);
        EdgeAutoHide.VisibleSpan(DockEdge.Right, 310, offset).Should().Be(eased == 0 ? (304, 310) : (0, 310));
        EdgeAutoHide.VisibleSpan(DockEdge.Left, 310, offset).Should().Be(eased == 0 ? (0, 6) : (0, 310));
    }

    [Fact]
    public void EaseStartsFastAndLandsSoftly()
    {
        EdgeAutoHide.Ease(0).Should().Be(0);
        EdgeAutoHide.Ease(1).Should().Be(1);
        EdgeAutoHide.Ease(0.5).Should().BeGreaterThan(0.5);
        EdgeAutoHide.Ease(-1).Should().Be(0);
        EdgeAutoHide.Ease(2).Should().Be(1);
    }
}
