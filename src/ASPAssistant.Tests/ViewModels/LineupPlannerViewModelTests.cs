using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.ViewModels;

public class LineupPlannerViewModelTests
{
    [Fact]
    public async Task LimitedSearchDoesNotClaimUnsolvedVariantsAreImpossible()
    {
        var operators = Enumerable.Range(0, 49).Select(i => new Operator
        {
            Name = $"干员{i}", CoreCovenants = ["炎"], AdditionalCovenants = ["独行"]
        }).ToList();
        CovenantInfo[] covenants =
        [
            new() { Name = "炎", BondType = "SEASON", ActivateCount = 3 },
            new() { Name = "独行", BondType = "REGULAR", ActivateCount = 1 }
        ];
        var vm = new LineupPlannerViewModel(new LineupPlanner(operators, covenants), operators, new BanViewModel(), EquipmentTestData.Rules)
        { DebounceMilliseconds = 0 };
        Item(vm, "炎").ToggleLevel1Command.Execute(null);
        Item(vm, "独行").ToggleLevel1Command.Execute(null);
        vm.IsActive = true;
        await vm.PendingSearch!;

        vm.TotalResults.Should().Be(0);
        vm.VariantCells.Should().Contain(c => c.IsLimited);
        vm.StatusDetail.Should().Contain("搜索上限");
        vm.VariantCells.Should().Contain(c => !c.HasResults && c.SubText == "受限");
        vm.VariantCells.Should().OnlyContain(c => !c.ToolTip.Contains("无解"));
    }

    private static Operator Op(string name, params string[] core) => new() { Name = name, CoreCovenants = [.. core], IconPath = $"icons/operators/{name}.png" };

    private static (LineupPlannerViewModel Vm, BanViewModel Bans) Create()
    {
        var operators = new List<Operator>();
        for (var i = 1; i <= 6; i++) operators.Add(Op($"炎{i}", "炎"));
        for (var i = 1; i <= 6; i++) operators.Add(Op($"维{i}", "维多利亚"));
        operators.Add(new Operator { Name = "炎突", CoreCovenants = ["炎"], AdditionalCovenants = ["突袭"], IconPath = "icons/operators/炎突.png" });
        operators.Add(new Operator { Name = "维突", CoreCovenants = ["维多利亚"], AdditionalCovenants = ["突袭"], IconPath = "icons/operators/维突.png" });
        var covenants = new[]
        {
            new CovenantInfo { Name = "炎", BondType = "SEASON", ActivateCount = 3 },
            new CovenantInfo { Name = "维多利亚", BondType = "SEASON", ActivateCount = 3 },
            new CovenantInfo { Name = "独行", BondType = "REGULAR", ActivateCount = 1 },
            new CovenantInfo { Name = "突袭", BondType = "REGULAR", ActivateCount = 2 },
        };
        var bans = new BanViewModel();
        var vm = new LineupPlannerViewModel(new LineupPlanner(operators, covenants), operators, bans, EquipmentTestData.Rules) { DebounceMilliseconds = 0 };
        return (vm, bans);
    }

    private static PlannerCovenantItem Item(LineupPlannerViewModel vm, string name) => vm.AllCovenants.Single(c => c.Name == name);

    [Fact]
    public async Task ToggleLevel_SearchesAndSelectsFirstVariantWithResults()
    {
        var (vm, _) = Create();
        vm.IsActive = true;

        Item(vm, "炎").ToggleLevel1Command.Execute(null);
        await vm.PendingSearch!;

        vm.SelectedGoalCount.Should().Be(1);
        vm.TotalResults.Should().BeGreaterThan(0);
        vm.SelectedVariant!.HasResults.Should().BeTrue();
        vm.VisibleLineups.Should().NotBeEmpty();
        vm.VisibleLineups[0].Operators.Where(o => !o.IsFree).Should().OnlyContain(o => o.IconPath.Length > 0);
    }

    [Fact]
    public void ClickingActiveLevelAgainClearsIt()
    {
        var (vm, _) = Create();
        var fire = Item(vm, "炎");

        fire.ToggleLevel2Command.Execute(null);
        fire.Level.Should().Be(2);
        fire.ToggleLevel2Command.Execute(null);
        fire.Level.Should().Be(0);
    }

    [Fact]
    public void CovenantWithoutSecondLevelIgnoresLevelTwo()
    {
        var (vm, _) = Create();
        var solo = Item(vm, "独行");

        solo.HasSecond.Should().BeFalse();
        solo.ToggleLevel2Command.Execute(null);
        solo.Level.Should().Be(0);
    }

    [Fact]
    public void ChangesWhileHiddenAreDeferredUntilActive()
    {
        var (vm, _) = Create();

        Item(vm, "炎").ToggleLevel1Command.Execute(null);
        vm.PendingSearch.Should().BeNull();

        vm.IsActive = true;
        vm.PendingSearch.Should().NotBeNull();
    }

    [Fact]
    public async Task BanningASelectedGoalDropsIt()
    {
        var (vm, bans) = Create();
        vm.IsActive = true;
        Item(vm, "炎").ToggleLevel1Command.Execute(null);
        await vm.PendingSearch!;

        bans.SetCovenantBans(["炎"]);
        await vm.PendingSearch!;

        Item(vm, "炎").IsBanned.Should().BeTrue();
        Item(vm, "炎").Level.Should().Be(0);
        vm.SelectedGoalCount.Should().Be(0);
        vm.TotalResults.Should().Be(0);
    }

    [Fact]
    public async Task NewerSearchReplacesOlderOne()
    {
        var (vm, _) = Create();
        vm.DebounceMilliseconds = 50;
        vm.IsActive = true;

        Item(vm, "炎").ToggleLevel1Command.Execute(null);
        var first = vm.PendingSearch!;
        Item(vm, "维多利亚").ToggleLevel1Command.Execute(null);
        await first;
        await vm.PendingSearch!;

        vm.VisibleLineups.Should().NotBeEmpty();
        vm.VisibleLineups.Should().OnlyContain(l => l.Source.Goals.Count == 2);
    }

    [Fact]
    public async Task LockedOperatorsAreAddedFromSearchAndSaved()
    {
        var (vm, _) = Create();
        vm.IsActive = true;
        Item(vm, "炎").ToggleLevel1Command.Execute(null);

        vm.LockSearchText = "维";
        vm.LockSuggestions.Should().NotBeEmpty();
        vm.AddLocked("维3").Should().BeTrue();
        await vm.PendingSearch!;

        Lineup? saved = null;
        vm.SaveRequested += l => saved = l;
        vm.SaveLineupCommand.Execute(vm.VisibleLineups[0]);

        saved.Should().NotBeNull();
        saved!.Slots.Select(s => s.OperatorName).Should().Contain("维3");
        vm.VisibleLineups[0].Operators[0].IsLocked.Should().BeTrue();
    }

    [Fact]
    public void OnlyCurrentRunHidesUnavailableCovenants()
    {
        var (vm, _) = Create();
        vm.SetAvailableCovenants(["炎"]);

        vm.OnlyCurrentRun = true;

        Item(vm, "炎").IsVisible.Should().BeTrue();
        Item(vm, "维多利亚").IsVisible.Should().BeFalse();
        vm.HasRunInfo.Should().BeTrue();
    }

    [Fact]
    public async Task BannedAdditionalCovenantStaysSelectable()
    {
        var (vm, bans) = Create();
        vm.IsActive = true;
        bans.SetCovenantBans(["突袭"]);
        vm.SetAvailableCovenants(["炎", "维多利亚"]);
        vm.OnlyCurrentRun = true;

        var raid = Item(vm, "突袭");
        raid.IsBanned.Should().BeTrue();
        raid.IsBlocked.Should().BeFalse();
        raid.IsVisible.Should().BeTrue();

        raid.ToggleLevel1Command.Execute(null);
        await vm.PendingSearch!;

        raid.Level.Should().Be(1);
        vm.TotalResults.Should().BeGreaterThan(0);
    }

    [Fact]
    public void BannedAdditionalCovenantIsBlockedWhenTooFewHoldersRemain()
    {
        var (vm, bans) = Create();
        vm.IsActive = true;
        var raid = Item(vm, "突袭");
        raid.ToggleLevel1Command.Execute(null);
        raid.Level.Should().Be(1);

        // 炎 + 突袭 banned: 炎突 leaves (core + additional banned), only 维突 holds 突袭 — Ⅰ needs 2.
        bans.SetCovenantBans(["炎", "突袭"]);

        raid.AvailableMembers.Should().Be(1);
        raid.IsBlocked.Should().BeTrue();
        raid.Level.Should().Be(0);
        raid.BanNote.Should().Contain("凑不齐");
    }

    [Fact]
    public void CovenantDetailsListMembersAndMarkUnavailableOnes()
    {
        var (vm, bans) = Create();
        var fire = Item(vm, "炎");
        fire.Members.Select(m => m.Name).Should().Contain(["炎1", "炎突"]);
        fire.Members.Should().OnlyContain(m => !m.IsUnavailable);

        bans.SetBans(["炎1"]);

        Item(vm, "炎").Members.Single(m => m.Name == "炎1").IsUnavailable.Should().BeTrue();
        Item(vm, "炎").MemberSummary.Should().Contain("本局可用");
        Item(vm, "炎").FirstRequirement.Should().Be("3 人");
        Item(vm, "独行").SecondRequirement.Should().Be("没有 Ⅱ 级");
    }

    [Fact]
    public void ManualSecondLevelIsUnavailableToPlanner()
    {
        var (vm, _) = Create();
        var raid = Item(vm, "突袭");

        raid.SecondManual.Should().BeTrue();
        raid.CanPlanSecond.Should().BeFalse();
        raid.ToggleLevel2Command.Execute(null);
        raid.Level.Should().Be(0);
        raid.SecondRequirement.Should().Be("规划不提供 Ⅱ 级效果");
    }

    [Fact]
    public async Task ResultOperatorsCarryDetailsAndPlannerNotes()
    {
        var (vm, _) = Create();
        vm.IsActive = true;
        vm.AddLocked("维3");
        Item(vm, "炎").ToggleLevel1Command.Execute(null);
        await vm.PendingSearch!;

        var ops = vm.VisibleLineups[0].Operators;
        ops.Where(o => !o.IsFree).Should().OnlyContain(o => o.HasDetails && o.Operator!.Name == o.Name);
        ops.Where(o => o.IsFree).Should().OnlyContain(o => !o.HasDetails);
        ops.Single(o => o.Name == "维3").ExtraInfo.Should().Contain("必带");
        vm.LockedOperators.Single().Operator.Name.Should().Be("维3");
    }

    [Fact]
    public async Task ResultsShowAllActuallyActivatedCovenantLevels()
    {
        var operators = new List<Operator>
        {
            Op("炎1", "炎"), Op("炎2", "炎"), Op("炎3", "炎"),
            new() { Name = "精准1", AdditionalCovenants = ["精准"], IconPath = "icons/operators/精准1.png" },
            new() { Name = "精准2", AdditionalCovenants = ["精准"], IconPath = "icons/operators/精准2.png" },
            new() { Name = "精准3", AdditionalCovenants = ["精准"], IconPath = "icons/operators/精准3.png" },
            Op("空1"), Op("空2")
        };
        var covenants = new[]
        {
            new CovenantInfo { Name = "炎", BondType = "SEASON", ActivateCount = 3 },
            new CovenantInfo { Name = "精准", BondType = "REGULAR", ActivateCount = 2 }
        };
        var vm = new LineupPlannerViewModel(
            new LineupPlanner(operators, covenants), operators, new BanViewModel(), EquipmentTestData.Rules) { DebounceMilliseconds = 0 };
        vm.IsActive = true;
        vm.AllCovenants.Single(c => c.Name == "炎").ToggleLevel1Command.Execute(null);
        await vm.PendingSearch!;

        vm.VisibleLineups[0].Goals.Select(g => g.Text)
            .Should().Contain(["炎 3", "精准 3"]);
    }

    [Fact]
    public async Task RouteForACandidateListsStagesAndClosesOnNewResults()
    {
        var (vm, _) = Create();
        vm.IsActive = true;
        Item(vm, "炎").ToggleLevel1Command.Execute(null);
        await vm.PendingSearch!;

        await vm.PlanRouteAsync(vm.VisibleLineups[0]);

        vm.ShowRoute.Should().BeTrue();
        vm.RouteStages.Select(s => s.Title).Should().Equal("开局", "前期", "中期", "后期", "大成");
        vm.RouteSteps.Select(s => s.Name).Should().Equal("开局", "前期", "中期", "后期", "大成");
        // Terminal level unknown: 开局 is the current stage and the only one open
        vm.RouteStages.Count(s => s.IsExpanded).Should().Be(1);
        vm.RouteStages[0].IsCurrent.Should().BeTrue();
        vm.RouteStages.SelectMany(s => s.Roles).Should().NotBeEmpty();
        vm.RouteStages[^1].IsFinal.Should().BeTrue();
        vm.RouteStages.SelectMany(s => s.Operators).Should().OnlyContain(o => o.RoleText == "最终" || o.RoleText == "过渡");

        Item(vm, "维多利亚").ToggleLevel1Command.Execute(null);
        await vm.PendingSearch!;
        vm.ShowRoute.Should().BeFalse();
        vm.RouteStages.Should().BeEmpty();
    }

    [Fact]
    public async Task SavedRouteKeepsTheStagesAsLineupPhases()
    {
        var (vm, _) = Create();
        Lineup? saved = null;
        vm.SaveRequested += l => saved = l;
        vm.IsActive = true;
        Item(vm, "炎").ToggleLevel1Command.Execute(null);
        await vm.PendingSearch!;
        await vm.PlanRouteAsync(vm.VisibleLineups[0]);

        vm.SaveRouteCommand.Execute(null);

        saved.Should().NotBeNull();
        saved!.Slots.Select(s => s.OperatorName).Should().BeEquivalentTo(vm.VisibleLineups[0].Source.Operators.Select(o => o.Name));
        saved.EarlySlots.Select(s => s.OperatorName).Should().Equal(vm.RouteStages.Single(s => s.Title == "前期").Operators.Select(o => o.Name));
        saved.MidSlots.Select(s => s.OperatorName).Should().Equal(vm.RouteStages.Single(s => s.Title == "中期").Operators.Select(o => o.Name));
        saved.SupportSlots.Select(s => s.OperatorName).Should().OnlyContain(n => !saved.Slots.Any(f => f.OperatorName == n));
    }

    [Fact]
    public async Task MechStrategyIsSavedWithItsTransformations()
    {
        var (vm, _) = Create();
        Lineup? saved = null;
        vm.SaveRequested += l => saved = l;
        vm.IsActive = true;
        vm.Strategy = PlannerStrategy.Mech;
        Item(vm, "炎").ToggleLevel1Command.Execute(null);
        await vm.PendingSearch!;

        vm.VariantCells.Select(c => c.Transforms).Distinct().Should().Equal(0, 1, 2, 3);
        vm.SelectVariantCommand.Execute(vm.VariantCells.First(c => c.Transforms == 1 && c.HasResults));
        var item = vm.VisibleLineups.FirstOrDefault(l => l.Source.MechCovenants.Count > 0)
            ?? throw new Xunit.Sdk.XunitException("no lineup uses the mech");
        item.HasStrategyPiece.Should().BeTrue();
        vm.SaveLineupCommand.Execute(item);

        saved!.Strategies.Should().ContainSingle(s => s.StrategyId == PlannerStrategies.MechId);
        var mech = saved.Slots.Single(s => s.OperatorName == LineupEquipmentService.MechName);
        mech.Equipments.Should().StartWith(LineupEquipmentService.Shapeshifter);
        mech.Equipments.Should().HaveCount(item.Source.MechCovenants.Count + 1);
    }

    [Fact]
    public async Task Repair_LocksSurvivorsAndKeepsTheirGear()
    {
        var (vm, bans) = Create();
        vm.IsActive = true;
        var source = new Lineup
        {
            Id = "src", Name = "炎 速攻",
            Slots = [.. new[] { "炎1", "炎2", "炎3", "炎4", "炎5", "炎6" }.Select(n => new LineupSlot { OperatorName = n })],
            EarlySlots = [new LineupSlot { OperatorName = "维1" }],
            TargetCovenants = [new TargetCovenant { Name = "炎", Tier = 2 }],
        };
        source.Slots[0].CandidateEquipments = [new CandidateEquipment { Name = "歌利亚头盔", IsPriority = true }];
        bans.SetBans(["炎5", "炎6"]);
        vm.BeginRepair(source, source.TargetCovenants, ["炎5", "炎6"]);
        await vm.PendingSearch!;

        vm.IsRepairing.Should().BeTrue();
        vm.LockedOperators.Select(l => l.Name).Should().Equal("炎1", "炎2", "炎3", "炎4");
        Item(vm, "炎").Level.Should().Be(2);
        vm.RepairSummary.Should().StartWith("固定没被禁的 4 人，替补 2 人");
        vm.VisibleLineups.Should().NotBeEmpty();
        var first = vm.VisibleLineups[0];
        first.RepairText.Should().StartWith("炎5、炎6 → ");
        first.LineupName.Should().Be("炎 速攻（修补）");

        Lineup? overwritten = null;
        vm.OverwriteRequested += l => overwritten = l;
        vm.OverwriteLineupCommand.Execute(first);
        overwritten!.Id.Should().Be("src");
        overwritten.EarlySlots.Single().OperatorName.Should().Be("维1");
        overwritten.Slots.Single(s => s.OperatorName == "炎1").CandidateEquipments.Single().Name.Should().Be("歌利亚头盔");
        overwritten.Slots.Should().NotContain(s => s.OperatorName == "炎5");

        vm.EndRepairCommand.Execute(null);
        vm.IsRepairing.Should().BeFalse();
    }
}
