using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.Data;

public class RegulationDataTests
{
    private readonly JsonDataStore _store = new(Path.Combine(
        AppContext.BaseDirectory, "TestData", "Regulation"));

    [Fact]
    public async Task Operators_LoadRegulationCovenantsAndTraits()
    {
        var operators = await _store.LoadOperatorsAsync();

        operators.Should().HaveCount(142);
        operators.Count(o => o.RequiredStrategyId.Length == 0).Should().Be(141);
        operators.Single(o => o.Name == "“暴躁铁皮”").RequiredStrategyId.Should().Be("band_mech");
        operators.Should().OnlyHaveUniqueItems(o => o.Name);
        operators.Single(o => o.Name == "塔露拉").CoreCovenants.Should().Contain("乌萨斯");
        operators.Single(o => o.Name == "车尔尼").CoreCovenants.Should().Contain("莱塔尼亚");
        operators.Single(o => o.Name == "艾丽妮").CoreCovenants.Should().Contain("伊比利亚");
        operators.Should().OnlyContain(o => o.Normal.Traits.Count > 0 && o.Elite.Traits.Count > 0);
    }

    [Fact]
    public async Task Operators_LoadProfilesWithSkillsAndRoles()
    {
        // Strategy-only pieces such as “暴躁铁皮” have no recruitable profile or skill.
        var operators = (await _store.LoadOperatorsAsync()).Where(o => o.RequiredStrategyId.Length == 0).ToList();

        operators.Should().OnlyContain(o => o.Profile != null && o.Normal.Skill != null && o.Elite.Skill != null);
        var by = operators.ToDictionary(o => o.Name);
        by["折桠"].Profile!.IsTank.Should().BeTrue();
        by["折桠"].Profile!.Profession.Should().Be("重装");
        by["乌啾"].Profile!.IsHealer.Should().BeTrue();
        by["空弦"].Profile!.Stack!.Kind.Should().Be("adjacent");
        by["折光"].Profile!.Stack!.Covenants.Should().Equal("莱塔尼亚");
        by["维伊"].Profile!.Consumes.Should().Contain("乌萨斯");
        by["折桠"].Elite.Skill!.Description.Should().Contain("攻击力+120%");
        operators.Count(o => o.Profile!.IsTank).Should().BeGreaterThan(10);
        operators.Count(o => o.Profile!.IsHealer).Should().BeGreaterThan(10);
    }

    [Fact]
    public async Task Covenants_LoadHandMaintainedEffects()
    {
        var (covenants, _) = await _store.LoadCovenantsAsync();

        covenants.Should().HaveCount(26);
        covenants.Should().OnlyContain(c => c.Effects.Count > 0);
        var fire = covenants.Single(c => c.Name == "炎");
        fire.Effects[0].HasCondition.Should().BeFalse();
        fire.Effects[0].Text.Should().StartWith("【炎】干员攻击力");
        fire.Effects.Should().Contain(e => e.Condition == "在场6名不同的【炎】干员");
        covenants.Single(c => c.Name == "远见").Effects.Should().Contain(e => e.Text.Contains("整备区"));
    }

    [Fact]
    public async Task Equipment_NewHammerCanBeFoundByJobChangeFilter()
    {
        var operators = await _store.LoadOperatorsAsync();
        var (equipment, jobChangeNames) = await _store.LoadEquipmentAsync();
        var (_, mappings) = await _store.LoadCovenantsAsync();
        var viewModel = new EquipmentBrowseViewModel();
        viewModel.LoadEquipment(equipment,
            operators.SelectMany(o => o.CoreCovenants.Concat(o.AdditionalCovenants)), jobChangeNames, mappings);
        viewModel.SelectedTypeFilter = "转职";
        viewModel.SearchText = "电流维式重锤";

        equipment.Should().HaveCount(70);
        viewModel.FilteredEquipment.Should().ContainSingle(e => e.Name == "电流维式重锤");
        viewModel.FilteredEquipment.Single().Type.Should().Be(EquipmentType.JobChange);
        viewModel.FilteredEquipment.Single().JobChangeCovenant.Should().Be("维多利亚");
        equipment.Where(e => e.Type == EquipmentType.JobChange).Should().HaveCount(24)
            .And.OnlyContain(e => e.JobChangeCovenant.Length > 0);
        equipment.Single(e => e.Name == "坚守盾牌").JobChangeCovenant.Should().Be("坚守");
        EquipmentBrowseViewModel.AvailableTypes.Should().Equal("", "普通", "转职");
        viewModel.SearchText = "";
        viewModel.SelectedTypeFilter = "普通";
        viewModel.FilteredEquipment.Should().HaveCount(46);
        viewModel.SelectedTypeFilter = "转职";
        viewModel.FilteredEquipment.Should().HaveCount(24);
        viewModel.SelectedTypeFilter = "";
        viewModel.FilteredEquipment.Should().HaveCount(70);
        equipment.Should().OnlyContain(e => !e.Normal.EffectDescription.Contains("<$")
            && !e.Normal.EffectDescription.Contains("\\n"));
    }

    [Fact]
    public async Task Covenants_LoadAggregationThresholdAndEquipmentMappings()
    {
        var (covenants, equipmentMappings) = await _store.LoadCovenantsAsync();

        covenants.Should().HaveCount(26);
        covenants.Single(c => c.Name == "聚合").ActivateCount.Should().Be(2);
        covenants.Single(c => c.Name == "莱塔尼亚").BondType.Should().Be("SEASON");
        // 绝技 (bondId suntShip) has no operator but is listed on the ban screen.
        covenants.Single(c => c.Name == "绝技").BondType.Should().Be("REGULAR");
        equipmentMappings["电流维式重锤"].Should().Be("维多利亚");
        equipmentMappings["终极维式重锤"].Should().Be("维多利亚");
        equipmentMappings["炎国短刀"].Should().Be("炎");
    }
}
