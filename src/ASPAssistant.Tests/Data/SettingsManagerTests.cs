// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using FluentAssertions;

namespace ASPAssistant.Tests.Data;

public class SettingsManagerTests : IDisposable
{
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
        var loaded = await _manager.LoadTrackingEntriesAsync();

        loaded.Should().HaveCount(2);
        loaded.Should().Contain(e => e.Name == "角峰" && e.Type == TrackingType.Operator);
        loaded.Should().Contain(e => e.Name == "不屈弹射器" && e.Type == TrackingType.Equipment);
        loaded.Single(e => e.Name == "角峰").TransformedCovenant.Should().Be("炎");
    }

    [Fact]
    public async Task OldTrackingEntriesDefaultToNoPlannedTransformation()
    {
        await File.WriteAllTextAsync(Path.Combine(_tempDir, "tracking.json"), """[{"name":"角峰","type":0}]""");
        (await _manager.LoadTrackingEntriesAsync()).Should().ContainSingle().Which.TransformedCovenant.Should().BeEmpty();
    }

    [Fact]
    public async Task LoadTrackingEntries_NoFile_ReturnsEmpty()
    {
        var loaded = await _manager.LoadTrackingEntriesAsync();
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
        settings.FreeMovement.Should().BeFalse();
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

}
