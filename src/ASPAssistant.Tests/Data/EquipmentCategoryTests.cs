// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
using ASPAssistant.Core.Data;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.Data;

/// <summary>
/// 装备功能分类（data/equipment_categories.json）与装备页「类型」筛选。
/// 分类是手工维护的：攻击/防御/生存/技力/经济/盟约/其他，一件装备可属多类。
/// </summary>
public class EquipmentCategoryTests
{
    private readonly JsonDataStore _store = new(Path.Combine(
        AppContext.BaseDirectory, "TestData", "Regulation"));

    [Fact]
    public async Task EveryEquipmentHasAtLeastOneCategory()
    {
        var (equipment, _) = await _store.LoadEquipmentAsync();

        equipment.Should().HaveCount(70);
        equipment.Should().OnlyContain(e => e.Categories.Count > 0);
        equipment.Should().OnlyContain(e => e.Categories.All(EquipmentBrowseViewModel.CategoryOrder.Contains));
    }

    [Fact]
    public async Task CategoryCountsMatchTheMaintainedMapping()
    {
        var (equipment, _) = await _store.LoadEquipmentAsync();

        int Count(string category) => equipment.Count(e => e.Categories.Contains(category));

        Count("攻击").Should().Be(34);
        Count("防御").Should().Be(6);
        Count("生存").Should().Be(17);
        Count("技力").Should().Be(4);
        Count("经济").Should().Be(5);
        Count("盟约").Should().Be(9);
        Count("其他").Should().Be(8);

        // 一件装备可属多类
        equipment.Where(e => e.Categories.Count > 1).Should().HaveCount(11);
        equipment.Single(e => e.Name == "乌萨斯呼吸器").Categories.Should().Equal("防御", "攻击");
        equipment.Single(e => e.Name == "盟约之币").Categories.Should().Equal("经济");
    }

    [Fact]
    public async Task CategoryFilterCombinesWithOtherFilters()
    {
        var operators = await _store.LoadOperatorsAsync();
        var (equipment, jobChangeNames) = await _store.LoadEquipmentAsync();
        var (_, mappings) = await _store.LoadCovenantsAsync();

        var viewModel = new EquipmentBrowseViewModel();
        viewModel.LoadEquipment(equipment,
            operators.SelectMany(o => o.CoreCovenants.Concat(o.AdditionalCovenants)),
            jobChangeNames, mappings);

        viewModel.AvailableCategories.Should().Equal("", "攻击", "防御", "生存", "技力", "经济", "盟约", "其他");

        viewModel.SelectedCategoryFilters = ["攻击"];
        viewModel.FilteredEquipment.Should().HaveCount(34);
        viewModel.ActiveFilterCount.Should().Be(1);

        // Multi-select: 攻击 + 经济 同时生效，任一命中即保留（跨类装备会同时出现）
        viewModel.SelectedCategoryFilters = ["攻击", "经济"];
        viewModel.FilteredEquipment.Should().HaveCount(39);
        viewModel.ActiveFilterCount.Should().Be(2);

        viewModel.SelectedCategoryFilters = ["攻击"];
        viewModel.SelectedTierFilters = [1];
        viewModel.FilteredEquipment.Should().HaveCount(2);
        viewModel.ActiveFilterCount.Should().Be(2);

        viewModel.SelectedCategoryFilters = ["技力"];
        viewModel.FilteredEquipment.Should().BeEmpty();

        viewModel.SelectedTierFilters = [];
        viewModel.SelectedCategoryFilters = [];
        viewModel.FilteredEquipment.Should().HaveCount(70);
        viewModel.ActiveFilterCount.Should().Be(0);
    }
}
