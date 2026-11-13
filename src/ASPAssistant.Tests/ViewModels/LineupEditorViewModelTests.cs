// Modified for ASP Assistant Arknights on 2026-11-10; see NOTICE.
using System.Net.Http;
using System.Text.Json;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.ViewModels;

public class LineupEditorViewModelTests
{
    private static Lineup Source() => new()
    {
        Id = "editor-test",
        Name = "打开时的阵容",
        Slots = [new()
        {
            OperatorName = "角峰", Equipments = ["维式重锤", "坚守盾牌"],
            CoreEquipments = ["维式重锤"], Tags = [LineupTag.输出, LineupTag.经济]
        }]
    };

    private static LineupEditorViewModel Create(Lineup? source)
    {
        Operator[] operators = [new() { Name = "角峰", CoreCovenants = ["乌萨斯"] }];
        return new LineupEditorViewModel(source, operators, [],
            new CovenantCalculator(operators, [new() { Name = "乌萨斯", BondType = "SEASON", ActivateCount = 1 }],
                new Dictionary<string, string>()));
    }

    [Fact]
    public void Reset_RestoresOpeningSnapshotRepeatedlyWithoutChangingSource()
    {
        var source = Source();
        var vm = Create(source);
        vm.Name = "修改后";
        vm.SetSlotEquipment(0, 0, "盟约之币", false);
        vm.ToggleSlotTag(0, LineupTag.阵容核心);
        vm.SetSlotOperator(1, "角峰");
        vm.Reset();
        vm.Name.Should().Be(source.Name);
        vm.LineupId.Should().Be(source.Id);
        vm.ToLineup().Slots.Should().BeEquivalentTo(source.Slots);
        vm.SetSlotEquipment(0, 0, "", false);
        vm.Reset();
        vm.Slots[0].CoreEquipments.Should().Equal("维式重锤");
        source.Slots[0].Equipments.Should().Equal("维式重锤", "坚守盾牌");
        vm.Slots.Should().HaveCount(9);
    }

    [Fact]
    public void Clear_EmptiesEditorWithoutDeletingSourceAndCanBeReset()
    {
        var source = Source();
        var vm = Create(source);
        vm.Clear();
        vm.Name.Should().Be(source.Name);
        vm.LineupId.Should().Be(source.Id);
        vm.Slots.Should().HaveCount(9).And.OnlyContain(s => s.OperatorName == "" && s.Equipments.Count == 0
            && s.CoreEquipments.Count == 0 && s.Tags.Count == 0);
        vm.FilledSlotCount.Should().Be(0);
        vm.ActivatedCount.Should().Be(0);
        source.Slots.Should().HaveCount(1);
        vm.Reset();
        vm.ToLineup().Slots.Should().BeEquivalentTo(source.Slots);
    }

    [Fact]
    public void NewEditor_ResetReturnsToNewLineupState()
    {
        var vm = Create(null);
        vm.Name = "临时名字";
        vm.SetSlotOperator(0, "角峰");
        vm.Reset();
        vm.Name.Should().Be("新阵容");
        vm.FilledSlotCount.Should().Be(0);
        vm.Slots.Should().HaveCount(9);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void SameEquipment_CanChangeCoreFlagWithoutChangingSelection(bool isCore)
    {
        var vm = Create(Source());
        vm.SetSlotEquipment(0, 0, "维式重锤", isCore);
        vm.Slots[0].Equipments.Should().Equal("维式重锤", "坚守盾牌");
        vm.Slots[0].CoreEquipments.Contains("维式重锤").Should().Be(isCore);
    }

    [Fact]
    public void ReplacingCoreEquipment_RemovesItsOldMark()
    {
        var vm = Create(Source());
        vm.SetSlotEquipment(0, 0, "盟约之币", false);
        vm.Slots[0].Equipments.Should().Equal("盟约之币", "坚守盾牌");
        vm.Slots[0].CoreEquipments.Should().BeEmpty();
    }

    [Fact]
    public void ClearingEquipment_PreservesOtherCoreEquipmentAndTags()
    {
        var source = Source();
        source.Slots[0].CoreEquipments.Add("坚守盾牌");
        var vm = Create(source);
        vm.SetSlotEquipment(0, 0, "", false);
        vm.Slots[0].Equipments.Should().Equal("坚守盾牌");
        vm.Slots[0].CoreEquipments.Should().Equal("坚守盾牌");
        vm.Slots[0].Tags.Should().Equal(source.Slots[0].Tags);
    }

    [Theory]
    [InlineData(0, "坚守盾牌")]
    [InlineData(1, "维式重锤")]
    public void SelectingAlreadyEquippedItem_DoesNotKeepTheReplacedItem(int index, string name)
    {
        var vm = Create(Source());
        vm.SetSlotEquipment(0, index, name, true);
        vm.Slots[0].Equipments.Should().Equal(name);
        vm.Slots[0].CoreEquipments.Should().Equal(name);
    }

    [Fact]
    public void ToggleEquipment_RemovesStaleCoreMark()
    {
        var vm = Create(Source());
        vm.ToggleSlotEquipment(0, "维式重锤");
        vm.Slots[0].CoreEquipments.Should().BeEmpty();
    }

    [Fact]
    public void CoreTag_UsesExistingTagLimitAndDoesNotChangeCovenantCalculation()
    {
        var vm = Create(Source());
        var previous = vm.ActiveStats.ToList();
        vm.ToggleSlotTag(0, LineupTag.阵容核心);
        vm.Slots[0].Tags.Should().Equal(LineupTag.经济, LineupTag.阵容核心);
        vm.SetSlotEquipment(0, 0, "维式重锤", false);
        vm.ActiveStats.Should().BeEquivalentTo(previous);
        vm.ToggleSlotTag(0, LineupTag.阵容核心);
        vm.Slots[0].Tags.Should().Equal(LineupTag.经济);
    }

    [Fact]
    public void ExportedLineup_IsIndependentOfEditorAndSnapshot()
    {
        var vm = Create(Source());
        var result = vm.ToLineup();
        result.Slots[0].CoreEquipments.Clear();
        result.Slots[0].Tags.Clear();
        vm.Slots[0].CoreEquipments.Should().Equal("维式重锤");
        vm.Reset();
        vm.Slots[0].Tags.Should().HaveCount(2);
    }

    [Fact]
    public async Task LocalSaveAndJsonImport_PreserveCoreEquipmentAndTag()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lineup_core_{Guid.NewGuid():N}");
        try
        {
            var vm = Create(Source());
            vm.ToggleSlotTag(0, LineupTag.阵容核心);
            var store = new LineupStore(directory, EquipmentTestData.Rules);
            await store.SaveAsync(vm.ToLineup());
            var loaded = (await store.LoadAllAsync()).Single();
            loaded.Slots[0].CoreEquipments.Should().Equal("维式重锤");
            loaded.Slots[0].Tags.Should().Contain(LineupTag.阵容核心);
            var imported = await new LineupShareService(EquipmentTestData.Rules, Path.Combine(directory, "share-links")).ImportAsync(JsonSerializer.Serialize(loaded));
            imported.Slots.Should().BeEquivalentTo(loaded.Slots);
        }
        finally
        {
            Path.GetFullPath(directory).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                .Should().BeTrue();
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task OldJson_KeepsTagNumbersAndDefaultsToNoCoreEquipment()
    {
        var imported = await new LineupShareService(EquipmentTestData.Rules, Path.GetTempPath()).ImportAsync(
            """{"id":"old","name":"旧阵容","slots":[{"operatorName":"角峰","equipments":["维式重锤"],"tags":[0,3]}]}""");
        imported.Slots[0].CoreEquipments.Should().BeEmpty();
        imported.EarlySlots.Should().BeEmpty();
        imported.MidSlots.Should().BeEmpty();
        imported.SupportSlots.Should().BeEmpty();
        imported.Strategies.Should().BeEmpty();
        imported.Slots[0].Tags.Should().Equal(LineupTag.叠层, LineupTag.经济);
        ((int)LineupTag.阵容核心).Should().Be(4);
        Create(imported).ToLineup().Slots.Should().BeEquivalentTo(imported.Slots);
    }

    [Fact]
    public void NewEditor_StartsInEarlyPhaseAndExportsLatePhaseForPreview()
    {
        var vm = Create(null);
        vm.CurrentPhase.Should().Be(LineupPhase.前期);
        vm.SetSlotOperator(0, "前期干员");
        vm.ToLineup().EarlySlots.Single().OperatorName.Should().Be("前期干员");
        vm.ToLineup().Slots.Should().BeEmpty();
        vm.SelectPhase(LineupPhase.后期);
        vm.SetSlotOperator(0, "角峰");
        vm.SelectPhase(LineupPhase.挂件);
        var exported = vm.ToLineup();
        exported.EarlySlots.Single().OperatorName.Should().Be("前期干员");
        exported.Slots.Single().OperatorName.Should().Be("角峰");
        vm.Reset();
        vm.CurrentPhase.Should().Be(LineupPhase.前期);
        vm.ToLineup().EarlySlots.Should().BeEmpty();
        vm.ToLineup().Slots.Should().BeEmpty();
    }

    [Fact]
    public void LatePhaseCountsSupportOperatorsForBenchCovenants()
    {
        Operator[] operators =
        [
            new() { Name = "锡人", CoreCovenants = ["炎"], AdditionalCovenants = ["投资人"] },
            new() { Name = "山", CoreCovenants = ["炎"], AdditionalCovenants = ["投资人"] },
        ];
        var vm = new LineupEditorViewModel(new Lineup
        {
            Slots = [new() { OperatorName = "锡人" }], SupportSlots = [new() { OperatorName = "山" }],
            TargetCovenants = [new() { Name = "投资人", Tier = 1 }],
        }, operators, [], new CovenantCalculator(operators,
            [new() { Name = "炎", BondType = "SEASON", ActivateCount = 2 }, new() { Name = "投资人", BondType = "REGULAR", ActivateCount = 2 }],
            new Dictionary<string, string>()));

        vm.ComputeLate()["投资人"].Should().Be(2);
        vm.ComputeLate()["炎"].Should().Be(1, "挂件 only count for the covenants that 整备区 also activates");
        vm.TargetStates.Single().Reached.Should().BeTrue();
        vm.ActivatedCount.Should().Be(1);
    }

    [Fact]
    public void Phases_KeepIndependentSlotsEquipmentTagsAndCovenantCounts()
    {
        var vm = Create(Source());
        vm.CurrentPhase.Should().Be(LineupPhase.后期);
        vm.ActivatedCount.Should().Be(1);
        foreach (var phase in new[] { LineupPhase.前期, LineupPhase.中期, LineupPhase.挂件 })
        {
            vm.SelectPhase(phase);
            vm.FilledSlotCount.Should().Be(0);
            vm.ActivatedCount.Should().Be(0);
            vm.SetSlotOperator(0, phase.ToString());
            vm.SetSlotEquipment(0, 0, $"{phase}装备", true);
            vm.ToggleSlotTag(0, LineupTag.阵容核心);
        }
        foreach (var phase in new[] { LineupPhase.前期, LineupPhase.中期, LineupPhase.挂件 })
        {
            vm.SelectPhase(phase);
            vm.Slots[0].OperatorName.Should().Be(phase.ToString());
            vm.Slots[0].CoreEquipments.Should().Equal($"{phase}装备");
            vm.Slots[0].Tags.Should().Equal(LineupTag.阵容核心);
        }
        vm.SelectPhase(LineupPhase.后期);
        vm.Slots[0].OperatorName.Should().Be("角峰");
        vm.ActivatedCount.Should().Be(1);
    }

    [Fact]
    public void Clear_OnlyClearsCurrentPhaseAndResetRestoresAllOpeningPhases()
    {
        var source = Source();
        source.EarlySlots = [new() { OperatorName = "前期干员", Equipments = ["前期装备"], CoreEquipments = ["前期装备"] }];
        source.MidSlots = [new() { OperatorName = "中期干员" }];
        source.SupportSlots = [new() { OperatorName = "挂件干员", Tags = [LineupTag.阵容核心] }];
        var vm = Create(source);
        vm.SelectPhase(LineupPhase.前期);
        vm.Clear();
        var cleared = vm.ToLineup();
        cleared.EarlySlots.Should().BeEmpty();
        cleared.Slots.Should().BeEquivalentTo(source.Slots);
        cleared.MidSlots.Should().BeEquivalentTo(source.MidSlots);
        cleared.SupportSlots.Should().BeEquivalentTo(source.SupportSlots);
        cleared.Name.Should().Be(source.Name);
        vm.SelectPhase(LineupPhase.挂件);
        vm.Clear();
        vm.Name = "改名";
        vm.Reset();
        vm.CurrentPhase.Should().Be(LineupPhase.后期);
        var reset = vm.ToLineup();
        reset.EarlySlots.Should().BeEquivalentTo(source.EarlySlots);
        reset.MidSlots.Should().BeEquivalentTo(source.MidSlots);
        reset.SupportSlots.Should().BeEquivalentTo(source.SupportSlots);
        reset.Name.Should().Be(source.Name);
        vm.SelectPhase(LineupPhase.前期);
        vm.SetSlotEquipment(0, 0, "临时", false);
        vm.Reset();
        vm.ToLineup().EarlySlots[0].CoreEquipments.Should().Equal("前期装备");
        source.EarlySlots[0].CoreEquipments.Should().Equal("前期装备");
    }

    [Fact]
    public async Task FourPhases_RoundTripLocalStoreShareExportAndImport()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lineup_phases_{Guid.NewGuid():N}");
        try
        {
            var vm = Create(Source());
            foreach (var phase in new[] { LineupPhase.前期, LineupPhase.中期, LineupPhase.挂件 })
            {
                vm.SelectPhase(phase);
                vm.SetSlotOperator(0, phase.ToString());
                vm.SetSlotEquipment(0, 0, phase.ToString(), true);
                vm.ToggleSlotTag(0, LineupTag.阵容核心);
            }
            var expected = vm.ToLineup();
            var store = new LineupStore(directory, EquipmentTestData.Rules);
            await store.SaveAsync(expected);
            var loaded = (await store.LoadAllAsync()).Single();
            loaded.Should().BeEquivalentTo(expected);
            var handler = new ShareCaptureHandler();
            var service = new LineupShareService(EquipmentTestData.Rules, Path.Combine(directory, "share-links"), new HttpClient(handler));
            (await service.ExportAsync(loaded)).Should().Be("https://dpaste.com/TEST123");
            var imported = await service.ImportAsync(handler.Json);
            imported.Should().BeEquivalentTo(expected);
            var reopened = Create(imported);
            reopened.CurrentPhase.Should().Be(LineupPhase.后期);
            reopened.ToLineup().EarlySlots.Should().BeEquivalentTo(expected.EarlySlots);
            var exported = reopened.ToLineup();
            exported.EarlySlots[0].CoreEquipments.Clear();
            reopened.ToLineup().EarlySlots[0].CoreEquipments.Should().HaveCount(1);
            var tracking = new TrackingViewModel();
            tracking.TrackLineup(imported);
            tracking.TrackedOperators.Select(entry => entry.Name).Should().BeEquivalentTo("角峰", "前期", "中期", "挂件");
            tracking.TrackedEquipment.Select(entry => entry.Name).Should().BeEquivalentTo("维式重锤", "坚守盾牌", "前期", "中期", "挂件");
        }
        finally
        {
            Path.GetFullPath(directory).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase).Should().BeTrue();
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Strategies_EditClearResetAndExportWithoutChangingOperatorPhases()
    {
        var source = Source();
        source.Strategies = [new() { StrategyId = "band_copy", Tags = [StrategyTag.阵容核心] }];
        var vm = Create(source);
        var stats = vm.ActiveStats.ToList();
        vm.StrategySlots.Should().HaveCount(9);
        vm.SetStrategy(0, "band_amiya");
        vm.ToggleStrategyTag(0, StrategyTag.优选);
        vm.SetStrategy(1, "band_upgrade");
        vm.ToggleStrategyTag(1, StrategyTag.次选);
        vm.ToggleStrategyTag(1, StrategyTag.次选);
        vm.StrategySlots[1].Tags.Should().BeEmpty();
        vm.ActiveStats.Should().BeEquivalentTo(stats);
        vm.ToLineup().Slots.Should().BeEquivalentTo(source.Slots);
        source.Strategies[0].StrategyId.Should().Be("band_copy");
        source.Strategies[0].Tags.Should().Equal(StrategyTag.阵容核心);
        var exported = vm.ToLineup();
        exported.Strategies.Should().HaveCount(2);
        exported.Strategies[0].Tags.Clear();
        vm.StrategySlots[0].Tags.Should().Equal(StrategyTag.阵容核心, StrategyTag.优选);
        vm.Clear();
        vm.ToLineup().Strategies.Should().HaveCount(2);
        vm.Reset();
        vm.ToLineup().Strategies.Should().BeEquivalentTo(source.Strategies);
        vm.ClearStrategies();
        vm.ToLineup().Strategies.Should().BeEmpty();
        vm.ToLineup().Slots.Should().BeEquivalentTo(source.Slots);
        vm.Reset();
        vm.SetStrategy(0, "");
        vm.StrategySlots[0].Tags.Should().BeEmpty();
        vm.Reset();
        vm.ToLineup().Strategies.Should().BeEquivalentTo(source.Strategies);
    }

    [Fact]
    public async Task Strategies_RoundTripStoreShareAndReopenWithoutEnteringTracking()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"lineup_strategies_{Guid.NewGuid():N}");
        try
        {
            var vm = Create(Source());
            vm.SetStrategy(0, "band_amiya");
            vm.ToggleStrategyTag(0, StrategyTag.阵容核心);
            vm.SetStrategy(1, "band_upgrade");
            vm.ToggleStrategyTag(1, StrategyTag.优选);
            vm.SetStrategy(2, "unknown-shared-strategy");
            vm.ToggleStrategyTag(2, StrategyTag.次选);
            var store = new LineupStore(directory, EquipmentTestData.Rules);
            await store.SaveAsync(vm.ToLineup());
            var loaded = (await store.LoadAllAsync()).Single();
            var handler = new ShareCaptureHandler();
            var share = new LineupShareService(EquipmentTestData.Rules, Path.Combine(directory, "share-links"), new HttpClient(handler));
            await share.ExportAsync(loaded);
            var imported = await share.ImportAsync(handler.Json);
            var reopened = Create(imported);
            reopened.ToLineup().Strategies.Should().BeEquivalentTo(loaded.Strategies);
            reopened.Reset();
            reopened.ToLineup().Strategies.Should().BeEquivalentTo(loaded.Strategies);
            var tracking = new TrackingViewModel();
            tracking.TrackLineup(imported);
            tracking.TrackedOperators.Select(s => s.Name).Should().Equal("角峰");
            tracking.TrackedEquipment.Select(s => s.Name).Should().BeEquivalentTo("维式重锤", "坚守盾牌");
        }
        finally
        {
            Path.GetFullPath(directory).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase)
                .Should().BeTrue();
            Directory.Delete(directory, true);
        }
    }

    private sealed class ShareCaptureHandler : HttpMessageHandler
    {
        public string Json { get; private set; } = "";

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var form = await request.Content!.ReadAsStringAsync(cancellationToken);
            var content = form.Split('&').Single(field => field.StartsWith("content="));
            Json = Uri.UnescapeDataString(content["content=".Length..].Replace('+', ' '));
            return new HttpResponseMessage(System.Net.HttpStatusCode.OK) { Content = new StringContent("https://dpaste.com/TEST123") };
        }
    }
}
