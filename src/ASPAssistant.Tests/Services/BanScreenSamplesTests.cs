// Modified for ASP Assistant Regulation on 2026-11-10; see NOTICE.
using ASPAssistant.Core.Data;
using ASPAssistant.Core.GameModes.GarrisonProtocol;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using FluentAssertions;
using Xunit;

namespace ASPAssistant.Tests.Services;

/// <summary>
/// 用户实拍的 1920×1080 “确认本局信息” 截图回归。
/// 这两张图曾经完全识别失败（页头判定失败、核心盟约 0/8），原因是：
/// ① 标题里的「盟」被 OCR 读成「明」；② 附加盟约只显示本局阵容包含的 10 项，
/// 而旧逻辑要求与数据文件里的 18 项数量相等；③ 整行 OCR 读不出名字时没有退回到
/// 图标槽位匹配。修复后两张图都能完整读出。
/// </summary>
[Collection("Native OCR")]
public sealed class BanScreenSamplesTests : IDisposable
{
    // 与生产一致：候选名单来自 data/covenants.json（该赛季 10 核心 / 16 附加）。
    private static readonly (List<string> Core, List<string> Additional) Names = LoadNames();

    private static (List<string> Core, List<string> Additional) LoadNames()
    {
        using var json = System.Text.Json.JsonDocument.Parse(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "data", "covenants.json")));
        var season = new List<string>();
        var other = new List<string>();
        foreach (var item in json.RootElement.GetProperty("covenants").EnumerateArray())
        {
            var name = item.GetProperty("name").GetString()!;
            (item.GetProperty("bondType").GetString() == "SEASON" ? season : other).Add(name);
        }
        return (season, other);
    }

    private readonly MaaOcrEngine _ocr = new(Path.Combine(AppContext.BaseDirectory, "data"));

    public void Dispose() => _ocr.Dispose();

    /// <summary>
    /// 每张样本的期望值。难度不同，盟约池就不同：
    /// 标准模拟核心 8 / 附加 10（卡西米尔、阿戈尔、绝技等不出现）；
    /// 险境核心 10 / 附加 16（池子全开）。所以判据必须是“屏幕上有什么读什么”，
    /// 而不是与数据文件里的总数相等。
    /// </summary>
    private sealed record Sample(
        string File,
        string[] Core,
        string[] BannedCore,
        string[] Additional,
        string[] BannedAdditional,
        string Boss,
        string[] Types);

    private static readonly Sample[] Samples =
    [
        new("user-ban-1-1920x1080.png",
            ["炎", "萨尔贡", "维多利亚", "谢拉格", "叙拉古", "乌萨斯", "伊比利亚", "莱塔尼亚"],
            [],
            ["精准", "坚守", "助力", "远见", "调和", "协防干员", "聚合", "奥术", "迅捷", "不屈"],
            ["迅捷", "不屈"],
            "假想敌：铳", ["特训敌人·折射", "特训敌人·元素", "特训敌人·频次"]),
        new("user-ban-2-1920x1080.png",
            ["炎", "萨尔贡", "维多利亚", "谢拉格", "叙拉古", "乌萨斯", "伊比利亚", "莱塔尼亚"],
            [],
            ["精准", "坚守", "助力", "远见", "调和", "协防干员", "聚合", "奥术", "迅捷", "不屈"],
            ["迅捷", "不屈"],
            "假想敌：铳", ["特训敌人·折射", "特训敌人·元素", "特训敌人·频次"]),
        new("user-ban-3-xianjing-1936x1119.png",
            ["炎", "萨尔贡", "谢拉格", "阿戈尔", "伊比利亚", "维多利亚", "叙拉古", "卡西米尔", "乌萨斯", "莱塔尼亚"],
            ["维多利亚", "叙拉古", "卡西米尔", "乌萨斯", "莱塔尼亚"],
            ["奥术", "精准", "坚守", "迅捷", "助力", "灵巧", "奇迹", "远见", "投资人", "突袭",
             "不屈", "聚合", "调和", "协防干员", "独行", "绝技"],
            ["迅捷", "灵巧", "远见", "突袭", "聚合"],
            "阿利斯泰尔，帝国余晖", ["特训敌人·折射", "特训敌人·复苏", "特训敌人·飞行"]),
    ];

    public static TheoryData<string> SampleFiles()
    {
        var data = new TheoryData<string>();
        foreach (var sample in Samples) data.Add(sample.File);
        return data;
    }

    private static Sample Expect(string file) => Samples.Single(s => s.File == file);

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public async Task ReadsEveryCoreCovenantAndShownAdditionalCovenant(string file)
    {
        var expected = Expect(file);
        var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "TestData", file));
        var (png, width, height) = ScreenshotNormalizer.EnsureAnalysable(bytes, 1936, 1119);
        width.Should().BeGreaterThanOrEqualTo(ScreenshotNormalizer.MinimumAnalysisWidth);

        var detector = new InformationScreenDetector(_ocr, new GarrisonOcrStrategy());
        (await detector.IsInformationScreenAsync(png, width, height)).Should().BeTrue("这是一张确认本局信息页");

        var read = await BanCovenantReader.ReadAsync(
            _ocr, new GarrisonOcrStrategy(), png, width, height, Names.Core, Names.Additional);

        read.Core.Select(c => c.Name).Should().BeEquivalentTo(expected.Core, o => o.WithStrictOrdering());
        read.Core.Where(c => c.IsBanned).Select(c => c.Name)
            .Should().BeEquivalentTo(expected.BannedCore);
        read.Additional.Select(c => c.Name).Should().BeEquivalentTo(expected.Additional, o => o.WithoutStrictOrdering());
        read.Additional.Where(c => c.IsBanned).Select(c => c.Name)
            .Should().BeEquivalentTo(expected.BannedAdditional);
        read.UnnamedCore.Should().BeEmpty();
        read.UnnamedAdditional.Should().BeEmpty();
    }

    [Theory]
    [MemberData(nameof(SampleFiles))]
    public async Task WholePipelinePublishesCovenantsAndEnemy(string file)
    {
        var expected = Expect(file);
        var bytes = await File.ReadAllBytesAsync(Path.Combine(AppContext.BaseDirectory, "TestData", file));
        var store = new JsonDataStore(Path.Combine(AppContext.BaseDirectory, "data"));
        var (covenants, _) = await store.LoadCovenantsAsync();
        var strategy = new GarrisonOcrStrategy();
        var analyzer = new BanScreenAnalyzer(
            () => Task.FromResult<byte[]?>(bytes), _ocr, strategy,
            new EnemyRecognizer(_ocr, strategy, new BossDataService()), await store.LoadOperatorsAsync(),
            covenants.Where(c => c.BondType == "SEASON").Select(c => c.Name).ToList(),
            covenants.Where(c => c.BondType != "SEASON").Select(c => c.Name).ToList());

        BanScreenResult? result = null;
        EnemyResult? enemy = null;
        string? failure = null;
        analyzer.AnalysisCompleted += r => result = r;
        analyzer.EnemyDetected += e => enemy = e;
        analyzer.AnalysisFailed += m => failure = m;

        await analyzer.RunAnalysisAsync();

        failure.Should().BeNull();
        result.Should().NotBeNull();
        result!.CoreCovenants.Where(c => c.IsBanned).Select(c => c.Name)
            .Should().BeEquivalentTo(expected.BannedCore);
        result.AdditionalCovenants.Where(c => c.IsBanned).Select(c => c.Name)
            .Should().BeEquivalentTo(expected.BannedAdditional);
        enemy.Should().NotBeNull();
        enemy!.BossName.Should().Be(expected.Boss);
        enemy.EnemyTypes.Should().Equal(expected.Types);
    }

    [Fact]
    public async Task SmallDownscaledCaptureIsUpscaledBeforeAnalysis()
    {
        var small = await File.ReadAllBytesAsync(
            Path.Combine(AppContext.BaseDirectory, "TestData", "user-ban-2-532x300.png"));

        var (png, width, height) = ScreenshotNormalizer.EnsureAnalysable(small, 532, 300);

        width.Should().BeGreaterThanOrEqualTo(ScreenshotNormalizer.MinimumAnalysisWidth);
        png.Length.Should().BeGreaterThan(small.Length);
        height.Should().Be((int)Math.Round(300.0 * width / 532));
    }
}
