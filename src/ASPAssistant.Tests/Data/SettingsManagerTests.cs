// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using FluentAssertions;

namespace ASPAssistant.Tests.Data;

public class SettingsManagerTests : IDisposable
{
    [Fact]
    public async Task RapidTrackingSavesAreSerializedAndLeaveCompleteLatestJson()
    {
        await Task.WhenAll(Enumerable.Range(0, 20).Select(i => _manager.SaveTrackingEntriesAsync(
            [new() { Name = $"目标{i}", Type = TrackingType.Operator }])));
        var loaded = await _manager.LoadTrackingEntriesAsync(EquipmentTestData.Rules);
        loaded.Should().ContainSingle().Which.Name.Should().Be("目标19");
        Directory.GetFiles(_tempDir, "*.tmp").Should().BeEmpty();
    }

    private readonly string _tempDir;
    private readonly SettingsManager _manager;

    public SettingsManagerTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"asp_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _manager = new SettingsManager(_tempDir);
    }

    public void Dispose()
    {
        if (Directory.Exists(_tempDir))
            Directory.Delete(_tempDir, true);
    }

    [Fact]
    public async Task SaveAndLoad_TrackingEntries_RoundTrips()
    {
        var entries = new List<TrackingEntry>
        {
            new() { Name = "角峰", Type = TrackingType.Operator, TransformedCovenant = "炎" },
            new() { Name = "不屈弹射器", Type = TrackingType.Equipment }
        };

        await _manager.SaveTrackingEntriesAsync(entries);
        var loaded = await _manager.LoadTrackingEntriesAsync(EquipmentTestData.Rules);

        loaded.Should().HaveCount(4);
        loaded.Should().Contain(e => e.Name == "角峰" && e.Type == TrackingType.Operator);
        loaded.Should().Contain(e => e.Name == "不屈弹射器" && e.Type == TrackingType.Equipment);
        loaded.Single(e => e.Name == "角峰").Equipments.Should().Equal("变形同构体", "炎国短刀");
        loaded.Single(e => e.Name == "角峰").TransformedCovenant.Should().BeNull();
    }

    [Fact]
    public async Task OverlappingTrackingSaves_KeepTheNewestList()
    {
        var saves = Enumerable.Range(1, 20).Select(n => _manager.SaveTrackingEntriesAsync(
            Enumerable.Range(0, n).Select(i => new TrackingEntry
            {
                Name = $"干员{i}", Type = TrackingType.Operator,
                Role = i % 2 == 0 ? TrackingRole.Final : TrackingRole.Transition, Stages = i % 2 == 0 ? "" : "开局"
            }).ToList())).ToList();
        await Task.WhenAll(saves);
        // The last caller may return while an earlier caller is still writing its snapshot.
        for (var i = 0; i < 50 && (await _manager.LoadTrackingEntriesAsync(EquipmentTestData.Rules)).Count != 20; i++) await Task.Delay(20);

        var loaded = await _manager.LoadTrackingEntriesAsync(EquipmentTestData.Rules);
        loaded.Should().HaveCount(20);
        loaded[1].Role.Should().Be(TrackingRole.Transition);
        loaded[1].Stages.Should().Be("开局");
    }

    [Fact]
    public async Task OldTrackingEntriesDefaultToNoPlannedTransformation()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "tracking.json"), """[{"name":"角峰","type":0}]""");
        (await _manager.LoadTrackingEntriesAsync(EquipmentTestData.Rules)).Should().ContainSingle().Which.TransformedCovenant.Should().BeNull();
    }

    [Fact]
    public async Task LoadTrackingEntries_NoFile_ReturnsEmpty()
    {
        var loaded = await _manager.LoadTrackingEntriesAsync(EquipmentTestData.Rules);
        loaded.Should().BeEmpty();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SaveAndLoad_LightTheme_RoundTrips(bool isLight)
    {
        await _manager.SaveLightThemeAsync(isLight);
        (await _manager.LoadLightThemeAsync()).Should().Be(isLight);
    }

    [Fact]
    public async Task LoadLightTheme_NoFile_UsesDarkTheme()
    {
        (await _manager.LoadLightThemeAsync()).Should().BeFalse();
    }

    [Fact]
    public async Task NewPreferences_RequireThemeChoiceAndUseSingleLineup()
    {
        (await _manager.LoadGameShopThemeAsync()).Should().BeNull();
        (await _manager.LoadTrackMultipleLineupsAsync()).Should().BeFalse();
    }

    [Theory]
    [InlineData(GameShopTheme.Terminal)]
    [InlineData(GameShopTheme.Ursus)]
    [InlineData(GameShopTheme.Closure)]
    public async Task GameShopTheme_PersistsAcrossManagerInstances(GameShopTheme theme)
    {
        await _manager.SaveGameShopThemeAsync(theme);
        (await new SettingsManager(_tempDir).LoadGameShopThemeAsync()).Should().Be(theme);
        (await File.ReadAllTextAsync(Path.Combine(_tempDir, "game-theme.json")))
            .Should().Be($"\"{theme}\"");
    }

    [Theory]
    [InlineData("invalid JSON")]
    [InlineData("\"UnknownTheme\"")]
    [InlineData("99")]
    public async Task InvalidThemePreference_RequiresChoice(string json)
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "game-theme.json"), json);
        (await _manager.LoadGameShopThemeAsync()).Should().BeNull();
    }

    [Fact]
    public async Task MissingSettingsDirectory_RequiresChoice()
    {
        var manager = new SettingsManager(Path.Combine(_tempDir, "first-start"));
        (await manager.LoadGameShopThemeAsync()).Should().BeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task MultipleLineups_PersistsAcrossManagerInstances(bool enabled)
    {
        await _manager.SaveTrackMultipleLineupsAsync(enabled);
        (await new SettingsManager(_tempDir).LoadTrackMultipleLineupsAsync()).Should().Be(enabled);
    }

    [Fact]
    public async Task SidePanelPosition_FirstStartUsesDefaultAttachment()
    {
        var settings = await new SettingsManager(Path.Combine(_tempDir, "first-start")).LoadSidePanelPositionAsync();
        // Free movement by default; without a placement the panel docks beside the game once.
        settings.FreeMovement.Should().BeTrue();
        settings.Placement.Should().BeNull();
        settings.FollowOffsetDip.Should().BeNull();
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SidePanelPosition_PreservesModePlacementAndFollowOffset(bool free)
    {
        var settings = new SidePanelPositionSettings
        {
            FreeMovement = free,
            Placement = new SidePanelPlacement(-1280, 150, 310, 600),
            FollowOffsetDip = new SidePanelOffset(-900.5, 80.25),
        };
        await _manager.SaveSidePanelPositionAsync(settings);
        (await new SettingsManager(_tempDir).LoadSidePanelPositionAsync()).Should().Be(settings);
        File.Exists(Path.Combine(_tempDir, "side-panel-position.json.tmp")).Should().BeFalse();
    }

    [Fact]
    public async Task SidePanelPosition_OverlappingSavesKeepLastCompleteState()
    {
        var saves = Enumerable.Range(0, 8).Select(i => _manager.SaveSidePanelPositionAsync(new SidePanelPositionSettings
        {
            FreeMovement = i % 2 == 1, FollowOffsetDip = new SidePanelOffset(i, i * 2),
        })).ToArray();
        await Task.WhenAll(saves);
        var saved = await new SettingsManager(_tempDir).LoadSidePanelPositionAsync();
        saved.FreeMovement.Should().BeTrue();
        saved.FollowOffsetDip.Should().Be(new SidePanelOffset(7, 14));
    }

    [Theory]
    [InlineData("null")]
    [InlineData("invalid JSON")]
    [InlineData("{\"Placement\":{\"Left\":0,\"Top\":0,\"WidthDip\":0,\"HeightDip\":600}}")]
    [InlineData("{\"Placement\":{\"Left\":1e300,\"Top\":0,\"WidthDip\":310,\"HeightDip\":600}}")]
    [InlineData("{\"Placement\":{\"Left\":0,\"Top\":0,\"WidthDip\":310,\"HeightDip\":-1}}")]
    public async Task SidePanelPosition_RejectsMalformedOrInvalidCoordinates(string json)
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "side-panel-position.json"), json);
        Func<Task> load = () => _manager.LoadSidePanelPositionAsync();
        await load.Should().ThrowAsync<System.Text.Json.JsonException>();
    }

    [Fact]
    public async Task TerminalLevel_RoundTripsAndExpires()
    {
        var t0 = new DateTime(2026, 11, 12, 0, 0, 0, DateTimeKind.Utc);
        (await _manager.LoadTerminalLevelAsync(t0)).Should().BeNull();
        await _manager.SaveTerminalLevelAsync(4, t0);
        (await _manager.LoadTerminalLevelAsync(t0.AddMinutes(30))).Should().Be(4);
        (await _manager.LoadTerminalLevelAsync(t0.AddHours(3))).Should().BeNull("a level from hours ago belongs to an old run");
        await _manager.SaveTerminalLevelAsync(0, t0);
        (await _manager.LoadTerminalLevelAsync(t0)).Should().BeNull();
    }

    [Fact]
    public async Task AcquiredTrackingRowsRoundTripWithoutReactivatingEquipment()
    {
        await _manager.SaveTrackingEntriesAsync([
            new() { Name = "角峰", Type = TrackingType.Operator, IsAcquired = true, Equipments = ["不屈弹射器"] },
            new() { Name = "不屈弹射器", Type = TrackingType.Equipment, IsAcquired = true, IsManual = false }
        ]);
        var loaded = await _manager.LoadTrackingEntriesAsync(EquipmentTestData.Rules);
        var vm = new ASPAssistant.Core.ViewModels.TrackingViewModel();
        vm.LoadEntries(loaded);
        vm.AllEntries.Should().HaveCount(2).And.OnlyContain(e => e.IsAcquired);
        vm.IsTracked("角峰").Should().BeFalse();
        vm.IsTracked("不屈弹射器").Should().BeFalse();
        vm.ToggleAcquired("角峰");
        vm.ToggleAcquired("不屈弹射器");
        await _manager.SaveTrackingEntriesAsync(vm.AllEntries.ToList());
        var resumed = await _manager.LoadTrackingEntriesAsync(EquipmentTestData.Rules);
        resumed.Should().HaveCount(2).And.OnlyContain(e => !e.IsAcquired);
    }

    [Fact]
    public async Task TrackingJsonWithoutAcquiredFlagRemainsActive()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "tracking.json"),
            """[{"name":"角峰","type":0,"equipments":[]}]""");
        var loaded = await _manager.LoadTrackingEntriesAsync(EquipmentTestData.Rules);
        loaded.Should().ContainSingle().Which.IsAcquired.Should().BeFalse();
    }
}
