// Added for ASP Assistant Arknights on 2026-11-14; see NOTICE. Modified 2026-11-14.
// Lookup adapted from LBEILC/StrongholdAssistant BoardRouteStore.cs (Apache-2.0).
using System.Text.Json;
using System.Text.Json.Serialization;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Data;

/// <summary>
/// data/garrison_routes.json: which level each mode plays in each round, and every level's enemy routes per map.
/// Ordinary rounds play the same level in every mode; boss rounds depend on the boss and on solo / multiplayer.
/// </summary>
public sealed class EnemyRouteStore
{
    private sealed record Mode(string Id, bool Solo);
    private sealed record Battle(string? BossId, string Level);
    private sealed record Level(EnemyRoute[] Routes, RoutePoint[] BossSpots);
    private sealed record Map(Dictionary<string, Level> Levels);
    private sealed record Data(Dictionary<string, Mode> Modes, Dictionary<string, Dictionary<string, Battle[]>> Battles,
        Dictionary<string, string> BossIds, Dictionary<string, Map> Maps);

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
    private readonly Data _data;

    private EnemyRouteStore(Data data) => _data = data;

    public static EnemyRouteStore Parse(string json) => new(JsonSerializer.Deserialize<Data>(json, Options)!);

    public static EnemyRouteStore? Load(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, "garrison_routes.json");
        return File.Exists(path) ? Parse(File.ReadAllText(path)) : null;
    }

    /// <summary>Whether the HUD mode name (规程等级5, 险境模拟…) is a solo mode; null when unknown.</summary>
    public bool? IsSolo(string? modeName) => modeName is not null && _data.Modes.TryGetValue(Normalize(modeName), out var m) ? m.Solo : null;

    public RoundRoutes? Find(string mapId, int round, string? modeName, string? bossEnemyId) => Find(mapId, round, modeName, bossEnemyId, out _);

    /// <summary>
    /// This round's routes. <paramref name="modeName"/> is the HUD name; when it is not read yet the multiplayer
    /// table is used, which plays the same levels as every mode outside the boss rounds (极速模拟 excepted).
    /// <paramref name="reason"/> says why nothing was found.
    /// </summary>
    public RoundRoutes? Find(string mapId, int round, string? modeName, string? bossEnemyId, out string? reason)
    {
        reason = null;
        Mode? mode = modeName is not null && _data.Modes.TryGetValue(Normalize(modeName), out var m) ? m : null;
        string modeId = mode?.Id ?? "mode_multi_normal";
        if (!_data.Battles.TryGetValue(modeId, out var rounds) || !rounds.TryGetValue(round.ToString(), out var battles) || battles.Length == 0)
        {
            reason = "这一回合没有数据";
            return null;
        }
        string? level;
        if (battles.Any(b => !string.IsNullOrEmpty(b.BossId)))
        {
            if (bossEnemyId is null) { reason = "首领还没识别"; return null; }
            if (mode is null) { reason = "模式还没识别"; return null; }
            // The boss comes from the information screen; look it up across every mode with the same solo / multi levels.
            level = _data.Battles.Values.SelectMany(r => r.GetValueOrDefault(round.ToString()) ?? [])
                .FirstOrDefault(b => b.BossId is not null && _data.BossIds.GetValueOrDefault(b.BossId) == bossEnemyId
                    && b.Level.EndsWith("_s", StringComparison.Ordinal) == mode.Solo)?.Level;
            if (level is null) { reason = "这个首领没有数据"; return null; }
        }
        else level = battles[0].Level;
        if (!_data.Maps.TryGetValue(mapId, out var map) || !map.Levels.TryGetValue(level, out var data))
        {
            reason = "这张地图没有数据";
            return null;
        }
        return new RoundRoutes(mapId, round, level, bossEnemyId, data.Routes, data.BossSpots);
    }

    /// <summary>How many rounds the mode has (the multiplayer table when the mode is not read yet).</summary>
    public int RoundCount(string? modeName)
    {
        string id = modeName is not null && _data.Modes.TryGetValue(Normalize(modeName), out var m) ? m.Id : "mode_multi_normal";
        return _data.Battles.TryGetValue(id, out var rounds) && rounds.Count > 0 ? rounds.Keys.Select(int.Parse).Max() : 15;
    }

    private static string Normalize(string modeName) => modeName.Replace(" ", "", StringComparison.Ordinal);
}
