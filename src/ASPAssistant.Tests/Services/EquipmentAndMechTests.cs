using System.Text.Json;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

public class EquipmentAndMechTests
{
    [Fact]
    public void ClearingEmptyTransferAndReselectingSameEquipmentPreserveUserChoices()
    {
        var slot = new LineupSlot { OperatorName = "普通干员", Equipments = ["变形同构体"], CoreEquipments = ["变形同构体"] };
        EquipmentTestData.Rules.SetTransformation(slot, 0, "");
        slot.Equipments.Should().Equal("变形同构体");
        EquipmentTestData.Rules.SetTransformation(slot, 0, "炎国短刀");
        slot.CoreEquipments.Add("炎国短刀");
        EquipmentTestData.Rules.SetTransformation(slot, 0, "炎国短刀");
        slot.CoreEquipments.Should().Equal("变形同构体", "炎国短刀");
    }

    [Fact]
    public void ClearingUnresolvedLegacyPlanDoesNotRemoveActualOrManuallyTrackedEquipment()
    {
        var vm = new TrackingViewModel { TrackMultipleLineups = true };
        vm.AddTracking("炎国短刀", TrackingType.Equipment);
        vm.TrackLineup(new Lineup { Slots = [new() { OperatorName = "普通干员", TransformedCovenant = "助力", Equipments = ["变形同构体", "炎国短刀"] }] });
        vm.ClearPlannedTransformation("普通干员", "", EquipmentTestData.Rules);
        vm.TrackedOperators.Single().Equipments.Should().Equal("变形同构体", "炎国短刀");
        vm.TrackedOperators.Single().TransformedCovenant.Should().BeNull();
        vm.ClearPlannedTransformation("普通干员", "炎国短刀", EquipmentTestData.Rules);
        vm.TrackedEquipment.Should().ContainSingle().Which.Name.Should().Be("炎国短刀");
        vm.TrackedEquipment.Single().IsManual.Should().BeTrue();
    }

    private static readonly string[] FiveTransfers = ["炎国短刀", "维式重锤", "萨尔贡浓茶", "叙拉古正装", "乌萨斯呼吸器"];
    private static Operator Mech() => new() { Name = LineupEquipmentService.MechName, RequiredStrategyId = "band_mech", AdditionalCovenants = ["协防干员"] };
    private static CovenantCalculator Calculator() => new([Mech(), new() { Name = "普通干员" }],
        FiveTransfers.Select(e => new CovenantInfo { Name = EquipmentTestData.Rules.EquipmentToCovenant[e], BondType = "SEASON", ActivateCount = 3 })
            .Append(new() { Name = "协防干员", ActivateCount = 2 }),
        EquipmentTestData.Rules.EquipmentToCovenant.ToDictionary(p => p.Key, p => p.Value));

    [Theory]
    [InlineData(LineupPhase.前期)]
    [InlineData(LineupPhase.中期)]
    [InlineData(LineupPhase.后期)]
    [InlineData(LineupPhase.挂件)]
    public void MechRequiresStrategyInEveryPhaseAndRemovalPreservesItsConfiguration(LineupPhase phase)
    {
        var vm = new LineupEditorViewModel(null, [Mech()], [], Calculator());
        vm.SelectPhase(phase);
        vm.CanSelectOperator(Mech()).Should().BeFalse();
        var select = () => vm.SetSlotOperator(0, LineupEquipmentService.MechName);
        select.Should().Throw<InvalidOperationException>().WithMessage("*森蚺*");
        vm.SetStrategy(0, "band_mech");
        vm.SetSlotOperator(0, LineupEquipmentService.MechName);
        vm.SetSlotTransformation(0, 0, "炎国短刀", []);
        vm.ValidationError.Should().BeEmpty();
        vm.ClearStrategies();
        vm.ValidationError.Should().Contain("森蚺");
        vm.Slots[0].OperatorName.Should().Be(LineupEquipmentService.MechName);
        vm.Slots[0].Equipments.Should().Equal("变形同构体", "炎国短刀");
        vm.SetStrategy(0, "band_mech");
        vm.ValidationError.Should().BeEmpty();
    }

    [Fact]
    public void EliteMechAllowsFiveTransfersAndDowngradingNeverDiscardsGear()
    {
        var vm = new LineupEditorViewModel(null, [Mech()], [], Calculator());
        vm.SetStrategy(0, "band_mech");
        vm.SetSlotOperator(0, LineupEquipmentService.MechName);
        for (var i = 0; i < 3; i++) vm.SetSlotTransformation(0, i, FiveTransfers[i], []);
        vm.Slots[0].Equipments.Should().HaveCount(4);
        var fourth = () => vm.SetSlotTransformation(0, 3, FiveTransfers[3], []);
        fourth.Should().Throw<InvalidOperationException>();
        vm.Slots[0].Equipments.Should().HaveCount(4);
        vm.SetSlotElite(0, true);
        vm.SetSlotTransformation(0, 3, FiveTransfers[3], []);
        vm.SetSlotTransformation(0, 4, FiveTransfers[4], []);
        vm.Slots[0].Equipments.Should().HaveCount(6);
        vm.EquipmentRules.Transformations(vm.Slots[0]).Should().HaveCount(5);
        foreach (var name in FiveTransfers.Select(e => EquipmentTestData.Rules.EquipmentToCovenant[e]))
            vm.ActiveStats.Single(s => s.Name == name).Count.Should().Be(1);
        vm.SetSlotElite(0, false);
        vm.Slots[0].Equipments.Should().HaveCount(6);
        vm.ValidationError.Should().Contain("超过4件");
        vm.ClearSlotTransformation(0, 4);
        vm.ClearSlotTransformation(0, 3);
        vm.ValidationError.Should().BeEmpty();
        vm.Slots[0].Equipments.Should().HaveCount(4);
    }

    [Fact]
    public void AddingTransferRequiresExplicitEquipmentReplacementAndRemovingShapeshifterRemovesItsEffect()
    {
        var vm = new LineupEditorViewModel(null, [new() { Name = "普通干员" }], [], Calculator());
        vm.SetSlotOperator(0, "普通干员");
        vm.SetSlotEquipment(0, 0, "普通装备甲", true);
        vm.SetSlotEquipment(0, 1, "普通装备乙", false);
        var transfer = () => vm.SetSlotTransformation(0, 0, "炎国短刀", []);
        transfer.Should().Throw<InvalidOperationException>();
        vm.Slots[0].Equipments.Should().Equal("普通装备甲", "普通装备乙");
        vm.SetSlotTransformation(0, 0, "炎国短刀", ["普通装备甲", "普通装备乙"]);
        vm.Slots[0].CoreEquipments.Should().BeEmpty();
        vm.ActiveStats.Single(s => s.Name == "炎").Count.Should().Be(1);
        vm.SetSlotEquipment(0, 0, "", false);
        vm.EquipmentRules.Transformations(vm.Slots[0]).Should().BeEmpty();
        vm.ActiveStats.Should().NotContain(s => s.Name == "炎");
        vm.Slots[0].Equipments.Should().Contain("炎国短刀");
    }

    [Fact]
    public async Task LegacyLineupMigrationBacksUpOnceAndPreservesAllPhasesAndExistingEquipment()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lineup_upgrade_{Guid.NewGuid():N}");
        try
        {
            var store = new LineupStore(directory, EquipmentTestData.Rules);
            var original = new Lineup { Id = "old", Name = "原设备阵容",
                Slots = [new() { OperatorName = "普通干员", TransformedCovenant = "炎", Equipments = ["保留装备甲", "保留装备乙"], Tags = [LineupTag.阵容核心] }],
                EarlySlots = [new() { OperatorName = "前期干员" }], MidSlots = [new() { OperatorName = "中期干员" }],
                SupportSlots = [new() { OperatorName = "挂件干员" }], Strategies = [new() { StrategyId = "band_mech" }] };
            await store.SaveAsync(original);
            var path = Path.Combine(directory, "old.json");
            var before = await File.ReadAllTextAsync(path);
            var loaded = (await store.LoadAllAsync()).Single();
            loaded.Slots[0].Equipments.Should().BeEquivalentTo("保留装备甲", "保留装备乙", "变形同构体", "炎国短刀");
            loaded.Slots[0].Tags.Should().Equal(LineupTag.阵容核心);
            loaded.EarlySlots.Should().BeEquivalentTo(original.EarlySlots);
            loaded.MidSlots.Should().BeEquivalentTo(original.MidSlots);
            loaded.SupportSlots.Should().BeEquivalentTo(original.SupportSlots);
            loaded.Strategies.Should().BeEquivalentTo(original.Strategies);
            (await File.ReadAllTextAsync(Directory.GetFiles(directory, "*.bak").Single())).Should().Be(before);
            await store.LoadAllAsync();
            Directory.GetFiles(directory, "*.bak").Should().ContainSingle();
            new LineupEditorViewModel(loaded, [new() { Name = "普通干员" }], [], Calculator()).ValidationError.Should().Contain("超过2件");
        }
        finally { DeleteTemporary(directory); }
    }

    [Fact]
    public async Task LegacyTrackingMigrationPreservesManualTargetsAndSharedRequirements()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"tracking_upgrade_{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "tracking.json");
            var before = """[{"name":"甲","type":0,"transformedCovenant":"炎"},{"name":"乙","type":0,"transformedCovenant":"炎"},{"name":"手动装备","type":1}]""";
            await File.WriteAllTextAsync(path, before);
            var settings = new SettingsManager(directory);
            var loaded = await settings.LoadTrackingEntriesAsync(EquipmentTestData.Rules);
            (await File.ReadAllTextAsync(Directory.GetFiles(directory, "*.bak").Single())).Should().Be(before);
            var tracking = new TrackingViewModel();
            tracking.LoadEntries(loaded);
            tracking.ClearPlannedTransformation("甲", "炎国短刀", EquipmentTestData.Rules);
            tracking.TrackedEquipment.Select(e => e.Name).Should().BeEquivalentTo("手动装备", "变形同构体", "炎国短刀");
            tracking.RemoveTracking("乙");
            tracking.TrackedEquipment.Select(e => e.Name).Should().Equal("手动装备");
            await settings.SaveTrackingEntriesAsync(tracking.AllEntries.ToList());
            (await settings.LoadTrackingEntriesAsync(EquipmentTestData.Rules)).Select(e => e.Name).Should().BeEquivalentTo("甲", "手动装备");
            Directory.GetFiles(directory, "*.bak").Should().ContainSingle();
        }
        finally { DeleteTemporary(directory); }
    }

    [Fact]
    public async Task MechDataAndFiveTransfersRoundTripThroughStoreAndShare()
    {
        var data = new JsonDataStore(Path.Combine(AppContext.BaseDirectory, "TestData", "Regulation"));
        var operators = await data.LoadOperatorsAsync();
        var mech = operators.Single(o => o.Name == LineupEquipmentService.MechName);
        mech.RequiredStrategyId.Should().Be("band_mech");
        mech.Tier.Should().Be(5);
        mech.AdditionalCovenants.Should().Contain("协防干员");
        var original = new Lineup { Slots = [new() { OperatorName = mech.Name, IsElite = true,
            Equipments = ["变形同构体", .. FiveTransfers] }], Strategies = [new() { StrategyId = "band_mech" }] };
        var shared = await new LineupShareService(EquipmentTestData.Rules, Path.GetTempPath()).ImportAsync(JsonSerializer.Serialize(original));
        shared.Should().BeEquivalentTo(original);
        var vm = new TrackingViewModel();
        vm.TrackLineup(shared);
        vm.TrackedEquipment.Should().HaveCount(6);
        vm.TrackedOperators.Single().IsElite.Should().BeTrue();
        new LineupPlanner(operators, []).Plan(new PlannerRequest { Goals = [new("炎", 1, 1, false, true)],
            LockedOperators = new HashSet<string> { mech.Name } }).Error.Should().Contain(mech.Name);
    }

    private static void DeleteTemporary(string path)
    {
        Path.GetFullPath(path).StartsWith(Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar,
            StringComparison.OrdinalIgnoreCase).Should().BeTrue();
        Directory.Delete(path, true);
    }
}
