using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

public class InformationScreenStateTests
{
    [Fact]
    public void MissingScrollTargetsDoNotSendInput()
    {
        new MaaScrollService().TryScrollAtWindowCenter(IntPtr.Zero, -120).Should().BeFalse();
        using var connection = new GameConnectionService(new ASPAssistant.Core.Models.GameConnectionSettings
            { Source = ASPAssistant.Core.Models.GameCaptureSource.Adb });
        connection.ScrollInformationPanelDown().Should().BeFalse();
    }

    [Fact]
    public void ConfirmedBanChangesStartAnotherRunWithoutWaitingForGameplay()
    {
        var state = new InformationScreenState();
        state.TryStartNewRun(false).Should().BeTrue();
        state.MarkRecorded();
        state.TryStartNewRun(false).Should().BeFalse();
        state.TryStartNewRun(true).Should().BeTrue();
        state.MarkRecorded();
        state.TryStartNewRun(false).Should().BeFalse();
        state.TryStartNewRun(true).Should().BeTrue();
    }

    [Fact]
    public void ManualOpeningStartsOnceAndRequiresConfirmedGameplayBeforeAnotherReset()
    {
        var state = new InformationScreenState();
        state.TryStartNewRun(false).Should().BeTrue();
        state.MarkRecorded();
        state.TryStartNewRun(false).Should().BeFalse();
        state.Suspend();
        state.TryStartNewRun(false).Should().BeFalse();
        for (var i = 0; i < 10; i++) state.Observe(false, false);
        state.TryStartNewRun(false).Should().BeFalse();
        state.Observe(false, true);
        state.TryStartNewRun(false).Should().BeFalse();
        state.Observe(false, true);
        state.TryStartNewRun(false).Should().BeTrue();
    }

    [Fact]
    public void AutomaticDetectionAndManualRecordingShareTheSameRunReset()
    {
        var state = new InformationScreenState();
        state.Observe(true, false).Should().BeFalse();
        state.Observe(true, false).Should().BeTrue();
        state.TryStartNewRun(false).Should().BeTrue("detecting an automatic attempt does not yet reset progress");
        state.MarkRecorded();
        state.TryStartNewRun(false).Should().BeFalse("a manual retry of this opening must keep progress");
        state.Observe(true, false).Should().BeFalse();
    }

    [Fact]
    public void OcrMissesNeverRearmASuccessfullyEnteredOpening()
    {
        var state = new InformationScreenState();
        state.Observe(true, false).Should().BeFalse();
        state.Observe(true, false).Should().BeTrue();
        for (int i = 0; i < 100; i++) state.Observe(false, false).Should().BeFalse();
        state.Observe(true, false).Should().BeFalse();
        state.Observe(true, false).Should().BeFalse();
        state.IsConsumed.Should().BeTrue();
    }

    [Fact]
    public void MinimizingResizingOrLosingFocusSuspendsWithoutForgettingRecording()
    {
        var state = new InformationScreenState();
        state.MarkRecorded();
        state.Observe(false, true);
        state.Suspend();
        state.Observe(false, true);
        state.IsConsumed.Should().BeTrue("a frame interrupted by resizing cannot confirm gameplay");
        state.Suspend();
        state.Observe(true, false).Should().BeFalse();
        state.Observe(true, false).Should().BeFalse();
    }

    [Fact]
    public void OnlyConfirmedGameplayAllowsTheNextOpening()
    {
        var state = new InformationScreenState();
        state.MarkRecorded();
        state.Observe(false, true);
        state.Observe(false, true);
        state.IsConsumed.Should().BeFalse();
        state.Observe(false, false).Should().BeFalse("an in-game info panel has no opening title");
        state.Observe(true, false).Should().BeFalse();
        state.Observe(true, false).Should().BeTrue();
        state.Observe(true, false).Should().BeFalse();
    }

    [Fact]
    public void ManualRecordingConsumesThePendingAutomaticEntry()
    {
        var state = new InformationScreenState();
        state.Observe(true, false);
        state.MarkRecorded();
        state.Observe(true, false).Should().BeFalse();
    }
}
