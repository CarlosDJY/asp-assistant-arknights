// Added for ASP Assistant Regulation on 2026-11-14; see NOTICE.
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Services;
using FluentAssertions;
using Xunit.Abstractions;

namespace ASPAssistant.Tests.Services;

[Collection("Native OCR")]
public class CovenantLayerReaderTests(EnemyRecognitionFixture fixture, ITestOutputHelper output) : IClassFixture<EnemyRecognitionFixture>
{
    private static async Task<List<string>> Names()
    {
        var (covenants, _) = await new JsonDataStore(Path.Combine(AppContext.BaseDirectory, "data")).LoadCovenantsAsync();
        return [.. covenants.Select(c => c.Name)];
    }

    [Fact]
    public async Task PairsEachNameWithTheNumberAboveIt()
    {
        var reader = new CovenantLayerReader(new NoOcr(), await Names());
        var layers = reader.Parse(
        [
            new("155", (590, 135, 40, 20)), new("远见", (588, 158, 44, 20)),
            new("124", (698, 135, 40, 20)), new("灵巧", (696, 158, 44, 20)),
            new("调和", (1020, 158, 44, 20)),          // no number: does not stack
            new("84精准", (800, 140, 60, 36)),         // read as one line
        ], 1.0);
        layers.Should().BeEquivalentTo(new Dictionary<string, int> { ["远见"] = 155, ["灵巧"] = 124, ["精准"] = 84 });
    }

    [Fact]
    public async Task ReadsTheBattleScreensBadgeStrip()
    {
        var png = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "covenant-layers-1920x1080.png"));
        var layers = await new CovenantLayerReader(fixture.Engine, await Names()).ReadAsync(png, 1920, 1080);
        output.WriteLine(string.Join(" ", layers.Select(kv => $"{kv.Key}={kv.Value}")));
        layers.Should().Contain("远见", 155).And.Contain("灵巧", 124).And.Contain("精准", 84).And.Contain("维多利亚", 69);
        layers.Should().NotContainKey("调和", "it shows no number");
    }

    private sealed class NoOcr : IOcrEngine
    {
        public Task<IReadOnlyList<OcrTextResult>> RecognizeRegionAsync(byte[] pngBytes, int x, int y, int width, int height) =>
            Task.FromResult<IReadOnlyList<OcrTextResult>>([]);
        public Task<IReadOnlyList<OcrTextResult>> RecognizeLineAsync(byte[] pngBytes, int x, int y, int width, int height) =>
            Task.FromResult<IReadOnlyList<OcrTextResult>>([]);
        public Task<string?> FindCandidateInRegionAsync(byte[] pngBytes, int x, int y, int width, int height, IReadOnlyList<string> candidates) =>
            Task.FromResult<string?>(null);
    }
}
