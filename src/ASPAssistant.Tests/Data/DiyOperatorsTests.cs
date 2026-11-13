// Added for ASP Assistant Arknights on 2026-11-13; see NOTICE.
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Data;

/// <summary>自选编队: 6★ outside the pool, core covenants by faction only, counted in 我的阵容.</summary>
public class DiyOperatorsTests
{
    private readonly JsonDataStore _store = new(Path.Combine(AppContext.BaseDirectory, "TestData", "Regulation"));

    [Fact]
    public async Task DiyOperatorsAreSixStarsOutsideThePoolWithCoreCovenantsOnly()
    {
        var pool = await _store.LoadOperatorsAsync();
        var diy = await _store.LoadDiyOperatorsAsync();

        diy.Should().NotBeEmpty();
        diy.Should().OnlyContain(o => o.IsDiy && o.Tier == 6 && o.AdditionalCovenants.Count == 0 && o.CoreCovenants.Count > 0);
        diy.Select(o => o.Name).Should().NotIntersectWith(pool.Select(o => o.Name), "a pool operator cannot go in 自选编队");
        diy.Single(o => o.Name == "煌").CoreCovenants.Should().BeEquivalentTo(["炎", "维多利亚"]);
        diy.Single(o => o.Name == "推进之王").CoreCovenants.Should().Equal("维多利亚");
        pool.Should().NotContain(o => o.IsDiy);
    }

    [Fact]
    public async Task LineupCalculatorCountsADiyOperatorsCovenant()
    {
        var pool = await _store.LoadOperatorsAsync();
        var diy = await _store.LoadDiyOperatorsAsync();
        var (covenants, jobs) = await _store.LoadCovenantsAsync();
        var calc = new CovenantCalculator([.. pool, .. diy], covenants, jobs);

        var stats = calc.Compute(new Lineup { Slots = [new() { OperatorName = "推进之王" }] });
        stats.Single(s => s.Name == "维多利亚").Count.Should().Be(1);
    }
}
