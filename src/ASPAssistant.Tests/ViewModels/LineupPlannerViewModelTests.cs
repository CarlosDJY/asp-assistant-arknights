using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.ViewModels;

public class LineupPlannerViewModelTests
{
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
        var vm = new LineupPlannerViewModel(new LineupPlanner(operators, covenants), operators, bans) { DebounceMilliseconds = 0 };
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
}
