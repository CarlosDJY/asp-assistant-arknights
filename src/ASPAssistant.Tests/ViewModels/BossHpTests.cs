using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.ViewModels;

public class BossHpTests
{
    [Theory]
    [InlineData(283680, "28.4 万")]
    [InlineData(1182000, "118 万")]
    [InlineData(100000, "10 万")]
    [InlineData(9500, "9500")]
    public void HpIsShownInTenThousands(long hp, string text) => BossHpCell.FormatHp(hp).Should().Be(text);

    [Theory]
    [InlineData("规程等级12", "12")]
    [InlineData("极速模拟", "极速")]
    [InlineData("绝境模拟", "绝境")]
    public void LabelsAreShortened(string name, string label) => BossHpCell.ShortLabel(name).Should().Be(label);

    [Fact]
    public async Task EveryBossHasEachDifficulty_AndHiddenFormsCarryTheirOwnHp()
    {
        var bosses = new BossDataService();
        await bosses.LoadAsync(Path.Combine(AppContext.BaseDirectory, "TestData", "Regulation"));
        var stmk = bosses.Lookup("假想敌：胄")!;
        stmk.Hp.Should().HaveCount(25);
        stmk.Hp.Count(l => l.Group == "single").Should().Be(21);
        stmk.Hp.Should().Contain(l => l.Mode == "mode_single_1" && l.Hp == 283680 && l.HiddenHp == 492480);
        stmk.Hp.Select(l => l.Name).Should().StartWith(["极速模拟", "规程等级1"]);
        stmk.Hp.TakeLast(4).Select(l => l.Name).Should().Equal("标准模拟", "险境模拟", "绝境模拟", "终极模拟");
    }

    [Fact]
    public void UpdateSplitsSingleAndMultiAndClearResets()
    {
        var boss = new BossInfo
        {
            Name = "假想敌：胄",
            Hp =
            [
                new() { Mode = "mode_single_1", Name = "规程等级1", Group = "single", Hp = 283680, HiddenHp = 492480 },
                new() { Mode = "mode_multi_funny", Name = "标准模拟", Group = "multi", Hp = 283680 },
            ],
        };
        var vm = new EnemyViewModel();
        vm.Update(new("假想敌：胄", [], []), boss, []);
        vm.BossHpSingle.Should().ContainSingle().Which.Should().Be(new BossHpCell("1", "规程等级1", "28.4 万", "49.2 万"));
        vm.BossHpMulti.Should().ContainSingle().Which.Label.Should().Be("标准");
        vm.BossHasHiddenForm.Should().BeTrue();
        vm.CurrentMode = "规程等级1";
        vm.BossHpSingle.Single().IsCurrent.Should().BeTrue();
        vm.BossHpMulti.Single().IsCurrent.Should().BeFalse();
        vm.Clear();
        vm.BossHpSingle.Should().BeEmpty();
        vm.BossHasHiddenForm.Should().BeFalse();
    }
}
