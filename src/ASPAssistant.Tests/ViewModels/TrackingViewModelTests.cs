// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
using ASPAssistant.Core.Models;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.ViewModels;

public class TrackingViewModelTests
{
    [Fact]
    public void MergedLineupsUseLatestTransformationAndNotifySoChangesAreSaved()
    {
        var vm = new TrackingViewModel { TrackMultipleLineups = true };
        var changes = 0;
        vm.TrackedOperators.CollectionChanged += (_, _) => changes++;
        vm.TrackLineup(new Lineup { Slots = [new() { OperatorName = "角峰", TransformedCovenant = "炎" }] });
        vm.TrackLineup(new Lineup { Slots = [new() { OperatorName = "角峰", TransformedCovenant = "维多利亚" }] });
        vm.TrackedOperators.Should().ContainSingle().Which.TransformedCovenant.Should().Be("维多利亚");
        vm.TrackLineup(new Lineup { Slots = [new() { OperatorName = "角峰" }] });
        vm.TrackedOperators.Single().TransformedCovenant.Should().BeEmpty();
        changes.Should().Be(3);
        vm.TrackLineup(new Lineup { Slots = [new() { OperatorName = "角峰", TransformedCovenant = "炎" }] });
        vm.ClearPlannedTransformation("角峰");
        changes.Should().Be(5);
        vm.TrackedOperators.Single().TransformedCovenant.Should().BeEmpty();
    }

    private static RouteStage Stage(string name, bool final, params string[] ops)
        => new(new RouteStageDefinition(name, 6, 6, 0, ops.Length),
            ops.Select(o => new RouteOperator(o, 1, "", RouteOperatorRole.Final)).ToList(), []) { IsFinal = final };

    [Fact]
    public void TrackRoute_MarksFinalAndTransitionOperators()
    {
        var vm = new TrackingViewModel();
        vm.AddTracking("旧", TrackingType.Operator);
        var route = new PlannedRoute
        {
            Stages =
            [
                Stage("开局", false, "过渡A", "过渡B", "角峰"),
                Stage("前期", false, "过渡A", "角峰"),
                Stage("大成（打 Boss）", true, "角峰", "史尔特尔"),
            ]
        };

        var added = vm.TrackRoute(route);

        added.Should().Be((2, 2));
        vm.TrackedOperators.Should().NotContain(e => e.Name == "旧");
        vm.TrackedOperators.Where(e => e.Role == TrackingRole.Final).Select(e => e.Name).Should().BeEquivalentTo(["角峰", "史尔特尔"]);
        vm.TrackedOperators.Single(e => e.Name == "过渡A").Should().Match<TrackingEntry>(e => e.IsTransition && e.Stages == "开局 · 前期");
        vm.TrackedOperators.Single(e => e.Name == "过渡B").Stages.Should().Be("开局");

        // Tracking a lineup that contains a transition operator promotes it to 大成.
        vm.TrackMultipleLineups = true;
        vm.TrackLineup(new Lineup { Slots = [new() { OperatorName = "过渡B" }] });
        vm.TrackedOperators.Single(e => e.Name == "过渡B").Role.Should().Be(TrackingRole.Final);
    }

    private static PlannedRoute FiveStageRoute() => new()
    {
        Stages =
        [
            new(new RouteStageDefinition("开局", 2, 2, 1, 2), [new("开局卡", 1, "", RouteOperatorRole.Transition), new("中期也用", 1, "", RouteOperatorRole.Transition)], []),
            new(new RouteStageDefinition("前期", 3, 3, 1, 1), [new("前期卡", 1, "", RouteOperatorRole.Transition)], []),
            new(new RouteStageDefinition("中期", 4, 4, 0, 1), [new("中期也用", 1, "", RouteOperatorRole.Transition)], []),
            new(new RouteStageDefinition("六本叠层", 6, 6, 0, 1) { Stacking = true }, [new("叠层卡", 1, "", RouteOperatorRole.Transition)], []),
            Stage("大成（打 Boss）", true, "角峰"),
        ]
    };

    [Fact]
    public void RouteStagesExpireAtTheNextHigherTerminalLevel()
    {
        var route = FiveStageRoute();
        Enumerable.Range(0, route.Stages.Count).Select(route.ExpiresAt).Should().Equal(3, 4, 6, 0, 0);
        route.IsPast(0, 4).Should().BeTrue();
        route.IsPast(1, 3).Should().BeFalse();
        route.IsPast(3, 6).Should().BeFalse();
    }

    [Fact]
    public void TerminalLevel_StopsMarkingTransitionCardsOfPastStages()
    {
        var vm = new TrackingViewModel();
        vm.TrackRoute(FiveStageRoute());
        vm.ActiveEntries.Select(e => e.Name).Should().BeEquivalentTo(["角峰", "开局卡", "中期也用", "前期卡", "叠层卡"]);

        vm.DetectedTerminalLevel = 4;
        vm.ActiveEntries.Select(e => e.Name).Should().BeEquivalentTo(["角峰", "中期也用", "叠层卡"]);
        vm.TrackedOperators.Single(e => e.Name == "开局卡").IsExpired.Should().BeTrue();
        vm.TerminalLevelText.Should().Contain("4").And.Contain("自动");

        // Manual override wins; 自动 (0) goes back to the detected level.
        vm.TerminalLevelChoice = 2;
        vm.ActiveEntries.Should().HaveCount(5);
        vm.TerminalLevelChoice = 0;
        vm.TerminalLevel.Should().Be(4);

        // Importing at terminal 4 does not add cards of stages that are already past.
        var fresh = new TrackingViewModel { DetectedTerminalLevel = 4 };
        fresh.TrackRoute(FiveStageRoute());
        fresh.TrackedOperators.Select(e => e.Name).Should().BeEquivalentTo(["角峰", "中期也用", "叠层卡"]);
    }

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

    [Fact]
    public void ManualTerminalLevel_GivesWayOnceDetectionCatchesUp()
    {
        var vm = new TrackingViewModel { DetectedTerminalLevel = 2 };
        vm.TerminalLevelChoice = 4;
        vm.TerminalLevel.Should().Be(4);
        vm.DetectedTerminalLevel = 3;
        vm.TerminalLevel.Should().Be(4, "detection is still behind the manual value");
        vm.DetectedTerminalLevel = 4;
        vm.ManualTerminalLevel.Should().BeNull();
        vm.DetectedTerminalLevel = 5;
        vm.TerminalLevel.Should().Be(5);
    }

    [Fact]
    public void RestoredTerminalLevel_IsLabelled()
    {
        var vm = new TrackingViewModel { DetectedTerminalLevel = 4, IsTerminalLevelRestored = true };
        vm.TerminalLevelText.Should().Contain("上次记录");
        vm.IsTerminalLevelRestored = false;
        vm.TerminalLevelText.Should().Contain("自动识别");
    }
}
