using System.Drawing;
using System.Drawing.Imaging;
using ASPAssistant.Core.GameModes.GarrisonProtocol;
using ASPAssistant.Core.GameState;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using FluentAssertions;
using Moq;

namespace ASPAssistant.Tests.Services;

public class GameShopThemeDetectorTests
{
    private readonly GameShopThemeDetector _detector = new(Path.Combine(AppContext.BaseDirectory, "data", "shop_theme_refs.json"));

    [Trait("Category", "LocalScreenshot")]
    [Theory]
    [InlineData("shop-terminal-tier1-color.png", GameShopTheme.Terminal)]
    [InlineData("shop-terminal-tier2-duplicates.png", GameShopTheme.Terminal)]
    [InlineData("shop-terminal-tier4-gray.png", GameShopTheme.Terminal)]
    [InlineData("shop-terminal-tier4-frozen-gray.png", GameShopTheme.Terminal)]
    [InlineData("shop-frontier-tier1-color.png", GameShopTheme.Ursus)]
    [InlineData("shop-frontier-tier2-glow.png", GameShopTheme.Ursus)]
    [InlineData("shop-frontier-tier2-frozen-gray.png", GameShopTheme.Ursus)]
    [InlineData("shop-frontier-tier4-gray-glow.png", GameShopTheme.Ursus)]
    [InlineData("shop-tier2-color.png", GameShopTheme.Closure)]
    [InlineData("shop-tier4-gray.png", GameShopTheme.Closure)]
    [InlineData("shop-tier6-frozen.png", GameShopTheme.Closure)]
    [InlineData("shop-tier6-frozen-gray-glow.png", GameShopTheme.Closure)]
    [InlineData("shop-tier6-glow-a.png", GameShopTheme.Closure)]
    [InlineData("shop-tier6-glow-b.png", GameShopTheme.Closure)]
    [InlineData("shop-closure-controls-expanded-1449.png", GameShopTheme.Closure)]
    [InlineData("shop-closure-controls-collapsed-1449.png", GameShopTheme.Closure)]
    public void RecognizesControlArtworkAcrossThemesAndUiScaling(string file, GameShopTheme expected)
        => _detector.Detect(Sample(file)).Should().Be(expected);

    [Theory]
    [InlineData("shop-terminal-tier2-duplicates.png", GameShopTheme.Terminal)]
    [InlineData("shop-frontier-tier2-glow.png", GameShopTheme.Ursus)]
    [InlineData("shop-tier2-color.png", GameShopTheme.Closure)]
    public void Recognizes720pCaptures(string file, GameShopTheme expected)
    {
        using var original = new Bitmap(new MemoryStream(Sample(file)));
        using var resized = new Bitmap(original, new Size(1280, 720));
        using var stream = new MemoryStream();
        resized.Save(stream, ImageFormat.Png);
        _detector.Detect(stream.ToArray()).Should().Be(expected);
    }

    [Trait("Category", "LocalScreenshot")]
    [Fact]
    public void NoThemeControlDoesNotGuessATheme()
    {
        _detector.Detect(Sample("ban-sample-1920x1080.png")).Should().BeNull();
        _detector.Detect(Sample("shop-closure-tier3-expanded-1449.png")).Should().BeNull();
        using var blank = new Bitmap(1920, 1080);
        using var stream = new MemoryStream();
        blank.Save(stream, ImageFormat.Png);
        _detector.Detect(stream.ToArray()).Should().BeNull();
    }

    [Trait("Category", "LocalScreenshot")]
    [Fact]
    public async Task ScannerRequiresTwoReadingsAndManualModeKeepsTheSelectedTheme()
    {
        var ocr = new Mock<IOcrEngine>();
        ocr.Setup(engine => engine.RecognizeRegionAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Array.Empty<OcrTextResult>());
        ocr.Setup(engine => engine.RecognizeLineAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<int>()))
            .ReturnsAsync(Array.Empty<OcrTextResult>());
        var frame = Sample("shop-closure-controls-expanded-1449.png");
        var reader = new ShopReader(ocr.Object, []) { Theme = GameShopTheme.Terminal };
        using var scanner = new OcrScannerService(() => frame, new GarrisonOcrStrategy(), reader, ocr.Object,
            new ASPAssistant.Core.GameState.GameState(), []) { ThemeDetector = _detector };
        var changes = new List<GameShopTheme>();
        scanner.GameThemeDetected += changes.Add;
        await scanner.ScanNowAsync();
        await scanner.ScanNowAsync();
        reader.Theme.Should().Be(GameShopTheme.Terminal);
        scanner.AutomaticGameTheme = true;
        await scanner.ScanNowAsync();
        reader.Theme.Should().Be(GameShopTheme.Terminal);
        await scanner.ScanNowAsync();
        reader.Theme.Should().Be(GameShopTheme.Closure);
        await scanner.ScanNowAsync();
        changes.Should().Equal(GameShopTheme.Closure);
        frame = Sample("ban-sample-1920x1080.png");
        await scanner.ScanNowAsync();
        reader.Theme.Should().Be(GameShopTheme.Closure);
        scanner.AutomaticGameTheme = false;
        reader.Theme = GameShopTheme.Ursus;
        frame = Sample("shop-closure-controls-expanded-1449.png");
        await scanner.ScanNowAsync();
        await scanner.ScanNowAsync();
        reader.Theme.Should().Be(GameShopTheme.Ursus);
        changes.Should().Equal(GameShopTheme.Closure);
    }

    private static byte[] Sample(string file) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", file));
}
