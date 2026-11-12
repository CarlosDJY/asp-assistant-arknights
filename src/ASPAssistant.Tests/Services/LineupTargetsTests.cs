// Added for ASP Assistant Arknights on 2026-11-13; see NOTICE.
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

public class LineupTargetsTests
{
    private static readonly Operator[] Ops =
    [
        Op("塔露拉", ["乌萨斯"], ["奥术"]), Op("冻河", ["乌萨斯"], ["迅捷"]), Op("怒潮凛冬", ["乌萨斯"], ["奇迹"]),
        Op("赫拉格", ["乌萨斯"], ["突袭"]), Op("维伊", ["乌萨斯"], ["奥术"]), Op("乌啾", ["乌萨斯"], ["聚合"]),
        Op("奥尔佳", ["乌萨斯"], ["灵巧"]), Op("折桠", ["乌萨斯"], ["不屈"]),
        Op("角峰", ["谢拉格"], ["坚守"]), Op("耶拉", ["谢拉格"], ["奇迹"]), Op("初雪", ["谢拉格"], ["协防干员"]),
        Op("车尔尼", ["莱塔尼亚"], ["坚守"]),
    ];

    private static Operator Op(string name, List<string> core, List<string> extra)
        => new() { Name = name, CoreCovenants = core, AdditionalCovenants = extra };

    private static CovenantCalculator Calc() => new(Ops,
    [
        new() { Name = "乌萨斯", BondType = "SEASON", ActivateCount = 3 }, new() { Name = "谢拉格", BondType = "SEASON", ActivateCount = 3 },
        new() { Name = "莱塔尼亚", BondType = "SEASON", ActivateCount = 3 },
        new() { Name = "奥术", BondType = "REGULAR", ActivateCount = 2 }, new() { Name = "坚守", BondType = "REGULAR", ActivateCount = 2 },
        new() { Name = "迅捷", BondType = "REGULAR", ActivateCount = 2 }, new() { Name = "奇迹", BondType = "REGULAR", ActivateCount = 2 },
    ], new Dictionary<string, string>());

    private static Lineup Make(string name, params string[] ops) => new()
    {
        Id = name, Name = name, Slots = [.. ops.Select(o => new LineupSlot { OperatorName = o })]
    };

    private static Lineup Ursus() => Make("乌萨斯 速攻", "塔露拉", "冻河", "怒潮凛冬", "赫拉格", "维伊", "乌啾", "奥尔佳", "折桠");

    [Fact]
    public void DeriveTakesActiveLateCovenants_CoreFirst_WithSecondTierFromPeople()
    {
        var targets = LineupTargets.Derive(Ursus(), Calc());
        targets.Select(t => (t.Name, t.Tier)).Should().Equal(("乌萨斯", 2), ("奥术", 1));
        LineupTargets.Of(new Lineup { TargetCovenants = [new() { Name = "奇迹", Tier = 1 }], Slots = Ursus().Slots }, Calc())
            .Single().Name.Should().Be("奇迹");
    }

    [Fact]
    public void BansDecideTheKind()
    {
        var calc = Calc();
        HashSet<string> none = [];
        LineupTargets.Recommend(Ursus(), calc, _ => false, none).Kind.Should().Be(RecommendationKind.Usable);

        // 冻河 out: 乌萨斯 still 7 ≥ 6, 奥术 still 2
        var fine = LineupTargets.Recommend(Ursus(), calc, n => n == "冻河", none);
        fine.Kind.Should().Be(RecommendationKind.BannedButFine);
        fine.Note.Should().Be("冻河被禁，目标盟约不受影响");

        // 塔露拉 out: 奥术 drops to 1
        var repair = LineupTargets.Recommend(Ursus(), calc, n => n is "塔露拉" or "冻河", none);
        repair.Kind.Should().Be(RecommendationKind.NeedsRepair);
        repair.StatusText.Should().Be("被禁 2 人 · 需替补");
        repair.Note.Should().Be("塔露拉、冻河被禁，奥术 Ⅰ 差 1 人");

        var broken = LineupTargets.Recommend(Ursus(), calc, _ => false, new HashSet<string> { "乌萨斯" });
        broken.Kind.Should().Be(RecommendationKind.Broken);
        broken.Note.Should().Be("本局乌萨斯不完整，原目标凑不齐");
    }

    [Fact]
    public void RankPutsUsableFirst()
    {
        var kjerag = Make("谢拉格", "角峰", "耶拉", "初雪", "车尔尼");
        var ranked = LineupTargets.Rank([Ursus(), kjerag], Calc(), n => n == "塔露拉", new HashSet<string>());
        ranked.Select(r => (r.Lineup.Name, r.Kind)).Should().Equal(("谢拉格", RecommendationKind.Usable), ("乌萨斯 速攻", RecommendationKind.NeedsRepair));
    }

    [Fact]
    public void FilterBySearchAndTargets()
    {
        var vm = new LineupViewModel(null!, Calc());
        vm.LoadLineups([Ursus(), Make("谢拉格", "角峰", "耶拉", "初雪", "车尔尼")]);
        vm.FilteredLineups.Should().HaveCount(2);
        vm.TargetCount("乌萨斯").Should().Be(1);
        vm.TargetCount("坚守").Should().Be(1);

        vm.ToggleFilterCovenant("乌萨斯");
        vm.FilteredLineups.Select(l => l.Name).Should().Equal("乌萨斯 速攻");
        vm.ToggleFilterCovenant("坚守");
        vm.FilteredLineups.Should().BeEmpty();
        vm.FilterMatchAll = false;
        vm.FilteredLineups.Should().HaveCount(2);
        vm.FilterTier = 2;
        vm.FilteredLineups.Select(l => l.Name).Should().Equal("乌萨斯 速攻");
        vm.ActiveFilterCount.Should().Be(3);
        vm.ClearFilters();
        vm.SearchText = "耶拉";
        vm.FilteredLineups.Select(l => l.Name).Should().Equal("谢拉格");

        vm.SetRunInfo(n => n == "塔露拉", new HashSet<string>());
        vm.HasRunInfo.Should().BeTrue();
        vm.Recommendations.Select(r => r.Kind).Should().Equal(RecommendationKind.Usable, RecommendationKind.NeedsRepair);
        vm.SetRunInfo(null, null);
        vm.Recommendations.Should().BeEmpty();
    }

    [Fact]
    public void EditorFillsTargetsForOldLineupsAndSavesEdits()
    {
        var calc = Calc();
        var vm = new LineupEditorViewModel(Ursus(), Ops, [], calc);
        vm.TargetCovenants.Select(t => (t.Name, t.Tier)).Should().Equal(("乌萨斯", 2), ("奥术", 1));
        vm.TargetStates.Should().OnlyContain(t => t.Reached);

        vm.AddTarget("迅捷");
        vm.TargetStates.Single(t => t.Name == "迅捷").Missing.Should().Be(1);
        vm.SetTargetTier("奥术", 2);
        vm.TargetStates.Single(t => t.Name == "奥术").Missing.Should().Be(1, "奥术 Ⅱ needs 3 people");
        vm.SetTargetTier("奥术", 1);
        vm.RemoveTarget("乌萨斯");
        vm.ToLineup().TargetCovenants.Select(t => t.Name).Should().Equal("奥术", "迅捷");

        vm.FillTargetsFromLate();
        vm.TargetCovenants.Select(t => t.Name).Should().Equal("乌萨斯", "奥术");
        vm.RemoveTarget("奥术");
        vm.Reset();
        vm.TargetCovenants.Should().HaveCount(2);
        new LineupEditorViewModel(null, Ops, [], calc).TargetCovenants.Should().BeEmpty();
    }
}
