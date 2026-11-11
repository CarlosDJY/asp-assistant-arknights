using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.ViewModels;

public class RunAvailabilityTests
{
    [Fact]
    public void PlannerOnlyOffersTransformationsWithRealEquipment()
    {
        CovenantInfo[] covenants = [new() { Name = "精准", ActivateCount = 2 }, new() { Name = "助力", ActivateCount = 2 }];
        var planner = new LineupPlannerViewModel(new LineupPlanner([], covenants), [], new BanViewModel(), EquipmentTestData.Rules);
        planner.AllCovenants.Single(c => c.Name == "助力").CanTransformInto.Should().BeFalse();
        planner.AllCovenants.Single(c => c.Name == "精准").CanTransformInto.Should().BeTrue();
        planner.BuildRequest().TransformAllowed.Should().OnlyContain(c => EquipmentTestData.Rules.CanTransformInto(c));
    }

    [Fact]
    public void OnlyCurrentRunHidesUnavailableChoicesAndRemovesInvisibleFilters()
    {
        var vm = new OperatorBrowseViewModel();
        vm.LoadOperators([
            new() { Name = "甲", CoreCovenants = ["炎"], AdditionalCovenants = ["精准"] },
            new() { Name = "乙", CoreCovenants = ["维多利亚"], AdditionalCovenants = ["坚守"] }
        ]);
        vm.HasRunInfo.Should().BeFalse();
        vm.SetCoreCovenantFilters(["炎"]);
        vm.SetOtherCovenantFilters(["精准"]);
        vm.SetAvailableCovenants(["维多利亚", "坚守"]);
        vm.OnlyCurrentRun = true;
        vm.AvailableCoreCovenants.Should().Equal("", "维多利亚");
        vm.AvailableOtherCovenants.Should().Equal("", "坚守");
        vm.ActiveFilterTags.Should().NotContain("炎").And.NotContain("精准");
        vm.FilteredOperators.Should().HaveCount(2);
        vm.ClearCovenantHighlights();
        vm.HasRunInfo.Should().BeFalse();
        vm.OnlyCurrentRun.Should().BeFalse();
        vm.AvailableCoreCovenants.Should().Contain("炎");
        vm.AvailableOtherCovenants.Should().Contain("精准");
    }

    [Fact]
    public void ClearingRunLimitsPreservesManualGoalsLocksAndOperatorBans()
    {
        Operator[] operators = [new() { Name = "固定干员", CoreCovenants = ["维多利亚"] }, new() { Name = "手动禁用", CoreCovenants = ["炎"] }];
        CovenantInfo[] covenants = [new() { Name = "炎", BondType = "SEASON", ActivateCount = 3 }, new() { Name = "维多利亚", BondType = "SEASON", ActivateCount = 3 }];
        var bans = new BanViewModel();
        var planner = new LineupPlannerViewModel(new LineupPlanner(operators, covenants), operators, bans, EquipmentTestData.Rules);
        planner.AllCovenants.Single(c => c.Name == "维多利亚").ToggleLevel1Command.Execute(null);
        planner.AddLocked("固定干员");
        bans.ToggleBan("手动禁用");
        bans.SetCovenantBans(["炎"]);
        planner.SetAvailableCovenants(["维多利亚"]);
        planner.OnlyCurrentRun = true;
        bans.SetCovenantBans([]);
        planner.SetAvailableCovenants([]);
        planner.HasRunInfo.Should().BeFalse();
        planner.OnlyCurrentRun.Should().BeFalse();
        planner.AllCovenants.Should().OnlyContain(c => !c.IsBanned && c.IsVisible);
        planner.SelectedGoalCount.Should().Be(1);
        planner.LockedOperators.Should().ContainSingle().Which.Name.Should().Be("固定干员");
        bans.IsBanned("手动禁用").Should().BeTrue();
    }
}
