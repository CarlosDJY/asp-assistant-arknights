// Modified for ASP Assistant Arknights on 2026-11-10; see NOTICE.
using ASPAssistant.Core.Data;
using FluentAssertions;

namespace ASPAssistant.Tests.Data;

public class StrategyDataTests
{
    [Fact]
    public async Task CurrentSeason_LoadsStrategiesUnlockRequirementsAndPortraits()
    {
        var strategies = await new JsonDataStore(Path.Combine(AppContext.BaseDirectory, "TestData", "Regulation"))
            .LoadStrategiesAsync();
        strategies.Should().HaveCount(61).And.OnlyHaveUniqueItems(s => s.Id);
        strategies.Count(s => s.UnlockRequirement.Length == 0).Should().Be(16);
        var amiya = strategies.Single(s => s.Id == "band_amiya");
        amiya.Name.Should().Be("众志合一");
        amiya.Initiator.Should().Be("阿米娅");
        amiya.InitialHp.Should().Be(25);
        amiya.Description.Should().Contain("20%/30%/40%");
        amiya.UnlockRequirement.Should().BeEmpty();
        strategies.Single(s => s.Id == "band_upgrade").UnlockRequirement.Should().Be("通关任意模拟8次解锁");
        strategies.Single(s => s.Id == "band_pith").UnlockRequirement.Should().Contain("调和");
        strategies.Single(s => s.Id == "band_protect").InactiveModes.Should().Contain("mode_single_20");
        strategies.Should().OnlyContain(s => !s.Description.Contains("<@") && !s.Description.Contains("\\n"));
        foreach (var strategy in strategies)
        {
            var portrait = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "data", strategy.IconPath));
            portrait.Take(8).Should().Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        }
    }
}
