using System.Text.Json;
using ASPAssistant.Core.GameModes.GarrisonProtocol;
using ASPAssistant.Core.Services;
using FluentAssertions;
using Moq;

namespace ASPAssistant.Tests.Services;

/// <summary>
/// Offline regression for the "确认本局信息" screen, driven by two real 1920×1080 captures
/// with the OCR output the game produced for those frames.
///
/// Sample 1 (ban-sample-1920x1080.png): every label read cleanly, one line per covenant.
/// Sample 2 (ban-sample-2-1920x1080.png): OCR merged "伊比利亚维多利亚" into one line and the
/// second additional row sits lower — both used to drop covenants from the result.
/// </summary>
public class BanCovenantReaderTests
{
    private const int Width = 1920;
    private const int Height = 1080;

    // Measured on both samples: column pitch 116 px, name text height 19 px,
    // core names at y≈390, additional rows at y≈673 and y≈868.
    private const int ColumnPitch = 116;
    private const int FirstColumnCenter = 175;
    private const int NameTextHeight = 19;
    private const int CoreNameTop = 390;
    private const int AdditionalRow1NameTop = 673;
    private const int AdditionalRow2NameTop = 868;

    private static string SamplePath(string fileName) =>
        Path.Combine(AppContext.BaseDirectory, "TestData", fileName);

    private static byte[] LoadSample(string fileName) => File.ReadAllBytes(SamplePath(fileName));

    private static (IReadOnlyList<string> Core, IReadOnlyList<string> Additional) LoadCovenantNames()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "TestData", "Regulation", "covenants.json");
        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var core = new List<string>();
        var additional = new List<string>();
        foreach (var element in document.RootElement.GetProperty("covenants").EnumerateArray())
        {
            var name = element.GetProperty("name").GetString() ?? "";
            var bondType = element.TryGetProperty("bondType", out var bt) ? bt.GetString() ?? "" : "";
            if (string.IsNullOrEmpty(name)) continue;
            if (string.Equals(bondType, "SEASON", StringComparison.OrdinalIgnoreCase)) core.Add(name);
            else additional.Add(name);
        }
        return (core, additional);
    }

    private static OcrTextResult Text(string text, int slot, int nameTop)
    {
        int width = NameTextHeight * text.Length;
        int centerX = FirstColumnCenter + ColumnPitch * slot;
        return new OcrTextResult(text, (centerX - width / 2, nameTop, width, NameTextHeight));
    }

    /// <summary>A single OCR line that spans several columns, the way the game's OCR merges labels.</summary>
    private static OcrTextResult Merged(string text, int firstSlot, int lastSlot, int nameTop)
    {
        int left = FirstColumnCenter + ColumnPitch * firstSlot - NameTextHeight;
        int right = FirstColumnCenter + ColumnPitch * lastSlot + NameTextHeight;
        return new OcrTextResult(text, (left, nameTop, right - left, NameTextHeight));
    }

    private static List<OcrTextResult> Sample1Core() =>
    [
        Text("维多利亚", 0, CoreNameTop),
        Text("谢拉格", 1, CoreNameTop),
        Text("卡西米尔", 2, CoreNameTop),
        Text("伊比利亚", 3, CoreNameTop),
        Text("莱塔尼亚", 4, CoreNameTop),
        Text("炎", 5, CoreNameTop),
        Text("萨尔贡", 6, CoreNameTop),
        Text("阿戈尔", 7, CoreNameTop),
        Text("叙拉古", 8, CoreNameTop),
        Text("乌萨斯", 9, CoreNameTop),
    ];

    private static List<OcrTextResult> Sample1Additional() =>
    [
        Text("迅捷", 0, AdditionalRow1NameTop),
        Text("助力", 1, AdditionalRow1NameTop),
        Text("远见", 2, AdditionalRow1NameTop),
        Text("投资人", 3, AdditionalRow1NameTop),
        Text("突袭", 4, AdditionalRow1NameTop),
        Text("不屈", 5, AdditionalRow1NameTop),
        Text("调和", 6, AdditionalRow1NameTop),
        Text("协防干员", 7, AdditionalRow1NameTop),
        Text("独行", 8, AdditionalRow1NameTop),
        Text("绝技", 9, AdditionalRow1NameTop),
        Text("聚合", 0, AdditionalRow2NameTop),
        Text("精准", 1, AdditionalRow2NameTop),
        Text("灵巧", 2, AdditionalRow2NameTop),
        Text("坚守", 3, AdditionalRow2NameTop),
        Text("奇迹", 4, AdditionalRow2NameTop),
        Text("奥术", 5, AdditionalRow2NameTop),
    ];

    private static List<OcrTextResult> Sample2Core() =>
    [
        Text("炎", 0, CoreNameTop),
        Text("萨尔贡", 1, CoreNameTop),
        Text("谢拉格", 2, CoreNameTop),
        Text("卡西米尔", 3, CoreNameTop),
        // The real frame produced exactly this: two neighbours in one OCR line.
        Merged("伊比利亚维多利亚", 4, 5, CoreNameTop),
        Text("阿戈尔", 6, CoreNameTop),
        Text("叙拉古", 7, CoreNameTop),
        Text("乌萨斯", 8, CoreNameTop),
        Text("莱塔尼亚", 9, CoreNameTop),
    ];

    private static List<OcrTextResult> Sample2Additional() =>
    [
        Text("迅捷", 0, AdditionalRow1NameTop),
        Text("灵巧", 1, AdditionalRow1NameTop),
        Text("坚守", 2, AdditionalRow1NameTop),
        Text("远见", 3, AdditionalRow1NameTop),
        Text("奇迹", 4, AdditionalRow1NameTop),
        Text("投资人", 5, AdditionalRow1NameTop),
        Text("突袭", 6, AdditionalRow1NameTop),
        Text("调和", 7, AdditionalRow1NameTop),
        Text("协防干员", 8, AdditionalRow1NameTop),
        Text("独行", 9, AdditionalRow1NameTop),
        Text("绝技", 0, AdditionalRow2NameTop),
        Text("精准", 1, AdditionalRow2NameTop),
        Text("助力", 2, AdditionalRow2NameTop),
        Text("不屈", 3, AdditionalRow2NameTop),
        Text("聚合", 4, AdditionalRow2NameTop),
        Text("奥术", 5, AdditionalRow2NameTop),
    ];

    [Fact]
    public async Task Sample1_ReadsBothPanelsAndMarksGreyIconsAsBanned()
    {
        var (coreNames, additionalNames) = LoadCovenantNames();
        var ocr = new StubOcrEngine(Sample1Core(), Sample1Additional());

        var read = await BanCovenantReader.ReadAsync(
            ocr, new GarrisonOcrStrategy(), LoadSample("ban-sample-1920x1080.png"),
            Width, Height, coreNames, additionalNames);

        var core = read.Core;
        var additional = read.Additional;

        core.Should().HaveCount(10);
        core.Where(c => c.IsBanned).Select(c => c.Name).Should().BeEquivalentTo(
            ["炎", "萨尔贡", "阿戈尔", "叙拉古", "乌萨斯"]);

        // 绝技 (bondId suntShip, REGULAR) has no operator of its own but is listed on the
        // ban screen, so all 16 additional icons can be named.
        additional.Should().HaveCount(16);
        additional.Where(c => c.IsBanned).Select(c => c.Name).Should().BeEquivalentTo(
            ["精准", "灵巧", "坚守", "奇迹", "奥术"]);

        // Every icon found on screen received a name.
        read.UnnamedCore.Should().BeEmpty();
        read.UnnamedAdditional.Should().BeEmpty();
    }

    [Fact]
    public async Task Sample2_SplitsMergedLabelsAndReadsTheLowerRow()
    {
        var (coreNames, additionalNames) = LoadCovenantNames();
        var ocr = new StubOcrEngine(Sample2Core(), Sample2Additional());

        var read = await BanCovenantReader.ReadAsync(
            ocr, new GarrisonOcrStrategy(), LoadSample("ban-sample-2-1920x1080.png"),
            Width, Height, coreNames, additionalNames);

        var core = read.Core;
        var additional = read.Additional;

        // "伊比利亚维多利亚" must yield two covenants, each with its own icon state.
        core.Should().HaveCount(10);
        core.Where(c => c.IsBanned).Select(c => c.Name).Should().BeEquivalentTo(
            ["维多利亚", "阿戈尔", "叙拉古", "乌萨斯", "莱塔尼亚"]);
        core.Where(c => !c.IsBanned).Select(c => c.Name).Should().BeEquivalentTo(
            ["炎", "萨尔贡", "谢拉格", "卡西米尔", "伊比利亚"]);

        // The lower additional row (y≈868) must be inside the scanned band.
        additional.Should().HaveCount(16);
        additional.Where(c => c.IsBanned).Select(c => c.Name).Should().BeEquivalentTo(
            ["精准", "助力", "不屈", "聚合", "奥术"]);
        additional.Single(c => c.Name == "绝技").IsBanned.Should().BeFalse();

        read.UnnamedCore.Should().BeEmpty();
        read.UnnamedAdditional.Should().BeEmpty();
    }

    [Fact]
    public void IconGrid_FindsEverySlotAndItsStateFromPixels()
    {
        var sample = LoadSample("ban-sample-1920x1080.png");

        var coreSlots = BanIconGrid.Detect(sample, Width, Height, 0.16, 0.40);
        var additionalSlots = BanIconGrid.Detect(sample, Width, Height, 0.43, 0.85);

        coreSlots.Should().HaveCount(10);
        coreSlots.Count(s => s.GreenFraction > BanIconBrightness.BannedThreshold).Should().Be(5);
        coreSlots.Should().BeInAscendingOrder(s => s.CenterX);

        additionalSlots.Should().HaveCount(16);
        additionalSlots.Count(s => s.GreenFraction > BanIconBrightness.BannedThreshold).Should().Be(11);
    }

    [Fact]
    public async Task MissingLabel_IsReportedAsUnnamedIconInsteadOfBeingDropped()
    {
        var (coreNames, additionalNames) = LoadCovenantNames();
        // OCR lost "卡西米尔" (slot 2); the icon is still on screen and must be reported.
        var core = Sample1Core().Where(t => t.Text != "卡西米尔").ToList();
        var ocr = new StubOcrEngine(core, Sample1Additional());

        var read = await BanCovenantReader.ReadAsync(
            ocr, new GarrisonOcrStrategy(), LoadSample("ban-sample-1920x1080.png"),
            Width, Height, coreNames, additionalNames);

        read.Core.Should().HaveCount(9);
        read.UnnamedCore.Should().HaveCount(1);
        read.UnnamedCore[0].X.Should().BeApproximately(FirstColumnCenter + ColumnPitch * 2, ColumnPitch * 0.4);
        read.UnnamedCore[0].IsBanned.Should().BeFalse();
        read.UnnamedAdditional.Should().BeEmpty();
    }

    [Fact]
    public async Task SectionTitlesAndFragmentsAreNotMatchedAsCovenants()
    {
        var (coreNames, additionalNames) = LoadCovenantNames();
        var additional = Sample1Additional();
        // Real OCR noise from a scrolled frame: the section title used to be fuzzy-matched
        // onto "协防干员", and the single character "人" onto "投资人".
        additional.Add(new OcrTextResult("本局禁用干员", (990, AdditionalRow1NameTop, 144, NameTextHeight)));
        additional.Add(new OcrTextResult("人", (520, AdditionalRow1NameTop, 24, NameTextHeight)));

        var ocr = new StubOcrEngine(Sample1Core(), additional);
        var read = await BanCovenantReader.ReadAsync(
            ocr, new GarrisonOcrStrategy(), LoadSample("ban-sample-1920x1080.png"),
            Width, Height, coreNames, additionalNames);

        read.Additional.Should().OnlyHaveUniqueItems(c => c.Name);
        read.Additional.Should().HaveCount(16);
        read.Additional.Count(c => c.Name == "协防干员").Should().Be(1);
    }

    [Fact]
    public void IconBrightnessSeparatesAvailableFromBannedIcons()
    {
        var sample = LoadSample("ban-sample-1920x1080.png");
        var names = Sample1Core();

        var fractions = BanIconBrightness.MeasureGreenFractions(sample, Width, Height, names);

        fractions.Take(5).Should().OnlyContain(f => f > 0.3);
        fractions.Skip(5).Should().OnlyContain(f => f < 0.05);
    }

    [Fact]
    public async Task FirstRowLabelsDoNotPreventRecoveringTheSameColumnsInSecondRow()
    {
        var (coreNames, additionalNames) = LoadCovenantNames();
        var secondRow = Sample1Additional().Skip(10).ToList();
        var compositeCalls = 0;
        var ocr = new Mock<IOcrEngine>();
        ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .Returns((byte[] _, int x, int y, int _, int height) =>
            {
                IReadOnlyList<OcrTextResult> texts;
                if (x == 0 && y == 0)
                {
                    compositeCalls++;
                    texts = secondRow.Select((label, index) => new OcrTextResult(label.Text,
                        (5, index * height / secondRow.Count + 5, 50, 15))).ToList();
                }
                else
                    texts = y < 300 ? Sample1Core() : Sample1Additional().Take(10).ToList();
                return Task.FromResult(texts);
            });

        var read = await BanCovenantReader.ReadAsync(ocr.Object, new GarrisonOcrStrategy(),
            LoadSample("ban-sample-1920x1080.png"), Width, Height, coreNames, additionalNames);

        compositeCalls.Should().Be(1);
        read.Additional.Select(c => c.Name).Should().BeEquivalentTo(additionalNames);
        read.UnnamedAdditional.Should().BeEmpty();
        read.Additional.Where(c => c.IsBanned).Select(c => c.Name).Should()
            .BeEquivalentTo("精准", "灵巧", "坚守", "奇迹", "奥术");
    }

    [Fact]
    public void RightClippedMapBorderIsNotAnEleventhCoreIcon()
    {
        var sample = LoadSample("ban-sample-1920x1080.png");
        var core = BanIconGrid.Detect(sample, Width, Height, .16, .40);
        core.Should().HaveCount(10);
        using var input = new MemoryStream(sample);
        using var bitmap = new System.Drawing.Bitmap(input);
        using (var graphics = System.Drawing.Graphics.FromImage(bitmap))
            graphics.FillRectangle(System.Drawing.Brushes.Gray, 1320, core[0].Top, 80, core[0].Bottom - core[0].Top);
        using var output = new MemoryStream();
        bitmap.Save(output, System.Drawing.Imaging.ImageFormat.Png);

        var detected = BanIconGrid.Detect(output.ToArray(), Width, Height, .16, .40);

        detected.Should().HaveCount(10);
        detected.Select(s => s.CenterX).Should().Equal(core.Select(s => s.CenterX));
    }

    [Theory]
    [InlineData("添核心盘约", "附加盟药", true)]
    [InlineData("核心明約", "附加盟約", true)]
    [InlineData("协议数据库", "成就统计", false)]
    public async Task SectionReaderAndInformationDetectorAgreeOnObservedHeaderTypos(string core, string additional, bool expected)
    {
        var ocr = new Mock<IOcrEngine>();
        ocr.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync([new(core, (100, 180, 140, 25)), new(additional, (100, 500, 140, 25))]);
        var (coreNames, additionalNames) = LoadCovenantNames();
        var strategy = new GarrisonOcrStrategy();
        var sample = LoadSample("ban-sample-1920x1080.png");

        var read = await BanCovenantReader.ReadAsync(ocr.Object, strategy, sample, Width, Height, coreNames, additionalNames);

        read.SawCoreHeader.Should().Be(expected);
        read.SawAdditionalHeader.Should().Be(expected);
        read.UnnamedCore.Should().NotBeEmpty("correcting titles must not bypass missing-name checks");
        (await new InformationScreenDetector(ocr.Object, strategy).IsInformationScreenAsync(sample, Width, Height)).Should().Be(expected);
    }

    /// <summary>
    /// Feeds pre-recorded OCR boxes. The two section bands are distinguished by their Y
    /// origin: the core band starts near the top, the additional band below the middle.
    /// </summary>
    private sealed class StubOcrEngine(
        IReadOnlyList<OcrTextResult> core,
        IReadOnlyList<OcrTextResult> additional) : IOcrEngine
    {
        public Task<IReadOnlyList<OcrTextResult>> RecognizeRegionAsync(
            byte[] pngBytes, int x, int y, int width, int height) =>
            Task.FromResult(y < 300 ? core : additional);

        public Task<string?> FindCandidateInRegionAsync(
            byte[] pngBytes, int x, int y, int width, int height, IReadOnlyList<string> candidates) =>
            Task.FromResult<string?>(null);

        public Task<IReadOnlyList<OcrTextResult>> RecognizeLineAsync(
            byte[] pngBytes, int x, int y, int width, int height) => throw new NotSupportedException();
    }
}
