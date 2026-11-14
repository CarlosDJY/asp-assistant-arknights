// Modified for ASP Assistant Regulation on 2026-11-14; see NOTICE.
using ASPAssistant.Core.Data;
using ASPAssistant.Core.GameModes.GarrisonProtocol;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;
using Moq;
using Xunit.Abstractions;

namespace ASPAssistant.Tests.Services;

[Collection("Native OCR")]
public class InformationRecordingTests(EnemyRecognitionFixture fixture, ITestOutputHelper output) : IClassFixture<EnemyRecognitionFixture>
{
    [Trait("Category", "LocalScreenshot")]
    [Theory]
    [InlineData("adb-run-lobby-1280x720.png", true)]
    [InlineData("adb-opening-1280x720.png", false)]
    [InlineData("info-ingame-1920x1080.png", false)]
    [InlineData("gameplay-markers-1920x1080.png", false)]
    public async Task OnlyTheRunLobbyHasBothModeAndStartControls(string file, bool lobby)
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", file));
        var (width, height) = ImageBytes.GetDimensions(bytes);
        (await new InformationScreenDetector(fixture.Engine, new GarrisonOcrStrategy())
            .IsRunLobbyAsync(bytes, width, height)).Should().Be(lobby);
    }

    [Trait("Category", "LocalScreenshot")]
    [Theory]
    [InlineData(GameCaptureSource.Adb)]
    [InlineData(GameCaptureSource.Pc)]
    public async Task ReturningToRunLobbyAllowsTheNextAutomaticOpening(GameCaptureSource source)
    {
        var lobby = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "adb-run-lobby-1280x720.png"));
        var opening = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "adb-opening-1280x720.png"));
        byte[]? shot = lobby;
        using var connection = new GameConnectionService(new() { Source = source });
        using var scanner = new OcrScannerService(() => shot, new GarrisonOcrStrategy(),
            new ShopReader(fixture.Engine, []), fixture.Engine, new(), [], getConnection: () => connection);
        scanner.TryStartNewRun(false).Should().BeTrue();
        scanner.MarkInformationRecorded();
        var attempts = 0;
        scanner.BanScreenDetected += () => attempts++;

        // A single lobby frame interrupted by capture failure cannot rearm the opening.
        await scanner.ScanNowAsync();
        shot = null;
        await scanner.ScanNowAsync();
        shot = lobby;
        await scanner.ScanNowAsync();
        shot = opening;
        await scanner.ScanNowAsync();
        await scanner.ScanNowAsync();
        attempts.Should().Be(0);

        shot = lobby;
        await scanner.ScanNowAsync();
        await scanner.ScanNowAsync();
        attempts.Should().Be(0, "the lobby itself must not start recording or reset progress");
        shot = opening;
        await scanner.ScanNowAsync();
        attempts.Should().Be(0);
        await scanner.ScanNowAsync();
        attempts.Should().Be(1);
        scanner.TryStartNewRun(false).Should().BeTrue();
        scanner.MarkInformationRecorded();
        await scanner.ScanNowAsync();
        attempts.Should().Be(1, "the same opening must remain consumed after recording");
    }

    [Trait("Category", "LocalScreenshot")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompleteCoreSurvivesOverscrollButConflictingStatesAreRejected(bool conflict)
    {
        var before = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "solo-overscroll-before-1280x720.png"));
        var after = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "solo-overscroll-after-2560x1440.png"));
        var shot = before;
        var ocr = new Mock<IOcrEngine>();
        ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns(async (byte[] png, int x, int y, int w, int h) =>
            {
                var texts = await fixture.Engine.RecognizeRegionAsync(png, x, y, w, h);
                return conflict && ReferenceEquals(png, after)
                    ? texts.Select(t => new OcrTextResult(t.Text.Replace("萨尔贡", "谢拉格"), t.Box)).ToArray()
                    : texts;
            });
        var analyzer = await CreateCapturingAnalyzerAsync(() => Task.FromResult<byte[]?>(shot), ocr.Object);
        var results = new List<BanScreenResult>();
        var failures = new List<string>();
        var prepares = 0;
        var scrolls = 0;
        var prompts = 0;
        analyzer.ScrollInformationPanelAsync = () =>
        {
            results.Should().BeEmpty();
            prepares.Should().Be(0);
            scrolls++;
            shot = after;
            return Task.FromResult<byte[]?>(after);
        };
        analyzer.AnalysisCompleted += results.Add;
        analyzer.AnalysisFailed += failures.Add;
        analyzer.ScrollRequired += _ => prompts++;
        analyzer.PrepareNewRunAsync = _ => { prepares++; return Task.CompletedTask; };
        await analyzer.RunAnalysisAsync();
        scrolls.Should().Be(1);
        prompts.Should().Be(0, "a changed selection must not ask the user to scroll farther");
        if (conflict)
        {
            results.Should().BeEmpty();
            prepares.Should().Be(0);
            failures.Should().ContainSingle().Which.Should().Contain("变化");
            return;
        }
        failures.Should().BeEmpty();
        results.Should().ContainSingle();
        results[0].CoreCovenants.Should().HaveCount(10);
        results[0].CoreCovenants.Where(c => c.IsBanned).Select(c => c.Name)
            .Should().BeEquivalentTo("谢拉格", "莱塔尼亚", "阿戈尔", "伊比利亚", "叙拉古");
        results[0].AdditionalCovenants.Should().HaveCount(16);
        prepares.Should().Be(1);
        await analyzer.RunAnalysisAsync();
        results.Should().HaveCount(2).And.OnlyContain(r => r.CoreCovenants.Count == 10);
        scrolls.Should().Be(1);
    }

    [Trait("Category", "LocalScreenshot")]
    [Fact]
    public async Task OverscrolledCoreWithoutAnEarlierCompleteReadIsNotPublished()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "solo-overscroll-after-2560x1440.png"));
        var analyzer = await CreateAnalyzerAsync(bytes, fixture.Engine);
        var publications = 0;
        var prepares = 0;
        string? failure = null;
        analyzer.AnalysisCompleted += _ => publications++;
        analyzer.PrepareNewRunAsync = _ => { prepares++; return Task.CompletedTask; };
        analyzer.AnalysisFailed += message => failure = message;
        await analyzer.RunAnalysisAsync();
        publications.Should().Be(0);
        prepares.Should().Be(0);
        failure.Should().Contain("核心 4");
    }

    [Trait("Category", "LocalScreenshot")]
    [Fact]
    public async Task MissingOpeningTitleNeverSendsScrollInputEvenWithAClippedCovenantPanel()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "solo-left-partial-1449x815.png"));
        var ocr = new Mock<IOcrEngine>();
        ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns((byte[] png, int x, int y, int w, int h) => y == 0 && x > 0
                ? Task.FromResult<IReadOnlyList<OcrTextResult>>([])
                : fixture.Engine.RecognizeRegionAsync(png, x, y, w, h));
        var analyzer = await CreateAnalyzerAsync(bytes, ocr.Object);
        var scrolls = 0;
        var publications = 0;
        analyzer.ScrollInformationPanelAsync = () => { scrolls++; return Task.FromResult<byte[]?>(bytes); };
        analyzer.AnalysisCompleted += _ => publications++;
        await analyzer.RunAnalysisAsync();
        scrolls.Should().Be(0);
        publications.Should().Be(0);
    }

    [Trait("Category", "LocalScreenshot")]
    [Theory]
    [InlineData("solo-left-partial-1449x815.png", true)]
    [InlineData("solo-left-full-1449x815.png", false)]
    [InlineData("solo-right-partial-1449x815.png", true)]
    [InlineData("solo-right-full-1449x815.png", false)]
    [InlineData("ban-change-a-1280x720.png", true)]
    [InlineData("ban-change-b-1280x720.png", false)]
    [InlineData("ban-change-c-1280x720.png", false)]
    [InlineData("adb-short-scroll-before-1280x720.png", true)]
    [InlineData("adb-short-scroll-after-2560x1440.png", true)]
    [InlineData("adb-short-scroll-position-1449x815.png", true)]
    [InlineData("adb-two-scroll-before-1280x720.png", true)]
    [InlineData("adb-two-scroll-middle-1280x720.png", true)]
    [InlineData("adb-two-scroll-full-1280x720.png", false)]
    public async Task ActualSoloScreensDistinguishClippedBottomRowsFromCompletePanels(string file, bool needsScroll)
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", file));
        var (width, height) = ImageBytes.GetDimensions(bytes);
        (bytes, width, height) = ScreenshotNormalizer.EnsureAnalysable(bytes, width, height);
        var store = new JsonDataStore(Path.Combine(AppContext.BaseDirectory, "data"));
        var (covenants, _) = await store.LoadCovenantsAsync();
        var read = await BanCovenantReader.ReadAsync(fixture.Engine, new GarrisonOcrStrategy(), bytes, width, height,
            covenants.Where(c => c.BondType == "SEASON").Select(c => c.Name).ToList(),
            covenants.Where(c => c.BondType != "SEASON").Select(c => c.Name).ToList());
        output.WriteLine($"{file}: core={read.Core.Count}, additional={read.Additional.Count}, slots={read.AdditionalSlots}, incomplete={read.HasIncompleteAdditionalRows}, unnamed={read.UnnamedAdditional.Count}, needsScroll={read.NeedsScroll}");
        read.NeedsScroll.Should().Be(needsScroll);
        if (!needsScroll) read.Additional.Should().HaveCount(16);
    }

    [Trait("Category", "LocalScreenshot")]
    [Fact]
    public async Task TwoAdbSwipesPublishOnlyTheCompletePanelAndKeepTheOriginalCore()
    {
        var before = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "adb-two-scroll-before-1280x720.png"));
        var middle = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "adb-two-scroll-middle-1280x720.png"));
        var full = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "adb-two-scroll-full-1280x720.png"));
        var analyzer = await CreateCapturingAnalyzerAsync(() => Task.FromResult<byte[]?>(before), fixture.Engine);
        var scrolls = 0;
        var prepares = 0;
        var results = new List<BanScreenResult>();
        var failures = new List<string>();
        analyzer.ScrollInformationPanelAsync = () =>
        {
            results.Should().BeEmpty();
            prepares.Should().Be(0);
            return Task.FromResult<byte[]?>(++scrolls == 1 ? middle : full);
        };
        analyzer.AnalysisCompleted += results.Add;
        analyzer.AnalysisFailed += failures.Add;
        analyzer.PrepareNewRunAsync = _ => { prepares++; return Task.CompletedTask; };
        await analyzer.RunAnalysisAsync();
        scrolls.Should().Be(2);
        prepares.Should().Be(1);
        failures.Should().BeEmpty();
        results.Should().ContainSingle();
        results[0].CoreCovenants.Should().HaveCount(10);
        results[0].CoreCovenants.Where(c => c.IsBanned).Select(c => c.Name)
            .Should().BeEquivalentTo("炎", "叙拉古", "谢拉格", "卡西米尔", "乌萨斯");
        results[0].AdditionalCovenants.Should().HaveCount(16);
        results[0].AdditionalCovenants.Where(c => c.IsBanned).Select(c => c.Name)
            .Should().BeEquivalentTo("迅捷", "坚守", "远见", "聚合", "奥术");
        results[0].EnemyFaction.Should().Be("假想敌：铳");
    }

    [Trait("Category", "LocalScreenshot")]
    [Fact]
    public async Task ShortAdbScrollKeepsScrollingAndPromptsAtTheLimitWithoutPublishing()
    {
        var before = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "adb-short-scroll-before-1280x720.png"));
        var after = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "adb-short-scroll-after-2560x1440.png"));
        var analyzer = await CreateCapturingAnalyzerAsync(() => Task.FromResult<byte[]?>(before), fixture.Engine);
        var scrolls = 0;
        var publications = 0;
        var prepares = 0;
        var prompts = 0;
        var failures = new List<string>();
        var busy = false;
        analyzer.AnalysisBusyChanged += value => busy = value;
        analyzer.ScrollInformationPanelAsync = () =>
        {
            scrolls++;
            publications.Should().Be(0);
            prepares.Should().Be(0);
            return Task.FromResult<byte[]?>(after);
        };
        analyzer.AnalysisCompleted += _ => publications++;
        analyzer.PrepareNewRunAsync = _ => { prepares++; return Task.CompletedTask; };
        analyzer.AnalysisFailed += failures.Add;
        analyzer.ScrollRequired += _ => { busy.Should().BeFalse(); prompts++; };
        await analyzer.RunAnalysisAsync();
        scrolls.Should().Be(3);
        publications.Should().Be(0);
        prepares.Should().Be(0);
        failures.Should().ContainSingle().Which.Should().Contain("未完整显示");
        prompts.Should().Be(1);
    }

    [Trait("Category", "LocalScreenshot")]
    [Theory]
    [InlineData("left")]
    [InlineData("right")]
    public async Task AutoScrollReadsTheCompleteSoloPanelBeforePublishingOrResetting(string side)
    {
        var before = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", $"solo-{side}-partial-1449x815.png"));
        var after = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", $"solo-{side}-full-1449x815.png"));
        var shot = before;
        var analyzer = await CreateCapturingAnalyzerAsync(() => Task.FromResult<byte[]?>(shot), fixture.Engine);
        var scrolls = 0;
        var publications = 0;
        var prepares = 0;
        var prompts = 0;
        var failures = 0;
        analyzer.ScrollInformationPanelAsync = () =>
        {
            publications.Should().Be(0);
            prepares.Should().Be(0, "a partial Ban set must not clear progress");
            scrolls++;
            shot = after;
            return Task.FromResult<byte[]?>(shot);
        };
        analyzer.PrepareNewRunAsync = _ => { prepares++; return Task.CompletedTask; };
        analyzer.ScrollRequired += _ => prompts++;
        analyzer.AnalysisFailed += _ => failures++;
        analyzer.AnalysisCompleted += r => { publications++; r.CoreCovenants.Should().HaveCount(10); r.AdditionalCovenants.Should().HaveCount(16); };
        await analyzer.RunAnalysisAsync();
        scrolls.Should().Be(1);
        publications.Should().Be(1);
        prepares.Should().Be(1);
        failures.Should().Be(0);
        prompts.Should().Be(0);
        await analyzer.RunAnalysisAsync();
        scrolls.Should().Be(1, "a complete cached panel requires no scrolling");
        publications.Should().Be(2);
    }

    [Trait("Category", "LocalScreenshot")]
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedAutoScrollIsBoundedAndPromptsAfterReleasingTheInputMask(bool captureFails)
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "solo-left-partial-1449x815.png"));
        var analyzer = await CreateAnalyzerAsync(bytes, fixture.Engine);
        var scrolls = 0;
        var prompts = 0;
        var publications = 0;
        var prepares = 0;
        var busy = false;
        analyzer.ScrollInformationPanelAsync = () => { scrolls++; return Task.FromResult(captureFails ? null : (byte[]?)bytes); };
        analyzer.AnalysisBusyChanged += value => busy = value;
        analyzer.PrepareNewRunAsync = _ => { prepares++; return Task.CompletedTask; };
        analyzer.AnalysisCompleted += _ => publications++;
        analyzer.ScrollRequired += message =>
        {
            busy.Should().BeFalse();
            message.Should().Contain("向下滚动").And.Contain("重新录入");
            prompts++;
        };
        await analyzer.RunAnalysisAsync();
        await analyzer.RunAnalysisAsync();
        scrolls.Should().Be(captureFails ? 2 : 6);
        prompts.Should().Be(1, "repeated incomplete attempts must not repeat the same reminder");
        publications.Should().Be(0);
        prepares.Should().Be(0);
    }

    [Trait("Category", "LocalScreenshot")]
    [Fact]
    public async Task ScrolledOpeningStillStartsWithTheCoreHeaderOutsideTheViewport()
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "opening-scrolled-1280x720.png"));
        var detector = new InformationScreenDetector(fixture.Engine, new GarrisonOcrStrategy());
        (await detector.IsInformationScreenAsync(bytes, 1280, 720)).Should().BeFalse("the core header has scrolled out of view");
        (await detector.IsOpeningScreenAsync(bytes, 1280, 720)).Should().BeTrue();
        var analyzer = await CreateAnalyzerAsync(bytes, fixture.Engine);
        var state = new InformationScreenState();
        var resets = 0;
        analyzer.PrepareNewRunAsync = changed =>
        {
            if (state.TryStartNewRun(changed)) resets++;
            return Task.CompletedTask;
        };
        await analyzer.RunAnalysisAsync();
        await analyzer.RunAnalysisAsync();
        resets.Should().Be(0, "incomplete covenant reads must preserve progress and must not establish a new run");
    }

    [Theory]
    [InlineData("ban-sample-1920x1080.png", true)]
    [InlineData("info-ingame-1920x1080.png", false)]
    public async Task ManualRecordingResetsOnlyTheFirstOpeningAndAwaitsResetBeforePublishing(string file, bool opening)
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", file));
        var analyzer = await CreateAnalyzerAsync(bytes, fixture.Engine);
        var state = new InformationScreenState();
        // Stands in for the run progress (terminal level, economy) that App resets in PrepareNewRunAsync.
        var progress = 5;
        var resets = 0;
        var publications = 0;
        string? failure = null;
        analyzer.PrepareNewRunAsync = async changed =>
        {
            if (!state.TryStartNewRun(changed)) return;
            await Task.Yield();
            progress = 0;
            resets++;
        };
        analyzer.AnalysisFailed += message => failure = message;
        analyzer.AnalysisCompleted += _ =>
        {
            state.MarkRecorded();
            publications++;
            if (publications == 1) (progress == 0).Should().Be(opening, "reset must finish before the result is published");
        };
        await analyzer.RunAnalysisAsync();
        failure.Should().BeNull();
        publications.Should().Be(1);
        resets.Should().Be(opening ? 1 : 0);
        progress = 3;
        await analyzer.RunAnalysisAsync(); // Includes the identical-screenshot cache path.
        progress.Should().Be(3, "a manual retry inside the same run keeps its progress");
        resets.Should().Be(opening ? 1 : 0);
        publications.Should().Be(2);
        if (!opening) return;
        state.Observe(false, true);
        state.Observe(false, true);
        await analyzer.RunAnalysisAsync();
        resets.Should().Be(2);
        progress.Should().Be(0);
    }

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

    private Task<BanScreenAnalyzer> CreateAnalyzerAsync(byte[]? bytes, IOcrEngine ocr)
        => CreateCapturingAnalyzerAsync(() => Task.FromResult(bytes), ocr);

    private async Task<BanScreenAnalyzer> CreateCapturingAnalyzerAsync(Func<Task<byte[]?>> capture, IOcrEngine ocr)
    {
        var store = new JsonDataStore(Path.Combine(AppContext.BaseDirectory, "data"));
        var (covenants, _) = await store.LoadCovenantsAsync();
        var strategy = new GarrisonOcrStrategy();
        return new BanScreenAnalyzer(capture, ocr, strategy,
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
                // The same visible texts are returned whether the caller scans the full panel or a narrow band.
                : new OcrTextResult[] { new("核心盟约", (200, 180, 160, 30)),
                    new("附加盟约", (200, 470, 160, 30)), new("炎", (160, 390, 25, 25)),
                    new("迅捷", (150, 673, 50, 25)) }
                    .Where(t => t.Box.X >= x && t.Box.X + t.Box.W <= x + w
                        && t.Box.Y >= y && t.Box.Y + t.Box.H <= y + h).ToArray());
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
