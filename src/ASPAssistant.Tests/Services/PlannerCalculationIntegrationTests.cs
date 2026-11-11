using ASPAssistant.Core.Models;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

public class PlannerCalculationIntegrationTests
{
    [Fact]
    public async Task RealOperatorDataCountsHarmonyOnceAndAddsItToPresentCore()
    {
        var data = new JsonDataStore(Path.Combine(AppContext.BaseDirectory, "TestData", "Regulation"));
        var operators = await data.LoadOperatorsAsync();
        var (covenants, mappings) = await data.LoadCovenantsAsync();
        var natives = operators.Where(o => o.CoreCovenants.Contains("炎")).Take(2).Select(o => o.Name).ToList();
        natives.Should().HaveCount(2);
        operators.Single(o => o.Name == "魔王").AdditionalCovenants.Should().Contain("调和");
        var planned = new LineupPlanner(operators, covenants).Solve(new PlannerRequest
        {
            Goals = [new("炎", 1, 3, false, true), new("调和", 1, 1, false, false)],
            // Exactly the three locked operators: free slots would otherwise be filled for the additional score.
            LockedOperators = natives.Append("魔王").ToHashSet(), PeopleOptions = [3], TransformOptions = [0]
        }).Single();
        planned.Counts["炎"].Should().Be(3);
        planned.Counts["调和"].Should().Be(1);
        planned.Operators.Single(o => o.Name == "魔王").IsHarmony.Should().BeTrue();
        var editor = new LineupEditorViewModel(new PlannerLineupItem { Source = planned }.ToLineup(_calculator.EquipmentRules), operators, [],
            new CovenantCalculator(operators, covenants, mappings));
        editor.ActiveStats.Single(s => s.Name == "炎").Count.Should().Be(3);
        editor.ActiveStats.Single(s => s.Name == "调和").Count.Should().Be(1);
    }

    [Fact]
    public async Task PlannedTransformationSurvivesSaveShareAndTrackingAndCanBeCancelledIndependently()
    {
        var planner = new LineupPlanner(Operators, Covenants);
        var planned = planner.Solve(new PlannerRequest
        {
            Goals = [new("炎", 1, 1, false, true)],
            LockedOperators = new HashSet<string> { "甲", "魔王" },
            TransformAllowed = new HashSet<string> { "炎" }, PeopleOptions = [2], TransformOptions = [1]
        }).Single();
        var directory = Path.Combine(Path.GetTempPath(), $"planner_transform_{Guid.NewGuid():N}");
        try
        {
            var store = new LineupStore(directory, _calculator.EquipmentRules);
            await store.SaveAsync(new PlannerLineupItem { Source = planned }.ToLineup(_calculator.EquipmentRules));
            var loaded = (await store.LoadAllAsync()).Single();
            var imported = await new LineupShareService(_calculator.EquipmentRules, Path.Combine(directory, "share-links")).ImportAsync(System.Text.Json.JsonSerializer.Serialize(loaded));
            var editor = new LineupEditorViewModel(imported, Operators, [], _calculator);
            editor.ActiveStats.Single(s => s.Name == "炎").Count.Should().Be(planned.Counts["炎"]);

            var tracking = new TrackingViewModel();
            tracking.TrackLineup(editor.ToLineup());
            var settings = new SettingsManager(directory);
            await settings.SaveTrackingEntriesAsync(tracking.AllEntries.ToList());
            tracking.LoadEntries(await settings.LoadTrackingEntriesAsync(_calculator.EquipmentRules));
            tracking.TrackedOperators.Single(e => e.Name == "甲").Equipments.Should().Contain("炎转职");
            tracking.ClearPlannedTransformation("甲", "炎转职", _calculator.EquipmentRules);
            tracking.TrackedOperators.Single(e => e.Name == "甲").Equipments.Should().BeEmpty();
            editor.Slots[0].Equipments.Should().Contain("炎转职");

            editor.ClearSlotTransformation(0, 0);
            editor.ActiveStats.Should().NotContain(s => s.Name == "炎");
            await store.SaveAsync(editor.ToLineup());
            (await store.LoadAllAsync()).Single().Slots[0].Equipments.Should().BeEmpty();
            editor.Reset();
            editor.Slots[0].Equipments.Should().Contain("炎转职");
            imported.Slots[0].Equipments.Should().Contain("炎转职");
        }
        finally
        {
            Path.GetFullPath(directory).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase).Should().BeTrue();
            Directory.Delete(directory, true);
        }
    }

    private static readonly Operator[] Operators =
    [
        new() { Name = "甲", CoreCovenants = ["乌萨斯"], AdditionalCovenants = ["精准"] },
        new() { Name = "乙", CoreCovenants = ["炎"] },
        new() { Name = "魔王", AdditionalCovenants = ["调和"] },
    ];
    private static readonly CovenantInfo[] Covenants =
    [
        new() { Name = "炎", BondType = "SEASON", ActivateCount = 3 },
        new() { Name = "乌萨斯", BondType = "SEASON", ActivateCount = 3 },
        new() { Name = "精准", BondType = "REGULAR", ActivateCount = 2 },
        new() { Name = "调和", BondType = "REGULAR", ActivateCount = 1 },
    ];
    private readonly CovenantCalculator _calculator = new(Operators, Covenants,
        new Dictionary<string, string> { ["炎转职"] = "炎", ["精准转职"] = "精准" });

    [Fact]
    public void TransformedCoreReceivesHarmonyBonus()
    {
        var lineup = new Lineup { Slots =
        [
            new() { OperatorName = "甲", TransformedCovenant = "炎" },
            new() { OperatorName = "魔王" },
        ] };
        _calculator.EquipmentRules.Upgrade(lineup);
        _calculator.Compute(lineup).Single(c => c.Name == "炎").Count.Should().Be(2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NativeOrEquipmentCovenantIsNotCountedAgainByPlannerMetadata(bool fromEquipment)
    {
        var lineup = new Lineup { Slots = [new()
        {
            OperatorName = fromEquipment ? "甲" : "乙",
            TransformedCovenant = "炎",
            Equipments = fromEquipment ? [CovenantCalculator.ShapeshifterEquipment, "炎转职"] : [],
        }] };
        _calculator.EquipmentRules.Upgrade(lineup);
        _calculator.Compute(lineup).Single(c => c.Name == "炎").Count.Should().Be(1);
    }

    [Fact]
    public void NativeAdditionalCovenantIsNotCountedAgainByPlannerMetadata()
    {
        var lineup = new Lineup { Slots = [new() { OperatorName = "甲", TransformedCovenant = "精准" }] };
        _calculator.EquipmentRules.Upgrade(lineup);
        _calculator.Compute(lineup).Single(c => c.Name == "精准").Count.Should().Be(1);
    }

    [Fact]
    public void PlannerAndSavedLineupAgreeWhenTransformationIntroducesCoreForHarmony()
    {
        var planner = new LineupPlanner(Operators, Covenants);
        var result = planner.Solve(new PlannerRequest
        {
            Goals = [new("炎", 1, 1, false, true)],
            LockedOperators = new HashSet<string> { "甲", "魔王" },
            TransformAllowed = new HashSet<string> { "炎" },
            PeopleOptions = [2], TransformOptions = [1],
        }).Should().ContainSingle().Subject;
        result.Counts["炎"].Should().Be(2);
        var saved = new PlannerLineupItem { Source = result }.ToLineup(_calculator.EquipmentRules);
        _calculator.Compute(saved).Single(c => c.Name == "炎").Count.Should().Be(result.Counts["炎"]);
        var editor = new LineupEditorViewModel(saved, Operators, [], _calculator);
        editor.ToLineup().Slots.Single(s => s.OperatorName == "甲").Equipments.Should().Contain("炎转职");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HarmonyAndTwoTransformsCanReachThreeCoreMembersWithoutNativeHolder(bool locked)
    {
        Operator[] operators = [Operators[0], new() { Name = "丙", CoreCovenants = ["乌萨斯"] }, Operators[2]];
        var planner = new LineupPlanner(operators, Covenants);
        var results = planner.Solve(new PlannerRequest
        {
            Goals = [new("炎", 1, 3, false, true)],
            LockedOperators = locked ? new HashSet<string> { "甲", "丙", "魔王" } : new HashSet<string>(),
            TransformAllowed = new HashSet<string> { "炎" },
            PeopleOptions = [8], TransformOptions = [2],
        });
        results.Should().NotBeEmpty();
        var calculator = new CovenantCalculator(operators, Covenants, _calculator.EquipmentRules.EquipmentToCovenant.ToDictionary(p => p.Key, p => p.Value));
        foreach (var result in results)
        {
            result.Counts["炎"].Should().BeGreaterThanOrEqualTo(3);
            calculator.Compute(new PlannerLineupItem { Source = result }.ToLineup(_calculator.EquipmentRules))
                .Single(c => c.Name == "炎").Count.Should().Be(result.Counts["炎"]);
        }
    }
}
