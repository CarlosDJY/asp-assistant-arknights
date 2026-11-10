using System.Text.Json;
using ASPAssistant.Core.GameModes.GarrisonProtocol;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using FluentAssertions;
using Moq;
using Xunit.Abstractions;

namespace ASPAssistant.Tests.Services;

[Collection("Native OCR")]
public class EnemyRecognizerTests(EnemyRecognitionFixture fixture, ITestOutputHelper output)
    : IClassFixture<EnemyRecognitionFixture>
{
    [Theory]
    [InlineData("ban-sample-1920x1080.png", "假想敌：鳍", "特训敌人·元素|特训敌人·频次|特训敌人·致命")]
    [InlineData("ban-sample-2-1920x1080.png", "阿利斯泰尔，帝国余晖", "特训敌人·频次|特训敌人·元素|特训敌人·复苏")]
    public async Task RealInfoScreens_ReadRightHandLeaderAndAllThreeTypes(string image, string boss, string types)
    {
        var recording = new Mock<IOcrEngine>();
        recording.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<int>(), It.IsAny<int>())).Returns(async (byte[] bytes, int x, int y, int w, int h) =>
        {
            var texts = await fixture.Engine.RecognizeRegionAsync(bytes, x, y, w, h);
            output.WriteLine(JsonSerializer.Serialize(texts.Select(t => new { t.Text, t.Box.X, t.Box.Y, t.Box.W, t.Box.H })));
            return texts;
        });
        var reader = new EnemyRecognizer(recording.Object, new GarrisonOcrStrategy(), fixture.Bosses);
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", image));
        var result = await reader.RecognizeAsync(bytes, 1920, 1080);
        result.Should().NotBeNull();
        result!.BossName.Should().Be(boss);
        result.EnemyTypes.Should().Equal(types.Split('|'));
        if (image == "ban-sample-1920x1080.png")
            fixture.Bosses.Lookup(result.BossName).Should().NotBeNull("鳍 belongs to the current season catalogue");
    }

    [Fact]
    public async Task SplitNamesAndMergedTypes_AreJoinedInScreenOrderAndDeduplicated()
    {
        var engine = new Mock<IOcrEngine>();
        engine.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync([
                new("敌方领袖", (1540, 460, 150, 30)),
                new("帝国余晖", (1500, 550, 160, 30)),
                new("频次", (1680, 635, 80, 30)),
                new("阿利斯泰尔，", (1500, 510, 170, 30)),
                new("特训敌人·", (1500, 635, 170, 30)),
                new("特训致·元素特训敌人·复苏", (1500, 700, 280, 30)),
                new("特训敌人·元素", (1500, 770, 230, 30))]);
        var result = await new EnemyRecognizer(engine.Object, new GarrisonOcrStrategy(), fixture.Bosses)
            .RecognizeAsync([], 1920, 1080);
        result!.BossName.Should().Be("阿利斯泰尔，帝国余晖");
        result.EnemyTypes.Should().Equal("特训敌人·频次", "特训敌人·元素", "特训敌人·复苏");
    }

    [Fact]
    public async Task OtherPages_DoNotProduceAnEnemyResult()
    {
        var engine = new Mock<IOcrEngine>();
        engine.Setup(e => e.RecognizeRegionAsync(It.IsAny<byte[]>(), It.IsAny<int>(), It.IsAny<int>(),
            It.IsAny<int>(), It.IsAny<int>())).ReturnsAsync([new("假想敌：胄", (1500, 510, 200, 30))]);
        (await new EnemyRecognizer(engine.Object, new GarrisonOcrStrategy(), fixture.Bosses)
            .RecognizeAsync([], 1920, 1080)).Should().BeNull();
    }

    [Theory]
    [InlineData("假想敌：刃")]
    [InlineData("假想敌：盾")]
    public void UnlistedBossIsNotReplacedWithAnotherImaginaryEnemy(string text)
    {
        fixture.Bosses.TryMatchBossFromOcr([text], out _).Should().BeFalse();
    }

    [Fact]
    public void KnownBossMatchesAcrossLineBreaksAndPunctuation()
    {
        fixture.Bosses.TryMatchBossFromOcr(["阿利斯泰尔，", "帝国余晖"], out var name).Should().BeTrue();
        name.Should().Be("阿利斯泰尔，帝国余晖");
    }

    [Theory]
    [InlineData("剧毒")]
    [InlineData("腐蚀")]
    [InlineData("飞向")]
    public void UnlistedTypeDoesNotGetUnrelatedDetailCards(string text)
        => fixture.Types.LookupByTypeName(text).Should().BeNull();
}

public sealed class EnemyRecognitionFixture : IDisposable
{
    public MaaOcrEngine Engine { get; } = new(Path.Combine(AppContext.BaseDirectory, "data"));
    public BossDataService Bosses { get; } = new();
    public EnemyTypeDataService Types { get; } = new();

    public EnemyRecognitionFixture()
    {
        var data = Path.Combine(AppContext.BaseDirectory, "data");
        Task.WhenAll(Bosses.LoadAsync(data), Types.LoadAsync(data)).GetAwaiter().GetResult();
    }

    public void Dispose() => Engine.Dispose();
}
