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
        vm.TrackLineup(new Lineup { Slots = [new() { OperatorName = "角峰", Equipments = ["变形同构体", "炎国短刀"] }] });
        vm.TrackLineup(new Lineup { Slots = [new() { OperatorName = "角峰", Equipments = ["变形同构体", "维式重锤"] }] });
        vm.TrackedOperators.Should().ContainSingle().Which.Equipments.Should().Contain("维式重锤");
        vm.TrackLineup(new Lineup { Slots = [new() { OperatorName = "角峰" }] });
        vm.TrackedOperators.Single().Equipments.Should().BeEmpty();
        changes.Should().Be(3);
        vm.TrackLineup(new Lineup { Slots = [new() { OperatorName = "角峰", Equipments = ["变形同构体", "炎国短刀"] }] });
        vm.ClearPlannedTransformation("角峰", "炎国短刀", EquipmentTestData.Rules);
        changes.Should().Be(5);
        vm.TrackedOperators.Single().Equipments.Should().BeEmpty();
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

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TrackLineup_IncludesAllPhasesAndDeduplicatesEquipment(bool multiple)
    {
        var vm = new TrackingViewModel { TrackMultipleLineups = multiple };
        vm.AddTracking("原有干员", TrackingType.Operator);
        vm.AddTracking("手动装备", TrackingType.Equipment);
        var lineup = new Lineup
        {
            Slots = [new() { OperatorName = "后期", Equipments = ["共享装备"] }, new()],
            MidSlots = [new() { OperatorName = "中期", Equipments = ["共享装备"] }],
            EarlySlots = [new() { OperatorName = "前期", Equipments = ["变形同构体", "炎国短刀"] }],
            SupportSlots = [new() { OperatorName = "挂件", Equipments = ["坚守盾牌"] }]
        };

        vm.TrackLineup(lineup).Should().Be((4, 4));

        vm.TrackedOperators.Select(e => e.Name).Should().BeEquivalentTo(multiple
            ? new[] { "原有干员", "后期", "中期", "前期", "挂件" }
            : new[] { "后期", "中期", "前期", "挂件" });
        vm.TrackedEquipment.Select(e => e.Name).Should().BeEquivalentTo(multiple
            ? new[] { "手动装备", "共享装备", "变形同构体", "炎国短刀", "坚守盾牌" }
            : new[] { "共享装备", "变形同构体", "炎国短刀", "坚守盾牌" });
        vm.TrackedOperators.Single(e => e.Name == "前期").Equipments.Should().Equal("变形同构体", "炎国短刀");
        vm.TrackMultipleLineups = true;
        vm.TrackLineup(lineup).Should().Be((0, 0));
    }

    [Fact]
    public void TrackLineup_WithoutLateOperatorsStillTracksOtherPhases()
    {
        var vm = new TrackingViewModel();

        vm.TrackLineup(new Lineup
        {
            EarlySlots = [new() { OperatorName = "前期" }],
            MidSlots = [new() { OperatorName = "中期" }],
            SupportSlots = [new() { OperatorName = "挂件" }]
        }).Should().Be((3, 0));

        vm.TrackedOperators.Select(e => e.Name).Should().BeEquivalentTo("前期", "中期", "挂件");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void TrackLineup_DuplicateOperatorUsesHighestPriorityPhase(int firstPhase)
    {
        var vm = new TrackingViewModel { TrackMultipleLineups = true };
        vm.TrackLineup(new Lineup { Slots = [new() { OperatorName = "重复干员", Equipments = ["旧装备"] }] });
        var phases = new List<LineupSlot>[] { [], [], [], [] };
        for (var i = firstPhase; i < phases.Length; i++)
            phases[i].Add(new LineupSlot
            {
                OperatorName = "重复干员", IsElite = i == firstPhase,
                Equipments = i == firstPhase ? [] : ["变形同构体", "炎国短刀"]
            });

        vm.TrackLineup(new Lineup
        {
            Slots = phases[0], MidSlots = phases[1], EarlySlots = phases[2], SupportSlots = phases[3]
        }).Should().Be((0, 0));

        var entry = vm.TrackedOperators.Should().ContainSingle().Which;
        entry.IsElite.Should().BeTrue();
        entry.Equipments.Should().BeEmpty();
        vm.TrackedEquipment.Should().BeEmpty();
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
