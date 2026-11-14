// Modified for ASP Assistant Regulation on 2026-11-14; see NOTICE.
using System.Text.Json.Serialization;

namespace ASPAssistant.Core.Models;

/// <summary>The skill a 卫戍协议 chess piece uses (default skill index, at the chess piece's own skill level).</summary>
public class OperatorSkill
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("description")] public string Description { get; set; } = "";
    [JsonPropertyName("spCost")] public int SpCost { get; set; }
    [JsonPropertyName("initSp")] public int InitSp { get; set; }
    [JsonPropertyName("duration")] public double Duration { get; set; }
    [JsonPropertyName("spType")] public string SpType { get; set; } = "";

    [JsonIgnore]
    public string CostText => SpCost > 0
        ? $"{SpType} · 初始 {InitSp} / 消耗 {SpCost}" + (Duration > 0 ? $" · 持续 {Duration:g} 秒" : "")
        : SpType;
}

public class OperatorStackInfo
{
    /// <summary>covenant / self / adjacent / once / amplifier (see build_operator_profiles.py).</summary>
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("covenants")] public List<string> Covenants { get; set; } = [];
    /// <summary>Layers per trigger for each stacked covenant.</summary>
    [JsonPropertyName("amount")] public double Amount { get; set; }
    /// <summary>Per-round (or per-battle) stacking cap; null when uncapped.</summary>
    [JsonPropertyName("cap")] public double? Cap { get; set; }
    /// <summary>Estimated layers per round for each stacked covenant (0 for amplifiers and 获得时).</summary>
    [JsonPropertyName("perRound")] public double PerRound { get; set; }
    /// <summary>Stacks 乌萨斯 temporary layers, which vanish unless converted.</summary>
    [JsonPropertyName("temporary")] public bool Temporary { get; set; }
    [JsonPropertyName("amplify")] public OperatorStackAmplify? Amplify { get; set; }
}

/// <summary>How an amplifier (Mon3tr, 阿米娅, 薇薇安娜, 塔露拉, 凛御银灰) boosts other stacking.</summary>
public class OperatorStackAmplify
{
    /// <summary>capPercent / capFlat / perEvent / tempBoost.</summary>
    [JsonPropertyName("type")] public string Type { get; set; } = "";
    [JsonPropertyName("value")] public double Value { get; set; }
    /// <summary>around (周围八格) / front (身前一格) / sameCovenant (同盟约干员).</summary>
    [JsonPropertyName("scope")] public string Scope { get; set; } = "";
}

/// <summary>Which way a deployed operator faces; attack ranges are stored facing right.</summary>
public enum Facing { Right, Down, Left, Up }

/// <summary>
/// An attack range as grid cells relative to the operator, facing right:
/// <c>Forward</c> grows in the facing direction, <c>Side</c> grows downwards on screen.
/// </summary>
public sealed class AttackRange
{
    public AttackRange(string id, IEnumerable<(int Forward, int Side)> cells)
    {
        Id = id;
        Cells = cells.Distinct().ToList();
    }

    public string Id { get; }
    public IReadOnlyList<(int Forward, int Side)> Cells { get; }

    /// <summary>Board offsets (columns right, rows down) covered when facing <paramref name="facing"/>.</summary>
    public IEnumerable<(int Col, int Row)> Offsets(Facing facing) => Cells.Select(c => facing switch
    {
        Facing.Right => (c.Forward, c.Side),
        Facing.Left => (-c.Forward, -c.Side),
        Facing.Down => (-c.Side, c.Forward),
        _ => (c.Side, -c.Forward),
    });
}

/// <summary>Range ids of one variant: the normal range and the one the skill switches to (if any).</summary>
public class OperatorRangeRef
{
    [JsonPropertyName("base")] public string? Base { get; set; }
    [JsonPropertyName("skill")] public string? Skill { get; set; }

    /// <summary>Resolved from the file's <c>ranges</c> table when the profiles are loaded.</summary>
    [JsonIgnore] public AttackRange? BaseRange { get; set; }
    [JsonIgnore] public AttackRange? SkillRange { get; set; }
}

/// <summary>
/// A piece's stats in 卫戍协议: the operator's attributes at the piece's elite phase and level (linear between the
/// game's key frames) plus its module level, not counting trust, potential or talents.
/// </summary>
public sealed class ChessStats
{
    [JsonPropertyName("atk")] public double Atk { get; set; }
    [JsonPropertyName("hp")] public double Hp { get; set; }
    [JsonPropertyName("def")] public double Def { get; set; }
    [JsonPropertyName("res")] public double Res { get; set; }
    /// <summary>Base attack interval, seconds.</summary>
    [JsonPropertyName("interval")] public double Interval { get; set; } = 1;
    [JsonPropertyName("aspd")] public double AttackSpeed { get; set; } = 100;
    [JsonPropertyName("block")] public int Block { get; set; }
    [JsonPropertyName("level")] public int Level { get; set; }
    [JsonPropertyName("phase")] public int Phase { get; set; }
    /// <summary>Panel damage per second: attack ÷ interval × attack speed.</summary>
    [JsonPropertyName("dps")] public double Dps { get; set; }
    /// <summary>How much the carried skill raises average output over a fight (1 = no damage skill).</summary>
    [JsonPropertyName("skillFactor")] public double SkillFactor { get; set; } = 1;
    // What the skill does while it runs (build_operator_profiles.py skill_effect); absent = not read from the data.
    [JsonPropertyName("skillGain")] public double? SkillGain { get; set; }
    [JsonPropertyName("skillTargets")] public int SkillTargets { get; set; }
    [JsonPropertyName("skillHits")] public int SkillHits { get; set; }
    [JsonPropertyName("skillBlocked")] public bool SkillBlocked { get; set; }
    [JsonPropertyName("skillDamage")] public string? SkillDamage { get; set; }
    [JsonPropertyName("skillResIgnore")] public double SkillResIgnore { get; set; }
    [JsonPropertyName("skillEnemyDef")] public double? SkillEnemyDef { get; set; }
    [JsonPropertyName("skillEnemyRes")] public double SkillEnemyRes { get; set; }
    [JsonPropertyName("skillHeal")] public SkillHeal? SkillHeal { get; set; }
    [JsonPropertyName("skillControl")] public SkillControl? SkillControl { get; set; }
    [JsonPropertyName("skillDef")] public double SkillDef { get; set; }
    /// <summary>Max HP while the skill runs: a ratio (0.6 = +60 %) or, above 10, a flat amount.</summary>
    [JsonPropertyName("skillHp")] public double SkillHp { get; set; }
    [JsonPropertyName("skillBlock")] public int SkillBlock { get; set; }
    /// <summary>Damage per second (× attack) to every enemy in its reach while the skill runs (zones, fields).</summary>
    [JsonPropertyName("skillZone")] public double SkillZone { get; set; }
    /// <summary>Enemies in its reach move this much slower while the skill runs (0.5 = −50 %).</summary>
    [JsonPropertyName("skillSlow")] public double SkillSlow { get; set; }
    /// <summary>The skill's hits push enemies away or drag them in.</summary>
    [JsonPropertyName("skillPush")] public PushData? SkillPush { get; set; }
    /// <summary>The talents at each potential they change at (build_operator_profiles.py talent_levels).</summary>
    [JsonPropertyName("talents")] public List<TalentLevel> Talents { get; set; } = [];
    /// <summary>The summon that fights beside it (null: none modelled).</summary>
    [JsonPropertyName("summon")] public SummonData? Summon { get; set; }

    /// <summary>The talents in force at <paramref name="potential"/> (0–5; null: none read).</summary>
    public TalentLevel? TalentAt(int potential) => Talents.LastOrDefault(t => t.Pot <= potential);

    /// <summary>
    /// Effective HP against a physical hit of <paramref name="enemyAtk"/> (damage = attack − defence, at least 5 %).
    /// </summary>
    public double EffectiveHp(double enemyAtk) => Hp * enemyAtk / Math.Max(enemyAtk - Def, 0.05 * enemyAtk);
}

/// <summary>
/// Class, skill and planner role tags of an operator, from the hand-checkable
/// <c>data/operator_profiles.json</c> generated by <c>build_operator_profiles.py</c>.
/// </summary>
/// <summary>A skill's healing: once on cast to everyone in range (burst), or per second while it runs (hot); × attack.</summary>
public sealed class SkillHeal
{
    [JsonPropertyName("burst")] public double Burst { get; set; }
    [JsonPropertyName("hot")] public double Hot { get; set; }
}

/// <summary>Control the skill's hits put on enemies: "stun" (no moving or attacking) or "stop" (no moving).</summary>
public sealed class SkillControl
{
    [JsonPropertyName("kind")] public string Kind { get; set; } = "";
    [JsonPropertyName("seconds")] public double Seconds { get; set; }
    [JsonPropertyName("chance")] public double Chance { get; set; } = 1;
}

/// <summary>A displacement: about this many cells for a 1-weight enemy, away from the operator or towards it.</summary>
public sealed class PushData
{
    [JsonPropertyName("cells")] public double Cells { get; set; }
    [JsonPropertyName("pull")] public bool Pull { get; set; }
}

/// <summary>
/// What an operator's talents do, read from their descriptions (data/talent_effects.py): fractions are ratios
/// (0.12 = +12 %), conditional effects already scaled by how often they hold.
/// </summary>
public class TalentEffect
{
    [JsonPropertyName("atk")] public double Atk { get; set; }
    [JsonPropertyName("atkFlat")] public double AtkFlat { get; set; }
    [JsonPropertyName("aspd")] public double Aspd { get; set; }
    [JsonPropertyName("def")] public double Def { get; set; }
    [JsonPropertyName("defFlat")] public double DefFlat { get; set; }
    [JsonPropertyName("hp")] public double Hp { get; set; }
    [JsonPropertyName("res")] public double Res { get; set; }
    [JsonPropertyName("block")] public double Block { get; set; }
    [JsonPropertyName("dodge")] public double Dodge { get; set; }
    /// <summary>Damage taken reduction (庇护 and the like).</summary>
    [JsonPropertyName("sanctuary")] public double Sanctuary { get; set; }
    /// <summary>HP regenerated per second: a share of max HP, and a flat amount.</summary>
    [JsonPropertyName("regen")] public double Regen { get; set; }
    [JsonPropertyName("regenFlat")] public double RegenFlat { get; set; }
    /// <summary>Extra SP per second, and SP on deploy.</summary>
    [JsonPropertyName("sp")] public double Sp { get; set; }
    [JsonPropertyName("initSp")] public double InitSp { get; set; }
}

/// <summary>A talent buff on other operators: who gets it (scope) and what.</summary>
public sealed class TalentAura : TalentEffect
{
    /// <summary>all / range (its attack range) / around8 / behind / front / members (<see cref="Members"/>).</summary>
    [JsonPropertyName("scope")] public string Scope { get; set; } = "";
    [JsonPropertyName("members")] public List<string>? Members { get; set; }
    /// <summary>The operator itself gets it too.</summary>
    [JsonPropertyName("self")] public bool Self { get; set; }
}

/// <summary>The talents in force from potential <see cref="Pot"/> on: own stats, attack riders, auras.</summary>
public sealed class TalentLevel : TalentEffect
{
    [JsonPropertyName("pot")] public int Pot { get; set; }
    /// <summary>Expected extra attack from chance-based empowered hits (0.15 = × 1.15 on average).</summary>
    [JsonPropertyName("crit")] public double Crit { get; set; }
    /// <summary>Damage dealt + this.</summary>
    [JsonPropertyName("dmg")] public double Dmg { get; set; }
    [JsonPropertyName("defIgnore")] public double DefIgnore { get; set; }
    [JsonPropertyName("defIgnoreRatio")] public double DefIgnoreRatio { get; set; }
    [JsonPropertyName("resIgnore")] public double ResIgnore { get; set; }
    /// <summary>Extra arts damage per hit, × attack.</summary>
    [JsonPropertyName("extraArts")] public double ExtraArts { get; set; }
    /// <summary>Poison on hit: arts damage per second (flat, or × attack) for <see cref="DotSeconds"/>.</summary>
    [JsonPropertyName("dotDps")] public double DotDps { get; set; }
    [JsonPropertyName("dotScale")] public double DotScale { get; set; }
    [JsonPropertyName("dotSeconds")] public double DotSeconds { get; set; }
    /// <summary>Enemies in its range take this much more damage.</summary>
    [JsonPropertyName("fragile")] public double Fragile { get; set; }
    /// <summary>Enemies in its range move this much slower.</summary>
    [JsonPropertyName("slow")] public double Slow { get; set; }
    /// <summary>Its attacks deal arts damage (塔露拉).</summary>
    [JsonPropertyName("arts")] public bool Arts { get; set; }
    [JsonPropertyName("control")] public SkillControl? Control { get; set; }
    [JsonPropertyName("auras")] public List<TalentAura> Auras { get; set; } = [];
    /// <summary>Talent sentences not read (for checking by hand).</summary>
    [JsonPropertyName("notes")] public List<string> Notes { get; set; } = [];
}

/// <summary>A summon that fights beside its operator, with its stats at the operator's level.</summary>
public sealed class SummonData
{
    [JsonPropertyName("name")] public string Name { get; set; } = "";
    [JsonPropertyName("hp")] public double Hp { get; set; }
    [JsonPropertyName("atk")] public double Atk { get; set; }
    [JsonPropertyName("def")] public double Def { get; set; }
    [JsonPropertyName("res")] public double Res { get; set; }
    [JsonPropertyName("interval")] public double Interval { get; set; } = 1;
    [JsonPropertyName("aspd")] public double AttackSpeed { get; set; } = 100;
    [JsonPropertyName("block")] public int Block { get; set; }
    [JsonPropertyName("range")] public string? Range { get; set; }
    [JsonPropertyName("melee")] public bool Melee { get; set; }
    /// <summary>Seconds on the field (0: stays, back <see cref="Cooldown"/> s after it falls).</summary>
    [JsonPropertyName("duration")] public double Duration { get; set; }
    [JsonPropertyName("cooldown")] public double Cooldown { get; set; }
    [JsonPropertyName("appear")] public double Appear { get; set; }
    /// <summary>Stands on the enemies' path in its operator's range (else beside its operator, sharing its aim).</summary>
    [JsonPropertyName("lane")] public bool Lane { get; set; }
    [JsonPropertyName("hits")] public int Hits { get; set; } = 1;
    [JsonPropertyName("push")] public PushData? Push { get; set; }
    [JsonIgnore] public AttackRange? RangeShape { get; set; }
}

public class OperatorProfile
{
    public const string TankRole = "盾位";
    public const string HealerRole = "奶位";

    [JsonPropertyName("profession")] public string Profession { get; set; } = "";
    [JsonPropertyName("subProfession")] public string SubProfession { get; set; } = "";
    [JsonPropertyName("position")] public string Position { get; set; } = "";
    [JsonPropertyName("blockCount")] public int BlockCount { get; set; }
    [JsonPropertyName("normalSkill")] public OperatorSkill? NormalSkill { get; set; }
    [JsonPropertyName("eliteSkill")] public OperatorSkill? EliteSkill { get; set; }
    [JsonPropertyName("normalRange")] public OperatorRangeRef? NormalRange { get; set; }
    [JsonPropertyName("eliteRange")] public OperatorRangeRef? EliteRange { get; set; }
    /// <summary>Carried skill (0 = S1): the player's choice where it differs from the game default.</summary>
    [JsonPropertyName("skillIndex")] public int? SkillIndex { get; set; }
    /// <summary>Combat stats of the normal / elite piece (level, module and carried skill of the mode).</summary>
    [JsonPropertyName("normalStats")] public ChessStats? NormalStats { get; set; }
    [JsonPropertyName("eliteStats")] public ChessStats? EliteStats { get; set; }
    /// <summary>What each potential rank (2 潜 … 6 潜) adds: atk / hp / def / res / aspd.</summary>
    [JsonPropertyName("potential")] public List<Dictionary<string, double>> Potential { get; set; } = [];
    /// <summary>The potential ranks' bonuses summed up to <paramref name="potential"/> (0 = none).</summary>
    public double PotentialBonus(string stat, int potential) =>
        Potential.Take(Math.Clamp(potential, 0, 5)).Sum(p => p.GetValueOrDefault(stat));
    public ChessStats? StatsFor(bool elite) => elite ? EliteStats ?? NormalStats : NormalStats;
    [JsonPropertyName("roles")] public List<string> Roles { get; set; } = [];
    [JsonPropertyName("stack")] public OperatorStackInfo? Stack { get; set; }
    /// <summary>Covenants whose layers make this operator stronger (吃层核心).</summary>
    [JsonPropertyName("consumes")] public List<string> Consumes { get; set; } = [];

    [JsonIgnore] public bool IsMelee => Position == "近战";
    /// <summary>Normal-state attack range (elite uses <see cref="EliteRange"/>).</summary>
    public AttackRange? RangeFor(bool elite, bool duringSkill = false)
    {
        var r = elite ? EliteRange ?? NormalRange : NormalRange;
        return r is null ? null : duringSkill ? r.SkillRange ?? r.BaseRange : r.BaseRange;
    }

    [JsonIgnore] public bool IsTank => Roles.Contains(TankRole);
    [JsonIgnore] public bool IsHealer => Roles.Contains(HealerRole);
    /// <summary>Stacks layers during the run (获得时 one-off stacking does not count for planning).</summary>
    [JsonIgnore] public bool IsStacker => Stack is not null && Stack.Kind != "once";

    [JsonIgnore]
    public string ClassText => $"{Profession} · {SubProfession} · {Position}" + (BlockCount > 0 ? $" · 阻挡 {BlockCount}" : "");

    /// <summary>Short role summary for UI, e.g. “盾位 · 叠层【乌萨斯】”.</summary>
    [JsonIgnore]
    public string RoleText
    {
        get
        {
            var parts = new List<string>(Roles);
            if (Stack is { } s)
                parts.Add(s.Kind switch
                {
                    "adjacent" => "通用叠层（自身+相邻）",
                    "grant" => "给相邻干员叠层",
                    "self" => "叠层（自身盟约）",
                    "amplifier" => "叠层放大",
                    "once" => "一次性叠层" + Bracket(s.Covenants),
                    _ => "叠层" + Bracket(s.Covenants),
                });
            if (Consumes.Count > 0) parts.Add("吃层" + Bracket(Consumes));
            return string.Join(" · ", parts);
        }
    }

    private static string Bracket(IEnumerable<string> names) => string.Concat(names.Select(n => $"【{n}】"));
}
