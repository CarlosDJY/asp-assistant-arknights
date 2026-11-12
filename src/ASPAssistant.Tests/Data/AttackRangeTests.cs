using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using FluentAssertions;

namespace ASPAssistant.Tests.Data;

/// <summary>攻击范围：从 operator_profiles.json 的 ranges 表解析，并能按朝向旋转。</summary>
public class AttackRangeTests
{
    private readonly JsonDataStore _store = new(Path.Combine(AppContext.BaseDirectory, "TestData", "Regulation"));

    [Fact]
    public async Task EveryProfiledOperator_HasAResolvedBaseRange()
    {
        var operators = await _store.LoadOperatorsAsync();
        var profiled = operators.Where(o => o.Profile is not null).ToList();
        profiled.Should().NotBeEmpty();
        foreach (var op in profiled)
        {
            op.Profile!.RangeFor(false, false).Should().NotBeNull(op.Name);
            op.Profile.RangeFor(true, false).Should().NotBeNull(op.Name);
            op.Profile.RangeFor(false, false)!.Cells.Should().NotBeEmpty(op.Name);
        }
    }

    [Fact]
    public async Task MeleeBlocker_CoversOwnTileAndTheOneAhead()
    {
        var operators = await _store.LoadOperatorsAsync();
        var skadi = operators.Single(o => o.Name == "斯卡蒂").Profile!;
        skadi.IsMelee.Should().BeTrue();
        skadi.RangeFor(false, false)!.Cells.Should().BeEquivalentTo(new[] { (0, 0), (1, 0) });
    }

    [Fact]
    public void Offsets_RotateWithFacing()
    {
        var range = new AttackRange("t", [(0, 0), (1, 0), (2, 1)]);
        range.Offsets(Facing.Right).Should().BeEquivalentTo(new[] { (0, 0), (1, 0), (2, 1) });
        range.Offsets(Facing.Left).Should().BeEquivalentTo(new[] { (0, 0), (-1, 0), (-2, -1) });
        range.Offsets(Facing.Up).Should().BeEquivalentTo(new[] { (0, 0), (0, -1), (1, -2) });
        range.Offsets(Facing.Down).Should().BeEquivalentTo(new[] { (0, 0), (0, 1), (-1, 2) });
    }
}
