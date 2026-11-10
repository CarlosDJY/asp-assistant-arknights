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
            new() { Name = "角峰", Type = TrackingType.Operator },
            new() { Name = "不屈弹射器", Type = TrackingType.Equipment }
        };

        await _manager.SaveTrackingEntriesAsync(entries);
        var loaded = await _manager.LoadTrackingEntriesAsync();

        loaded.Should().HaveCount(2);
        loaded.Should().Contain(e => e.Name == "角峰" && e.Type == TrackingType.Operator);
        loaded.Should().Contain(e => e.Name == "不屈弹射器" && e.Type == TrackingType.Equipment);
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

}
