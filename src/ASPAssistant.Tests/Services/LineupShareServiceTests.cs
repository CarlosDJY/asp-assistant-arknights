// Modified for ASP Assistant Regulation on 2026-11-14; see NOTICE.
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

public class LineupShareServiceTests : IDisposable
{
    [Fact]
    public async Task ExistingCacheWithoutSourceUrlKeepsItsOriginalFingerprint()
    {
        var lineup = new Lineup { Name = "已有阵容", Slots = [new() { OperatorName = "角峰" }] };
        var oldContent = JsonSerializer.SerializeToUtf8Bytes(new
        {
            // The fingerprint fields before SourceUrl existed (target covenants and recommended equipment included).
            lineup.Name, lineup.Slots, lineup.EarlySlots, lineup.MidSlots, lineup.SupportSlots, lineup.Strategies,
            lineup.TargetCovenants, lineup.RecommendedEquipment
        });
        Directory.CreateDirectory(CacheDirectory);
        var key = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(oldContent));
        await File.WriteAllTextAsync(Path.Combine(CacheDirectory, key + ".json"),
            JsonSerializer.Serialize(new { Url = "https://dpaste.com/KEPT", CreatedAt = DateTimeOffset.UtcNow }));
        using var handler = new ShareHandler();
        using var http = new HttpClient(handler);
        (await new LineupShareService(EquipmentTestData.Rules, CacheDirectory, http).ExportAsync(lineup))
            .Should().Be("https://dpaste.com/KEPT");
        handler.Calls.Should().Be(0);
    }

    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"lineup_share_{Guid.NewGuid():N}");
    private string CacheDirectory => Path.Combine(_directory, "share-links");

    public LineupShareServiceTests() => Directory.CreateDirectory(_directory);

    [Fact]
    public async Task UnchangedContentReusesLinkAfterSaveAndServiceRestart()
    {
        using var handler = new ShareHandler();
        using var http = new HttpClient(handler);
        var service = new LineupShareService(EquipmentTestData.Rules, CacheDirectory, http);
        var lineup = new Lineup { Name = "炎队", Slots = [new() { OperatorName = "角峰" }] };
        var originalUrl = await service.ExportAsync(lineup);
        (await service.ExportAsync(lineup)).Should().Be(originalUrl);

        var store = new LineupStore(Path.Combine(_directory, "lineups"), EquipmentTestData.Rules);
        await store.SaveAsync(lineup);
        lineup = (await store.LoadAllAsync()).Single();
        lineup.Id = Guid.NewGuid().ToString("N");
        lineup.CreatedAt = DateTime.UtcNow.AddDays(-1);
        lineup.UpdatedAt = DateTime.UtcNow;
        var restarted = new LineupShareService(EquipmentTestData.Rules, CacheDirectory, http);

        (await restarted.ExportAsync(lineup)).Should().Be(originalUrl);
        handler.Calls.Should().Be(1);
        Directory.GetFiles(CacheDirectory).Should().ContainSingle();
    }

    [Theory]
    [InlineData("name")]
    [InlineData("late")]
    [InlineData("mid")]
    [InlineData("early")]
    [InlineData("support")]
    [InlineData("equipment")]
    [InlineData("coreEquipment")]
    [InlineData("operatorTag")]
    [InlineData("elite")]
    [InlineData("legacyTransformation")]
    [InlineData("strategy")]
    [InlineData("strategyTag")]
    [InlineData("sourceUrl")]
    public async Task ChangedContentGetsNewLinkAndReturningToOriginalReusesItsLink(string change)
    {
        using var handler = new ShareHandler();
        using var http = new HttpClient(handler);
        var service = new LineupShareService(EquipmentTestData.Rules, CacheDirectory, http);
        var lineup = new Lineup
        {
            Name = "炎队", Slots = [new() { OperatorName = "角峰", Equipments = ["坚守盾牌"] }],
            Strategies = [new() { StrategyId = "band_mech" }]
        };
        var originalJson = JsonSerializer.Serialize(lineup);
        var originalUrl = await service.ExportAsync(lineup);
        switch (change)
        {
            case "name": lineup.Name = "新名字"; break;
            case "late": lineup.Slots[0].OperatorName = "隐现"; break;
            case "mid": lineup.MidSlots.Add(new() { OperatorName = "隐现" }); break;
            case "early": lineup.EarlySlots.Add(new() { OperatorName = "隐现" }); break;
            case "support": lineup.SupportSlots.Add(new() { OperatorName = "隐现" }); break;
            case "equipment": lineup.Slots[0].Equipments.Add("不屈弹射器"); break;
            case "coreEquipment": lineup.Slots[0].CoreEquipments.Add("坚守盾牌"); break;
            case "operatorTag": lineup.Slots[0].Tags.Add(LineupTag.阵容核心); break;
            case "elite": lineup.Slots[0].IsElite = true; break;
            case "legacyTransformation": lineup.Slots[0].TransformedCovenant = "助力"; break;
            case "strategy": lineup.Strategies[0].StrategyId = "band_amiya"; break;
            case "strategyTag": lineup.Strategies[0].Tags.Add(StrategyTag.优选); break;
            case "sourceUrl": lineup.SourceUrl = SklandGuideImporterTests.Url; break;
        }

        (await service.ExportAsync(lineup)).Should().NotBe(originalUrl);
        (await service.ExportAsync(JsonSerializer.Deserialize<Lineup>(originalJson)!)).Should().Be(originalUrl);
        handler.Calls.Should().Be(2);
    }

    [Theory]
    [InlineData(29, 1)]
    [InlineData(30, 2)]
    [InlineData(31, 2)]
    public async Task CacheReuseEndsThirtyDaysAfterGeneration(int days, int expectedUploads)
    {
        using var handler = new ShareHandler();
        using var http = new HttpClient(handler);
        var service = new LineupShareService(EquipmentTestData.Rules, CacheDirectory, http);
        var lineup = new Lineup { Name = "炎队" };
        var originalUrl = await service.ExportAsync(lineup);
        var file = Directory.GetFiles(CacheDirectory, "*.json").Single();
        var cache = JsonNode.Parse(await File.ReadAllTextAsync(file))!;
        cache["CreatedAt"] = DateTimeOffset.UtcNow.AddDays(-days);
        await File.WriteAllTextAsync(file, cache.ToJsonString());
        var restarted = new LineupShareService(EquipmentTestData.Rules, CacheDirectory, http);

        var url = await restarted.ExportAsync(lineup);

        handler.Calls.Should().Be(expectedUploads);
        url.Should().Be(expectedUploads == 1 ? originalUrl : "https://dpaste.com/TEST2");
        if (expectedUploads == 1)
            (await File.ReadAllTextAsync(file)).Should().Be(cache.ToJsonString(), "reuse must not extend the thirty-day period");
        Directory.GetFiles(CacheDirectory).Should().ContainSingle("cache writes must leave no temporary files");
    }

    [Fact]
    public async Task ConcurrentClicksUploadUnchangedContentOnlyOnce()
    {
        using var handler = new ShareHandler(async (_, call) =>
        {
            await Task.Delay(20);
            return new(HttpStatusCode.OK) { Content = new StringContent($"https://dpaste.com/TEST{call}") };
        });
        using var http = new HttpClient(handler);
        var service = new LineupShareService(EquipmentTestData.Rules, CacheDirectory, http);
        var lineup = new Lineup { Name = "炎队" };

        var urls = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => service.ExportAsync(lineup)));

        urls.Should().OnlyContain(url => url == "https://dpaste.com/TEST1");
        handler.Calls.Should().Be(1);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedUploadIsNotCachedAndCanBeRetried(bool invalidResponse)
    {
        using var handler = new ShareHandler((_, call) => Task.FromResult(new HttpResponseMessage(
            call == 1 && !invalidResponse ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
        {
            Content = new StringContent(call == 1 ? "unavailable" : "https://dpaste.com/TEST2")
        }));
        using var http = new HttpClient(handler);
        var service = new LineupShareService(EquipmentTestData.Rules, CacheDirectory, http);
        var lineup = new Lineup { Name = "炎队" };

        if (invalidResponse)
            await Assert.ThrowsAsync<InvalidOperationException>(() => service.ExportAsync(lineup));
        else
            await Assert.ThrowsAsync<HttpRequestException>(() => service.ExportAsync(lineup));
        Directory.GetFiles(CacheDirectory).Should().BeEmpty();
        (await service.ExportAsync(lineup)).Should().Be("https://dpaste.com/TEST2");
        handler.Calls.Should().Be(2);
    }

    [Theory]
    [InlineData("https://dpaste.com/TEST123")]
    [InlineData("<说明名字> https://dpaste.com/TEST123")]
    [InlineData("<https://dpaste.com/WRONG> https://dpaste.com/TEST123")]
    [InlineData("<名字中有>符号> https://dpaste.com/TEST123")]
    public async Task ImportExtractsLinkAndUsesNameInsideSharedData(string input)
    {
        using var handler = new ShareHandler((request, _) =>
        {
            request.Method.Should().Be(HttpMethod.Get);
            request.RequestUri!.AbsoluteUri.Should().Be("https://dpaste.com/TEST123.txt");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new Lineup { Name = "真实阵容名" }))
            });
        });
        using var http = new HttpClient(handler);
        var service = new LineupShareService(EquipmentTestData.Rules, CacheDirectory, http);

        (await service.ImportAsync(input)).Name.Should().Be("真实阵容名");
        handler.Calls.Should().Be(1);
        Directory.Exists(CacheDirectory).Should().BeFalse("import must not create share cache files");
    }

    [Fact]
    public async Task RawJsonContainingUrlRemainsJsonAndExportPayloadKeepsOriginalFormat()
    {
        var lineup = new Lineup { Name = "https://dpaste.com/NAME", EarlySlots = [new() { OperatorName = "角峰" }] };
        using var handler = new ShareHandler(async (request, _) =>
        {
            request.Method.Should().Be(HttpMethod.Post);
            var form = await request.Content!.ReadAsStringAsync();
            var fields = form.Split('&').Select(part => part.Split('=', 2))
                .ToDictionary(pair => pair[0], pair => Uri.UnescapeDataString(pair[1].Replace('+', ' ')));
            fields["expiry_days"].Should().Be("365");
            fields["syntax"].Should().Be("json");
            var payload = JsonSerializer.Deserialize<Lineup>(fields["content"])!;
            payload.Should().BeEquivalentTo(lineup);
            return new(HttpStatusCode.OK) { Content = new StringContent("https://dpaste.com/TEST1") };
        });
        using var http = new HttpClient(handler);
        var service = new LineupShareService(EquipmentTestData.Rules, CacheDirectory, http);

        (await service.ImportAsync(JsonSerializer.Serialize(lineup))).Should().BeEquivalentTo(lineup);
        handler.Calls.Should().Be(0);
        (await service.ExportAsync(lineup)).Should().Be("https://dpaste.com/TEST1");
        handler.Calls.Should().Be(1);
    }

    public void Dispose()
    {
        Path.GetDirectoryName(Path.GetFullPath(_directory)).Should().Be(Path.GetFullPath(Path.GetTempPath()).TrimEnd('\\', '/'));
        Directory.Delete(_directory, true);
    }

    private sealed class ShareHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, int, Task<HttpResponseMessage>> _respond;
        private int _calls;
        public int Calls => _calls;

        public ShareHandler() : this((_, call) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent($"https://dpaste.com/TEST{call}")
        })) { }

        public ShareHandler(Func<HttpRequestMessage, int, Task<HttpResponseMessage>> respond) => _respond = respond;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => _respond(request, Interlocked.Increment(ref _calls));
    }
}
