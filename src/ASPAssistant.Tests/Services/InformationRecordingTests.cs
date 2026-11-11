using ASPAssistant.Core.Data;
using ASPAssistant.Core.GameModes.GarrisonProtocol;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;
using Moq;
using Xunit.Abstractions;

namespace ASPAssistant.Tests.Services;

[Collection("Native OCR")]
public class InformationRecordingTests(EnemyRecognitionFixture fixture, ITestOutputHelper output) : IClassFixture<EnemyRecognitionFixture>
{
    [Theory]
    [InlineData("ban-sample-1920x1080.png", true)]
    [InlineData("ban-sample-2-1920x1080.png", true)]
    [InlineData("info-ingame-1920x1080.png", false)]
    public async Task NativeOpeningAndInGamePanelsAreDistinguished(string file, bool opening)
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", file));
        var detector = new InformationScreenDetector(fixture.Engine, new GarrisonOcrStrategy());
        (await detector.IsInformationScreenAsync(bytes, 1920, 1080)).Should().BeTrue();
        (await detector.IsOpeningScreenAsync(bytes, 1920, 1080)).Should().Be(opening);
        (await detector.IsGameplayAsync(bytes, 1920, 1080)).Should().BeFalse();
    }

    [Fact]
    public async Task NativeGameplayMarkersProvidePositiveEvidenceToRearm()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "gameplay-markers-1920x1080.png"));
        (await new InformationScreenDetector(fixture.Engine, new GarrisonOcrStrategy())
            .IsGameplayAsync(bytes, 1920, 1080)).Should().BeTrue();
    }

    [Fact]
    public async Task ATitleInOtherContentIsNotAnOpeningPage()
    {
        var ocr = new Mock<IOcrEngine>();
        ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync([new("确认本局信息", (500, 30, 200, 30))]);
        (await new InformationScreenDetector(ocr.Object, new GarrisonOcrStrategy())
            .IsOpeningScreenAsync([], 1920, 1080)).Should().BeFalse();
    }

    [Theory]
    [InlineData("ban-sample-1920x1080.png", "假想敌：鳍")]
    [InlineData("ban-sample-2-1920x1080.png", "阿利斯泰尔，帝国余晖")]
    [InlineData("info-ingame-1920x1080.png", "盐风主教昆图斯")]
    public async Task FullRecordingCommitsCovenantsBansAndEnemyTogether(string file, string expectedBoss)
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", file));
        var recording = new Mock<IOcrEngine>();
        recording.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<int>(), It.IsAny<int>())).Returns(async (byte[] input, int x, int y, int w, int h) =>
        {
            var texts = await fixture.Engine.RecognizeRegionAsync(input, x, y, w, h);
            output.WriteLine($"ROI({x},{y},{w},{h}): {string.Join("|", texts.Select(t => t.Text))}");
            return texts;
        });
        var analyzer = await CreateAnalyzerAsync(bytes, recording.Object);
        BanScreenResult? recorded = null;
        string? boss = null;
        string? failure = null;
        analyzer.AnalysisCompleted += result => recorded = result;
        analyzer.EnemyDetected += result => boss = result.BossName;
        analyzer.AnalysisFailed += message => failure = message;
        await analyzer.RunAnalysisAsync();
        failure.Should().BeNull();
        recorded.Should().NotBeNull();
        recorded!.CoreCovenants.Should().HaveCount(10);
        recorded.AdditionalCovenants.Should().HaveCount(16);
        boss.Should().Be(expectedBoss);
        if (file == "info-ingame-1920x1080.png")
            recorded.CoreCovenants.Where(c => c.IsBanned).Select(c => c.Name).Should()
                .BeEquivalentTo("维多利亚", "谢拉格", "阿戈尔", "卡西米尔", "乌萨斯");
    }

    [Fact]
    public async Task MissingCaptureDoesNotPublishReplacementState()
    {
        var analyzer = await CreateAnalyzerAsync(null, fixture.Engine);
        int committed = 0;
        string? failure = null;
        analyzer.AnalysisCompleted += _ => committed++;
        analyzer.AutoBanListComputed += _ => committed++;
        analyzer.EnemyDetected += _ => committed++;
        analyzer.AnalysisFailed += message => failure = message;
        await analyzer.RunAnalysisAsync();
        committed.Should().Be(0);
        failure.Should().NotBeNullOrEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UnusableOcrPreservesThePreviousEnemyAndPublishesNoBanChanges(bool headersOnly)
    {
        var ocr = new Mock<IOcrEngine>();
        IReadOnlyList<OcrTextResult> texts = headersOnly
            ? [new("核心盟约", (200, 180, 160, 30)), new("附加盟约", (200, 460, 160, 30))] : [];
        ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync(texts);
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "info-ingame-1920x1080.png"));
        var analyzer = await CreateAnalyzerAsync(bytes, ocr.Object);
        var vm = new EnemyViewModel();
        vm.Update(new("盐风主教昆图斯", ["特训敌人·隐匿"], ["盐风主教昆图斯"]),
            fixture.Bosses.Lookup("盐风主教昆图斯"), [fixture.Types.LookupByTypeName("隐匿")]);
        int committed = 0;
        analyzer.AnalysisBusyChanged += busy => { if (busy) vm.BeginRecognition(); };
        analyzer.AnalysisFailed += vm.RecognitionFailed;
        analyzer.AnalysisCompleted += _ => committed++;
        analyzer.AutoBanListComputed += _ => committed++;
        analyzer.EnemyDetected += _ => committed++;
        await analyzer.RunAnalysisAsync();
        committed.Should().Be(0);
        vm.BossName.Should().Be("盐风主教昆图斯");
        vm.RecognitionStatus.Should().Contain("保留上次录入结果");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompleteCovenantsPublishWithoutEnemyAndCacheDoesNotPublishPartialEnemy(bool bossOnly)
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "info-ingame-1920x1080.png"));
        var ocr = new Mock<IOcrEngine>();
        ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<int>(), It.IsAny<int>())).Returns((byte[] png, int x, int y, int w, int h) =>
                x > 1200
                    ? Task.FromResult<IReadOnlyList<OcrTextResult>>(bossOnly
                        ? [new("敌方领袖", (1540, 460, 160, 30)), new("盐风主教昆图斯", (1500, 510, 240, 30))]
                        : [])
                    : fixture.Engine.RecognizeRegionAsync(png, x, y, w, h));
        var analyzer = await CreateAnalyzerAsync(bytes, ocr.Object);
        var results = new List<BanScreenResult>();
        var enemyEvents = 0;
        var failures = 0;
        analyzer.AnalysisCompleted += results.Add;
        analyzer.EnemyDetected += _ => enemyEvents++;
        analyzer.AnalysisFailed += _ => failures++;
        await analyzer.RunAnalysisAsync();
        await analyzer.RunAnalysisAsync();
        results.Should().HaveCount(2);
        results.Should().OnlyContain(r => r.CoreCovenants.Count == 10
            && r.AdditionalCovenants.Count == 16 && r.EnemyFaction == null);
        enemyEvents.Should().Be(0);
        failures.Should().Be(0);
    }

    private async Task<BanScreenAnalyzer> CreateAnalyzerAsync(byte[]? bytes, IOcrEngine ocr)
    {
        var store = new JsonDataStore(Path.Combine(AppContext.BaseDirectory, "data"));
        var (covenants, _) = await store.LoadCovenantsAsync();
        var strategy = new GarrisonOcrStrategy();
        return new BanScreenAnalyzer(() => Task.FromResult(bytes), ocr, strategy,
            new EnemyRecognizer(ocr, strategy, fixture.Bosses), await store.LoadOperatorsAsync(),
            covenants.Where(c => c.BondType == "SEASON").Select(c => c.Name).ToList(),
            covenants.Where(c => c.BondType != "SEASON").Select(c => c.Name).ToList());
    }

    [Fact]
    public async Task PartialNamesWithoutAnIconGridDoNotPublishAnIncompleteBanSet()
    {
        using var blank = new System.Drawing.Bitmap(1920, 1080);
        using var png = new MemoryStream();
        blank.Save(png, System.Drawing.Imaging.ImageFormat.Png);
        var ocr = new Mock<IOcrEngine>();
        ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync((byte[] _, int x, int y, int w, int h) =>
                x > 1200 ? [new("敌方领袖", (1540, 460, 160, 30)),
                    new("盐风主教昆图斯", (1500, 510, 240, 30)), new("特训敌人·隐匿", (1500, 635, 240, 30))]
                // 页面检测已折进盟约区 OCR：标题与名字会在同一次调用里返回。
                : y < 300 ? [new("核心盟约", (200, 180, 160, 30)), new("附加盟约", (200, 470, 160, 30)),
                    new("炎", (160, 390, 25, 25))]
                : [new("附加盟约", (200, 470, 160, 30)), new("迅捷", (150, 673, 50, 25))]);
        var analyzer = await CreateAnalyzerAsync(png.ToArray(), ocr.Object);
        int commits = 0;
        string? failure = null;
        analyzer.AnalysisCompleted += _ => commits++;
        analyzer.AutoBanListComputed += _ => commits++;
        analyzer.EnemyDetected += _ => commits++;
        analyzer.AnalysisFailed += message => failure = message;
        await analyzer.RunAnalysisAsync();
        commits.Should().Be(0);
        failure.Should().Contain("核心 1、附加 1");
    }
}
