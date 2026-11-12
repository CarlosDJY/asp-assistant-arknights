using System.Drawing;
using System.Drawing.Imaging;
using ASPAssistant.Core.GameModes.GarrisonProtocol;
using ASPAssistant.Core.Services;
using FluentAssertions;
using Moq;

namespace ASPAssistant.Tests.Services;

public sealed class SoloLayoutCompatibilityTests
{
    [Theory]
    [InlineData(6, 7)]
    [InlineData(9, 12)]
    public async Task CompleteVisiblePoolsDoNotRequireKnownSeasonTotals(int coreCount, int additionalCount)
    {
        string[] core = ["萨尔贡", "维多利亚", "叙拉古", "卡西米尔", "莱塔尼亚", "炎", "谢拉格", "阿戈尔", "乌萨斯"];
        string[] additional = ["迅捷", "灵巧", "远见", "投资人", "突袭", "调和", "协防干员", "独行", "绝技", "聚合", "奥术", "精准"];
        var texts = new List<OcrTextResult>
        {
            new("核心盘约", (90, 150, 120, 30)), new("附加盟药", (90, 430, 120, 30)),
            new("本局禁用干员", (90, additionalCount > 8 ? 845 : 660, 180, 30)),
        };
        using var bitmap = new Bitmap(1600, 900);
        using (var graphics = Graphics.FromImage(bitmap))
        {
            graphics.Clear(Color.FromArgb(12, 12, 12));
            for (int section = 0; section < 2; section++)
            {
                var names = (section == 0 ? core.Take(coreCount) : additional.Take(additionalCount)).ToArray();
                for (int i = 0; i < names.Length; i++)
                {
                    int x = 90 + (section == 0 ? i : i % 8) * 110;
                    int y = section == 0 ? 280 : i < 8 ? 550 : 735;
                    using var brush = new SolidBrush(i % 2 == 0 ? Color.FromArgb(20, 220, 140) : Color.Gray);
                    graphics.FillEllipse(brush, x - 34, y - 34, 68, 68);
                    texts.Add(new(names[i], (x - 38, y + 42, 76, 20)));
                }
            }
        }
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        var bytes = stream.ToArray();
        var ocr = new Mock<IOcrEngine>();
        ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns((byte[] _, int x, int y, int w, int h) => Task.FromResult<IReadOnlyList<OcrTextResult>>(
                texts.Where(t => t.Box.X >= x && t.Box.X + t.Box.W <= x + w
                    && t.Box.Y >= y && t.Box.Y + t.Box.H <= y + h).ToArray()));
        var strategy = new GarrisonOcrStrategy();
        var analyzer = new BanScreenAnalyzer(() => Task.FromResult<byte[]?>(bytes), ocr.Object, strategy,
            new EnemyRecognizer(ocr.Object, strategy, new BossDataService()), [], core, additional);
        BanScreenResult? result = null;
        string? failure = null;
        analyzer.AnalysisCompleted += r => result = r;
        analyzer.AnalysisFailed += m => failure = m;
        await analyzer.RunAnalysisAsync();
        failure.Should().BeNull();
        result.Should().NotBeNull();
        result!.CoreCovenants.Select(c => c.Name).Should().BeEquivalentTo(core.Take(coreCount));
        result.AdditionalCovenants.Select(c => c.Name).Should().BeEquivalentTo(additional.Take(additionalCount));
    }
}
