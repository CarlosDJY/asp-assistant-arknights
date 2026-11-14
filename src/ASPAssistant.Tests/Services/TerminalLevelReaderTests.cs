// Modified for ASP Assistant Arknights on 2026-11-11; see NOTICE.
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

/// <summary>Real bundled OCR on the anonymized shop samples (the upgrade card left of the shop).</summary>
[Collection("Native OCR")]
public class TerminalLevelReaderTests(ShopReaderFixture fixture) : IClassFixture<ShopReaderFixture>
{
    [Trait("Category", "LocalScreenshot")]
    [Theory]
    [InlineData("shop-closure-tier3-expanded-1449.png", 3)]
    [InlineData("shop-closure-controls-expanded-1449.png", 3)]
    [InlineData("shop-closure-tier3-collapsed-1449.png", null)]
    public async Task ClosureTierThreeAtWindowScale(string file, int? expected)
    {
        var reader = new TerminalLevelReader(fixture.Engine);
        (await reader.ReadAsync(File.ReadAllBytes(fixture.Sample(file)), 1449, 815, GameShopTheme.Closure))
            .Should().Be(expected);
    }

    [Theory]
    [InlineData("shop-terminal-tier1-color.png", 1)]
    [InlineData("shop-terminal-tier2-duplicates.png", 2)]
    [InlineData("shop-terminal-tier4-gray.png", 4)]
    [InlineData("shop-terminal-tier4-frozen-gray.png", 4)]
    [InlineData("shop-frontier-tier1-color.png", 1)]
    [InlineData("shop-frontier-tier2-glow.png", 2)]
    [InlineData("shop-frontier-tier2-frozen-gray.png", 2)]
    [InlineData("shop-frontier-tier4-gray-glow.png", 4)]
    [InlineData("shop-tier2-color.png", 2)]
    [InlineData("shop-tier4-gray.png", 4)]
    [InlineData("shop-tier6-frozen.png", 6)]
    [InlineData("shop-tier6-glow-a.png", 6)]
    public async Task ReadsTheUpgradeCardInEverySkin(string file, int level)
    {
        var theme = file.StartsWith("shop-terminal-", StringComparison.Ordinal) ? GameShopTheme.Terminal
            : file.StartsWith("shop-frontier-", StringComparison.Ordinal) ? GameShopTheme.Ursus : GameShopTheme.Closure;
        var reader = new TerminalLevelReader(fixture.Engine);
        (await reader.ReadAsync(File.ReadAllBytes(fixture.Sample(file)), 1920, 1080, theme)).Should().Be(level);
    }

    [Fact]
    public async Task NoUpgradeCardMeansNoLevel()
    {
        var reader = new TerminalLevelReader(fixture.Engine);
        (await reader.ReadAsync(File.ReadAllBytes(fixture.Sample("ban-sample-1920x1080.png")), 1920, 1080, GameShopTheme.Closure))
            .Should().BeNull();
    }
}

public class TerminalLevelTrackerTests
{
    [Fact]
    public void ChangesOnlyAfterTwoIdenticalReadings()
    {
        var tracker = new TerminalLevelTracker();
        tracker.Observe(4).Should().BeFalse();
        tracker.Observe(null).Should().BeFalse();
        tracker.Observe(4).Should().BeTrue();
        tracker.Level.Should().Be(4);
        // One misread frame is ignored.
        tracker.Observe(6).Should().BeFalse();
        tracker.Observe(4).Should().BeFalse();
        tracker.Observe(6).Should().BeFalse();
        tracker.Level.Should().Be(4);
        tracker.Observe(5).Should().BeFalse();
        tracker.Observe(5).Should().BeTrue();
        tracker.Reset();
        tracker.Level.Should().BeNull();
    }

    [Fact]
    public void LevelSurvivesHiddenShopAndOnlyFallsOnStrongEvidence()
    {
        var t0 = new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc);
        var tracker = new TerminalLevelTracker();
        tracker.Observe(4, t0); tracker.Observe(4, t0.AddSeconds(1));
        // Battle / collapsed shop: nothing read for a long time, level is kept.
        for (var i = 0; i < 50; i++) tracker.Observe(null, t0.AddSeconds(10 + i));
        tracker.Level.Should().Be(4);

        // A lower reading (misread or new run) needs 4 readings over at least 15 s.
        tracker.Observe(1, t0.AddSeconds(100)).Should().BeFalse();
        tracker.Observe(1, t0.AddSeconds(101)).Should().BeFalse();
        tracker.Observe(1, t0.AddSeconds(102)).Should().BeFalse();
        tracker.Observe(1, t0.AddSeconds(103)).Should().BeFalse();
        tracker.Level.Should().Be(4);
        tracker.Observe(1, t0.AddSeconds(116)).Should().BeTrue();
        tracker.Level.Should().Be(1);
    }

    [Fact]
    public void JumpsOfSeveralLevelsNeedAnExtraReading()
    {
        var tracker = new TerminalLevelTracker();
        tracker.Observe(2); tracker.Observe(2);
        tracker.Observe(5).Should().BeFalse();
        tracker.Observe(5).Should().BeFalse();
        tracker.Observe(5).Should().BeTrue();
        tracker.Level.Should().Be(5);
    }

    [Fact]
    public void RestoredLevelIsReplacedByTheFirstStableReadingInEitherDirection()
    {
        var tracker = new TerminalLevelTracker();
        tracker.Restore(5);
        tracker.Level.Should().Be(5);
        tracker.IsRestored.Should().BeTrue();
        tracker.Observe(2).Should().BeFalse();
        tracker.Observe(2).Should().BeTrue();
        tracker.Level.Should().Be(2);
        tracker.IsRestored.Should().BeFalse();

        var same = new TerminalLevelTracker();
        same.Restore(3);
        same.Observe(3).Should().BeTrue("seeing the restored level confirms it");
        same.IsRestored.Should().BeFalse();
        same.Observe(3).Should().BeFalse();
    }
}
