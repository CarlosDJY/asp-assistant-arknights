// Added for ASP Assistant Regulation on 2026-11-14; see NOTICE.
using System.Text.Json;
using System.Text.Json.Serialization;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Core.Data;

/// <summary>
/// data/garrison_spawns.json (路标档案馆 卫戍协议出怪预览 + enemy_database): which enemies each round actually brings.
/// A run draws 3 of the 9 enemy types; every round brings one of them, as one of that type's groups (normal / elite /
/// special enemies filling the round's template), on the upper and lower gates, flying or on the ground.
/// </summary>
public sealed class GarrisonSpawnStore
{
    /// <summary>Type codes in the data and the names the information screen shows.</summary>
    public static readonly IReadOnlyDictionary<string, string> TypeNames = new Dictionary<string, string>
    {
        ["SPECIAL"] = "特异", ["FLY"] = "飞行", ["TIMES"] = "频次", ["ELEMENT"] = "元素", ["DOT"] = "持续",
        ["INVISIBLE"] = "隐匿", ["REFLECTION"] = "折射", ["REBORN"] = "复苏", ["CRITIC"] = "致命",
    };

    private const string DefaultMode = "mode_multi_normal";

    private sealed record Enemy(string Name, double Hp, double Atk, double Def, double Res, double Speed, double Interval,
        double Range, bool Flying, bool Arts, string Rank, int LifePoints, TraitsDto? Traits, int? Weight);
    private sealed record EmberDto(int Hits, double Seconds);
    private sealed record SpawnDto(string Enemy, int Count, double Delay);
    private sealed record ElementDto(string Kind, double Ratio);
    private sealed record BleedDto(double Dps, double Seconds);
    private sealed record CloudDto(double Dps, double AtkScale, double Range, double Seconds, bool AtSource);
    private sealed record CritDto(double Scale, double NoHeal);
    private sealed record AuraDto(double Def, double Res, double Range);
    private sealed record SlowDto(double Factor, double Range);
    private sealed record StunDto(int Every, double Seconds, double Radius);
    private sealed record SplashDto(double Radius, int? Every, bool? First, double? Scale);
    private sealed record ChainDto(int Targets, double Scale);
    private sealed record LineDto(double Cooldown, double Scale);
    private sealed record TraitsDto(bool Invisible, double? Refraction, double? RefractionHp, int? Shield, int? DeathShieldAura,
        EmberDto? Ember, double? Reborn, SpawnDto[]? OnDeath, ElementDto? Element, BleedDto? Bleed, CloudDto? Cloud,
        CritDto? Crit, AuraDto? Aura, SlowDto? SlowOperators, double? UnblockedTaken, int? BlockCost, int? MinBlock, double? Regen,
        StunDto? Stun, SplashDto? Splash, ChainDto? Chain, LineDto? Line);
    private sealed record Act(string Gate, string? Slot, string Enemy, int Count, double? Pre, double? Interval);
    private sealed record Group(string Key, string Name, string Type, double Weight, double Prob, bool Flying, Act[] Acts);
    private sealed record Endgame(string? Id, string? BossKey, string? Name, Act[] Actions);
    private sealed record Data(Dictionary<string, string> Modes, Dictionary<string, double[][]> Buffs,
        Dictionary<string, Group[]> Rounds, Dictionary<string, Endgame[]> Endgame, Dictionary<string, Enemy> Enemies,
        Dictionary<string, string[]>? Inactive);

    private static readonly JsonSerializerOptions Options = new() { PropertyNameCaseInsensitive = true };
    private readonly Data _data;

    private GarrisonSpawnStore(Data data) => _data = data;

    public static GarrisonSpawnStore Parse(string json) => new(JsonSerializer.Deserialize<Data>(json, Options)!);

    public static GarrisonSpawnStore? Load(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, "garrison_spawns.json");
        return File.Exists(path) ? Parse(File.ReadAllText(path)) : null;
    }

    /// <summary>Type code for an information-screen name (飞行, 隐匿…) or a code; null when unknown.</summary>
    public static string? TypeCode(string name)
    {
        var trimmed = name.Trim();
        if (TypeNames.ContainsKey(trimmed)) return trimmed;
        return TypeNames.FirstOrDefault(kv => trimmed.Contains(kv.Value)).Key;
    }

    /// <summary>The cumulative enemy buff of <paramref name="round"/> in the mode (HUD name or id).</summary>
    private string ModeId(string? mode) =>
        mode is not null && _data.Modes.TryGetValue(mode.Replace(" ", ""), out var m) ? m : mode ?? DefaultMode;

    public (double Atk, double Hp, double AttackSpeedAdd, double Move) Buff(string? mode, int round)
    {
        string id = ModeId(mode);
        if (!_data.Buffs.TryGetValue(id, out var schedule)) schedule = _data.Buffs[DefaultMode];
        if (schedule.Length == 0) return (1, 1, 0, 1);
        var b = schedule[Math.Clamp(round, 1, schedule.Length) - 1];
        return (b[0], b[1], b[2], b[3]);
    }

    /// <summary>
    /// The streams the round may bring: for each type the run drew (all types when none are known), each of that type's
    /// groups this round, weighted so each type counts the same and its groups by their draw chance. Rounds 14 / 15 bring
    /// their endgame stage (the boss when known). Enemies carry the mode's buff for the round.
    /// </summary>
    public IReadOnlyList<RoundStream> Streams(int round, string? mode, IReadOnlyCollection<string> types, string? bossKey = null)
    {
        var buff = Buff(mode, round);
        var result = new List<RoundStream>();
        if (_data.Rounds.TryGetValue(round.ToString(), out var groups) && groups.Length > 0)
        {
            var codes = types.Select(TypeCode).OfType<string>().Distinct().ToList();
            // Groups whose special enemy the mode leaves out never come (easier modes drop the harder ones).
            var off = _data.Inactive?.GetValueOrDefault(ModeId(mode)) is { } keys ? keys.ToHashSet() : [];
            groups = [.. groups.Where(g => !off.Contains(g.Key))];
            var pick = codes.Count > 0 ? groups.Where(g => codes.Contains(g.Type)).ToList() : [.. groups];
            if (pick.Count == 0) pick = [.. groups];
            foreach (var type in pick.GroupBy(g => g.Type))
            {
                double total = type.Sum(g => g.Prob);
                foreach (var g in type)
                    result.Add(new RoundStream($"{TypeNames.GetValueOrDefault(g.Type, g.Type)}·{g.Name}", g.Type,
                        g.Prob / Math.Max(1e-9, total) / pick.Select(x => x.Type).Distinct().Count(), g.Flying,
                        Spawns(g.Acts, buff)));
            }
        }
        else if (_data.Endgame.TryGetValue(round.ToString(), out var stages) && stages.Length > 0)
        {
            // Round 15 has the boss's second form (enemy_…_2).
            var chosen = stages.Where(s => bossKey is not null && s.BossKey is { } k && (k == bossKey || k == bossKey + "_2")).ToList();
            if (chosen.Count == 0) chosen = [.. stages];
            foreach (var s in chosen)
                result.Add(new RoundStream(s.Name ?? s.Id ?? $"第 {round} 回合", "BOSS", 1.0 / chosen.Count, false, Spawns(s.Actions, buff)));
        }
        return result;
    }

    /// <summary>An enemy with the round's buff and its mechanics (what it spawns on death too).</summary>
    private EnemyProfile? Profile(string key, (double Atk, double Hp, double AttackSpeedAdd, double Move) buff, int depth = 0)
    {
        if (!_data.Enemies.TryGetValue(key, out var e)) return null;
        var t = e.Traits;
        var traits = t is null ? EnemyTraits.None : new EnemyTraits
        {
            Invisible = t.Invisible,
            RefractionRes = t.Refraction ?? 0,
            Shield = t.Shield ?? 0,
            DeathShieldAura = t.DeathShieldAura ?? 0,
            EmberHits = t.Ember?.Hits ?? 0,
            EmberSeconds = t.Ember?.Seconds ?? 0,
            RebornSeconds = t.Reborn ?? 0,
            OnDeath = depth > 2 || t.OnDeath is null ? []
                : [.. t.OnDeath.Select(d => (d, p: Profile(d.Enemy, buff, depth + 1))).Where(x => x.p is not null)
                    .Select(x => new DeathSpawn(x.p!, Math.Max(1, x.d.Count), x.d.Delay))],
            Element = t.Element?.Kind switch
            {
                "burning" => ElementKind.Burning, "apoptosis" => ElementKind.Apoptosis,
                "neural" => ElementKind.Neural, "erosion" => ElementKind.Erosion, _ => ElementKind.None,
            },
            ElementRatio = t.Element?.Ratio ?? 0,
            BleedDps = t.Bleed?.Dps ?? 0,
            BleedSeconds = t.Bleed?.Seconds ?? 0,
            CloudDps = t.Cloud?.Dps ?? 0,
            CloudAtkScale = t.Cloud?.AtkScale ?? 0,
            CloudRange = t.Cloud?.Range ?? 0,
            CloudSeconds = t.Cloud?.Seconds ?? 0,
            CloudAtSource = t.Cloud?.AtSource ?? false,
            CritScale = t.Crit?.Scale ?? 1,
            CritNoHeal = t.Crit?.NoHeal ?? 0,
            AuraDef = t.Aura?.Def ?? 0,
            AuraRes = t.Aura?.Res ?? 0,
            AuraRange = t.Aura?.Range ?? 0,
            SlowFactor = t.SlowOperators?.Factor ?? 1,
            SlowRange = t.SlowOperators?.Range ?? 0,
            UnblockedTaken = t.UnblockedTaken ?? 1,
            BlockCost = Math.Max(1, t.BlockCost ?? 1),
            MinBlock = t.MinBlock ?? 0,
            Regen = (t.Regen ?? 0) * buff.Hp,
            StunEvery = t.Stun?.Every ?? 0,
            StunSeconds = t.Stun?.Seconds ?? 0,
            StunRadius = t.Stun?.Radius ?? 0,
            SplashRadius = t.Splash?.Radius ?? 0,
            SplashEvery = t.Splash?.Every ?? 1,
            SplashFirst = t.Splash?.First ?? false,
            SplashScale = t.Splash?.Scale ?? 1,
            ChainTargets = t.Chain?.Targets ?? 0,
            ChainScale = t.Chain?.Scale ?? 1,
            LineCooldown = t.Line?.Cooldown ?? 0,
            LineScale = t.Line?.Scale ?? 0,
        };
        return new EnemyProfile(e.Hp * buff.Hp * (t?.RefractionHp ?? 1), e.Def, e.Res, e.Atk * buff.Atk,
            e.Interval / (1 + buff.AttackSpeedAdd / 100), e.Speed * buff.Move)
        {
            Name = e.Name, Range = e.Range, Arts = e.Arts, Rank = e.Rank, LifePoints = Math.Max(1, e.LifePoints), Traits = traits,
            Weight = e.Weight ?? 1,
        };
    }

    private IReadOnlyList<SimSpawn> Spawns(IEnumerable<Act> acts, (double Atk, double Hp, double AttackSpeedAdd, double Move) buff)
    {
        var list = new List<SimSpawn>();
        foreach (var a in acts)
        {
            if (Profile(a.Enemy, buff) is not { } profile || !_data.Enemies.TryGetValue(a.Enemy, out var e)) continue;
            double pre = a.Pre ?? 3, gap = a.Interval ?? 3;
            for (int k = 0; k < a.Count; k++)
                list.Add(new SimSpawn(pre + k * gap, a.Gate == "upper", e.Flying, profile));
        }
        list.Sort((x, y) => x.Time.CompareTo(y.Time));
        return list;
    }
}

/// <summary>One possible stream of a round (a type's group), with its share of the round's chances.</summary>
public sealed record RoundStream(string Label, string Type, double Weight, bool Flying, IReadOnlyList<SimSpawn> Spawns);
