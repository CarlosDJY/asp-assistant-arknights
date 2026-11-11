using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using FluentAssertions;
using Moq;

namespace ASPAssistant.Tests.Services;

[Collection("Native OCR")]
public class ShopReaderTests(ShopReaderFixture fixture) : IClassFixture<ShopReaderFixture>
{
    // These run the real bundled PaddleOCR models against anonymized client crops.
    [Theory]
    [InlineData("shop-tier2-color.png", "聚合急救箱|渡桥|杰克|陨星|艾丝黛尔")]
    [InlineData("shop-tier4-gray.png", "卫戍推荐信|摆渡人|Miss.Christine|奥斯塔|空弦|山")]
    [InlineData("shop-tier6-frozen.png", "奥斯塔|拉普兰德|斥罪|鸿雪")]
    [InlineData("shop-tier6-glow-a.png", "突袭手雷|可露希尔|奥斯塔|拉普兰德|斥罪|鸿雪")]
    [InlineData("shop-tier6-glow-b.png", "突袭手雷|可露希尔|奥斯塔|拉普兰德|斥罪|鸿雪")]
    [InlineData("shop-tier6-frozen-gray-glow.png", "护盾无人机|奥斯塔|水灯心|忍冬|鸿雪|空弦")]
    [InlineData("shop-frontier-tier1-color.png", "坚守盾牌|野鬃|角峰|褐果")]
    [InlineData("shop-frontier-tier2-glow.png", "紧急调度券|承曦格雷伊|幽灵鲨|角峰|慕斯")]
    [InlineData("shop-frontier-tier2-frozen-gray.png", "卫戍推荐信|砾|哈洛德|雪猎|云迹")]
    [InlineData("shop-frontier-tier4-gray-glow.png", "伊比利亚骨刺刀|卡涅利安|极光|艾雅法拉|瑕光|云迹")]
    [InlineData("shop-terminal-tier1-color.png", "虹彩活血饮料|卡达|慕斯|角峰")]
    [InlineData("shop-terminal-tier2-duplicates.png", "源石溶剂|地灵|陨星|褐果|地灵")]
    [InlineData("shop-terminal-tier4-gray.png", "随身身份牌|斯卡蒂|深巡|复奏|风絮|耶拉")]
    [InlineData("shop-terminal-tier4-frozen-gray.png", "随身身份牌|斯卡蒂|深巡|复奏|风絮|耶拉")]
    public async Task RealShopSamples_ReadEquipmentAndOperators(string file, string expected)
    {
        fixture.SelectTheme(file);
        var items = await fixture.Reader.ReadAsync(File.ReadAllBytes(fixture.Sample(file)), 1920, 1080, fixture.Names);
        items.Select(i => i.Name).Should().Equal(expected.Split('|'));
        items.Select(i => i.Id).Should().OnlyHaveUniqueItems();
        items.Should().OnlyContain(i => i.OcrRegion.Y > .91 && i.OcrRegion.Y < .97
            && i.OcrRegion.X >= 0 && i.OcrRegion.X + i.OcrRegion.Width <= 1);
        if (file == "shop-tier6-frozen.png")
            items[0].OcrRegion.X.Should().BeGreaterThan(.40, "two sold slots must not shift the remaining names left");
        if (file.StartsWith("shop-frontier-", StringComparison.Ordinal) || file.StartsWith("shop-terminal-", StringComparison.Ordinal))
            for (int i = 0; i < items.Count; i++)
            {
                double left = 360 + (6 - items.Count + i) * 220;
                ((items[i].OcrRegion.X + items[i].OcrRegion.Width / 2) * 1920)
                    .Should().BeInRange(left, left + 220, "each marker must stay on its own card");
            }
    }

    [Theory]
    [InlineData("shop-tier6-glow-a.png", "突袭手雷|可露希尔|奥斯塔|拉普兰德|斥罪|鸿雪", 1280, 720, 0)]
    [InlineData("shop-tier6-glow-a.png", "突袭手雷|可露希尔|奥斯塔|拉普兰德|斥罪|鸿雪", 2560, 1080, 320)]
    [InlineData("shop-frontier-tier1-color.png", "坚守盾牌|野鬃|角峰|褐果", 1280, 720, 0)]
    [InlineData("shop-frontier-tier2-frozen-gray.png", "卫戍推荐信|砾|哈洛德|雪猎|云迹", 1280, 720, 0)]
    [InlineData("shop-frontier-tier2-frozen-gray.png", "卫戍推荐信|砾|哈洛德|雪猎|云迹", 2560, 1080, 320)]
    [InlineData("shop-terminal-tier4-frozen-gray.png", "随身身份牌|斯卡蒂|深巡|复奏|风絮|耶拉", 1280, 720, 0)]
    [InlineData("shop-terminal-tier4-frozen-gray.png", "随身身份牌|斯卡蒂|深巡|复奏|风絮|耶拉", 2560, 1080, 320)]
    public async Task SyntheticResizeAndLetterbox_KeepNamePositions(string file, string expected, int width, int height, int padding)
    {
        fixture.SelectTheme(file);
        using var source = new Bitmap(fixture.Sample(file));
        using var resized = new Bitmap(width, height);
        using (var g = Graphics.FromImage(resized))
        {
            g.Clear(Color.Black);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.DrawImage(source, new Rectangle(padding, 0, width - 2 * padding, height));
        }
        using var stream = new MemoryStream();
        resized.Save(stream, ImageFormat.Png);
        var items = await fixture.Reader.ReadAsync(stream.ToArray(), width, height, fixture.Names);
        items.Select(i => i.Name).Should().Equal(expected.Split('|'));
        double left = 360 + (6 - items.Count) * 220;
        ((items[0].OcrRegion.X + items[0].OcrRegion.Width / 2) * width)
            .Should().BeInRange(padding + left * height / 1080, padding + (left + 220) * height / 1080);
    }

    [Fact]
    public async Task SyntheticDuplicateCards_KeepTwoMarkersAfterMergingNameBands()
    {
        fixture.SelectTheme("shop-frontier-tier2-glow.png");
        using var original = new Bitmap(fixture.Sample("shop-frontier-tier2-glow.png"));
        using var shop = (Bitmap)original.Clone();
        using (var graphics = Graphics.FromImage(shop))
            graphics.DrawImage(original, new Rectangle(1020, 800, 220, 280), 1240, 800, 220, 280, GraphicsUnit.Pixel);
        using var stream = new MemoryStream();
        shop.Save(stream, ImageFormat.Png);
        var items = await fixture.Reader.ReadAsync(stream.ToArray(), 1920, 1080, ["角峰"]);
        items.Select(i => i.Id).Should().Equal("角峰@0", "角峰@1");
        (items[1].OcrRegion.X - items[0].OcrRegion.X).Should().BeGreaterThan(.08);
    }

    [Theory]
    [InlineData("shop-terminal-tier1-color.png")]
    [InlineData("shop-frontier-tier1-color.png")]
    [InlineData("shop-tier2-color.png")]
    public async Task NameStripWithoutShopControl_DoesNotProduceMarkers(string file)
    {
        fixture.SelectTheme(file);
        using var shop = new Bitmap(fixture.Sample(file));
        using (var graphics = Graphics.FromImage(shop))
            graphics.FillRectangle(Brushes.Black, 1680, 790, 240, 290);
        using var stream = new MemoryStream();
        shop.Save(stream, ImageFormat.Png);
        (await fixture.Reader.ReadAsync(stream.ToArray(), 1920, 1080, fixture.Names)).Should().BeEmpty();
    }

    [Fact]
    public async Task SyntheticTier1_FourSlots_AreNotTreatedAsFive()
    {
        fixture.SelectTheme("shop-tier2-color.png");
        using var source = new Bitmap(fixture.Sample("shop-tier2-color.png"));
        using var fourSlots = new Bitmap(1920, 1080);
        using (var g = Graphics.FromImage(fourSlots))
        {
            g.Clear(Color.Black);
            g.DrawImage(source, new Rectangle(1700, 990, 220, 50), 1700, 990, 220, 50, GraphicsUnit.Pixel);
            for (int slot = 0; slot < 4; slot++)
                g.DrawImage(source, new Rectangle(795 + slot * 220, 990, 220, 50),
                    575 + slot * 220, 990, 220, 50, GraphicsUnit.Pixel);
        }
        using var stream = new MemoryStream();
        fourSlots.Save(stream, ImageFormat.Png);
        var items = await fixture.Reader.ReadAsync(stream.ToArray(), 1920, 1080, fixture.Names);
        items.Select(i => i.Name).Should().Equal("聚合急救箱", "渡桥", "杰克", "陨星");
    }

    [Fact]
    public void Match_DuplicateNamesHaveSeparateStableIds()
    {
        var reader = new ShopReader(Mock.Of<IOcrEngine>(), ["奥斯塔"]);
        var items = reader.Match([
            new("奥斯塔", (1200, 390, 100, 40)),
            new("奥斯塔", (1640, 390, 100, 40)),
            new("收起", (3500, 370, 160, 60))], 1920, 1080, 810, ["奥斯塔"]);
        items.Should().HaveCount(2);
        items.Select(i => i.Id).Should().OnlyHaveUniqueItems();
        items[1].OcrRegion.X.Should().BeGreaterThan(items[0].OcrRegion.X);
    }

    [Fact]
    public void Match_UntrackedLongerEquipmentDoesNotMatchTrackedSuffix()
    {
        var reader = new ShopReader(Mock.Of<IOcrEngine>(), ["维式重锤", "终极维式重锤"]);
        OcrTextResult[] texts = [new("品终极维式重锤", (740, 390, 220, 40)),
            new("收起", (3500, 370, 160, 60))];
        reader.Match(texts, 1920, 1080, 810, ["维式重锤"]).Should().BeEmpty();
        reader.Match(texts, 1920, 1080, 810, ["维式重锤", "终极维式重锤"])
            .Select(i => i.Name).Should().Equal("终极维式重锤");
    }

    [Fact]
    public void Match_ClosedShopHasNoMarkers()
    {
        var reader = new ShopReader(Mock.Of<IOcrEngine>(), ["山"]);
        reader.Match([new("山", (740, 390, 30, 40))], 1920, 1080, 810, ["山"]).Should().BeEmpty();
    }
}

public sealed class ShopReaderFixture : IDisposable
{
    private readonly MaaOcrEngine _engine = new(Path.Combine(AppContext.BaseDirectory, "data"));
    public MaaOcrEngine Engine => _engine;
    public IReadOnlyList<string> Names { get; }
    public ShopReader Reader { get; }
    public void SelectTheme(string file) => Reader.Theme = file.StartsWith("shop-terminal-", StringComparison.Ordinal)
        ? GameShopTheme.Terminal : file.StartsWith("shop-frontier-", StringComparison.Ordinal) ? GameShopTheme.Ursus : GameShopTheme.Closure;
    public string Sample(string file) => Path.Combine(AppContext.BaseDirectory, "TestData", file);

    public ShopReaderFixture()
    {
        var store = new JsonDataStore(Path.Combine(AppContext.BaseDirectory, "TestData", "Regulation"));
        Names = store.LoadOperatorsAsync().GetAwaiter().GetResult().Select(o => o.Name)
            .Concat(store.LoadEquipmentAsync().GetAwaiter().GetResult().Equipment.Select(e => e.Name)).Distinct().ToList();
        Reader = new ShopReader(_engine, Names);
    }

    public void Dispose() => _engine.Dispose();
}
