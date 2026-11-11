// Modified for ASP Assistant Arknights on 2026-11-12; see NOTICE.
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

/// <summary>How the run's strategy (暴躁铁皮 / Pith / 变形同构) changes planning.</summary>
public class PlannerStrategyTests
{
    private static CovenantInfo Core(string name) => new() { Name = name, BondType = "SEASON", ActivateCount = 3 };
    private static CovenantInfo Extra(string name, int count = 2) => new() { Name = name, BondType = "REGULAR", ActivateCount = count };
    private static Operator Op(string name, string[]? core = null, string[]? extra = null)
        => new() { Name = name, CoreCovenants = [.. core ?? []], AdditionalCovenants = [.. extra ?? []] };

    private static readonly CovenantInfo[] Covenants = [Core("炎"), Core("维多利亚"), Extra("突袭"), Extra("调和", 1), Extra("协防干员")];

    [Fact]
    public void Mech_TakesAPlaceAndCarriesTheTransformation()
    {
        var planner = new LineupPlanner([Op("炎1", ["炎"]), Op("炎2", ["炎"]), Op("维1", ["维多利亚"]), Op("维2", ["维多利亚"])], Covenants);
        var lineups = planner.Solve(new PlannerRequest
        {
            Goals = [new("炎", 1, 3, false, true)], PeopleOptions = [3], TransformOptions = [1],
            TransformAllowed = planner.DefaultTransformAllowed(), Strategy = PlannerStrategy.Mech
        });

        lineups.Should().NotBeEmpty();
        lineups.Should().OnlyContain(l => l.MechCovenants.SequenceEqual(new[] { "炎" }) && l.Operators.Count == 2
            && l.Operators.All(o => o.TransformedCovenant == null) && l.Counts["炎"] >= 3 && l.FreeSlots == 0);
    }

    [Fact]
    public void Mech_TakesSeveralCovenantsButEachOnlyOnce()
    {
        var planner = new LineupPlanner([Op("甲", ["维多利亚"]), Op("乙", ["维多利亚"]), Op("丙", ["维多利亚"])], Covenants);
        var lineup = planner.Solve(new PlannerRequest
        {
            Goals = [new("炎", 1, 2, false, true)], PeopleOptions = [4], TransformOptions = [3],
            TransformAllowed = planner.DefaultTransformAllowed(), Strategy = PlannerStrategy.Mech
        }).First();

        // one 炎 on the mech, the second 炎 on a team member; the third transformation goes back to the mech
        lineup.MechCovenants.Should().OnlyHaveUniqueItems().And.Contain("炎").And.HaveCount(2);
        lineup.Operators.Count(o => o.TransformedCovenant == "炎").Should().Be(1);
        (lineup.MechCovenants.Count + lineup.Operators.Count(o => o.TransformedCovenant != null)).Should().Be(3);
    }

    [Fact]
    public void Mech_IsAnotherHolderOfItsOwnCovenant()
    {
        var planner = new LineupPlanner([Op("初雪", ["维多利亚"], ["协防干员"]), Op("维1", ["维多利亚"]), Op("维2", ["维多利亚"])], Covenants);
        var lineup = planner.Solve(new PlannerRequest
        {
            Goals = [new("维多利亚", 1, 3, false, true)], PeopleOptions = [4], TransformOptions = [0], Strategy = PlannerStrategy.Mech
        }).Single();

        lineup.Counts["协防干员"].Should().Be(2);
    }

    [Fact]
    public void Pith_TakesAPlaceAndAddsHarmony()
    {
        var planner = new LineupPlanner([Op("炎1", ["炎"]), Op("炎2", ["炎"]), Op("维1", ["维多利亚"])], Covenants);
        var request = new PlannerRequest { Goals = [new("炎", 1, 3, false, true)], PeopleOptions = [3], TransformOptions = [0] };

        planner.Solve(request).Should().BeEmpty();
        var lineup = planner.Solve(new PlannerRequest
        {
            Goals = request.Goals, PeopleOptions = [3], TransformOptions = [0], Strategy = PlannerStrategy.Pith
        }).Single();

        lineup.Operators.Should().HaveCount(2);
        lineup.Counts["炎"].Should().Be(3);
        lineup.HarmonyBonus["炎"].Should().Be(1);
        lineup.HasPith.Should().BeTrue();
    }

    [Theory]
    [InlineData(PlannerStrategy.Other, 3)]
    [InlineData(PlannerStrategy.Pith, 3)]
    [InlineData(PlannerStrategy.Mech, 4)]
    [InlineData(PlannerStrategy.Shapeshifter, 4)]
    public void TransformColumns_FollowTheStrategy(PlannerStrategy strategy, int columns)
        => PlannerStrategies.TransformOptions(strategy).Should().HaveCount(columns);

    [Fact]
    public void MechCap_DependsOnElite()
    {
        PlannerStrategies.MechTransformCap(false).Should().Be(3);
        PlannerStrategies.MechTransformCap(true).Should().Be(5);
    }
}

public class EditorMechSlotTests
{
    private static Operator Mech() => new() { Name = LineupEquipmentService.MechName, RequiredStrategyId = "band_mech", AdditionalCovenants = ["协防干员"] };
    private static CovenantCalculator Calculator() => new([Mech(), new() { Name = "普通干员" }],
        [new() { Name = "炎", BondType = "SEASON", ActivateCount = 3 }, new() { Name = "协防干员", BondType = "REGULAR", ActivateCount = 2 }],
        EquipmentTestData.Rules.EquipmentToCovenant.ToDictionary(p => p.Key, p => p.Value));

    [Fact]
    public void MechStrategyPutsTheMechIntoOneOfTheNinePlaces()
    {
        var vm = new ASPAssistant.Core.ViewModels.LineupEditorViewModel(null, [Mech(), new() { Name = "普通干员" }], [], Calculator());
        vm.SetStrategy(0, PlannerStrategies.MechId);

        vm.Slots.Should().HaveCount(9);
        vm.RegularSlotIndices.Should().HaveCount(8);
        vm.MechSlotIndex.Should().Be(8);
        vm.SetSlotOperator(0, "普通干员");
        vm.FilledSlotCount.Should().Be(2);

        // removing the strategy gives the untouched mech's place back
        vm.SetStrategy(0, "");
        vm.Slots.Should().HaveCount(9);
        vm.MechSlotIndex.Should().Be(-1);
        vm.RegularSlotIndices.Should().HaveCount(9);
    }

    [Fact]
    public void SavedMechKeepsItsGearAndReloadsIntoTheMechSlot()
    {
        var vm = new ASPAssistant.Core.ViewModels.LineupEditorViewModel(null, [Mech(), new() { Name = "普通干员" }], [], Calculator());
        vm.SetStrategy(0, PlannerStrategies.MechId);
        vm.SelectPhase(LineupPhase.后期);
        vm.SetSlotTransformation(vm.MechSlotIndex, 0, "炎国短刀", []);
        var lineup = vm.ToLineup();

        lineup.Slots.Should().ContainSingle(s => s.OperatorName == LineupEquipmentService.MechName);
        var again = new ASPAssistant.Core.ViewModels.LineupEditorViewModel(lineup, [Mech()], [], Calculator());
        again.MechSlotIndex.Should().Be(8);
        again.Slots[8].Equipments.Should().Equal("变形同构体", "炎国短刀");
    }
}

public class RouteStageNameTests
{
    [Theory]
    [InlineData("开局 · 四本对对碰", "开局 · 中期")]
    [InlineData("中期 · 四本对对碰 · 六本叠层", "中期 · 后期")]
    [InlineData("前期", "前期")]
    [InlineData("", "")]
    public void OldStageNamesAreRenamed(string stored, string shown)
        => RouteStageNames.Normalize(stored).Should().Be(shown);
}

public class SettingsDefaultsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"asp_defaults_{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, true);
    }

    [Fact]
    public async Task FirstStartUsesTheDefaultSettings()
    {
        var settings = new ASPAssistant.Core.Data.SettingsManager(_dir);
        var appearance = await settings.LoadSidebarAppearanceAsync();
        appearance.FollowModeTheme.Should().BeFalse();
        appearance.Theme.Should().Be(GameShopTheme.Terminal);
        appearance.IsNight(GameShopTheme.Terminal).Should().BeFalse();
        (await settings.LoadTrackMultipleLineupsAsync()).Should().BeFalse();
        (await settings.LoadSidePanelPositionAsync()).FreeMovement.Should().BeTrue();
    }

    [Fact]
    public async Task ChangedSettingsAreRememberedAfterRestart()
    {
        var settings = new ASPAssistant.Core.Data.SettingsManager(_dir);
        var appearance = await settings.LoadSidebarAppearanceAsync();
        appearance.FollowModeTheme = true;
        appearance.Theme = GameShopTheme.Ursus;
        appearance.Night[GameShopTheme.Closure] = true;
        await settings.SaveSidebarAppearanceAsync(appearance);
        await settings.SaveGameShopThemeAsync(GameShopTheme.Closure);
        await settings.SaveTrackMultipleLineupsAsync(true);
        await settings.SaveSidePanelPositionAsync(new SidePanelPositionSettings { FreeMovement = false });

        var again = new ASPAssistant.Core.Data.SettingsManager(_dir);
        var loaded = await again.LoadSidebarAppearanceAsync();
        loaded.FollowModeTheme.Should().BeTrue();
        loaded.Theme.Should().Be(GameShopTheme.Ursus);
        loaded.IsNight(GameShopTheme.Closure).Should().BeTrue();
        (await again.LoadGameShopThemeAsync()).Should().Be(GameShopTheme.Closure);
        (await again.LoadTrackMultipleLineupsAsync()).Should().BeTrue();
        (await again.LoadSidePanelPositionAsync()).FreeMovement.Should().BeFalse();
    }
}
