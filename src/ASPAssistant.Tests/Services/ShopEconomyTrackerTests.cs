using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

public class ShopEconomyTrackerTests
{
    private static ShopEconomy Read(int? round, int? money, int? cost = null, bool maxed = false, string? mode = null)
        => new(round, money, cost, maxed, mode, true);

    [Fact]
    public void ValuesNeedTwoMatchingReads_AndMissingReadsKeepThem()
    {
        var t = new ShopEconomyTracker();
        t.Observe(Read(3, 7, 5)).Should().BeFalse();
        t.Current.Should().BeNull();
        t.Observe(Read(3, 7, 5)).Should().BeTrue();
        t.Current.Should().Be(Read(3, 7, 5));
        t.Observe(Read(null, null)).Should().BeFalse();
        t.Current!.Money.Should().Be(7);
    }

    [Fact]
    public void OneOffMisreadIsIgnored()
    {
        var t = new ShopEconomyTracker();
        t.Observe(Read(3, 7)); t.Observe(Read(3, 7));
        t.Observe(Read(3, 2)).Should().BeFalse();
        t.Observe(Read(3, 7)).Should().BeFalse();
        t.Current!.Money.Should().Be(7);
        t.Observe(Read(3, 4)); t.Observe(Read(3, 4)).Should().BeTrue();
        t.Current!.Money.Should().Be(4);
    }

    [Fact]
    public void NewRoundDropsOldFundsUntilReadAgain()
    {
        var t = new ShopEconomyTracker();
        t.Observe(Read(3, 7, 5)); t.Observe(Read(3, 7, 5));
        t.Observe(Read(4, null)); t.Observe(Read(4, null)).Should().BeTrue();
        t.Current.Should().Be(new ShopEconomy(4, null, null, false, null, true));
        t.Observe(Read(4, 9, 4)); t.Observe(Read(4, 9, 4));
        t.Current!.Money.Should().Be(9);
        t.Current.UpgradeCost.Should().Be(4);
    }

    [Fact]
    public void MaxedHidesThePrice_AndModeIsKept()
    {
        var t = new ShopEconomyTracker();
        t.Observe(Read(9, 11, 6, mode: "规程等级3")); t.Observe(Read(9, 11, 6, mode: "规程等级3"));
        t.Observe(Read(9, 11, null, true)); t.Observe(Read(9, 11, null, true)).Should().BeTrue();
        t.Current.Should().Be(new ShopEconomy(9, 11, null, true, "规程等级3", true));
        t.Reset();
        t.Current.Should().BeNull();
    }
}
