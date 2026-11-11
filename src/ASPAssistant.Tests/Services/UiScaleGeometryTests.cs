using System.Drawing;
using System.Drawing.Imaging;
using ASPAssistant.Core.GameModes.GarrisonProtocol;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using FluentAssertions;
using Moq;

namespace ASPAssistant.Tests.Services;

public sealed class UiScaleGeometryTests
{
    [Theory]
    [InlineData(90)]
    [InlineData(100)]
    public async Task MovingInfoGridKeepsLeftmostIconAndLowerSecondRow(int ui)
    {
        string[] core = ["萨尔贡", "维多利亚", "叙拉古", "卡西米尔", "莱塔尼亚", "炎", "谢拉格", "阿戈尔", "乌萨斯", "伊比利亚"];
        string[] additional = ["迅捷", "灵巧", "远见", "投资人", "突袭", "调和", "协防干员", "独行", "绝技", "聚合", "奥术", "精准", "坚守", "助力", "奇迹", "不屈"];
        int firstX = ui == 100 ? 70 : 145;
        int pitch = ui == 100 ? 108 : 97;
        int coreY = ui == 100 ? 290 : 260;
        int additionalY = ui == 100 ? 550 : 495;
        int secondY = ui == 100 ? 735 : 660;
        int radius = ui == 100 ? 38 : 34;
        var texts = new List<OcrTextResult>
        {
            new("核心盟约", (90, 150, 120, 30)),
            new("附加盟约", (90, ui == 100 ? 430 : 385, 120, 30)),
            new("本局禁用干员", (90, 845, 180, 30)),
        };
        using var bitmap = new Bitmap(1600, 900);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.FromArgb(12, 12, 12));
            for (int section = 0; section < 2; section++)
            {
                var names = section == 0 ? core : additional;
                for (int i = 0; i < names.Length; i++)
                {
                    int x = firstX + i % 10 * pitch;
                    int y = section == 0 ? coreY : i < 10 ? additionalY : secondY;
                    bool available = section == 0 ? i < 5 : i < 11;
                    using var brush = new SolidBrush(available ? Color.FromArgb(20, 220, 140) : Color.Gray);
                    graphics.FillEllipse(brush, x - radius, y - radius, radius * 2, radius * 2);
                    texts.Add(new(names[i], (x - 38, y + radius + 8, 76, 20)));
                }
            }
        }
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        var ocr = new Mock<IOcrEngine>();
        ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns((byte[] _, int x, int y, int w, int h) => Task.FromResult<IReadOnlyList<OcrTextResult>>(
                texts.Where(t => t.Box.X >= x && t.Box.X + t.Box.W <= x + w
                    && t.Box.Y >= y && t.Box.Y + t.Box.H <= y + h).ToArray()));

        var read = await BanCovenantReader.ReadAsync(ocr.Object, new GarrisonOcrStrategy(),
            stream.ToArray(), 1600, 900, core, additional);

        read.Core.Select(c => c.Name).Should().BeEquivalentTo(core);
        read.Additional.Select(c => c.Name).Should().BeEquivalentTo(additional);
        read.CoreSlots.Should().Be(10);
        read.AdditionalSlots.Should().Be(16);
        read.Core.Where(c => c.IsBanned).Select(c => c.Name).Should().BeEquivalentTo(core.Skip(5));
        read.Additional.Where(c => c.IsBanned).Select(c => c.Name).Should().BeEquivalentTo(additional.Skip(11));
        read.Core.First(c => c.Name == core[0]).X.Should().BeApproximately(firstX, 2);
        read.UnnamedCore.Should().BeEmpty();
        read.UnnamedAdditional.Should().BeEmpty();
    }

    [Theory]
    [InlineData(360)]
    [InlineData(410)]
    public async Task MovingShopNamesKeepDuplicatesAndIgnorePortraitText(int nameY)
    {
        using var bitmap = new Bitmap(1920, 1080);
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        var ocr = new Mock<IOcrEngine>();
        ocr.SetupSequence(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync([
                new("山", (1100, 30, 70, 50)),
                new("源石溶剂", (1400, nameY, 200, 60)),
                new("野鬃", (1800, nameY, 100, 60)),
                new("野鬃", (2240, nameY, 100, 60)),
                new("拉普兰德", (2700, nameY, 200, 60)),
                new("收起", (3500, nameY - 20, 140, 60)),
            ])
            // A second pass can include the class icon in the same name box.
            .ReturnsAsync([new("野鬃", (1600, nameY, 350, 60)), new("野鬃", (2200, nameY, 200, 60))]);
        var reader = new ShopReader(ocr.Object, ["山", "源石溶剂", "野鬃", "拉普兰德"])
        {
            Theme = GameShopTheme.Closure,
        };

        var items = await reader.ReadAsync(stream.ToArray(), 1920, 1080, ["山", "源石溶剂", "野鬃", "拉普兰德"]);

        items.Select(i => i.Name).Should().Equal("源石溶剂", "野鬃", "野鬃", "拉普兰德");
        items.Select(i => i.Id).Should().OnlyHaveUniqueItems();
        items.Should().OnlyContain(i => i.OcrRegion.Y > .90);
        (items[2].OcrRegion.X - items[1].OcrRegion.X).Should().BeGreaterThan(.10);
    }
}
