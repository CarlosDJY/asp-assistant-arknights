// Added for ASP Assistant Arknights on 2026-11-13; see NOTICE.
using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

/// <summary>Real bundled OCR on shop frames: round and mode at the top, funds and upgrade price at the bottom.</summary>
[Collection("Native OCR")]
public class ShopEconomyReaderTests(ShopReaderFixture fixture) : IClassFixture<ShopReaderFixture>
{
    [Theory]
    [InlineData("shop-terminal-ui100-2000x1125.png", 2000, 1125)]
    [InlineData("shop-ursus-ui100-2000x1125.png", 2000, 1125)]
    [InlineData("shop-ui100-2000x1125.png", 2000, 1125)]
    [InlineData("shop-closure-round1-1600x1024.png", 1600, 1024)]
    public async Task ReadsFullFramesInEveryTheme(string file, int width, int height)
    {
        var read = await new ShopEconomyReader(fixture.Engine).ReadAsync(File.ReadAllBytes(fixture.Sample(file)), width, height);
        read.Should().Be(new ShopEconomy(1, 4, 5, false, "规程等级1", true));
    }

    [Theory]
    [InlineData("shop-terminal-tier1-color.png", 4, 5, false)]
    [InlineData("shop-terminal-tier2-duplicates.png", 2, 8, false)]
    [InlineData("shop-terminal-tier4-gray.png", 0, 12, false)]
    [InlineData("shop-terminal-tier4-frozen-gray.png", 0, 12, false)]
    [InlineData("shop-frontier-tier1-color.png", 4, 5, false)]
    [InlineData("shop-frontier-tier2-glow.png", 6, 7, false)]
    [InlineData("shop-frontier-tier2-frozen-gray.png", 0, 7, false)]
    [InlineData("shop-frontier-tier4-gray-glow.png", 0, 12, false)]
    [InlineData("shop-tier2-color.png", null, 6, false)]
    [InlineData("shop-tier4-gray.png", null, 12, false)]
    [InlineData("shop-tier6-frozen-gray-glow.png", 0, null, true)]
    [InlineData("shop-tier6-glow-a.png", 11, null, true)]
    [InlineData("shop-tier6-glow-b.png", 11, null, true)]
    public async Task ReadsFundsAndPrice_AndNeverGuessesTheMaskedTop(string file, int? money, int? cost, bool maxed)
    {
        var read = await new ShopEconomyReader(fixture.Engine).ReadAsync(File.ReadAllBytes(fixture.Sample(file)), 1920, 1080);
        read.Should().Be(new ShopEconomy(null, money, cost, maxed, null, false));
    }

    [Fact]
    public async Task BanScreenHasNoShopHud()
    {
        var read = await new ShopEconomyReader(fixture.Engine).ReadAsync(File.ReadAllBytes(fixture.Sample("ban-sample-1920x1080.png")), 1920, 1080);
        read.UpgradeCost.Should().BeNull();
        read.UpgradeMaxed.Should().BeFalse();
    }
}
