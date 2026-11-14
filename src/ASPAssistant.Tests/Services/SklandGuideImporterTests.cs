// Modified for ASP Assistant Regulation on 2026-11-14; see NOTICE.
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Nodes;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

public class SklandGuideImporterTests
{
    public const string Url = "https://game.skland.com/arknights/stronghold-guide/detail?header=0&guideId=2120392178422509756";
    private static string Fixture => File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "skland-guide-import.json"));

    private static async Task<(List<Operator> Operators, List<Strategy> Strategies)> CatalogAsync()
    {
        var store = new JsonDataStore(Path.Combine(AppContext.BaseDirectory, "data"));
        return ((await store.LoadOperatorsAsync()).ToList(), (await store.LoadStrategiesAsync()).ToList());
    }

    [Fact]
    public async Task OfficialExampleMapsEightEliteOperatorsAndStrategyWithoutInventingEquipmentOrRounds()
    {
        var (operators, strategies) = await CatalogAsync();
        using var handler = new GuideHandler(Fixture);
        using var http = new HttpClient(handler);
        var importer = new SklandGuideImporter(http, operators, strategies);
        var result = await importer.ImportAsync(Url);
        result.Lineup.Name.Should().Be("2本伊比利亚能兜底能打boss流派");
        result.Lineup.Slots.Select(x => x.OperatorName).Should().Equal("蓝毒", "瑰盐", "霍尔海雅", "棘刺", "空弦", "流明", "旅骨", "海沫");
        result.Lineup.Slots.Should().OnlyContain(x => x.IsElite && x.Equipments.Count == 0 && x.Tags.Count == 0);
        result.Lineup.Strategies.Should().ContainSingle().Which.StrategyId.Should().Be("band_bonus");
        result.Lineup.EarlySlots.Should().BeEmpty();
        result.Lineup.MidSlots.Should().BeEmpty();
        result.Lineup.SupportSlots.Should().BeEmpty();
        result.Lineup.SourceUrl.Should().Be(Url);
        result.Notice.Should().Contain("20 件").And.Contain("2 段").And.Contain("不自动追踪");
        handler.Calls.Should().Be(1);
        handler.RequestUri!.Host.Should().Be("zonai.skland.com");
        handler.RequestUri.AbsolutePath.Should().Be("/web/v1/game/garrison/guide/info");
        handler.RequestUri.Query.Should().Contain("guideId=2120392178422509756");
        (await importer.ImportAsync(Url)).Lineup.Id.Should().NotBe(result.Lineup.Id);
    }

    [Theory]
    [InlineData("https://evil.example/arknights/stronghold-guide/detail?guideId=1")]
    [InlineData("https://game.skland.com.evil.example/arknights/stronghold-guide/detail?guideId=1")]
    [InlineData("http://game.skland.com/arknights/stronghold-guide/detail?guideId=1")]
    [InlineData("https://name@game.skland.com/arknights/stronghold-guide/detail?guideId=1")]
    [InlineData("https://game.skland.com:8443/arknights/stronghold-guide/detail?guideId=1")]
    [InlineData("https://game.skland.com/arknights/stronghold-guide?header=0")]
    [InlineData("https://game.skland.com/arknights/stronghold-guide/detail")]
    [InlineData("https://game.skland.com/arknights/stronghold-guide/detail?guideId=abc")]
    [InlineData("https://game.skland.com/arknights/stronghold-guide/detail?guideId=1%26other%3D2")]
    public async Task InvalidLinksAreRejectedBeforeAnyRequest(string url)
    {
        using var handler = new GuideHandler(Fixture);
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<ArgumentException>(() => new SklandGuideImporter(http, [], []).ImportAsync(url));
        handler.Calls.Should().Be(0);
    }

    [Theory]
    [InlineData("unavailable")]
    [InlineData("invalid")]
    [InlineData("mismatch")]
    [InlineData("missing")]
    [InlineData("noFormation")]
    [InlineData("multipleFormations")]
    [InlineData("emptyFormation")]
    [InlineData("duplicate")]
    [InlineData("unknownOperator")]
    [InlineData("tierMismatch")]
    [InlineData("unknownStrategy")]
    [InlineData("unsupportedElite")]
    public async Task UnsupportedResponsesNeverReturnAPartialDraft(string change)
    {
        var (operators, strategies) = await CatalogAsync();
        var json = JsonNode.Parse(Fixture)!;
        var guide = json["data"]!["guide"]!["guide"]!;
        var tabs = guide["tabs"]!.AsArray();
        var chars = tabs[0]!["recChars"]!.AsArray();
        var resource = json["data"]!["seasonResourceMap"]!["act3autochess"]!["versionResourceMap"]!["V3_1"]!;
        switch (change)
        {
            case "unavailable": json["code"] = 1; json["message"] = "攻略不存在"; break;
            case "invalid": json["data"]!["guide"]!["invalid"] = 1; break;
            case "mismatch": guide["guideId"] = "123"; break;
            case "missing": json.AsObject().Remove("data"); break;
            case "noFormation": tabs[0]!["tabType"] = 2; break;
            case "multipleFormations": tabs.Add(tabs[0]!.DeepClone()); break;
            case "emptyFormation": chars.Clear(); break;
            case "duplicate": chars[1] = chars[0]!.DeepClone(); break;
            case "unknownOperator": resource["chars"]!["char_129_bluep"]!["name"] = "未收录干员"; break;
            case "tierMismatch": resource["chars"]!["char_129_bluep"]!["shopLevel"] = 1; break;
            case "unknownStrategy": strategies.Clear(); break;
            case "unsupportedElite": chars[0]!["mergeLevel"] = 2; break;
        }
        using var handler = new GuideHandler(json.ToJsonString());
        using var http = new HttpClient(handler);
        await Assert.ThrowsAsync<InvalidDataException>(() => new SklandGuideImporter(http, operators, strategies).ImportAsync(Url));
    }

    [Theory]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(20)]
    public async Task CandidatesUseTierPhasesOnlyAboveNineAndSurviveEditingAndTracking(int count)
    {
        var (operators, strategies) = await CatalogAsync();
        var candidates = Enumerable.Range(0, 4).SelectMany(index => Enumerable.Range(1, 6)
            .Select(tier => operators.Where(x => x.Tier == tier && x.RequiredStrategyId.Length == 0 && !x.IsDiy)
                .ElementAt(index))).Take(count).ToArray();
        using var handler = new GuideHandler(CandidateGuide(candidates));
        using var http = new HttpClient(handler);
        var result = await new SklandGuideImporter(http, operators, strategies).ImportAsync(Url);
        result.Lineup.EarlySlots.Select(s => s.OperatorName).Should()
            .Equal(candidates.Where(op => count > 9 && op.Tier <= 2).Select(op => op.Name));
        result.Lineup.MidSlots.Select(s => s.OperatorName).Should()
            .Equal(candidates.Where(op => count > 9 && op.Tier is 3 or 4).Select(op => op.Name));
        result.Lineup.Slots.Select(s => s.OperatorName).Should()
            .Equal(candidates.Where(op => count <= 9 || op.Tier >= 5).Select(op => op.Name));
        var allSlots = result.Lineup.EarlySlots.Concat(result.Lineup.MidSlots).Concat(result.Lineup.Slots).ToArray();
        allSlots.Should().HaveCount(count);
        foreach (var slot in allSlots)
            slot.IsElite.Should().Be(Array.FindIndex(candidates, op => op.Name == slot.OperatorName) % 2 == 1);
        if (count > 9) result.Notice.Should().Contain("按本数分组").And.Contain("全部保留");
        var calculator = new CovenantCalculator(operators, [], new Dictionary<string, string>());
        var editor = new LineupEditorViewModel(result.Lineup, operators, [], calculator);
        editor.ValidationError.Should().BeEmpty();
        editor.Reset();
        var saved = editor.ToLineup();
        saved.EarlySlots.Select(s => s.OperatorName).Should().Equal(result.Lineup.EarlySlots.Select(s => s.OperatorName));
        saved.MidSlots.Select(s => s.OperatorName).Should().Equal(result.Lineup.MidSlots.Select(s => s.OperatorName));
        saved.Slots.Select(s => s.OperatorName).Should().Equal(result.Lineup.Slots.Select(s => s.OperatorName));
        saved.SourceUrl.Should().Be(Url);
        var tracking = new TrackingViewModel();
        tracking.TrackLineup(saved);
        tracking.TrackedOperators.Select(e => e.Name).Should().BeEquivalentTo(candidates.Select(op => op.Name));
    }

    [Theory]
    [InlineData(1, LineupPhase.前期)]
    [InlineData(3, LineupPhase.中期)]
    [InlineData(5, LineupPhase.后期)]
    public async Task APhaseAboveNineKeepsEveryCandidateAndStillRequiresAdjustment(int tier, LineupPhase phase)
    {
        var (operators, strategies) = await CatalogAsync();
        var candidates = operators.Where(op => op.Tier == tier && op.RequiredStrategyId.Length == 0).Take(10).ToArray();
        using var handler = new GuideHandler(CandidateGuide(candidates));
        using var http = new HttpClient(handler);
        var result = await new SklandGuideImporter(http, operators, strategies).ImportAsync(Url);
        result.Notice.Should().Contain("单个阶段超过 9 人");
        var editor = new LineupEditorViewModel(result.Lineup, operators, [], new CovenantCalculator(operators, [], new Dictionary<string, string>()));
        editor.FilledCount(phase).Should().Be(10);
        editor.ValidationError.Should().Contain(phase.ToString()).And.Contain("9");
        editor.SelectPhase(phase);
        editor.SetSlotOperator(0, "");
        editor.ValidationError.Should().BeEmpty();
        editor.Reset();
        editor.FilledCount(phase).Should().Be(10);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(6)]
    public async Task DiyOperatorIsImportedWhateverTheGuideTier(int tier)
    {
        var (operators, strategies) = await CatalogAsync();
        var custom = new Operator { Name = "自选甲", Tier = 6, IsDiy = true };
        operators.Add(custom);
        var candidates = operators.Where(op => !op.IsDiy && op.RequiredStrategyId.Length == 0).Take(9).Append(custom).ToArray();
        var json = JsonNode.Parse(CandidateGuide(candidates))!;
        var info = json["data"]!["seasonResourceMap"]!["act3autochess"]!["versionResourceMap"]!["V3_1"]!["chars"]!["candidate-9"]!;
        info["shopLevel"] = tier;
        using var handler = new GuideHandler(json.ToJsonString());
        using var http = new HttpClient(handler);
        var result = await new SklandGuideImporter(http, operators, strategies).ImportAsync(Url);
        result.Lineup.Slots.Should().Contain(x => x.OperatorName == custom.Name);
        // 自选 operators are always available in 我的阵容, so the guide's tier is not checked against them.
        result.Notice.Should().NotContain(custom.Name);
    }

    [Fact]
    public async Task NetworkErrorsAndCancellationPropagateWithoutReturningADraft()
    {
        using var handler = new GuideHandler(Fixture, HttpStatusCode.ServiceUnavailable);
        using var http = new HttpClient(handler);
        var importer = new SklandGuideImporter(http, [], []);
        await Assert.ThrowsAsync<HttpRequestException>(() => importer.ImportAsync(Url));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => importer.ImportAsync(Url, cancellation.Token));
    }

    [Fact]
    public async Task MalformedJsonHasAnExplicitImportError()
    {
        using var handler = new GuideHandler("<html>unavailable</html>");
        using var http = new HttpClient(handler);
        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => new SklandGuideImporter(http, [], []).ImportAsync(Url));
        exception.Message.Should().Contain("数据格式");
    }

    [Fact]
    public async Task NormalEliteMixAndSourceSurviveEditorSaveStoreAndJsonSharing()
    {
        var (operators, strategies) = await CatalogAsync();
        var json = JsonNode.Parse(Fixture)!;
        json["data"]!["guide"]!["guide"]!["tabs"]![0]!["recChars"]![0]!["mergeLevel"] = 0;
        using var handler = new GuideHandler(json.ToJsonString());
        using var http = new HttpClient(handler);
        var folder = Path.Combine(Path.GetTempPath(), "skland-roundtrip-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            var share = new LineupShareService(EquipmentTestData.Rules, Path.Combine(folder, "share"), http);
            var imported = await share.ImportOfficialGuideAsync(Url, operators, strategies);
            imported.Lineup.Slots[0].IsElite.Should().BeFalse();
            imported.Lineup.Slots.Skip(1).Should().OnlyContain(x => x.IsElite);
            var calculator = new CovenantCalculator(operators, [], new Dictionary<string, string>());
            var editor = new LineupEditorViewModel(imported.Lineup, operators, [], calculator);
            editor.Reset();
            var saved = editor.ToLineup();
            saved.SourceUrl.Should().Be(Url);
            var store = new LineupStore(Path.Combine(folder, "lineups"), calculator.EquipmentRules);
            await store.SaveAsync(saved);
            var reloaded = (await store.LoadAllAsync()).Single();
            reloaded.SourceUrl.Should().Be(Url);
            var shared = await share.ImportAsync(JsonSerializer.Serialize(reloaded));
            shared.SourceUrl.Should().Be(Url);
            shared.Slots[0].IsElite.Should().BeFalse();
        }
        finally { Directory.Delete(folder, true); }
    }

    private sealed class GuideHandler(string json, HttpStatusCode status = HttpStatusCode.OK) : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public Uri? RequestUri { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            RequestUri = request.RequestUri;
            request.Method.Should().Be(HttpMethod.Get);
            request.Headers.Contains("cred").Should().BeFalse();
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json) });
        }
    }

    private static string CandidateGuide(IReadOnlyList<Operator> candidates)
    {
        var json = JsonNode.Parse(Fixture)!;
        var chars = json["data"]!["guide"]!["guide"]!["tabs"]![0]!["recChars"]!.AsArray();
        chars.Clear();
        var resource = json["data"]!["seasonResourceMap"]!["act3autochess"]!["versionResourceMap"]!["V3_1"]!["chars"]!;
        for (var i = 0; i < candidates.Count; i++)
        {
            var id = "candidate-" + i;
            chars.Add(new JsonObject { ["charId"] = id, ["mergeLevel"] = i % 2 });
            resource[id] = new JsonObject { ["name"] = candidates[i].Name, ["shopLevel"] = candidates[i].Tier };
        }
        return json.ToJsonString();
    }
}
