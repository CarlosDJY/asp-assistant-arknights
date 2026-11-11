using System.Text.Json;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Core.Data;

/// <summary>
/// 阵容文件持久化：每个阵容一个 &lt;id&gt;.json 文件，存于
/// %AppData%\ASPAssistant\lineups\，避免单文件并发风险。
/// </summary>
public class LineupStore
{
    private readonly string _dir;
    private readonly LineupEquipmentService _equipmentRules;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
    };

    public LineupStore(string lineupsDir, LineupEquipmentService equipmentRules)
    {
        _dir = lineupsDir;
        _equipmentRules = equipmentRules;
    }

    public async Task<List<Lineup>> LoadAllAsync()
    {
        if (!Directory.Exists(_dir))
            return [];

        var files = Directory.EnumerateFiles(_dir, "*.json");
        var result = new List<Lineup>();
        foreach (var file in files)
        {
            Lineup? lineup;
            try
            {
                var json = await File.ReadAllTextAsync(file);
                lineup = JsonSerializer.Deserialize<Lineup>(json, JsonOptions);
            }
            catch (JsonException ex)
            {
                AppLogger.Warn("LineupStore", $"Failed to load {file}: {ex.Message}");
                continue;
            }
            if (lineup is null) continue;
            if (string.IsNullOrEmpty(lineup.Id)) lineup.Id = Path.GetFileNameWithoutExtension(file);
            if (_equipmentRules.Upgrade(lineup))
            {
                File.Copy(file, file + $".pre-equipment-{Guid.NewGuid():N}.bak");
                await WriteAsync(file, lineup);
            }
            result.Add(lineup);
        }
        return [.. result.OrderByDescending(l => l.UpdatedAt)];
    }

    public async Task SaveAsync(Lineup lineup)
    {
        Directory.CreateDirectory(_dir);
        if (string.IsNullOrEmpty(lineup.Id))
            lineup.Id = Guid.NewGuid().ToString("N");
        lineup.UpdatedAt = DateTime.UtcNow;

        var path = Path.GetFullPath(Path.Combine(_dir, $"{lineup.Id}.json"));
        if (Path.GetDirectoryName(path) != Path.GetFullPath(_dir))
            throw new InvalidDataException("阵容 ID 不能包含目录路径");
        await WriteAsync(path, lineup);
    }

    private static async Task WriteAsync(string path, Lineup lineup)
    {
        var temporary = path + $".{Guid.NewGuid():N}.tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(lineup, JsonOptions));
            File.Move(temporary, path, true);
        }
        finally { File.Delete(temporary); }
    }

    public Task DeleteAsync(string id)
    {
        var path = Path.Combine(_dir, $"{id}.json");
        if (File.Exists(path))
            File.Delete(path);
        return Task.CompletedTask;
    }
}
