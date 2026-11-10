// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
using System.Text.Json;
using System.Text.Json.Serialization;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Data;

public class SettingsManager
{
    private readonly string _settingsDir;
    private static readonly JsonSerializerOptions ThemeJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter<GameShopTheme>(allowIntegerValues: false) }
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public SettingsManager(string settingsDir)
    {
        _settingsDir = settingsDir;
    }

    public async Task<bool> LoadLightThemeAsync()
    {
        var path = Path.Combine(_settingsDir, "light-theme.json");
        if (!File.Exists(path)) return false;
        return JsonSerializer.Deserialize<bool>(await File.ReadAllTextAsync(path));
    }

    public async Task SaveLightThemeAsync(bool isLight)
    {
        Directory.CreateDirectory(_settingsDir);
        await File.WriteAllTextAsync(Path.Combine(_settingsDir, "light-theme.json"),
            JsonSerializer.Serialize(isLight));
    }

    public async Task<GameShopTheme?> LoadGameShopThemeAsync()
    {
        var path = Path.Combine(_settingsDir, "game-theme.json");
        try
        {
            var theme = JsonSerializer.Deserialize<GameShopTheme>(await File.ReadAllTextAsync(path), ThemeJsonOptions);
            if (Enum.IsDefined(theme)) return theme;
            AppLogger.Warn("Settings", "模式主题值无效，启动时重新选择");
            return null;
        }
        catch (IOException ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return null;
        }
        catch (JsonException)
        {
            AppLogger.Warn("Settings", "模式主题配置无法解析，启动时重新选择");
            return null;
        }
    }

    public async Task SaveGameShopThemeAsync(GameShopTheme theme)
    {
        Directory.CreateDirectory(_settingsDir);
        await File.WriteAllTextAsync(Path.Combine(_settingsDir, "game-theme.json"),
            JsonSerializer.Serialize(theme, ThemeJsonOptions));
    }

    public async Task<bool> LoadTrackMultipleLineupsAsync()
    {
        var path = Path.Combine(_settingsDir, "multiple-lineups.json");
        if (!File.Exists(path)) return false;
        return JsonSerializer.Deserialize<bool>(await File.ReadAllTextAsync(path));
    }

    public async Task SaveTrackMultipleLineupsAsync(bool enabled)
    {
        Directory.CreateDirectory(_settingsDir);
        await File.WriteAllTextAsync(Path.Combine(_settingsDir, "multiple-lineups.json"),
            JsonSerializer.Serialize(enabled));
    }

    public async Task SaveTrackingEntriesAsync(List<TrackingEntry> entries)
    {
        Directory.CreateDirectory(_settingsDir);
        var path = Path.Combine(_settingsDir, "tracking.json");
        var json = JsonSerializer.Serialize(entries, JsonOptions);
        await File.WriteAllTextAsync(path, json);
    }

    public async Task<List<TrackingEntry>> LoadTrackingEntriesAsync()
    {
        var path = Path.Combine(_settingsDir, "tracking.json");
        if (!File.Exists(path))
            return [];

        var json = await File.ReadAllTextAsync(path);
        return JsonSerializer.Deserialize<List<TrackingEntry>>(json, JsonOptions) ?? [];
    }

    public async Task<string?> LoadSkippedVersionAsync()
    {
        var path = Path.Combine(_settingsDir, "skipped_version.txt");
        if (!File.Exists(path))
            return null;

        var tag = await File.ReadAllTextAsync(path);
        return tag.Trim();
    }

    public async Task SaveSkippedVersionAsync(string tag)
    {
        Directory.CreateDirectory(_settingsDir);
        var path = Path.Combine(_settingsDir, "skipped_version.txt");
        await File.WriteAllTextAsync(path, tag.Trim());
    }

}
