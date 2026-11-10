// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
using ASPAssistant.Core.Models;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.ViewModels;

public class TrackingViewModelTests
{
    [Fact]
    public void AddTracking_AddsEntry()
    {
        var vm = new TrackingViewModel();
        vm.AddTracking("角峰", TrackingType.Operator);
        vm.TrackedOperators.Should().ContainSingle(e => e.Name == "角峰");
    }

    [Fact]
    public void AddTracking_Duplicate_DoesNotAdd()
    {
        var vm = new TrackingViewModel();
        vm.AddTracking("角峰", TrackingType.Operator);
        vm.AddTracking("角峰", TrackingType.Operator);
        vm.TrackedOperators.Should().HaveCount(1);
    }

    [Fact]
    public void RemoveTracking_RemovesEntry()
    {
        var vm = new TrackingViewModel();
        vm.AddTracking("角峰", TrackingType.Operator);
        vm.RemoveTracking("角峰");
        vm.TrackedOperators.Should().BeEmpty();
    }

    [Fact]
    public void IsTracked_ReturnsTrueForTrackedItem()
    {
        var vm = new TrackingViewModel();
        vm.AddTracking("角峰", TrackingType.Operator);
        vm.IsTracked("角峰").Should().BeTrue();
        vm.IsTracked("隐现").Should().BeFalse();
    }

    [Fact]
    public void AllEntries_ReturnsBothTypes()
    {
        var vm = new TrackingViewModel();
        vm.AddTracking("角峰", TrackingType.Operator);
        vm.AddTracking("不屈弹射器", TrackingType.Equipment);
        vm.AllEntries.Should().HaveCount(2);
        vm.TrackedOperators.Should().HaveCount(1);
        vm.TrackedEquipment.Should().HaveCount(1);
    }

    [Fact]
    public void TrackLineup_DefaultModeReplacesExistingTargets()
    {
        var vm = new TrackingViewModel();
        vm.AddTracking("隐现", TrackingType.Operator);
        vm.AddTracking("旧装备", TrackingType.Equipment);

        var added = vm.TrackLineup(new Lineup
        {
            Slots = [new() { OperatorName = "角峰", Equipments = ["不屈弹射器"] }]
        });

        vm.TrackMultipleLineups.Should().BeFalse();
        vm.TrackedOperators.Should().ContainSingle(e => e.Name == "角峰");
        vm.TrackedEquipment.Should().ContainSingle(e => e.Name == "不屈弹射器");
        added.Should().Be((1, 1));
    }

    [Fact]
    public void TrackLineup_MultipleModeMergesAndDeduplicatesTeams()
    {
        var vm = new TrackingViewModel { TrackMultipleLineups = true };
        vm.TrackLineup(new Lineup
        {
            Slots = [new() { OperatorName = "角峰", Equipments = ["不屈弹射器"] }]
        });
        var added = vm.TrackLineup(new Lineup
        {
            Slots =
            [
                new() { OperatorName = "角峰", Equipments = ["不屈弹射器"] },
                new() { OperatorName = "隐现", Equipments = ["乌萨斯呼吸器", "不屈弹射器"] }
            ]
        });

        vm.TrackedOperators.Select(e => e.Name).Should().BeEquivalentTo(["角峰", "隐现"]);
        vm.TrackedEquipment.Select(e => e.Name).Should().BeEquivalentTo(["不屈弹射器", "乌萨斯呼吸器"]);
        added.Should().Be((1, 1));
    }

    [Fact]
    public void TrackLineup_DisablingMultipleModeKeepsListUntilNextTeam()
    {
        var vm = new TrackingViewModel { TrackMultipleLineups = true };
        vm.TrackLineup(new Lineup { Slots = [new() { OperatorName = "角峰" }] });
        vm.TrackLineup(new Lineup { Slots = [new() { OperatorName = "隐现" }] });

        vm.TrackMultipleLineups = false;
        vm.TrackedOperators.Should().HaveCount(2);
        vm.TrackLineup(new Lineup { Slots = [new() { OperatorName = "惊蛰" }] });

        vm.TrackedOperators.Should().ContainSingle(e => e.Name == "惊蛰");
    }

    [Fact]
    public void TrackLineup_SameTeamTwiceDoesNotDuplicateTargets()
    {
        var vm = new TrackingViewModel { TrackMultipleLineups = true };
        var lineup = new Lineup
        {
            Slots = [new() { OperatorName = "角峰", Equipments = ["不屈弹射器"] }]
        };
        vm.TrackLineup(lineup);

        vm.TrackLineup(lineup).Should().Be((0, 0));
        vm.AllEntries.Should().HaveCount(2);
    }

    [Fact]
    public void ClearAllTracking_RemovesBothTypesAndKeepsMultipleMode()
    {
        var vm = new TrackingViewModel { TrackMultipleLineups = true };
        vm.TrackLineup(new Lineup
        {
            Slots = [new() { OperatorName = "角峰", Equipments = ["不屈弹射器"] }]
        });

        vm.ClearAllTracking();

        vm.AllEntries.Should().BeEmpty();
        vm.TrackMultipleLineups.Should().BeTrue();
    }
}
