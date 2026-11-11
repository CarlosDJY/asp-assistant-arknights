// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
using ASPAssistant.Core.Data;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.ViewModels;

/// <summary>
/// 筛选增强：「同时选中」（行内 AND）、等阶多选、空列表提示、重置筛选。
/// 期望数字来自 data/operators.json、data/equipment.json 与 equipment_categories.json 的实际统计。
/// </summary>
public class FilterMatchAllTests
{
    private readonly JsonDataStore _store = new(Path.Combine(
        AppContext.BaseDirectory, "TestData", "Regulation"));

    private async Task<(OperatorBrowseViewModel Vm, List<ASPAssistant.Core.Models.Operator> Operators)>
        BuildOperatorVmAsync()
    {
        var operators = (await _store.LoadOperatorsAsync()).Where(o => o.RequiredStrategyId.Length == 0).ToList();
        var vm = new OperatorBrowseViewModel();
        vm.LoadOperators(operators);
        return (vm, operators);
    }

    private async Task<EquipmentBrowseViewModel> BuildEquipmentVmAsync()
    {
        var operators = await _store.LoadOperatorsAsync();
        var (equipment, jobChangeNames) = await _store.LoadEquipmentAsync();
        var (_, mappings) = await _store.LoadCovenantsAsync();
        var vm = new EquipmentBrowseViewModel();
        vm.LoadEquipment(equipment,
            operators.SelectMany(o => o.CoreCovenants.Concat(o.AdditionalCovenants)),
            jobChangeNames, mappings);
        return vm;
    }

    [Fact]
    public async Task CoreCovenantMatchAll_TurnsOrIntoAnd()
    {
        var (vm, _) = await BuildOperatorVmAsync();

        vm.SetCoreCovenantFilters(["维多利亚", "谢拉格"]);
        vm.FilteredOperators.Should().HaveCount(19); // OR

        vm.CoreCovenantMatchAll = true;
        vm.FilteredOperators.Should().ContainSingle().Which.Name.Should().Be("哈洛德"); // AND

        vm.CoreCovenantMatchAll = false;
        vm.FilteredOperators.Should().HaveCount(19);
    }

    [Fact]
    public async Task OtherCovenantMatchAll_TurnsOrIntoAnd()
    {
        var (vm, _) = await BuildOperatorVmAsync();

        vm.SetOtherCovenantFilters(["聚合", "远见"]);
        vm.FilteredOperators.Should().HaveCount(20); // OR

        vm.OtherCovenantMatchAll = true;
        vm.FilteredOperators.Should().ContainSingle().Which.Name.Should().Be("纯烬艾雅法拉"); // AND
    }

    [Fact]
    public async Task EquipmentCategoryMatchAll_RequiresEverySelectedCategory()
    {
        var vm = await BuildEquipmentVmAsync();

        vm.SelectedCategoryFilters = ["攻击", "生存"];
        vm.FilteredEquipment.Should().HaveCount(45); // OR

        vm.CategoryMatchAll = true;
        vm.FilteredEquipment.Select(e => e.Name).Should().BeEquivalentTo(
            ["坚固维式重锤", "汲取者之喙", "佣兵名牌", "不退者卫护系统", "汲取者之镰", "天马之耀"]);
    }

    [Fact]
    public async Task EmptyResultMessage_DependsOnMatchAllMode()
    {
        var (vm, _) = await BuildOperatorVmAsync();

        vm.SetCoreCovenantFilters(["维多利亚", "卡西米尔"]);
        vm.CoreCovenantMatchAll = true;
        vm.FilteredOperators.Should().BeEmpty();
        vm.EmptyListMessage.Should().Be("没有同时满足所选条件的干员");

        vm.CoreCovenantMatchAll = false;
        vm.FilteredOperators.Should().NotBeEmpty();
        vm.EmptyListMessage.Should().Be("没有符合条件的干员");
    }

    [Fact]
    public async Task TierFilter_SupportsMultipleSelections()
    {
        var (operatorVm, _) = await BuildOperatorVmAsync();

        operatorVm.SelectedTierFilters = [1];
        operatorVm.FilteredOperators.Should().HaveCount(19);
        operatorVm.SelectedTierFilters = [1, 2];
        operatorVm.FilteredOperators.Should().HaveCount(43);
        operatorVm.SelectedTierFilters = [1, 2, 3];
        operatorVm.FilteredOperators.Should().HaveCount(69);
        operatorVm.SelectedTierFilters = [];
        operatorVm.FilteredOperators.Should().HaveCount(141);

        var equipmentVm = await BuildEquipmentVmAsync();
        equipmentVm.SelectedTierFilters = [1];
        equipmentVm.FilteredEquipment.Should().HaveCount(7);
        equipmentVm.SelectedTierFilters = [1, 2];
        equipmentVm.FilteredEquipment.Should().HaveCount(17);
        equipmentVm.SelectedTierFilters = [];
        equipmentVm.FilteredEquipment.Should().HaveCount(70);
    }

    [Fact]
    public async Task ResetFilters_RestoresDefaults()
    {
        var (vm, operators) = await BuildOperatorVmAsync();
        vm.SetCoreCovenantFilters(["维多利亚"]);
        vm.SetOtherCovenantFilters(["聚合"]);
        vm.CoreCovenantMatchAll = true;
        vm.OtherCovenantMatchAll = true;
        vm.SelectedTierFilters = [3];
        vm.SelectedTraitTypeFilter = "t1";
        vm.SearchText = "哈洛德";
        vm.FilteredOperators.Should().NotHaveCount(operators.Count);

        vm.ResetFilters();

        vm.FilteredOperators.Should().HaveCount(operators.Count);
        vm.CoreCovenantMatchAll.Should().BeFalse();
        vm.OtherCovenantMatchAll.Should().BeFalse();
        vm.SelectedTierFilters.Should().BeEmpty();
        vm.SelectedTraitTypeFilter.Should().BeNull();
        vm.SearchText.Should().BeEmpty();
        vm.ActiveFilterTags.Should().BeEmpty();
        vm.ActiveFilterCount.Should().Be(0);
    }

    [Fact]
    public async Task EquipmentResetFilters_RestoresDefaults()
    {
        var vm = await BuildEquipmentVmAsync();
        vm.SelectedTierFilters = [2];
        vm.SelectedCategoryFilters = ["攻击", "生存"];
        vm.CategoryMatchAll = true;
        vm.SelectedTypeFilter = "转职";
        vm.FilteredEquipment.Should().NotHaveCount(70);

        vm.ResetFilters();

        vm.FilteredEquipment.Should().HaveCount(70);
        vm.SelectedTierFilters.Should().BeEmpty();
        vm.SelectedCategoryFilters.Should().BeEmpty();
        vm.SelectedTypeFilter.Should().BeEmpty();
        vm.CategoryMatchAll.Should().BeFalse();
        vm.ActiveFilterCount.Should().Be(0);
    }
}
