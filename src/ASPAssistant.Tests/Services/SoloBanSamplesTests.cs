// Modified for ASP Assistant Regulation on 2026-11-11; see NOTICE.
using ASPAssistant.Core.Data;
using ASPAssistant.Core.GameModes.GarrisonProtocol;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using FluentAssertions;
using Xunit;

namespace ASPAssistant.Tests.Services;

/// <summary>
/// 单人模式两种实拍布局的回归：
///   single_player1：核心 8（4+4 两行）/ 附加 10（一行），附加标题 y≈0.65；
///   single_player2：核心 10（3+3 / 2+2）/ 附加 16（两行，末行名字被画面底边裁掉）；
///   single_player2_scrolled：1080p 下向下滚动看附加末行后，“核心盟约”标题滑出视口。
/// 单人页的列之间有明显空隙，旧网格会整行丢弃；附加标题低于旧检测器的 0.51 上限。
/// </summary>
[Collection("Native OCR")]
public sealed class SoloBanSamplesTests : IDisposable
{
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

    private static byte[] Load(string file) =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", file));

    private async Task<BanScreenAnalyzer> CreateAnalyzerAsync(byte[] bytes)
    {
        var store = new JsonDataStore(Path.Combine(AppContext.BaseDirectory, "data"));
        var (covenants, _) = await store.LoadCovenantsAsync();
        var strategy = new GarrisonOcrStrategy();
        return new BanScreenAnalyzer(() => Task.FromResult<byte[]?>(bytes), _ocr, strategy,
            new EnemyRecognizer(_ocr, strategy, new BossDataService()), await store.LoadOperatorsAsync(),
            covenants.Where(c => c.BondType == "SEASON").Select(c => c.Name).ToList(),
            covenants.Where(c => c.BondType != "SEASON").Select(c => c.Name).ToList());
    }

    [Fact]
    public async Task SoloLayoutA_ReadsEightCoreAndTenAdditional()
    {
        var (png, width, height) =
            ScreenshotNormalizer.EnsureAnalysable(Load("single_player1.png"), 2559, 1599);

        var read = await BanCovenantReader.ReadAsync(
            _ocr, new GarrisonOcrStrategy(), png, width, height, Names.Core, Names.Additional);

        read.Core.Should().HaveCount(8);
        read.Additional.Should().HaveCount(10);
        read.CoreSlots.Should().Be(8);
        read.AdditionalSlots.Should().Be(10);
        read.UnnamedCore.Should().BeEmpty();
        read.UnnamedAdditional.Should().BeEmpty();
        read.Core.Where(c => c.IsBanned).Select(c => c.Name)
            .Should().BeEquivalentTo("伊比利亚", "叙拉古", "维多利亚", "炎");
    }

    [Fact]
    public async Task SoloLayoutB_ReadsAllCoreAndFlagsTheClippedAdditionalRow()
    {
        var (png, width, height) =
            ScreenshotNormalizer.EnsureAnalysable(Load("single_player2.png"), 2559, 1599);

        var read = await BanCovenantReader.ReadAsync(
            _ocr, new GarrisonOcrStrategy(), png, width, height, Names.Core, Names.Additional);

        read.Core.Should().HaveCount(10);
        read.CoreSlots.Should().Be(10);
        read.AdditionalSlots.Should().Be(16);
        read.Additional.Should().HaveCount(10);
        read.UnnamedAdditional.Should().HaveCount(6);
    }

    [Fact]
    public async Task SoloLayoutB_ScrolledPastCoreHeader_ReadsTenAndSixteen()
    {
        // 1080p 下 10/16 一屏放不下：玩家为了看附加末行向下滚动后，
        // “核心盟约”标题会滑出视口。此时仍应能按附加标题分区读完 10/16。
        var (png, width, height) =
            ScreenshotNormalizer.EnsureAnalysable(Load("single_player2_scrolled.png"), 1920, 1080);

        var read = await BanCovenantReader.ReadAsync(
            _ocr, new GarrisonOcrStrategy(), png, width, height, Names.Core, Names.Additional);

        read.CoreSlots.Should().Be(10);
        read.Core.Should().HaveCount(10);
        read.UnnamedCore.Should().BeEmpty();
        read.AdditionalSlots.Should().Be(16);
        read.Additional.Should().HaveCount(16);
        read.UnnamedAdditional.Should().BeEmpty();
    }

    [Fact]
    public async Task SoloLayoutB_ScrolledPastCoreHeader_WholePipelinePublishes()
    {
        var analyzer = await CreateAnalyzerAsync(Load("single_player2_scrolled.png"));
        BanScreenResult? result = null;
        string? failure = null;
        analyzer.AnalysisCompleted += r => result = r;
        analyzer.AnalysisFailed += m => failure = m;

        await analyzer.RunAnalysisAsync();

        failure.Should().BeNull();
        result.Should().NotBeNull();
        result!.CoreCovenants.Should().HaveCount(10);
        result.AdditionalCovenants.Should().HaveCount(16);
    }

    [Fact]
    public async Task SoloLayoutA_WholePipelinePublishes()
    {
        var analyzer = await CreateAnalyzerAsync(Load("single_player1.png"));
        BanScreenResult? result = null;
        string? failure = null;
        analyzer.AnalysisCompleted += r => result = r;
        analyzer.AnalysisFailed += m => failure = m;

        await analyzer.RunAnalysisAsync();

        failure.Should().BeNull();
        result.Should().NotBeNull();
        result!.CoreCovenants.Should().HaveCount(8);
        result.AdditionalCovenants.Should().HaveCount(10);
    }

    [Fact]
    public async Task SoloLayoutB_WholePipelineAsksForScrollInsteadOfPublishing()
    {
        var analyzer = await CreateAnalyzerAsync(Load("single_player2.png"));
        BanScreenResult? result = null;
        string? failure = null;
        analyzer.AnalysisCompleted += r => result = r;
        analyzer.AnalysisFailed += m => failure = m;

        await analyzer.RunAnalysisAsync();

        result.Should().BeNull();
        failure.Should().Contain("滚动");
    }

    [Fact]
    public async Task SoloLayoutA_WithCoreSecondRowRemoved_DoesNotPublish()
    {
        // 模拟“核心只露一行”：8/10 的页面被读成 4/10，必须失败而不是 4/4 成功。
        var analyzer = await CreateAnalyzerAsync(
            BlackOut(Load("single_player1.png"), 0.00, 0.47, 0.70, 0.62));
        BanScreenResult? result = null;
        string? failure = null;
        analyzer.AnalysisCompleted += r => result = r;
        analyzer.AnalysisFailed += m => failure = m;

        await analyzer.RunAnalysisAsync();

        result.Should().BeNull();
        failure.Should().NotBeNull();
    }

    [Fact]
    public async Task SoloLayoutB_WithAdditionalBottomRowRemoved_DoesNotPublish()
    {
        // 模拟“末行被裁切”：16 个附加只剩 10 个可见，10/10 不在白名单内。
        var analyzer = await CreateAnalyzerAsync(
            BlackOut(Load("single_player2.png"), 0.00, 0.86, 0.70, 1.00));
        BanScreenResult? result = null;
        string? failure = null;
        analyzer.AnalysisCompleted += r => result = r;
        analyzer.AnalysisFailed += m => failure = m;

        await analyzer.RunAnalysisAsync();

        result.Should().BeNull();
        failure.Should().NotBeNull();
    }

    [Theory]
    [InlineData("single_player1.png")]
    [InlineData("single_player2.png")]
    public async Task SoloOpeningHeadersAreDetected(string file)
    {
        var (png, width, height) = ScreenshotNormalizer.EnsureAnalysable(Load(file), 2559, 1599);
        var detector = new InformationScreenDetector(_ocr, new GarrisonOcrStrategy());
        (await detector.IsOpeningScreenAsync(png, width, height)).Should().BeTrue();
    }

    private static byte[] BlackOut(byte[] png, double x0, double y0, double x1, double y1)
    {
        using var input = new MemoryStream(png);
        using var image = new System.Drawing.Bitmap(input);
        using (var graphics = System.Drawing.Graphics.FromImage(image))
        {
            graphics.FillRectangle(System.Drawing.Brushes.Black,
                (int)(image.Width * x0), (int)(image.Height * y0),
                (int)(image.Width * (x1 - x0)), (int)(image.Height * (y1 - y0)));
        }

        using var output = new MemoryStream();
        image.Save(output, System.Drawing.Imaging.ImageFormat.Png);
        return output.ToArray();
    }
}
