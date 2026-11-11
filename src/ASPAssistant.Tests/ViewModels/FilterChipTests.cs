// Modified for ASP Assistant Arknights on 2026-11-12; see NOTICE.
using ASPAssistant.Core.Models;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.ViewModels;

/// <summary>Filter card chips: toggling, removable tags, counts / ban state, in-place list updates.</summary>
public class FilterChipTests
{
    private static List<Operator> Ops =>
    [
        new() { Name = "隐现", Tier = 6, CoreCovenants = ["拉特兰"], AdditionalCovenants = ["迅捷"],
                Normal = new() { Traits = [new() { TraitType = "作战能力", TraitDescription = "<战斗中>攻速+1" }] } },
        new() { Name = "角峰", Tier = 4, CoreCovenants = ["谢拉格"], AdditionalCovenants = ["坚守"],
                Normal = new() { Traits = [new() { TraitType = "单次叠加", TraitDescription = "<部署时>层数+2" }] } },
        new() { Name = "惊蛰", Tier = 6, CoreCovenants = ["炎"], AdditionalCovenants = ["迅捷"],
                Normal = new() { Traits = [new() { TraitType = "单次叠加", TraitDescription = "炎层数" }] } }
    ];

    [Fact]
    public void Chips_HaveNoAllEntry_AndFilterWhenToggled()
    {
        var vm = new OperatorBrowseViewModel();
        vm.LoadOperators(Ops);
        vm.TierChips.Select(c => c.Label).Should().Equal("Ⅳ", "Ⅵ");
        vm.CoreChips.Select(c => c.Value).Should().NotContain("");

        vm.TierChips.Single(c => c.Label == "Ⅵ").IsSelected = true;
        vm.FilteredOperators.Select(o => o.Name).Should().BeEquivalentTo(["隐现", "惊蛰"]);
        vm.SelectedTierFilters.Should().Equal(6);
        vm.ActiveChips.Single().TagText.Should().Be("Ⅵ 阶");
        vm.ChipFilterCount.Should().Be(1);

        vm.OtherChips.Single(c => c.Value == "迅捷").IsSelected = true;
        vm.CoreChips.Single(c => c.Value == "炎").IsSelected = true;
        vm.FilteredOperators.Single().Name.Should().Be("惊蛰");
        vm.MatchedCount.Should().Be(1);

        vm.RemoveChip(vm.ActiveChips.First(c => c.Kind == FilterChipKind.CoreCovenant));
        vm.FilteredOperators.Should().HaveCount(2);
    }

    [Fact]
    public void TraitChips_AreSingleChoice_AndResetClearsChips()
    {
        var vm = new OperatorBrowseViewModel();
        vm.LoadOperators(Ops);
        vm.TraitChips.Single(c => c.Value == "作战能力").IsSelected = true;
        vm.TraitChips.Single(c => c.Value == "单次叠加").IsSelected = true;
        vm.TraitChips.Where(c => c.IsSelected).Select(c => c.Value).Should().Equal("单次叠加");
        vm.FilteredOperators.Should().HaveCount(2);
        vm.TimingChips.Select(c => c.Value).Should().BeEquivalentTo(["战斗中", "部署时"]);

        vm.ResetFilters();
        vm.ActiveChips.Should().BeEmpty();
        vm.FilteredOperators.Should().HaveCount(3);
    }

    [Fact]
    public void ExternalFilterChanges_AreMirroredOnChips()
    {
        var vm = new OperatorBrowseViewModel();
        vm.LoadOperators(Ops);
        vm.SetCoreCovenantFilters(["炎"]);
        vm.CoreChips.Single(c => c.Value == "炎").IsSelected.Should().BeTrue();
        vm.SelectedTierFilters = [4];
        vm.TierChips.Single(c => c.Label == "Ⅳ").IsSelected.Should().BeTrue();
    }

    [Fact]
    public void CovenantChips_CountUsableHolders_AndShowBans()
    {
        var banned = new HashSet<string> { "惊蛰" };
        var vm = new OperatorBrowseViewModel();
        vm.LoadOperators(Ops);
        vm.SetBanChecker(banned.Contains);
        vm.SetCovenantBanChecker(c => c is "炎" or "迅捷");
        vm.CoreChips.Single(c => c.Value == "炎").IsBlocked.Should().BeTrue();
        vm.CoreChips.Single(c => c.Value == "炎").CountText.Should().BeEmpty();
        var swift = vm.OtherChips.Single(c => c.Value == "迅捷");
        swift.IsReduced.Should().BeTrue();
        swift.CountText.Should().Be("剩 1");
        vm.CoreChips.Single(c => c.Value == "谢拉格").CountText.Should().Be("1");

        vm.SetAvailableCovenants(["谢拉格"]);
        vm.CoreChips.Single(c => c.Value == "谢拉格").IsHighlighted.Should().BeTrue();
    }

    [Fact]
    public void SwitchingAvailability_UpdatesTheListInPlace()
    {
        var banned = new HashSet<string> { "角峰" };
        var vm = new OperatorBrowseViewModel();
        vm.LoadOperators(Ops);
        vm.SetBanChecker(banned.Contains);
        var list = vm.FilteredOperators;
        var first = list[0];
        vm.SelectedAvailabilityFilter = AvailabilityFilter.Available;
        vm.FilteredOperators.Should().BeSameAs(list);
        list.Select(o => o.Name).Should().Equal("隐现", "惊蛰");
        list[0].Should().BeSameAs(first);
        vm.SelectedAvailabilityFilter = AvailabilityFilter.All;
        list.Select(o => o.Name).Should().Equal("隐现", "角峰", "惊蛰");
    }
}
