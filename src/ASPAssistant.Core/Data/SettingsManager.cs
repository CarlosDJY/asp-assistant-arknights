// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
using System.Text.Json;
using System.Text.Json.Serialization;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Core.Data;

public class SettingsManager
{
    private readonly string _settingsDir;
    private readonly SemaphoreSlim _sidePanelPositionLock = new(1, 1);
    private readonly SemaphoreSlim _trackingLock = new(1, 1);
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

    /// <summary>
    /// Side panel theme + day/night. Older installs only have light-theme.json: its value becomes the night
    /// choice of 终端 (the former only theme).
    /// </summary>
    public async Task<SidebarAppearance> LoadSidebarAppearanceAsync()
    {
        var path = Path.Combine(_settingsDir, "appearance.json");
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<SidebarAppearance>(await File.ReadAllTextAsync(path), ThemeJsonOptions) ?? new();
        }
        catch (JsonException)
        {
            AppLogger.Warn("Settings", "侧栏外观配置无法解析，使用默认值");
            return new SidebarAppearance();
        }
        var appearance = new SidebarAppearance();
        if (File.Exists(Path.Combine(_settingsDir, "light-theme.json")) && await LoadLightThemeAsync())
            appearance.Night[GameShopTheme.Terminal] = false;
        return appearance;
    }

    private sealed record SavedTerminalLevel(int Level, DateTime SavedAtUtc);

    /// <summary>Terminal level of the current run, kept so restarting the app mid-run does not lose it.</summary>
    public static readonly TimeSpan TerminalLevelMaxAge = TimeSpan.FromHours(2);

    /// <summary>The saved level, or null when there is none or it is older than <see cref="TerminalLevelMaxAge"/>.</summary>
    public async Task<int?> LoadTerminalLevelAsync(DateTime? nowUtc = null)
    {
        var path = Path.Combine(_settingsDir, "terminal-level.json");
        try
        {
            if (!File.Exists(path)) return null;
            var saved = JsonSerializer.Deserialize<SavedTerminalLevel>(await File.ReadAllTextAsync(path));
            if (saved is null || saved.Level is < 1 or > 6) return null;
            return (nowUtc ?? DateTime.UtcNow) - saved.SavedAtUtc <= TerminalLevelMaxAge ? saved.Level : null;
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return null;
        }
    }

    /// <summary>Saves the run's level; 0 clears it (new run).</summary>
    public async Task SaveTerminalLevelAsync(int level, DateTime? nowUtc = null)
    {
        var path = Path.Combine(_settingsDir, "terminal-level.json");
        try
        {
            if (level <= 0) { if (File.Exists(path)) File.Delete(path); return; }
            Directory.CreateDirectory(_settingsDir);
            await File.WriteAllTextAsync(path,
                JsonSerializer.Serialize(new SavedTerminalLevel(level, nowUtc ?? DateTime.UtcNow)));
        }
        catch (IOException ex)
        {
            AppLogger.Warn("Settings", $"终端等级保存失败：{ex.Message}");
        }
    }

    public async Task SaveSidebarAppearanceAsync(SidebarAppearance appearance)
    {
        Directory.CreateDirectory(_settingsDir);
        await File.WriteAllTextAsync(Path.Combine(_settingsDir, "appearance.json"),
            JsonSerializer.Serialize(appearance, ThemeJsonOptions));
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

    public async Task<SidePanelPositionSettings> LoadSidePanelPositionAsync()
    {
        string json;
        try
        {
            json = await File.ReadAllTextAsync(Path.Combine(_settingsDir, "side-panel-position.json"));
        }
        catch (IOException ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
        {
            return new SidePanelPositionSettings();
        }
        var settings = JsonSerializer.Deserialize<SidePanelPositionSettings>(json, JsonOptions)
            ?? throw new JsonException("侧边栏位置配置不能为空");
        if (settings.Placement is { } placement && (!double.IsFinite(placement.Left) || !double.IsFinite(placement.Top)
            || placement.Left < int.MinValue || placement.Left > int.MaxValue
            || placement.Top < int.MinValue || placement.Top > int.MaxValue
            || !double.IsFinite(placement.WidthDip) || placement.WidthDip <= 0
            || !double.IsFinite(placement.HeightDip) || placement.HeightDip < 1))
            throw new JsonException("侧边栏位置或尺寸无效");
        if (settings.FollowOffsetDip is { } offset && (!double.IsFinite(offset.X) || !double.IsFinite(offset.Y)))
            throw new JsonException("侧边栏跟随偏移无效");
        return settings;
    }

    public async Task SaveSidePanelPositionAsync(SidePanelPositionSettings settings)
    {
        await _sidePanelPositionLock.WaitAsync();
        try
        {
            Directory.CreateDirectory(_settingsDir);
            var path = Path.Combine(_settingsDir, "side-panel-position.json");
            await File.WriteAllTextAsync(path + ".tmp", JsonSerializer.Serialize(settings, JsonOptions));
            File.Move(path + ".tmp", path, overwrite: true);
        }
        finally { _sidePanelPositionLock.Release(); }
    }

    public async Task SaveTrackingEntriesAsync(List<TrackingEntry> entries)
    {
        Directory.CreateDirectory(_settingsDir);
        var path = Path.Combine(_settingsDir, "tracking.json");
        var json = JsonSerializer.Serialize(entries, JsonOptions);
        await _trackingLock.WaitAsync();
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, json);
            File.Move(temporary, path, true);
        }
        finally { _trackingLock.Release(); File.Delete(temporary); }
    }

    public async Task<List<TrackingEntry>> LoadTrackingEntriesAsync(LineupEquipmentService equipmentRules)
    {
        var path = Path.Combine(_settingsDir, "tracking.json");
        if (!File.Exists(path))
            return [];

        var json = await File.ReadAllTextAsync(path);
        var entries = JsonSerializer.Deserialize<List<TrackingEntry>>(json, JsonOptions) ?? [];
        var changed = false;
        foreach (var entry in entries.Where(e => e.Type == TrackingType.Operator))
        {
            var slot = new LineupSlot { OperatorName = entry.Name, Equipments = entry.Equipments,
                IsElite = entry.IsElite, TransformedCovenant = entry.TransformedCovenant };
            if (!equipmentRules.UpgradeSlot(slot)) continue;
            entry.TransformedCovenant = slot.TransformedCovenant;
            changed = true;
        }
        if (changed)
        {
            foreach (var name in entries.Where(e => e.Type == TrackingType.Operator).SelectMany(e => e.Equipments).Distinct().ToList())
                if (!entries.Any(e => e.Type == TrackingType.Equipment && e.Name == name))
                    entries.Add(new TrackingEntry { Name = name, Type = TrackingType.Equipment, IsManual = false });
            File.Copy(path, path + $".pre-equipment-{Guid.NewGuid():N}.bak");
            await SaveTrackingEntriesAsync(entries);
        }
        return entries;
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
