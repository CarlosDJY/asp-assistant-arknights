// Modified for ASP Assistant Regulation on 2026-11-14; see NOTICE.
using System.Text.RegularExpressions;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

public sealed record PlacementOptions
{
    /// <summary>Operators on the field (8 unless the player says 9).</summary>
    public int FieldCount { get; init; } = 8;
    public PlacementMode Mode { get; init; } = PlacementMode.Balanced;
    /// <summary>Operators the player fixed by hand.</summary>
    public IReadOnlyDictionary<string, PlacementPin> Pins { get; init; } = new Dictionary<string, PlacementPin>();
    /// <summary>Operators (keys) the player keeps on the field, wherever the plan puts them.</summary>
    public IReadOnlySet<string> FieldPins { get; init; } = new HashSet<string>();
    /// <summary>Operators (keys) the player keeps in the 整备区: never put on the field.</summary>
    public IReadOnlySet<string> BenchPins { get; init; } = new HashSet<string>();
    /// <summary>Covenants a 转职 item can give (for the plan's 转职 hints; empty: no hints).</summary>
    public IReadOnlySet<string> TransformableCovenants { get; init; } = new HashSet<string>();
    public int Seed { get; init; } = 1;
    public int Iterations { get; init; } = 5000;
    public int Restarts { get; init; } = 5;

    /// <summary>
    /// The real enemy routes of the round being planned (garrison_routes.json). Null: the map's two fixed ground
    /// routes with the old 1 : 2 weighting, and no flying enemies.
    /// </summary>
    public RoundRoutes? Routes { get; init; }
    /// <summary>Whether this round brings flying enemies; the player can say no when the round has none.</summary>
    public bool ExpectFlying { get; init; } = true;
    /// <summary>The round being planned and how many rounds the mode has; balances stacking against this round's fight.</summary>
    public int? Round { get; init; }
    public int TotalRounds { get; init; } = 15;
    /// <summary>
    /// Current layers of covenants when known. They scale the members' buff now (炎 at 80 layers: attack
    /// +(23+0.9×80)%) and unlock thresholds (灵巧 at 40: the 8 surrounding cells; 突袭 at 50: everyone +50 attack speed).
    /// TODO(盟约层数识别): read the layers in game (shop / board HUD) and pass them in; until then this is empty
    /// unless set by hand, and only stacking per layer counts.
    /// </summary>
    public IReadOnlyDictionary<string, int> CovenantLayers { get; init; } = new Dictionary<string, int>();
    /// <summary>
    /// Potential assumed for every operator (0 = 1 潜 … 5 = 满潜): its attribute bonuses and the talents it unlocks
    /// or raises. The pieces' data carry no potential, so this is the player's call.
    /// </summary>
    public int Potential { get; init; } = 5;

    /// <summary>
    /// Judge the fight by simulating a steady enemy stream (<see cref="ClearSimulator"/>) and counting how many
    /// enemies the placement handles; off: the old hand-tuned combat score only.
    /// </summary>
    public bool Simulate { get; init; } = true;
    /// <summary>The stream to simulate; null: this round's counts with enemy HP calibrated so that the first plan
    /// handles about <see cref="TargetKillRate"/> of them (a moderately hard stream for this pool).</summary>
    public WaveScenario? Scenario { get; init; }
    public double TargetKillRate { get; init; } = 0.75;
    /// <summary>Annealing steps with the simulation (each step simulates one whole fight).</summary>
    public int SimulationIterations { get; init; } = 700;
    /// <summary>
    /// The real streams this round may bring (<see cref="GarrisonSpawnStore.Streams"/>: one of the run's 3 enemy types,
    /// as one of its groups). When set they replace the generic stream: the plan maximises the expected number of
    /// enemies handled over them, and flying enemies are planned for only when one of the types flies.
    /// </summary>
    public IReadOnlyList<RoundStream>? Streams { get; init; }
    /// <summary>How many streams to simulate per step at most (the likeliest groups of each type).</summary>
    public int MaxStreams { get; init; } = 6;
    /// <summary>
    /// HP scale of the copy of the real streams the search also counts (half and half with the real ones);
    /// null: calibrated so that the first plan handles about <see cref="TargetKillRate"/> of it, and no copy when the
    /// real round is already moderately hard (40–75 %). The results shown are always at the real strength.
    /// </summary>
    public double? StreamFactor { get; init; }

    private double ModeWeight => Mode switch
    {
        PlacementMode.Combat => 0.25,
        PlacementMode.Stacking => 5.0,
        _ => 2.5,
    };

    /// <summary>
    /// How much a layer counts against an enemy handled: by mode only (叠层 5, 平衡 2.5, 战斗 0.25). The same in every
    /// round: layers are what beats the bosses at the end, so they keep their weight late in the run too.
    /// </summary>
    public double StackWeight => ModeWeight;
}

/// <summary>
/// Picks which operators of the pool go on the field and where they stand, by simulated annealing over a
/// hand-tuned score. Ported from the positioning prototype; damage power and toughness come from the pieces'
/// real stats and carried skills (operator_profiles.json), the remaining weights are estimates:
/// <list type="bullet">
/// <item>Each route's block point is the first operator on it. Tanks belong there, melee damage dealers right
/// behind (they also refill the line), ranged operators and healers further back. The lower route gets about
/// twice the enemies of the upper one.</item>
/// <item>Damage is the time enemies spend in an operator's range, weighted towards the block point and the
/// 1–2 cells in front of it, times how much the operator gains from damage buffs.</item>
/// <item>Tiles that buff enemies (红地板, 蓝箭头) buff them whenever they walk over, so the defence line
/// should be in front of those cells; 沼泽 slows both sides, so operators avoid standing on it.</item>
/// <item>Stacking from positional traits (身前 / 身后一格, 周围四格 / 八格, 同一行, 攻击范围内), worth more the
/// more the stacked covenant's stat helps the lineup.</item>
/// <item>With this round's routes (<see cref="PlacementOptions.Routes"/>), the ground lanes are the real walking
/// paths weighted by their enemies (lanes that merge share a block point), and flying enemies are covered by
/// anti-air operators along the flying paths; without them, the map's two fixed routes and no flyers.</item>
/// <item>Stacking is weighed against this round's fight by the mode (<see cref="PlacementOptions.StackWeight"/>).</item>
/// </list>
/// 阿戈尔's devour and the snow map's stealth cells are not modelled.
/// </summary>
public sealed class PlacementPlanner
{
    private readonly IReadOnlyDictionary<string, Operator> _operators;
    private readonly IReadOnlyDictionary<string, CovenantInfo> _covenants;

    public PlacementPlanner(IReadOnlyDictionary<string, Operator> operators, IEnumerable<CovenantInfo> covenants)
    {
        _operators = operators;
        _covenants = covenants.GroupBy(c => c.Name).ToDictionary(g => g.Key, g => g.First());
    }

    /// <summary>Operators the planner can use (they need class, block and range data).</summary>
    public bool CanPlace(string name) =>
        _operators.TryGetValue(name, out var op) && op.Profile is { } p && p.NormalRange?.BaseRange is not null;

    public bool IsMelee(string name) => _operators.TryGetValue(name, out var op) && op.Profile?.IsMelee == true;

    /// <summary>Chooses the field operators from <paramref name="pool"/> and arranges them.</summary>
    public PlacementPlan Plan(GarrisonMap map, IReadOnlyList<PlacementCandidate> pool, PlacementOptions options,
        CancellationToken cancel = default)
    {
        var job = new Job(this, map, pool, options);
        var best = job.Solve(cancel);
        if (options.Simulate && best.Count > 0)
        {
            job.Calibrate(best);
            best = job.Solve(cancel, simulate: true, start: best);
        }
        return job.Explain(best);
    }

    /// <summary>Scores a given arrangement (e.g. after the player dragged someone), without moving anyone.</summary>
    public PlacementPlan Evaluate(GarrisonMap map, IReadOnlyList<PlacementCandidate> pool,
        IReadOnlyDictionary<string, (BoardCell Cell, Facing Facing)> placement, PlacementOptions options)
    {
        var job = new Job(this, map, pool, options);
        var pl = job.Normalise(placement);
        if (options.Simulate && pl.Count > 0) job.Calibrate(pl);
        return job.Explain(pl);
    }

    /// <summary>The best other cells for <paramref name="name"/> with everyone else where they are.</summary>
    public IReadOnlyList<PlacementAlternative> Alternatives(GarrisonMap map, IReadOnlyList<PlacementCandidate> pool,
        IReadOnlyDictionary<string, (BoardCell Cell, Facing Facing)> placement, string name, PlacementOptions options,
        int count = 2)
    {
        var job = new Job(this, map, pool, options);
        var pl = job.Normalise(placement);
        if (!pl.TryGetValue(name, out var here)) return [];
        double current = job.Score(pl);
        var occupied = pl.Where(kv => kv.Key != name).Select(kv => kv.Value.Cell).ToHashSet();
        var result = new List<PlacementAlternative>();
        foreach (var cell in job.Board.Deployable)
        {
            if (cell == here.Cell || occupied.Contains(cell) || !job.Board.Legal(job.Info[name], cell)) continue;
            (double Score, Facing Facing) best = (double.MinValue, Facing.Right);
            foreach (var f in Faces)
            {
                pl[name] = (cell, f);
                double s = job.Score(pl);
                if (s > best.Score) best = (s, f);
            }
            pl[name] = (cell, best.Facing);
            var notes = job.NotesFor(pl, name);
            result.Add(new PlacementAlternative(cell, best.Facing, Math.Round(best.Score - current, 1),
                notes.FirstOrDefault() ?? PlacementCells.FacingText(best.Facing)));
        }
        pl[name] = here;
        return [.. result.OrderByDescending(a => a.Delta).Take(count)];
    }

    // ── Static tables ──────────────────────────────────────────────────────

    private static readonly Facing[] Faces = [Facing.Right, Facing.Down, Facing.Left, Facing.Up];
    // ── Real stats (operator_profiles.json normalStats / eliteStats) ──
    /// <summary>Effective DPS (panel × carried skill) that counts as power 1; keeps the scores on the old scale.</summary>
    private const double ReferenceDps = 620;
    /// <summary>Skill estimates above this are outliers of the rough skill model.</summary>
    private const double MaxSkillFactor = 3;
    /// <summary>Talents, potential and 卫戍协议 traits' combat effects are not in the stats yet: a mild per-tier allowance.</summary>
    private const double UnmodelledPerTier = 0.05;
    /// <summary>A medic's attack is its healing; it counts this much as damage.</summary>
    private const double HealerDamageShare = 0.15;
    /// <summary>Typical enemy physical attack for effective HP, and the effective HP that holds a line "normally".</summary>
    private const double EnemyAtk = 700, HoldReferenceEhp = 5000;

    /// <summary>Fallback without stats (old estimate): tier × profession.</summary>
    private static readonly Dictionary<int, double> TierPower = new() { [1] = 0.6, [2] = 0.75, [3] = 0.9, [4] = 1.1, [5] = 1.35, [6] = 1.6 };
    private static readonly Dictionary<string, double> ProfessionDps = new()
    {
        ["近卫"] = 1.0, ["狙击"] = 1.0, ["术师"] = 1.0, ["特种"] = 0.85, ["先锋"] = 0.7, ["辅助"] = 0.5, ["医疗"] = 0.15, ["重装"] = 0.35,
    };

    private enum Stat { Atk, Aspd, Hp, Heal, None }

    /// <summary>How much each role gains from a kind of buff (0–1).</summary>
    private static double Affinity(Stat stat, PlacementRole role) => stat switch
    {
        Stat.Atk or Stat.Aspd => role switch { PlacementRole.Damage => 1.0, PlacementRole.Tank => 0.3, PlacementRole.Support => stat == Stat.Aspd ? 0.5 : 0.45, _ => 0.45 },
        Stat.Hp => role switch { PlacementRole.Tank => 1.0, PlacementRole.Damage => 0.35, _ => 0.3 },
        Stat.Heal => role switch { PlacementRole.Support => 1.0, PlacementRole.Tank => 0.2, _ => 0.1 },
        _ => 0,
    };

    private static string StatText(Stat stat) => stat switch
    {
        Stat.Atk => "攻击",
        Stat.Aspd => "攻速",
        Stat.Hp => "生存",
        Stat.Heal => "治疗",
        _ => "",
    };

    /// <summary>Who a covenant's per-layer buff reaches.</summary>
    private enum LayerScope { Members, Everyone, PreciseRanged }

    /// <summary>
    /// What one layer of a covenant adds, from the formulas in covenant_effects.json (e.g. 炎 攻击力 +(23+0.9×层数)%
    /// → 0.9 % attack per layer). Conditional effects are discounted by how often the condition holds; economy
    /// covenants (远见 / 奇迹 / 投资人) and flat ones (协防干员 / 独行 / 绝技 / 调和) add nothing per layer here.
    /// </summary>
    private sealed record LayerEffect(LayerScope Scope, params (Stat Stat, double PerLayer)[] Parts);

    private static readonly Dictionary<string, LayerEffect> CovenantLayers = new()
    {
        ["炎"] = new(LayerScope.Members, (Stat.Atk, 0.009)),
        // +12 攻速 for (5+0.22×层数) s on every skill cast: +0.22 s per layer of a buff that is up part of the time.
        ["萨尔贡"] = new(LayerScope.Members, (Stat.Aspd, 0.002)),
        // 携带装备的干员伤害 (125+0.8×层数)%: pieces carry equipment most of the run.
        ["维多利亚"] = new(LayerScope.Members, (Stat.Atk, 0.008)),
        // (135+1×层数)% only against cold / frozen enemies.
        ["谢拉格"] = new(LayerScope.Members, (Stat.Atk, 0.004)),
        ["阿戈尔"] = new(LayerScope.Members, (Stat.Hp, 0.01)),
        // 攻速 +(25+0.8×层数) for (32+0.4×层数) s after deploying, most of a fight.
        ["叙拉古"] = new(LayerScope.Members, (Stat.Aspd, 0.007)),
        // The attack cap (50+1×层数)% is reached after a few deploys.
        ["卡西米尔"] = new(LayerScope.Members, (Stat.Atk, 0.006)),
        ["乌萨斯"] = new(LayerScope.Members, (Stat.Atk, 0.0075), (Stat.Hp, 0.003)),
        // DoT (11+0.06×层数)% 攻击力 per second, stacking up to 10 times.
        ["伊比利亚"] = new(LayerScope.Members, (Stat.Atk, 0.004)),
        ["莱塔尼亚"] = new(LayerScope.Members, (Stat.Atk, 0.01)),
        // 3 名 精准: every ranged operator gets it too.
        ["精准"] = new(LayerScope.PreciseRanged, (Stat.Atk, 0.012)),
        // (20+0.35×层数)% chance to refund 12 SP when a skill ends.
        ["迅捷"] = new(LayerScope.Members, (Stat.Aspd, 0.002)),
        // Members and the 4 cells around them (the aura itself is scored by position).
        ["灵巧"] = new(LayerScope.Members, (Stat.Aspd, 0.01)),
        ["坚守"] = new(LayerScope.Everyone, (Stat.Hp, 0.012)),
        // 防御力 +(15+1.2×层数)% for everyone, worth about two thirds of as much HP.
        ["助力"] = new(LayerScope.Everyone, (Stat.Hp, 0.008)),
        // Only after the redeploy that 突袭 triggers.
        ["突袭"] = new(LayerScope.Members, (Stat.Atk, 0.006)),
        // (18+0.4×层数)% chance to redeploy when knocked out.
        ["不屈"] = new(LayerScope.Everyone, (Stat.Hp, 0.004)),
        ["奥术"] = new(LayerScope.Members, (Stat.Atk, 0.004)),
        // Heal (16+0.12×层数)% 攻击力 per second: 0.12 / 16 of the heal per layer.
        ["聚合"] = new(LayerScope.Members, (Stat.Heal, 0.0075)),
    };

    /// <summary>
    /// Score per (layer fraction × affinity-weighted power) of the members. Calibrated so 炎 (0.9 % attack per layer)
    /// keeps the value every covenant had before; the others now differ by their own formula.
    /// </summary>
    private const double LayerScale = 28;

    /// <summary>Per-layer buff of a covenant as a fraction of the stat (summed over its parts); 0 when none.</summary>
    public static double PerLayer(string covenant) =>
        CovenantLayers.TryGetValue(covenant, out var e) ? e.Parts.Sum(p => p.PerLayer) : 0;

    /// <summary>Covenants that count operators on the bench too.</summary>
    private static readonly HashSet<string> BenchCountedCovenants = ["远见", "奇迹", "投资人"];
    private static readonly HashSet<string> PositionalStackers = ["凛御银灰", "Mon3tr", "阿米娅", "华法琳", "铃兰", "塞雷娅", "罗比菈塔", "浊心斯卡蒂"];
    private static readonly HashSet<string> RowOfThree = ["机械师", "歌蕾蒂娅"];
    /// <summary>Fixed routes only: the lower route was guessed to get about twice the enemies of the upper one.</summary>
    private static readonly double[] StaticRouteWeight = [1.0, 2.0];
    private const double BlockNeed = 5;
    /// <summary>Score of one enemy handled in the simulation (50 points of stacking ≈ 5 more enemies).</summary>
    private const double KillPoint = 10;
    /// <summary>A real round this far below the target kill rate is hopeless for the pool (an easier copy is added).</summary>
    private const double HopelessBelow = 0.35;
    /// <summary>Anti-air coverage asked for, as a share of the round's flying presence (about two ranged operators).</summary>
    private const double AirNeedShare = 0.45;

    private enum TileKind { Slow, EnemyBuff, Facing }

    /// <summary><paramref name="EnemyPenalty"/>: per route, when enemies walk the tile before reaching the line.</summary>
    private sealed record TileEffect(TileKind Kind, Stat Stat, double Ally, bool Drain, string Name, double EnemyPenalty = 0, bool HintOnly = false);

    private static readonly Dictionary<string, TileEffect> Tiles = new()
    {
        ["desert_camo"] = new(TileKind.Slow, Stat.Aspd, -0.45, false, "沼泽"),
        ["desert_laser"] = new(TileKind.EnemyBuff, Stat.Atk, 0.3, true, "红地板", EnemyPenalty: 8),
        // Arrows only make enemies faster, so standing on them / keeping the line in front is a preference, not a must.
        ["palace_arrows"] = new(TileKind.EnemyBuff, Stat.Aspd, 0.25, false, "蓝箭头", EnemyPenalty: 3, HintOnly: true),
        ["city"] = new(TileKind.Facing, Stat.Atk, 0.3, false, "橙色通道"),
    };

    private static readonly (BoardCell From, BoardCell To) PalaceTeleport = (new(3, 0), new(3, 3));

    private static readonly Regex CapPattern = new(@"至多叠加(\d+)层");
    private static readonly Regex AmountPattern = new(@"层数\+(\d+)");
    private static readonly Regex OncePattern = new(@"<获得时>.*?层数\+(\d+)");
    private static readonly Regex SelfFrontPattern = new(@"自身(以|及)?及?身前一格干员已激活的盟约层数\+");
    private static readonly Regex SelfFrontAmount = new(@"身前一格干员已激活的盟约层数\+(\d+)");
    private static readonly Regex SkillBlockPattern = new(@"(?<!身前一格[^。；]*)阻挡数\+");
    private static readonly AttackRange FrontCell = new("front-1", [(1, 0)]);
    private static readonly Regex RevealPattern = new(@"(\d+)秒内[^；。]*隐匿失效");
    /// <summary>Fight length the profiles' skill factors average over (build_operator_profiles.py BATTLE_SECONDS).</summary>
    private const double SkillBattleSeconds = 45;

    /// <summary>
    /// The carried skill on its SP cycle, with the in-skill multiplier that gives back the profile's average factor
    /// over <see cref="SkillBattleSeconds"/> (the same timeline build_operator_profiles.py uses). Null for passive
    /// skills and those that never come up in a fight.
    /// </summary>
    private static SimSkill? SkillCycle(OperatorSkill? skill, double factor, double interval, ChessStats? stats = null)
    {
        if (skill is null || skill.SpCost <= 0 || skill.SpType == "被动") return null;
        // "持续时间无限": on for the rest of the fight once cast.
        double duration = skill.Duration < 0 && skill.Description.Contains("持续时间无限") ? 999 : skill.Duration;
        skill = new OperatorSkill
        {
            Name = skill.Name, Description = skill.Description, SpCost = skill.SpCost, InitSp = skill.InitSp,
            Duration = duration, SpType = skill.SpType,
        };
        var charge = skill.SpType switch { "攻击回复" => SkillCharge.Attack, "受击回复" => SkillCharge.Hit, _ => SkillCharge.Auto };
        double perSp = charge switch { SkillCharge.Attack => interval, SkillCharge.Hit => 2.0, _ => 1.0 };
        double active = skill.Duration > 0 ? skill.Duration : interval;
        double t = Math.Max(0, (skill.SpCost - skill.InitSp) * perSp), on = 0;
        while (t < SkillBattleSeconds)
        {
            on += Math.Min(active, SkillBattleSeconds - t);
            t += active + skill.SpCost * perSp;
        }
        if (on <= 0) return null;
        // The in-skill multiplier read from the skill's data when there is one (targets counted apart), else the one
        // that gives back the profile's average factor; never below 1 (damage over time or areas are not modelled).
        double gain = stats?.SkillGain is double g ? g : 1 + (factor - 1) * SkillBattleSeconds / on;
        return new SimSkill(Math.Clamp(gain, 1, 12), skill.SpCost, Math.Min(skill.InitSp, skill.SpCost), skill.Duration, charge)
        {
            Targets = stats is { SkillTargets: > 1 } ? stats.SkillTargets : 1,
            Hits = stats is { SkillHits: > 1 } ? stats.SkillHits : 1,
            HitsBlocked = stats?.SkillBlocked ?? false,
            Damage = stats?.SkillDamage switch { "true" => SkillDamage.True, "arts" => SkillDamage.Arts, _ => SkillDamage.Same },
            ResIgnore = stats?.SkillResIgnore ?? 0,
            EnemyDefScale = stats?.SkillEnemyDef ?? 1,
            EnemyResAdd = stats?.SkillEnemyRes ?? 0,
            HealBurst = stats?.SkillHeal?.Burst ?? 0,
            HealPerSecond = stats?.SkillHeal?.Hot ?? 0,
            ControlStuns = stats?.SkillControl?.Kind == "stun",
            ControlSeconds = stats?.SkillControl?.Seconds ?? 0,
            ControlChance = stats?.SkillControl?.Chance ?? 1,
            SelfDef = stats?.SkillDef ?? 0,
            SelfHp = stats?.SkillHp ?? 0,
            SelfBlock = stats?.SkillBlock ?? 0,
            Zone = stats?.SkillZone ?? 0,
            Slow = stats?.SkillSlow ?? 0,
            Push = stats?.SkillPush,
        };
    }

    /// <summary>How the branch's normal attack lands (its trait), for the simulation.</summary>
    private static AttackPattern PatternOf(string subProfession) => subProfession switch
    {
        "扩散术师" or "炮手" or "撼地者" => new AttackPattern { SplashRadius = 1.0 },
        "投掷手" => new AttackPattern { SplashRadius = 0.8 },
        "重剑手" or "强攻手" or "推击手" or "收割者" => new AttackPattern { HitsBlocked = true },
        "伏击客" or "散射手" => new AttackPattern { Targets = 99 },
        "链术师" => new AttackPattern { ChainTargets = 4 },
        "剑豪" => new AttackPattern { HitsPerAttack = 2 },
        "群愈师" or "链愈师" => new AttackPattern { HealTargets = 3 },
        "阵法术师" => new AttackPattern { SkillOnly = true },
        "吟游者" => new AttackPattern { HealAura = 0.1 },
        "护佑者" => new AttackPattern { HealsDuringSkill = true },
        _ => AttackPattern.Single,
    };
    private static readonly Dictionary<string, double> SelfFrontOverrides = new() { ["旅骨"] = 3 };
    private static readonly Dictionary<string, double> SelfBack = new() { ["青枳"] = 3, ["佩佩"] = 3 };

    private static double Number(Regex pattern, string text, double fallback = 0)
    {
        var m = pattern.Match(text);
        return m.Success && double.TryParse(m.Groups[1].Value, out var v) ? v : fallback;
    }

    // ── Per-operator facts ─────────────────────────────────────────────────

    private sealed class OpInfo
    {
        public required string Name;
        public required PlacementRole Role;
        public required double Power;
        /// <summary>Attack, HP and defence of the piece (estimates without stats); used for 阿戈尔's devour.</summary>
        public required double Atk, Hp, Def;
        public double Res;
        /// <summary>Seconds between attacks (interval ÷ attack speed) and whether it deals arts damage.</summary>
        public double AttackInterval = 1.2;
        public bool Arts;
        /// <summary>Effective HP against a typical enemy hit; how long it holds a block point.</summary>
        public required double Ehp;
        /// <summary>Effective DPS and the carried skill's factor, for the notes (null without stats).</summary>
        public double? EffectiveDps;
        public double SkillFactor = 1;
        /// <summary>The carried skill on its SP cycle (null: passive or unknown).</summary>
        public SimSkill? Skill;
        /// <summary>Attack range offsets per facing while the skill runs (null: the normal range).</summary>
        public (int Col, int Row)[][]? SkillOffsets;
        public AttackPattern Pattern = AttackPattern.Single;
        /// <summary>A medic: heals instead of attacking (the fight's healer).</summary>
        public bool Medic;
        /// <summary>Seconds its skill strips invisibility from the enemies in range (凛御银灰).</summary>
        public double SkillRevealSeconds;
        /// <summary>Its talents at the assumed potential (own stats already in Atk / Hp / …; riders and auras here).</summary>
        public TalentLevel? Talent;
        /// <summary>The summon fighting beside it, and its range offsets per facing.</summary>
        public SummonData? Summon;
        public (int Col, int Row)[][]? SummonOffsets;

        /// <summary>This operator with its covenants' current layers applied.</summary>
        public OpInfo Buffed(double power, double ehp)
        {
            var copy = (OpInfo)MemberwiseClone();
            copy.Power = power;
            copy.Ehp = ehp;
            return copy;
        }

        /// <summary>The 转职 it holds (one, or up to five for 暴躁铁皮).</summary>
        public string[] Transforms = [];

        /// <summary>This operator also holding <paramref name="covenant"/> (a 转职 tried out).</summary>
        public OpInfo WithCovenant(string covenant)
        {
            var copy = (OpInfo)MemberwiseClone();
            copy.Covenants = [.. Covenants, covenant];
            return copy;
        }

        /// <summary>This operator after 阿戈尔 devoured someone: more attack (power) and block.</summary>
        public OpInfo Devoured(double power, int block)
        {
            var copy = (OpInfo)MemberwiseClone();
            copy.Power = power;
            copy.Block = block;
            return copy;
        }
        public required bool Melee;
        public required int Block;
        public required (int Col, int Row)[][] Offsets; // by Facing
        public required string[] Covenants;
        public required int Tier;
        public required bool Healer;
        public required string Trait;
        public required double Cap;
        public required bool StacksLayers;
        public required double OnceAmount;
        public required double TriggersPerRound;
        public required int SkillCasts;
        public required double SelfFront;
        public required double SelfBack;
        public required bool KillRange;
        public required bool Around4;
        public required bool BenchOk;
        public required bool SkillBlock;
        /// <summary>Can hit flying enemies: every ranged operator, and melee 领主 (they attack at range).</summary>
        public required bool AntiAir;
        /// <summary>Own stacking from the profile: covenant (named covenants) / self (own covenants) / other.</summary>
        public required string StackKind;
        public required string[] StackCovenants;
        public required double StackPerRound;
        public required double StackAmount;
    }

    private OpInfo? BuildInfo(PlacementCandidate c, int potential = 5)
    {
        if (!_operators.TryGetValue(c.Name, out var op) || op.Profile is not { } p) return null;
        var range = p.RangeFor(c.IsElite);
        if (range is null) return null;
        var variant = c.IsElite && op.Elite.Traits.Count > 0 ? op.Elite : op.Normal;
        string trait = string.Join(" ", variant.Traits.Select(t => t.TraitDescription));
        double cap = Number(CapPattern, trait);
        double amount = Number(AmountPattern, trait, 1);
        if (amount <= 0) amount = 1;
        bool once = trait.Contains("<获得时>");
        var skill = c.IsElite ? p.EliteSkill ?? p.NormalSkill : p.NormalSkill;
        // "持续时间无限": cast once and it stays on for the rest of the fight.
        int casts = skill is null || skill.SpType == "被动" || skill.SpCost == 0 ? 0
            : skill.Description.Contains("持续时间无限") ? 1
            : skill.SpCost <= 8 ? 7 : skill.SpCost <= 20 ? 4 : skill.SpCost <= 40 ? 2 : 1;
        double selfFront = SelfFrontOverrides.TryGetValue(c.Name, out var sf) ? sf
            : SelfFrontPattern.IsMatch(trait) ? Number(SelfFrontAmount, trait) : 0;
        var offsets = Faces.Select(f => range.Offsets(f).ToArray()).ToArray();
        var info = new OpInfo
        {
            Name = c.Name,
            Role = PlacementRole.Damage,
            Power = PowerOf(p, op.Tier, c.IsElite),
            Atk = p.StatsFor(c.IsElite)?.Atk ?? 500,
            Hp = p.StatsFor(c.IsElite)?.Hp ?? (p.Profession == "重装" ? 3000 : 1800),
            Def = p.StatsFor(c.IsElite)?.Def ?? (p.Profession == "重装" ? 450 : 200),
            Res = p.StatsFor(c.IsElite)?.Res ?? 0,
            Ehp = p.StatsFor(c.IsElite)?.EffectiveHp(EnemyAtk) ?? (p.Profession == "重装" ? 7000 : p.IsMelee ? 3000 : 1600),
            EffectiveDps = p.StatsFor(c.IsElite) is { Dps: > 0 } es ? es.Dps * Math.Min(es.SkillFactor, MaxSkillFactor) : null,
            SkillFactor = Math.Min(p.StatsFor(c.IsElite)?.SkillFactor ?? 1, MaxSkillFactor),
            AttackInterval = p.StatsFor(c.IsElite) is { } si ? si.Interval / Math.Max(0.1, si.AttackSpeed / 100) : 1.2,
            // Casters and supporters hit with arts, as do 术战者 guards; everyone else deals physical damage.
            Arts = p.Profession is "术师" or "辅助" || p.SubProfession is "术战者" or "驭法铁卫",
            Melee = p.IsMelee,
            Block = p.StatsFor(c.IsElite)?.Block ?? p.BlockCount, // elite pieces can block more (e.g. 艾丝黛尔 2 → 3)
            Offsets = offsets,
            Covenants = [.. op.CoreCovenants, .. op.AdditionalCovenants, .. c.Transforms.Where(t => !op.CoreCovenants.Contains(t) && !op.AdditionalCovenants.Contains(t))],
            Tier = op.Tier,
            Healer = p.IsHealer || p.Profession == "医疗",
            Medic = p.Profession == "医疗",
            Trait = trait,
            Cap = cap,
            StacksLayers = !once && Regex.IsMatch(trait, @"层数\+\d|叠加"),
            OnceAmount = once ? Number(OncePattern, trait) : 0,
            TriggersPerRound = cap > 0 ? cap / amount : trait.Contains("<战斗中>") ? 4 : 1,
            SkillCasts = casts,
            SelfFront = selfFront,
            SelfBack = SelfBack.GetValueOrDefault(c.Name),
            KillRange = trait.Contains("攻击范围内每有一名敌人") && trait.Contains("层数"),
            Around4 = trait.Contains("周围四格"),
            BenchOk = trait.Contains("整备区也生效"),
            SkillBlock = c.Name != "帕拉斯" && skill is not null && SkillBlockPattern.IsMatch(skill.Description),
            AntiAir = !p.IsMelee || p.SubProfession == "领主",
            StackKind = p.Stack?.Kind ?? "",
            StackCovenants = [.. p.Stack?.Covenants ?? []],
            StackPerRound = p.Stack?.PerRound ?? 0,
            StackAmount = p.Stack?.Amount ?? 0,
        };
        info.Transforms = [.. c.Transforms];
        ApplyPotentialAndTalents(info, p, p.StatsFor(c.IsElite), potential);
        info.Skill = SkillCycle(skill, info.SkillFactor, info.AttackInterval, p.StatsFor(c.IsElite));
        if (info.Skill is { } cycle && info.Talent is { InitSp: > 0 } ti)
            info.Skill = cycle with { Init = Math.Min(cycle.Cost, cycle.Init + ti.InitSp) };
        info.Pattern = PatternOf(p.SubProfession);
        if (p.RangeFor(c.IsElite, duringSkill: true) is { } sr && sr != range)
            info.SkillOffsets = [.. Faces.Select(f => sr.Offsets(f).ToArray())];
        // "对前方一格…锤击" (怒潮凛冬): the skill strikes only the cell in front, so where it faces decides what it hits.
        else if (skill is not null && skill.Description.Contains("前方一格") && !skill.Description.Contains("身前一格"))
            info.SkillOffsets = [.. Faces.Select(f => FrontCell.Offsets(f).ToArray())];
        if (skill is not null && RevealPattern.Match(skill.Description) is { Success: true } reveal)
            info.SkillRevealSeconds = double.Parse(reveal.Groups[1].Value);
        info.Role = RoleOf(info, p, op.Tier);
        return info;
    }

    /// <summary>
    /// Damage power: effective DPS (panel attack ÷ interval × speed, times the carried skill's average gain) over
    /// <see cref="ReferenceDps"/>, with a small per-tier allowance for what the stats do not cover yet.
    /// Without stats: the old tier × profession estimate.
    /// </summary>
    /// <summary>
    /// The piece's potential bonuses and its talents' own stats on top of its data (attack, attack speed, HP,
    /// defence, resistance, block); the power and toughness estimates follow. Attack riders and auras are kept on
    /// <see cref="OpInfo.Talent"/> for the fight.
    /// </summary>
    private static void ApplyPotentialAndTalents(OpInfo info, OperatorProfile p, ChessStats? st, int potential)
    {
        var tal = st?.TalentAt(potential);
        info.Talent = tal;
        if (st?.Summon is { RangeShape: { } shape } summon)
        {
            info.Summon = summon;
            info.SummonOffsets = [.. Faces.Select(f => shape.Offsets(f).ToArray())];
        }
        if (st is null) return;
        if (tal?.Arts == true) info.Arts = true;
        double baseAtk = Math.Max(1, info.Atk), aspd = Math.Max(1, st.AttackSpeed);
        double aspdUp = p.PotentialBonus("aspd", potential) + (tal?.Aspd ?? 0);
        info.Atk = (info.Atk + p.PotentialBonus("atk", potential) + (tal?.AtkFlat ?? 0)) * (1 + (tal?.Atk ?? 0));
        info.AttackInterval = st.Interval / Math.Max(0.1, (aspd + aspdUp) / 100);
        info.Hp = (info.Hp + p.PotentialBonus("hp", potential)) * (1 + (tal?.Hp ?? 0));
        info.Def = Math.Max(0, (info.Def + p.PotentialBonus("def", potential) + (tal?.DefFlat ?? 0)) * (1 + (tal?.Def ?? 0)));
        info.Res += p.PotentialBonus("res", potential) + (tal?.Res ?? 0);
        info.Block += (int)Math.Floor((tal?.Block ?? 0) + 0.01);
        double up = info.Atk / baseAtk * (aspd + aspdUp) / aspd * (1 + (tal?.Crit ?? 0)) * (1 + (tal?.Dmg ?? 0));
        info.Power *= up;
        if (info.EffectiveDps is double d) info.EffectiveDps = d * up;
        double taken = Math.Max(0.2, 1 - (tal?.Sanctuary ?? 0)) * (1 - 0.6 * (tal?.Dodge ?? 0));
        info.Ehp = info.Hp * EnemyAtk / Math.Max(EnemyAtk - info.Def, 0.05 * EnemyAtk) / taken;
    }

    public static double PowerOf(OperatorProfile p, int tier, bool elite)
    {
        if (p.StatsFor(elite) is not { Dps: > 0 } s)
            return TierPower.GetValueOrDefault(tier, 1) * ProfessionDps.GetValueOrDefault(p.Profession, 0.6) * (elite ? 1.15 : 1);
        double power = s.Dps * Math.Min(s.SkillFactor, MaxSkillFactor) / ReferenceDps * (1 + UnmodelledPerTier * (tier - 1));
        return p.Profession == "医疗" ? power * HealerDamageShare : power;
    }

    private static PlacementRole RoleOf(OpInfo i, OperatorProfile p, int tier)
    {
        if (p.Profession == "重装") return PlacementRole.Tank;
        if (p.Profession == "医疗" || p.IsHealer) return PlacementRole.Support;
        bool positional = i.SelfFront > 0 || i.SelfBack > 0 || PositionalStackers.Contains(i.Name);
        bool stacker = positional || p.Stack is { } s && s.Kind != "once" && !string.IsNullOrEmpty(s.Kind) && s.PerRound >= 3;
        if (stacker && (tier <= 3 || p.Profession is "辅助" or "先锋") && i.Name != "凛御银灰") return PlacementRole.Stacker;
        if (p.Profession == "辅助") return PlacementRole.Support;
        return PlacementRole.Damage;
    }

    // ── Board ──────────────────────────────────────────────────────────────

    private sealed class BoardInfo
    {
        /// <summary>
        /// Rough total of the ground dwell weights a whole plan spreads over the cells (see LayRoute); flying
        /// enemies get a share of it in proportion to their count. Flyers are not blocked and pass through, so
        /// they count for half as much per enemy.
        /// </summary>
        private const double GroundDwellTotal = 16, FlyPerGround = 0.5;

        public BoardInfo(GarrisonMap map, RoundRoutes? round, bool flying)
        {
            Map = map;
            Deployable = map.AllCells().Where(c => c.Type is BoardCellType.Ground or BoardCellType.Special or BoardCellType.High)
                .Select(c => c.Cell).ToArray();
            Special = map.AllCells().Where(c => c.Type == BoardCellType.Special).Select(c => c.Cell).ToHashSet();
            Tile = Tiles.GetValueOrDefault(map.Id);
            TeleportHazard = map.Id == "palace";
            var lanes = round is null ? [] : GroundLanes(map, round);
            if (lanes.Count > 0)
            {
                Round = round;
                Routes = [.. lanes.Select(l => l.Cells)];
                double sum = lanes.Sum(l => l.Weight);
                Weights = [.. lanes.Select(l => 3.0 * l.Weight / sum)]; // same total as the fixed 1 + 2
                Labels = LaneLabels(lanes.Select(l => l.Cells).ToList());
                WalkCount = round!.Walking;
                if (flying && round.Flying > 0)
                {
                    FlyCount = round.Flying;
                    Fly = FlyDwell(round, GroundDwellTotal * FlyPerGround * Math.Min(2.0, (double)round.Flying / Math.Max(1, round.Walking)));
                }
            }
            else
            {
                Routes = map.Routes.Select(r => r.ToArray()).ToArray();
                Weights = [.. Routes.Select((_, k) => k < StaticRouteWeight.Length ? StaticRouteWeight[k] : 1)];
                Labels = Routes.Length == 2 ? ["上路", "下路"] : [.. Routes.Select((_, k) => $"路线 {k + 1} ")];
                if (map.Id == "palace")
                    foreach (var r in Routes)
                    {
                        int k = Array.IndexOf(r, PalaceTeleport.To);
                        if (k >= 0) { Extra = [r[k..]]; break; }
                    }
            }
            RouteIndex = Index(Routes);
            ExtraIndex = Index(Extra);
            FlyTotal = Fly.Values.Sum();
            FlyEnd = Fly.Count == 0 ? [] : FlyEndCells(round!);
        }

        public GarrisonMap Map { get; }
        /// <summary>The round the lanes come from; null when the map's fixed routes are used.</summary>
        public RoundRoutes? Round { get; }
        public BoardCell[][] Routes { get; }
        public double[] Weights { get; }
        public string[] Labels { get; }
        public Dictionary<BoardCell, int>[] RouteIndex { get; }
        public BoardCell[][] Extra { get; } = [];
        public Dictionary<BoardCell, int>[] ExtraIndex { get; }
        public BoardCell[] Deployable { get; }
        public HashSet<BoardCell> Special { get; }
        public TileEffect? Tile { get; }
        public bool TeleportHazard { get; }
        public int WalkCount { get; }
        public int FlyCount { get; }
        /// <summary>Flying enemies' weighted presence per cell (empty when the round has none or is unknown).</summary>
        public Dictionary<BoardCell, double> Fly { get; } = [];
        public double FlyTotal { get; }
        /// <summary>Cells on the flyers' last stretch before the defence point: whatever is not shot down passes here.</summary>
        public HashSet<BoardCell> FlyEnd { get; }

        public bool Legal(OpInfo op, BoardCell cell) => GarrisonMap.CanDeploy(Map.At(cell), op.Melee);
        public bool OnMainRoute(BoardCell c) => RouteIndex.Any(d => d.ContainsKey(c));

        private static Dictionary<BoardCell, int>[] Index(BoardCell[][] routes) => [.. routes.Select(r =>
        {
            var d = new Dictionary<BoardCell, int>();
            for (int j = 0; j < r.Length; j++) d.TryAdd(r[j], j);
            return d;
        })];

        /// <summary>
        /// Every walking segment as an ordered cell list (spawn → defence), weighted by the enemies on it; a boss
        /// counts as four. Segments that cover the same cells are one lane.
        /// </summary>
        private static List<(BoardCell[] Cells, double Weight)> GroundLanes(GarrisonMap map, RoundRoutes round)
        {
            var lanes = new List<(BoardCell[] Cells, double Weight)>();
            foreach (var r in round.Routes.Where(r => !r.Flying && r.Count > 0))
                foreach (var seg in r.Segments)
                {
                    var cells = Rasterize(seg).Where(c => c.Row >= 0 && c.Row < map.RowCount
                        && c.Col >= map.FirstColumn && c.Col < map.FirstColumn + map.ColumnCount).ToArray();
                    if (cells.Length < 2) continue;
                    double w = (double)r.Count / r.Segments.Count * (r.Boss ? 4 : 1);
                    int same = lanes.FindIndex(l => l.Cells.SequenceEqual(cells));
                    if (same >= 0) lanes[same] = (lanes[same].Cells, lanes[same].Weight + w);
                    else lanes.Add((cells, w));
                }
            return lanes;
        }

        /// <summary>Cells a polyline of cell-centre points passes through, in order.</summary>
        internal static List<BoardCell> Rasterize(IReadOnlyList<RoutePoint> points)
        {
            var cells = new List<BoardCell>();
            void Add(double col, double row)
            {
                var c = new BoardCell((int)Math.Floor(col), (int)Math.Floor(row));
                if (cells.Count == 0 || cells[^1] != c) cells.Add(c);
            }
            if (points.Count > 0) Add(points[0].Col, points[0].Row);
            for (int i = 1; i < points.Count; i++)
            {
                var (a, b) = (points[i - 1], points[i]);
                int steps = Math.Max(1, (int)Math.Ceiling(Math.Max(Math.Abs(b.Col - a.Col), Math.Abs(b.Row - a.Row)) * 4));
                for (int k = 1; k <= steps; k++)
                {
                    double t = (double)k / steps;
                    // Nudge off exact cell borders (diagonal steps hit corners) towards where the path is heading.
                    Add(a.Col + (b.Col - a.Col) * t - 1e-6 * Math.Sign(b.Col - a.Col),
                        a.Row + (b.Row - a.Row) * t - 1e-6 * Math.Sign(b.Row - a.Row));
                }
            }
            return cells;
        }

        /// <summary>Path length of every flying route in each cell × its enemies, scaled so the cells sum to <paramref name="total"/>.</summary>
        private static Dictionary<BoardCell, double> FlyDwell(RoundRoutes round, double total)
        {
            var raw = new Dictionary<BoardCell, double>();
            foreach (var r in round.Routes.Where(r => r.Flying && r.Count > 0))
                foreach (var seg in r.Segments)
                {
                    double per = (double)r.Count / r.Segments.Count;
                    for (int i = 1; i < seg.Count; i++)
                    {
                        var (a, b) = (seg[i - 1], seg[i]);
                        double len = Math.Sqrt((b.Col - a.Col) * (b.Col - a.Col) + (b.Row - a.Row) * (b.Row - a.Row));
                        int steps = Math.Max(1, (int)Math.Ceiling(len * 8));
                        for (int k = 0; k < steps; k++)
                        {
                            double t = (k + 0.5) / steps;
                            var c = new BoardCell((int)Math.Floor(a.Col + (b.Col - a.Col) * t), (int)Math.Floor(a.Row + (b.Row - a.Row) * t));
                            raw[c] = raw.GetValueOrDefault(c) + per * len / steps;
                        }
                    }
                }
            double sum = raw.Values.Sum();
            return sum <= 0 ? [] : raw.ToDictionary(kv => kv.Key, kv => total * kv.Value / sum);
        }

        /// <summary>The last two cells before the defence point on every flying path.</summary>
        private static HashSet<BoardCell> FlyEndCells(RoundRoutes round)
        {
            var end = new HashSet<BoardCell>();
            foreach (var r in round.Routes.Where(r => r.Flying))
                foreach (var seg in r.Segments)
                    foreach (var c in Rasterize(seg).Where(c => c.Col >= 0).TakeLast(2)) end.Add(c);
            return end;
        }

        /// <summary>上路 / 下路 by the row the lane starts on; lanes starting mid-board (after a teleport) say so.</summary>
        private static string[] LaneLabels(List<BoardCell[]> lanes)
        {
            var names = lanes.Select(l => l[0].Col < 5 ? "传送后" : l[0].Row < 2 ? "上路" : "下路").ToArray();
            var seen = new Dictionary<string, int>();
            var result = new string[names.Length];
            for (int k = 0; k < names.Length; k++)
            {
                if (names.Count(n => n == names[k]) == 1) { result[k] = names[k]; continue; }
                seen[names[k]] = seen.GetValueOrDefault(names[k]) + 1;
                result[k] = names[k] + seen[names[k]];
            }
            return result;
        }
    }

    // ── One planning job ───────────────────────────────────────────────────

    private sealed class CovenantContext
    {
        public required HashSet<string> Active;
        public required Dictionary<string, double> LayerValue;
        public required Dictionary<string, int> Counts;
        public double Lv(IEnumerable<string> covs) => covs.Sum(c => LayerValue.GetValueOrDefault(c));
        /// <summary>Per operator: attack (power) and toughness multipliers from the covenants' current layers.</summary>
        public Dictionary<string, (double Atk, double Hp)>? LayerBuffs;
    }

    private sealed class Job
    {
        private readonly PlacementPlanner _owner;
        private readonly PlacementOptions _options;
        private readonly List<string> _pool;
        private readonly List<string> _unplaceable;
        private readonly Dictionary<string, CovenantContext> _covCache = [];

        public Job(PlacementPlanner owner, GarrisonMap map, IReadOnlyList<PlacementCandidate> pool, PlacementOptions options)
        {
            _owner = owner;
            _options = options;
            // Real streams say whether flyers can come: only when one of the run's types flies.
            Board = new BoardInfo(map, options.Routes, options.Streams is { Count: > 0 } st ? st.Any(s => s.Flying) : options.ExpectFlying);
            _streams = PickStreams(options);
            Info = [];
            _unplaceable = [];
            foreach (var c in pool.DistinctBy(c => c.Key))
            {
                var info = owner.BuildInfo(c, options.Potential);
                if (info is null) { if (!_unplaceable.Contains(c.Name)) _unplaceable.Add(c.Name); }
                else Info[c.Key] = info;
            }
            _pool = [.. Info.Keys];
        }

        public BoardInfo Board { get; }
        public Dictionary<string, OpInfo> Info { get; }

        // ── Simulation ─────────────────────────────────────────────────────

        private WaveScenario? _scenario;
        private List<SimLane>? _lanes;
        private readonly List<(RoundStream Stream, double Weight)>? _streams;
        private List<(RoundStream Stream, double Weight, WaveScenario Scenario)>? _realRuns, _toughRuns;
        private double _streamFactor = 1;
        private bool _calibrated;

        /// <summary>The likeliest groups of each type, reweighted so each type keeps its share.</summary>
        private static List<(RoundStream, double)>? PickStreams(PlacementOptions o)
        {
            if (o.Streams is not { Count: > 0 } all) return null;
            int types = all.Select(s => s.Type).Distinct().Count();
            int per = Math.Max(1, (int)Math.Ceiling((double)Math.Max(1, o.MaxStreams) / types));
            var list = new List<(RoundStream, double)>();
            foreach (var g in all.Where(s => s.Spawns.Count > 0).GroupBy(s => s.Type))
            {
                double total = g.Sum(s => s.Weight);
                var keep = g.OrderByDescending(s => s.Weight).Take(per).ToList();
                double kept = keep.Sum(s => s.Weight);
                foreach (var s in keep) list.Add((s, kept > 0 ? s.Weight * total / kept : 1.0 / keep.Count));
            }
            double sum = list.Sum(x => x.Item2);
            return list.Count == 0 ? null : [.. list.Select(x => (x.Item1, sum > 0 ? x.Item2 / sum : 1.0 / list.Count))];
        }

        private List<(RoundStream Stream, double Weight, WaveScenario Scenario)> StreamRuns(double factor) =>
            [.. _streams!.Select(s => (s.Stream, s.Weight, new WaveScenario { Spawns = s.Stream.Spawns }.Tougher(factor)))];

        private void SetStreamFactor(double factor)
        {
            _streamFactor = factor;
            _realRuns ??= StreamRuns(1);
            _toughRuns = Math.Abs(factor - 1) > 0.001 ? StreamRuns(factor) : null;
        }

        /// <summary>
        /// Expected enemies handled over the streams: at the real strength, averaged with a rescaled copy when the real
        /// round cannot tell placements apart: a tougher copy when it is too easy (everyone clears it; the copy rewards
        /// the sturdier line), an easier one when it is hopeless (almost nothing dies; the copy rewards the line that
        /// would hold once the pieces are stronger).
        /// </summary>
        private double ExpectedKills(Dictionary<string, (BoardCell Cell, Facing Facing)> pl) => ExpectedFight(pl).Kills;

        /// <summary>Share of a lane's enemies counted lost when a fragile ranged operator is the first to block it.</summary>
        private const double SquishyBlockLoss = 0.15;

        /// <summary>
        /// The lanes whose first blocker (the first operator on a ground cell along the path that can block) is a
        /// ranged operator that is not a tank: the lane's enemies walk straight into a carry. Returns the lanes'
        /// label and blocker, and their summed enemy share.
        /// </summary>
        private List<(int Lane, bool First, string Label, string Blocker)> SquishyBlocks(Dictionary<string, (BoardCell Cell, Facing Facing)> pl)
        {
            var at = new Dictionary<BoardCell, string>();
            foreach (var (n, v) in pl) at[v.Cell] = n;
            var lanes = new List<(int, bool, string, string)>();
            for (int k = 0; k < Board.Routes.Length; k++)
            {
                // The first blocker takes the lane in full; a fragile one further on still gets what breaks through.
                bool first = true;
                foreach (var cell in Board.Routes[k])
                {
                    if (!at.TryGetValue(cell, out var n) || Info[n].Block <= 0
                        || Board.Map.At(cell) is not (BoardCellType.Ground or BoardCellType.Special)) continue;
                    if (!Info[n].Melee && Info[n].Role != PlacementRole.Tank)
                    {
                        lanes.Add((k, first, Board.Labels[k], Info[n].Name));
                    }
                    first = false;
                }
            }
            return lanes;
        }

        /// <summary>Enemies a fragile ranged blocker costs at least, however light its lane is this round.</summary>
        private const double SquishyMinLoss = 2;
        /// <summary>Kills an enemy's worth of damage counts for in the score (a tie-breaker).</summary>
        private const double DamageWeight = 0.1;
        /// <summary>
        /// Share of the hand-written structure score (block points held by tanks, healers reaching them, damage
        /// covering the line…) kept alongside the fight: when placements handle about as many, the sound one wins.
        /// </summary>
        private const double CombatPrior = 0.1;

        /// <summary>
        /// Enemies counted lost to fragile ranged blockers: the first blocker of a lane loses
        /// <see cref="SquishyBlockLoss"/> of that lane's enemies (at least <see cref="SquishyMinLoss"/>), one further on half.
        /// </summary>
        private double SquishyLoss(Dictionary<string, (BoardCell Cell, Facing Facing)> pl, double spawned) =>
            SquishyBlocks(pl).Where(b => b.First).Sum(b => Math.Max(SquishyMinLoss, SquishyBlockLoss * Board.Weights[b.Lane] * spawned));

        /// <summary>Enemies handled (real and rescaled streams averaged) and, from the real ones, what happened per operator.</summary>
        private (double Kills, SimEvents Events) ExpectedFight(Dictionary<string, (BoardCell Cell, Facing Facing)> pl)
        {
            // Real and rescaled fights in one batch, so they all run at once.
            var all = SimulateStreams(pl, _toughRuns is null ? _realRuns! : [.. _realRuns!, .. _toughRuns]);
            var real = all.Take(_realRuns!.Count).ToList();
            double kills = Expected(real, r => r.Handled);
            if (_toughRuns is not null) kills = (kills + Expected(all.Skip(_realRuns.Count), r => r.Handled)) / 2;
            return (kills, EventsOf(real));
        }

        /// <summary>
        /// Per operator, weighted over the streams: skills cast, stuns around it, enemies fallen in its range; and the
        /// damage everyone dealt, in enemies' worth of HP.
        /// </summary>
        private sealed record SimEvents(Dictionary<string, double> Casts, Dictionary<string, double> Stuns, Dictionary<string, double> Kills)
        {
            public double DamageInEnemies { get; set; }
        }

        private static SimEvents EventsOf(IEnumerable<(RoundStream Stream, double Weight, SimResult Result)> runs)
        {
            var ev = new SimEvents([], [], []);
            static void Add(Dictionary<string, double> to, IReadOnlyDictionary<string, int> from, double w)
            {
                foreach (var (k, c) in from) to[k] = to.GetValueOrDefault(k) + w * c;
            }
            foreach (var (stream, w, r) in runs)
            {
                // The others' HP: a boss's huge HP would make every other hit look worthless (the boss counts
                // through Handled instead).
                var others = stream.Spawns.Where(s => s.Profile.Rank != "BOSS").ToList();
                double meanHp = others.Count > 0 ? others.Average(s => s.Profile.Hp)
                    : stream.Spawns.Count > 0 ? stream.Spawns.Average(s => s.Profile.Hp) : 1;
                ev.DamageInEnemies += w * r.Damage.Values.Sum() / Math.Max(1, meanHp);
                Add(ev.Casts, r.Casts, w);
                Add(ev.Stuns, r.StunsAround, w);
                Add(ev.Kills, r.KillsInRange, w);
            }
            return ev;
        }

        /// <summary>
        /// Skills cast per round as the simulation played them (null: no simulation yet). Stacking that triggers on
        /// casting a skill counts these instead of a cast rate fixed by the SP cost, so where the operator stands
        /// (and whether anything walks into its range) decides how much it stacks.
        /// </summary>
        private SimEvents? _events;
        private double? _spawnedReal;   // enemies in the real streams (weighted), for the blocker check

        /// <summary>Each stream against the placement, with its weight.</summary>
        private List<(RoundStream Stream, double Weight, SimResult Result)> SimulateStreams(
            Dictionary<string, (BoardCell Cell, Facing Facing)> boardPl,
            List<(RoundStream Stream, double Weight, WaveScenario Scenario)> runs)
        {
            // Each fight is independent (the simulation only reads its inputs): run them on all cores.
            var units = SimUnits(boardPl);
            var lanes = SimLanes();
            var terrain = Board.Map.Terrain;
            var results = new SimResult[runs.Count];
            if (runs.Count == 1) results[0] = ClearSimulator.Run(units, lanes, runs[0].Scenario, terrain);
            else Parallel.For(0, runs.Count, i => results[i] = ClearSimulator.Run(units, lanes, runs[i].Scenario, terrain));
            return [.. runs.Select((r, i) => (r.Stream, r.Weight, results[i]))];
        }

        private static double Expected(IEnumerable<(RoundStream Stream, double Weight, SimResult Result)> results,
            Func<SimResult, double> of) => results.Sum(r => r.Weight * of(r.Result));

        /// <summary>Enemy paths for the simulation: this round's walking lanes (or the fixed routes) and flying paths.</summary>
        private List<SimLane> SimLanes()
        {
            if (_lanes is not null) return _lanes;
            var lanes = new List<SimLane>();
            for (int k = 0; k < Board.Routes.Length; k++)
                lanes.Add(new SimLane([.. Board.Routes[k].Select(c => (c.Col + 0.5, c.Row + 0.5))], Board.Weights[k], false));
            if (Board.FlyTotal > 0 && Board.Round is { } round)
                foreach (var r in round.Routes.Where(r => r.Flying && r.Count > 0))
                    foreach (var seg in r.Segments)
                        lanes.Add(new SimLane([.. seg.Select(pt => (pt.Col, pt.Row))], (double)r.Count / r.Segments.Count, true));
            return _lanes = lanes;
        }

        private WaveScenario BaseScenario() => _options.Scenario ?? new WaveScenario
        {
            GroundCount = Math.Max(12, Board.Round?.Walking ?? 24),
            FlyCount = Board.FlyTotal > 0 ? Board.FlyCount : 0,
        };

        /// <summary>
        /// Makes the stream moderately hard for this pool: enemy HP is scaled (bisection) until
        /// <paramref name="reference"/> handles about <see cref="PlacementOptions.TargetKillRate"/> of the enemies.
        /// </summary>
        public void Calibrate(Dictionary<string, (BoardCell Cell, Facing Facing)> reference)
        {
            _calibrated = true;
            if (_streams is not null)
            {
                if (_options.StreamFactor is double fixedFactor) { SetStreamFactor(fixedFactor); return; }
                // The real round alone when it is moderately hard already; otherwise a copy scaled to the target.
                double Rate(double f)
                {
                    var res = SimulateStreams(reference, StreamRuns(f));
                    return Expected(res, r => r.Handled) / Math.Max(1e-9, Expected(res, r => r.Worth));
                }
                double atReal = Rate(1);
                if (atReal <= _options.TargetKillRate && atReal >= _options.TargetKillRate - HopelessBelow)
                {
                    SetStreamFactor(1);
                    return;
                }
                double a = atReal > _options.TargetKillRate ? 1 : 0.05, b = atReal > _options.TargetKillRate ? 20 : 1;
                for (int i = 0; i < 10; i++)
                {
                    double mid = Math.Sqrt(a * b);
                    if (Rate(mid) > _options.TargetKillRate) a = mid; else b = mid;
                }
                SetStreamFactor(Math.Sqrt(a * b));
                return;
            }
            var baseline = BaseScenario();
            if (_options.Scenario is not null) { _scenario = baseline; return; }
            double lo = 0.05, hi = 20;
            for (int i = 0; i < 14; i++)
            {
                double mid = Math.Sqrt(lo * hi);
                _scenario = baseline.Tougher(mid);
                if (Simulate(reference).KillRate > _options.TargetKillRate) lo = mid; else hi = mid;
            }
            _scenario = baseline.Tougher(Math.Sqrt(lo * hi));
        }

        /// <summary>Runs the stream against <paramref name="boardPl"/> (after devouring; with covenant buffs).</summary>
        public SimResult Simulate(Dictionary<string, (BoardCell Cell, Facing Facing)> boardPl) =>
            ClearSimulator.Run(SimUnits(boardPl), SimLanes(), _scenario ?? BaseScenario(), Board.Map.Terrain);

        /// <summary>The placement as simulated operators (after devouring; with covenant buffs).</summary>
        private List<SimUnit> SimUnits(Dictionary<string, (BoardCell Cell, Facing Facing)> boardPl)
        {
            var cov = Covenants(boardPl.Keys);
            var atBoard = new Dictionary<BoardCell, string>();
            foreach (var (n, v) in boardPl) atBoard[v.Cell] = n;
            var (pl, info, _, _) = Devour(boardPl, atBoard, cov, (_, _) => { });
            info = WithCurrentLayers(info, boardPl.Keys, cov);
            var units = new List<SimUnit>();
            var auras = AurasOn(pl, info);
            var taken = new HashSet<BoardCell>(pl.Values.Select(v => v.Cell));
            foreach (var (n, v) in pl)
            {
                var I = info[n];
                var (atk, hp, def) = BaseCovenantBuffs(n, v.Cell, pl, cov);
                var aura = auras.GetValueOrDefault(n) ?? new AuraSum();
                var tal = I.Talent;
                atk *= (1 + aura.Atk) * (1 + (tal?.Crit ?? 0));
                hp *= 1 + aura.Hp;
                double powerUp = Info[n].Power > 0 ? I.Power / Info[n].Power : 1;
                double toughUp = Info[n].Ehp > 0 ? I.Ehp / Info[n].Ehp : 1;
                var range = I.Offsets[(int)v.Facing].Select(o => new BoardCell(v.Cell.Col + o.Col, v.Cell.Row + o.Row)).ToList();
                units.Add(new SimUnit(n, v.Cell, range,
                    CanBlock: Board.Map.At(v.Cell) is BoardCellType.Ground or BoardCellType.Special, I.Block, I.AntiAir,
                    Atk: I.Atk * I.SkillFactor * powerUp * atk, Interval: I.AttackInterval * 100 / (100 + aura.Aspd), Arts: I.Arts,
                    // In the fight only medics heal instead of attacking: 守护者 attack (their skill heals),
                    // 护佑者 attack and heal while their skill runs (their pattern), 吟游者 heal through an aura.
                    Hp: I.Hp * toughUp * hp, Def: (I.Def * def + aura.DefFlat) * (1 + aura.Def), Healer: I.Medic)
                {
                    Res = I.Res + aura.Res, SkillFactor = I.SkillFactor, Skill = I.Skill, SkillRevealSeconds = I.SkillRevealSeconds,
                    DefIgnore = tal?.DefIgnore ?? 0, DefIgnoreRatio = Math.Min(0.9, tal?.DefIgnoreRatio ?? 0),
                    ResIgnore = tal?.ResIgnore ?? 0, DamageScale = 1 + (tal?.Dmg ?? 0), ExtraArts = tal?.ExtraArts ?? 0,
                    DotDps = (tal?.DotDps ?? 0) + (tal?.DotScale ?? 0) * I.Atk * powerUp * atk, DotSeconds = tal?.DotSeconds ?? 3,
                    HitStuns = tal?.Control?.Kind == "stun", HitControlSeconds = tal?.Control?.Seconds ?? 0,
                    HitControlChance = tal?.Control?.Chance ?? 0,
                    Fragile = tal?.Fragile ?? 0, EnemySlow = tal?.Slow ?? 0,
                    Sanctuary = Math.Min(0.8, 1 - (1 - (tal?.Sanctuary ?? 0)) * (1 - aura.Sanctuary)),
                    Dodge = Math.Min(0.8, 1 - (1 - (tal?.Dodge ?? 0)) * (1 - aura.Dodge)),
                    Regen = ((tal?.Regen ?? 0) + aura.Regen) * I.Hp * toughUp * hp + (tal?.RegenFlat ?? 0) + aura.RegenFlat,
                    SpPerSecond = (tal?.Sp ?? 0) + aura.Sp,
                    Pattern = I.Pattern,
                    SkillRange = I.SkillOffsets is { } so
                        ? [.. so[(int)v.Facing].Select(o => new BoardCell(v.Cell.Col + o.Col, v.Cell.Row + o.Row))] : null,
                    // 奥术 with 3 different 奥术 operators: enemies in a 奥术 operator's range lose invisibility.
                    Reveals = cov.Active.Contains("奥术") && cov.Counts.GetValueOrDefault("奥术") >= 3 && Info[n].Covenants.Contains("奥术"),
                });
                if (SummonUnit(n, I, v.Cell, v.Facing, taken) is { } summon) units.Add(summon);
            }
            return units;
        }

        /// <summary>What other operators' talent auras give one operator (summed).</summary>
        private sealed class AuraSum
        {
            public double Atk, Aspd, Def, DefFlat, Hp, Res, Sanctuary, Dodge, Regen, RegenFlat, Sp;

            public void Add(TalentAura a)
            {
                Atk += a.Atk; Aspd += a.Aspd; Def += a.Def; DefFlat += a.DefFlat; Hp += a.Hp; Res += a.Res;
                Sanctuary = 1 - (1 - Sanctuary) * (1 - a.Sanctuary);
                Dodge = 1 - (1 - Dodge) * (1 - a.Dodge);
                Regen += a.Regen; RegenFlat += a.RegenFlat; Sp += a.Sp;
            }
        }

        private static (int Col, int Row) Step(Facing f) => f switch
        {
            Facing.Right => (1, 0), Facing.Left => (-1, 0), Facing.Down => (0, 1), _ => (0, -1),
        };

        /// <summary>
        /// Talent auras on the field: who gets what (全场 / 攻击范围内 / 周围八格 / 身后 / 身前 / 某盟约或势力的干员).
        /// The same aura from two copies of one operator counts once.
        /// </summary>
        private static Dictionary<string, AuraSum> AurasOn(Dictionary<string, (BoardCell Cell, Facing Facing)> pl,
            Dictionary<string, OpInfo> info)
        {
            var sums = new Dictionary<string, AuraSum>();
            var seen = new HashSet<string>();
            foreach (var (src, sv) in pl)
            {
                if (info[src].Talent is not { Auras.Count: > 0 } tal || !seen.Add(info[src].Name)) continue;
                var range = info[src].Offsets[(int)sv.Facing].Select(o => new BoardCell(sv.Cell.Col + o.Col, sv.Cell.Row + o.Row)).ToHashSet();
                var (dc, dr) = Step(sv.Facing);
                foreach (var a in tal.Auras)
                    foreach (var (dst, dv) in pl)
                    {
                        bool self = dst == src;
                        bool hit = a.Scope switch
                        {
                            "all" => true,
                            "members" => a.Members?.Contains(info[dst].Name) == true,
                            "range" => range.Contains(dv.Cell),
                            "around8" => self ? a.Self : Math.Max(Math.Abs(dv.Cell.Col - sv.Cell.Col), Math.Abs(dv.Cell.Row - sv.Cell.Row)) <= 1,
                            "behind" => self ? a.Self : dv.Cell == new BoardCell(sv.Cell.Col - dc, sv.Cell.Row - dr),
                            "front" => self ? a.Self : dv.Cell == new BoardCell(sv.Cell.Col + dc, sv.Cell.Row + dr),
                            _ => false,
                        };
                        if (!hit) continue;
                        if (!sums.TryGetValue(dst, out var sum)) sums[dst] = sum = new AuraSum();
                        sum.Add(a);
                    }
            }
            return sums;
        }

        /// <summary>
        /// The operator's summon as a fighter of its own: blockers and melee summons on the enemies' path inside its
        /// operator's range (the free cell nearest the operator), devices on their operator's cell with its aim.
        /// </summary>
        private SimUnit? SummonUnit(string n, OpInfo I, BoardCell cell, Facing facing, HashSet<BoardCell> taken)
        {
            if (I.Summon is not { } s || I.SummonOffsets is not { } offsets) return null;
            var at = cell;
            if (s.Lane)
            {
                var reach = I.Offsets[(int)facing].Select(o => new BoardCell(cell.Col + o.Col, cell.Row + o.Row)).ToHashSet();
                var spot = Board.Routes.SelectMany(r => r).Distinct()
                    .Where(c => reach.Contains(c) && !taken.Contains(c) && Board.Map.At(c) is BoardCellType.Ground or BoardCellType.Special)
                    .OrderBy(c => Manhattan(c, cell)).Cast<BoardCell?>().FirstOrDefault();
                if (spot is not { } free) return null;
                at = free;
                taken.Add(free);
            }
            var range = offsets[(int)facing].Select(o => new BoardCell(at.Col + o.Col, at.Row + o.Row)).ToList();
            if (range.Count == 0) range.Add(at);
            return new SimUnit($"{n}·{s.Name}", at, range, CanBlock: s.Lane && s.Block > 0, s.Block, AntiAir: !s.Melee,
                Atk: s.Atk, Interval: s.Interval / Math.Max(0.1, s.AttackSpeed / 100), Arts: false, Hp: s.Hp, Def: s.Def, Healer: false)
            {
                Res = s.Res,
                Pattern = s.Hits > 1 ? new AttackPattern { HitsPerAttack = s.Hits } : AttackPattern.Single,
                Push = s.Push,
                DefIgnore = I.Talent?.DefIgnore ?? 0,
                Summon = new SummonTiming(s.Appear, s.Duration, s.Cooldown),
            };
        }

        /// <summary>
        /// The flat part of the covenants' buffs (the layers are already in power / toughness): attack, HP and
        /// defence multipliers for one operator, from covenant_effects.json's base numbers.
        /// </summary>
        private (double Atk, double Hp, double Def) BaseCovenantBuffs(string n, BoardCell cell,
            Dictionary<string, (BoardCell Cell, Facing Facing)> pl, CovenantContext cov)
        {
            double atk = 1, hp = 1, def = 1;
            var mine = Info[n].Covenants;
            bool Has(string c) => cov.Active.Contains(c) && mine.Contains(c);
            int Count(string c) => cov.Counts.GetValueOrDefault(c);
            if (Has("炎")) atk *= 1.23;
            if (Has("乌萨斯")) { atk *= 1.35; hp *= 1.10; }
            if (Has("莱塔尼亚")) atk *= 1.30;
            if (Has("维多利亚")) atk *= 1.25;
            if (Has("谢拉格")) atk *= 1.25;
            if (Has("阿戈尔")) hp *= 1.35;
            if (Has("伊比利亚")) atk *= 1.25;   // stacking DoT, (11 %/s up to 10 stacks) averaged
            if (Has("卡西米尔")) atk *= 1.30;   // +20 % per deploy up to 50 %
            if (Has("叙拉古")) atk *= 1.20;     // +25 攻速 for most of the fight
            if (Has("萨尔贡")) atk *= 1.05;
            if (Has("奥术")) atk *= 1.20;
            if (Has("突袭")) { atk *= 1.12; hp *= 1.12; }
            if (cov.Active.Contains("精准") && (mine.Contains("精准") || Count("精准") >= 3 && !Info[n].Melee)) atk *= 1.10;
            if (cov.Active.Contains("灵巧") && pl.Any(kv => Info[kv.Key].Covenants.Contains("灵巧") && Manhattan(kv.Value.Cell, cell) <= 1))
                atk *= 1.10;                    // +10 攻速 for 灵巧 and the 4 cells around them
            if (cov.Active.Contains("坚守")) hp *= 1.25;
            if (cov.Active.Contains("助力")) def *= 1.15;
            if (cov.Active.Contains("协防干员")) { hp *= 1.25; if (mine.Contains("协防干员")) atk *= 1.2; }
            if (Has("独行") && Count("独行") == 1) { atk *= 1.6; hp *= 1.6; }
            return (atk, hp, def);
        }

        /// <summary>The objective with the simulation: enemies handled, plus covenant activation and stacking.</summary>
        private double SimScore(Dictionary<string, (BoardCell Cell, Facing Facing)> pl)
        {
            if (_realRuns is null)
            {
                var b = Evaluate(pl, null);
                var fight = Simulate(pl);
                // The generic stream gets the same fragile-blocker rule (and damage tie-breaker) as the real ones.
                double meanHp = Math.Max(1, (_scenario ?? BaseScenario()).Ground.Hp);
                return KillPoint * (fight.Handled - SquishyLoss(pl, fight.Spawned) + DamageWeight * fight.Damage.Values.Sum() / meanHp)
                    + CombatPrior * b.Combat + b.Activation + _options.StackWeight * b.Stack;
            }
            var (killed, events) = ExpectedFight(pl);
            // A carry made the first blocker of a lane loses part of that lane (the fight alone may not show it when
            // the lane is light this round, but it is the line the enemies hit first).
            _spawnedReal ??= Expected(SimulateStreams(pl, _realRuns), r => r.Spawned);
            double squishy = SquishyLoss(pl, _spawnedReal.Value);
            _events = events;
            try
            {
                var bd = Evaluate(pl, null);
                // Damage dealt breaks ties between placements that handle as many: facing the enemies, covering
                // their path (a tenth of a kill per enemy's worth of HP).
                return KillPoint * (killed - squishy + DamageWeight * events.DamageInEnemies)
                    + CombatPrior * bd.Combat + bd.Activation + _options.StackWeight * bd.Stack;
            }
            finally { _events = null; }
        }

        private int FieldCount => Math.Min(Math.Max(1, _options.FieldCount), _pool.Count);

        public Dictionary<string, (BoardCell Cell, Facing Facing)> Normalise(
            IReadOnlyDictionary<string, (BoardCell Cell, Facing Facing)> placement) =>
            placement.Where(kv => Info.ContainsKey(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);

        /// <summary>When set, every covenant's layer is worth this instead (LayerGains: count raw layers).</summary>
        private Dictionary<string, double>? _layerValueOverride;

        private CovenantContext Covenants(IEnumerable<string> field)
        {
            var ctx = CovenantsCore(field);
            if (_layerValueOverride is not { } over) return ctx;
            return new CovenantContext
            {
                Active = ctx.Active, Counts = ctx.Counts, LayerBuffs = ctx.LayerBuffs,
                LayerValue = ctx.Active.ToDictionary(c => c, c => over.GetValueOrDefault(c)),
            };
        }

        /// <summary>
        /// Layers each active covenant gains in a round with this placement: the stacking score re-counted with that
        /// covenant's layer worth 1 and every other one worth 0 (minus what is stacked regardless of worth).
        /// </summary>
        public Dictionary<string, double> LayerGains(Dictionary<string, (BoardCell Cell, Facing Facing)> pl)
        {
            var active = Covenants(pl.Keys).Active.OrderBy(c => c, StringComparer.Ordinal).ToList();
            var gains = new Dictionary<string, double>();
            try
            {
                _layerValueOverride = [];
                double baseline = Evaluate(pl, null).Stack;
                foreach (var c in active)
                {
                    _layerValueOverride = new() { [c] = 1 };
                    double layers = Evaluate(pl, null).Stack - baseline;
                    if (layers > 0.05) gains[c] = Math.Round(layers, 1);
                }
            }
            finally { _layerValueOverride = null; }
            return gains;
        }

        private CovenantContext CovenantsCore(IEnumerable<string> field)
        {
            var names = field.OrderBy(n => n, StringComparer.Ordinal).ToList();
            string key = string.Join("|", names);
            if (_covCache.TryGetValue(key, out var cached)) return cached;
            // Covenants count different operators: a second copy of the same operator adds nothing.
            var byCovenant = new Dictionary<string, HashSet<string>>();
            void Add(string c, string op) { if (!byCovenant.TryGetValue(c, out var set)) byCovenant[c] = set = []; set.Add(op); }
            var fieldSet = names.ToHashSet();
            foreach (var n in names)
                foreach (var c in Info[n].Covenants) Add(c, Info[n].Name);
            foreach (var n in _pool.Where(n => !fieldSet.Contains(n)))
                foreach (var c in Info[n].Covenants.Where(BenchCountedCovenants.Contains)) Add(c, Info[n].Name);
            var counts = byCovenant.ToDictionary(kv => kv.Key, kv => kv.Value.Count);
            var active = counts.Where(kv => kv.Value >= (_owner._covenants.TryGetValue(kv.Key, out var ci) ? ci.ActivateCount ?? 99 : 99))
                .Select(kv => kv.Key).ToHashSet();
            var lv = new Dictionary<string, double>();
            foreach (var c in active)
            {
                if (!CovenantLayers.TryGetValue(c, out var effect)) { lv[c] = 0; continue; }
                var members = effect.Scope switch
                {
                    LayerScope.Everyone => names,
                    LayerScope.PreciseRanged when counts.GetValueOrDefault(c) >= 3 =>
                        names.Where(n => Info[n].Covenants.Contains(c) || !Info[n].Melee),
                    _ => names.Where(n => Info[n].Covenants.Contains(c)),
                }; // same reach as LayerMembers (the context is not built yet here)
                // Different operators only: a second copy is buffed too, so it counts here.
                lv[c] = LayerScale * effect.Parts.Sum(part => part.PerLayer
                    * members.Sum(n => Affinity(part.Stat, Info[n].Role) * (part.Stat is Stat.Atk or Stat.Aspd ? Info[n].Power : 1)));
            }
            var ctx = new CovenantContext { Active = active, LayerValue = lv, Counts = counts };
            _covCache[key] = ctx;
            return ctx;
        }

        public double Score(Dictionary<string, (BoardCell Cell, Facing Facing)> pl) => Evaluate(pl, null).Total;

        public List<string> NotesFor(Dictionary<string, (BoardCell Cell, Facing Facing)> pl, string name)
        {
            var notes = pl.Keys.ToDictionary(k => k, _ => new List<string>());
            Evaluate(pl, notes);
            return notes[name].Distinct().ToList();
        }

        private sealed class Breakdown
        {
            public double Combat, Stack, Activation, Total;
            public double[] BlockTotal = [];
            public int?[] BlockAt = [];
            public bool TilePenalty;
            public bool OnSlowTile;
            public List<string> Healers = [];
            public bool HealerCoversLine;
            public double AirCover, AirNeed;
            public List<string> AirGuards = [];
            public bool AirEndCovered;
            public List<(string Aegir, string Eaten, bool Killed)> Devours = [];
            public string? KazimierzReserve;
            public CovenantContext Cov = null!;
        }

        private Breakdown Evaluate(Dictionary<string, (BoardCell Cell, Facing Facing)> boardPl,
            Dictionary<string, List<string>>? notes)
        {
            void Note(string n, string text) { if (notes is not null) notes[n].Add(text); }
            var cov = Covenants(boardPl.Keys);
            // boardPl / atBoard: the board at the end of the preparation phase (stacking, positional traits).
            // pl / at / info: who fights, after 阿戈尔 devoured at battle start.
            var atBoard = new Dictionary<BoardCell, string>();
            foreach (var (n, v) in boardPl) atBoard[v.Cell] = n;
            var (pl, info, devours, devourStack) = Devour(boardPl, atBoard, cov, Note);
            info = WithCurrentLayers(info, boardPl.Keys, cov);
            // Layers (and devouring) make the team hit harder, which inflates every damage term. The safety penalties
            // (lines held, anti-air, a healer) grow with them, so a uniformly stronger team does not trade them away.
            double stakes = 1;
            if (!ReferenceEquals(info, Info))
            {
                double before = boardPl.Keys.Sum(n => Info[n].Power), after = boardPl.Keys.Sum(n => info[n].Power);
                if (before > 0) stakes = Math.Max(1, after / before);
            }
            var at = new Dictionary<BoardCell, string>();
            foreach (var (n, v) in pl) at[v.Cell] = n;
            double comb = 0, stk = 0;
            var dwell = new Dictionary<BoardCell, double>();
            var kz = new Dictionary<BoardCell, double>();
            var blockNames = new HashSet<string>();
            int routes = Board.Routes.Length;
            var blockIdx = new int?[routes];
            var bd = new Breakdown { Cov = cov, BlockTotal = new double[routes], BlockAt = blockIdx };

            int? LayRoute(BoardCell[] rt, Dictionary<BoardCell, int> index, double rw, string label)
            {
                var idx = pl.Where(kv => index.ContainsKey(kv.Value.Cell))
                    .Select(kv => (J: index[kv.Value.Cell], N: kv.Key)).OrderBy(x => x.J).ThenBy(x => x.N, StringComparer.Ordinal).ToList();
                if (idx.Count == 0)
                {
                    comb -= 40 * rw * stakes;
                    foreach (var c in rt) dwell[c] = dwell.GetValueOrDefault(c) + 0.15 * rw;
                    return null;
                }
                var (b, first) = idx[0];
                blockNames.Add(first);
                for (int j = 0; j < rt.Length; j++)
                {
                    var c = rt[j];
                    double w = j <= b ? 0.15 + (b - j) switch { 0 => 1.9, 1 => 1.2, 2 => 0.6, _ => 0 } : 0.12;
                    dwell[c] = dwell.GetValueOrDefault(c) + w * rw;
                    if (j <= b && b - j <= 2) kz[c] = kz.GetValueOrDefault(c) + (b - j) switch { 0 => 2.0, 1 => 1.2, _ => 0.5 } * rw;
                }
                var I = info[first];
                var t = I.Melee || I.Role == PlacementRole.Support ? I.Role : PlacementRole.Support;
                double frontBonus = t switch { PlacementRole.Tank => 25, PlacementRole.Damage => 6, PlacementRole.Stacker => 3, _ => -15 };
                // Effective HP decides how long the block point holds (a 3-block tank vs a guard standing in for one).
                double hold = Math.Clamp(I.Ehp / HoldReferenceEhp, 0.4, 1.8);
                comb += rw * (frontBonus + 10 * (hold - 1) + Math.Min(I.Block, 3) * 1.5 + 1.2 * Math.Min(rt.Length - 1 - b, 5));
                foreach (var (_, m) in idx.Skip(1)) comb += 0.35 * info[m].Block * rw;
                Note(first, $"{label}拦截点（离保护点 {rt.Length - 1 - b} 格）");
                if (cov.Counts.GetValueOrDefault("乌萨斯") >= 6 && I.Covenants.Contains("乌萨斯"))
                {
                    comb += 5 * rw;
                    Note(first, "乌萨斯 6：阻挡的敌人每秒受到法术伤害");
                }
                return b;
            }

            for (int k = 0; k < routes; k++)
                blockIdx[k] = LayRoute(Board.Routes[k], Board.RouteIndex[k], RW(k), RouteLabel(k));
            for (int e = 0; e < Board.Extra.Length; e++)
                LayRoute(Board.Extra[e], Board.ExtraIndex[e], 0.6, "D6 传送");
            if (Board.TeleportHazard)
                foreach (var (n, v) in pl)
                    if (Math.Max(Math.Abs(v.Cell.Col - 3), Math.Abs(v.Cell.Row - 3)) <= 1)
                    {
                        comb -= info[n].Role == PlacementRole.Tank ? 2 : 4;
                        Note(n, "在 D6 传送点周围，会周期性受伤");
                    }

            // Tiles that affect enemies: they are buffed whenever they walk over, so the line should be in front.
            if (Board.Tile is { } tile)
                for (int k = 0; k < routes; k++)
                {
                    var rt = Board.Routes[k];
                    int? b = blockIdx[k];
                    for (int j = 0; j < rt.Length; j++)
                    {
                        if (!Board.Special.Contains(rt[j]) || b is not null && j > b) continue;
                        if (tile.Kind == TileKind.EnemyBuff) { comb -= tile.EnemyPenalty * RW(k); bd.TilePenalty = true; }
                        if (tile.Kind == TileKind.Slow) dwell[rt[j]] = dwell.GetValueOrDefault(rt[j]) * 1.4;
                    }
                }

            foreach (var (n, v) in pl)
            {
                var I = info[n];
                if (I.Role == PlacementRole.Tank && !Board.OnMainRoute(v.Cell) && !Board.ExtraIndex.Any(d => d.ContainsKey(v.Cell)))
                    comb -= 12;
                // Depth: tanks in front, melee next, ranged at the back.
                (int Dist, int Route, int J)? best = null;
                for (int k = 0; k < routes; k++)
                {
                    if (blockIdx[k] is null) continue;
                    var rt = Board.Routes[k];
                    int dist = int.MaxValue, jj = 0;
                    for (int j = 0; j < rt.Length; j++)
                    {
                        int d = Math.Max(Math.Abs(v.Cell.Col - rt[j].Col), Math.Abs(v.Cell.Row - rt[j].Row));
                        if (d < dist) { dist = d; jj = j; }
                    }
                    if (dist <= 2 && (best is null || dist < best.Value.Dist)) best = (dist, k, jj);
                }
                if (best is { } bb)
                {
                    int ahead = blockIdx[bb.Route]!.Value - bb.J;
                    double rw = RW(bb.Route);
                    if (!I.Melee && ahead > 0) comb -= 6 * ahead * rw;
                    else if (I.Role == PlacementRole.Damage && ahead > 0 && !blockNames.Contains(n)) comb -= 3 * ahead * rw;
                    if (!I.Melee && ahead <= -1) comb += 1.5 * rw;
                }
                if (Board.Tile is { Kind: TileKind.Slow } && Board.Special.Contains(v.Cell)) bd.OnSlowTile = true;
            }

            // Damage: time enemies spend in range × damage power × tile buff.
            foreach (var (n, v) in pl)
            {
                var I = info[n];
                double c = 0;
                foreach (var (dx, dy) in I.Offsets[(int)v.Facing]) c += dwell.GetValueOrDefault(new BoardCell(v.Cell.Col + dx, v.Cell.Row + dy));
                var (mult, txt) = TileMultiplier(I, v.Cell, v.Facing);
                if (txt is not null) Note(n, txt);
                if (txt is not null && Board.Tile is { Drain: true } && blockNames.Contains(n)) comb -= 4;
                comb += 2.2 * I.Power * c * mult;
                if (c > 0 && I.Role == PlacementRole.Damage) Note(n, $"输出区覆盖 {c * mult:0.0}");
                // Flying enemies: only anti-air operators reach them, and nobody blocks them.
                if (Board.FlyTotal > 0 && I.AntiAir && !I.Healer)
                {
                    double air = 0;
                    bool end = false;
                    foreach (var (dx, dy) in I.Offsets[(int)v.Facing])
                    {
                        var cell = new BoardCell(v.Cell.Col + dx, v.Cell.Row + dy);
                        air += Board.Fly.GetValueOrDefault(cell);
                        end |= Board.FlyEnd.Contains(cell);
                    }
                    if (air <= 0) continue;
                    comb += 2.2 * I.Power * air * mult;
                    bd.AirCover += I.Power * air * mult;
                    bd.AirGuards.Add(n);
                    bd.AirEndCovered |= end;
                    Note(n, $"对空覆盖 {air * mult:0.0}" + (end ? "（含飞行终点）" : ""));
                }
            }

            // Air defence: flyers that are not shot down reach the defence point. About two anti-air operators'
            // worth of coverage is asked for, and someone has to cover the flyers' last cells.
            if (Board.FlyTotal > 0)
            {
                double share = Math.Min(1.0, (double)Board.FlyCount / Math.Max(1, Board.WalkCount));
                bd.AirNeed = AirNeedShare * Board.FlyTotal;
                if (bd.AirCover < bd.AirNeed) comb -= 30 * share * stakes * (bd.AirNeed - bd.AirCover) / bd.AirNeed;
                if (!bd.AirEndCovered) comb -= 10 * share * stakes;
            }

            // Healers: cover whoever holds the line; a second healer on the same person adds less.
            var healed = new Dictionary<string, List<string>>();
            foreach (var (n, v) in pl)
            {
                var I = info[n];
                bool convergence = cov.Active.Contains("聚合") && I.Covenants.Contains("聚合");
                if (!I.Healer && !convergence) continue;
                var cells = I.Offsets[(int)v.Facing].Select(o => new BoardCell(v.Cell.Col + o.Col, v.Cell.Row + o.Row)).ToHashSet();
                if (convergence)
                {
                    // 【聚合】 heals every ally in its attack range; with 3 of them they also get 庇护.
                    var inRange = pl.Where(kv => kv.Key != n && cells.Contains(kv.Value.Cell)).Select(kv => kv.Key).ToList();
                    bool shelter = cov.Counts.GetValueOrDefault("聚合") >= 3;
                    comb += inRange.Sum(m => (blockNames.Contains(m) ? 4.0 : 1.5) * (shelter ? 1.5 : 1));
                    if (inRange.Count > 0) Note(n, "聚合：范围内回血" + (shelter ? "和庇护" : "") + " " + string.Join("、", inRange));
                    if (!I.Healer) continue;
                }
                var hit = pl.Where(kv => cells.Contains(kv.Value.Cell) && Board.OnMainRoute(kv.Value.Cell)).Select(kv => kv.Key).ToList();
                foreach (var m in hit)
                {
                    if (!healed.TryGetValue(m, out var list)) healed[m] = list = [];
                    list.Add(n);
                }
                if (hit.Count > 0) Note(n, "奶到 " + string.Join("、", hit));
            }
            foreach (var (m, hs) in healed)
                comb += (blockNames.Contains(m) ? 9 : 3) * (1 + 0.25 * (hs.Count - 1));
            // Composition: keep at least one healer on the field when the pool has one, and make it reach
            // whoever holds a line (a healer that heals nobody blocking is close to no healer).
            bd.Healers = pl.Keys.Where(n => info[n].Healer).ToList();
            bd.HealerCoversLine = healed.Keys.Any(blockNames.Contains);
            if (bd.Healers.Count == 0 && _pool.Any(n => info[n].Healer)) comb -= 45 * stakes;
            else if (bd.Healers.Count > 0 && !bd.HealerCoversLine) comb -= 12 * stakes;

            // Block count at each block point and the two cells behind it.
            for (int k = 0; k < routes; k++)
            {
                if (blockIdx[k] is not int b) continue;
                var rt = Board.Routes[k];
                double total = 0;
                var who = new List<string>();
                for (int j = b; j < Math.Min(b + 3, rt.Length); j++)
                {
                    if (!at.TryGetValue(rt[j], out var m)) continue;
                    double bl = info[m].Block * (info[m].Melee ? 1 : 0.5);
                    total += bl;
                    who.Add($"{m}{bl:0.#}");
                }
                bd.BlockTotal[k] = total;
                if (total < BlockNeed) comb -= 10 * (BlockNeed - total) * RW(k) * stakes;
                if (at.TryGetValue(rt[b], out var firstName))
                    Note(firstName, $"{RouteLabel(k)}阻挡 {total:0.#}/{BlockNeed:0}（{string.Join("、", who)}）");
            }

            // Second contact: 1–2 cells behind a block point is where enemies go next when it breaks.
            for (int k = 0; k < routes; k++)
            {
                if (blockIdx[k] is not int b) continue;
                var rt = Board.Routes[k];
                foreach (int j in new[] { b + 1, b + 2 })
                {
                    if (j >= rt.Length || !at.TryGetValue(rt[j], out var m)) continue;
                    var I = info[m];
                    double w = RW(k) * (j == b + 1 ? 1.0 : 0.6);
                    if (I.SkillBlock) { comb += 3 * w; Note(m, "第二接敌位（技能可阻挡，能顶前排）"); }
                    else if (I.Role == PlacementRole.Support || !I.Melee && I.Role != PlacementRole.Tank)
                    { comb -= 8 * w; Note(m, "站在第二接敌位，前排被突破就会先挨打"); }
                    else if (I.Melee && I.Role is PlacementRole.Damage or PlacementRole.Tank)
                    { comb += 5 * w * (I.Role == PlacementRole.Damage ? I.Power : 0.6); Note(m, "第二接敌位（补位 + 输出）"); }
                }
            }

            // 【灵巧】: the operator and the 4 cells around it get attack speed (the 8 cells from 40 layers).
            bool dexterity8 = _options.CovenantLayers.GetValueOrDefault("灵巧") >= 40;
            if (cov.Active.Contains("灵巧"))
                foreach (var (n, v) in pl)
                {
                    if (!info[n].Covenants.Contains("灵巧")) continue;
                    var nb = pl.Where(kv => kv.Key != n && (dexterity8 ? Chebyshev(kv.Value.Cell, v.Cell) == 1 : Manhattan(kv.Value.Cell, v.Cell) == 1))
                        .Select(kv => kv.Key).ToList();
                    comb += nb.Sum(m => 3 * Affinity(Stat.Aspd, info[m].Role) * info[m].Power);
                    if (nb.Count > 0) Note(n, $"灵巧光环（周围{(dexterity8 ? "八" : "四")}格攻速）：" + string.Join("、", nb));
                }

            // Positional traits.
            IEnumerable<string> Act(string n) => info[n].Covenants.Where(cov.Active.Contains);
            foreach (var (n, v) in boardPl)
            {
                var I = info[n];
                string nm = I.Name;
                var (fx, fy) = PlacementCells.Vector(v.Facing);
                atBoard.TryGetValue(new BoardCell(v.Cell.Col + fx, v.Cell.Row + fy), out var front);
                atBoard.TryGetValue(new BoardCell(v.Cell.Col - fx, v.Cell.Row - fy), out var back);
                if (I.SelfFront > 0 && front is not null)
                {
                    var tg = Act(front).ToList();
                    stk += I.SelfFront * cov.Lv(tg);
                    Note(n, $"身前 {front}：{(tg.Count > 0 ? string.Join("、", tg) : "无已激活盟约")} 各 +{I.SelfFront:0.#} 层/回合");
                }
                if (I.SelfBack > 0)
                {
                    // Self and the operator behind; with a fight simulated, one trigger per stun in the eight cells around.
                    double per = I.SelfBack;
                    if (_events is { } ev && I.Trait.Contains("眩晕"))
                    {
                        double stuns = ev.Stuns.GetValueOrDefault(n);
                        per = Math.Min(I.Cap > 0 ? I.Cap : double.MaxValue, Math.Max(1, I.StackAmount) * stuns);
                        Note(n, $"模拟周围晕眩 {stuns:0.#} 次，叠层 {per:0.#}");
                    }
                    stk += per * cov.Lv(Act(n));
                    if (back is not null)
                    {
                        var tg = Act(back).ToList();
                        stk += per * cov.Lv(tg);
                        Note(n, $"身后 {back}：{(tg.Count > 0 ? string.Join("、", tg) : "无已激活盟约")} 各 +{per:0.#} 层");
                    }
                    else Note(n, "身后一格没有干员");
                }
                if (nm == "帕拉斯" && front is not null && info[front].Melee)
                {
                    comb += 6 + (blockNames.Contains(front) ? 4 : 0);
                    Note(n, $"技能：身前 {front} 阻挡 +1、攻击 / 防御提升");
                }
                if (nm == "凛御银灰")
                {
                    if (front is not null && info[front].Covenants.Any(c => c is "谢拉格" or "灵巧") && info[front].Cap > 0)
                    {
                        stk += info[front].Cap * 0.75 * 0.8 * cov.Lv(Act(front));
                        Note(n, $"身前 {front} 叠层上限 {info[front].Cap:0.#}→{info[front].Cap * 1.75:0.#}");
                    }
                    else Note(n, "身前没有带上限的谢拉格 / 灵巧叠层干员，特质没用上");
                }
                if (nm == "Mon3tr")
                {
                    var tg = boardPl.Where(kv => kv.Key != n && Chebyshev(kv.Value.Cell, v.Cell) == 1 && info[kv.Key].Cap > 0).Select(kv => kv.Key).ToList();
                    stk += tg.Sum(m => info[m].Cap * 0.3 * 0.8 * cov.Lv(Act(m)));
                    if (tg.Count > 0) Note(n, "周围八格 " + string.Join("、", tg) + " 叠层上限 +30%");
                }
                if (nm == "阿米娅" && front is not null && info[front].StacksLayers)
                {
                    stk += info[front].TriggersPerRound * cov.Lv(Act(front)) * 0.8;
                    Note(n, $"身前 {front} 每次叠层额外 +1");
                }
                if (nm == "华法琳" && front is not null)
                {
                    // Casts as the simulated fight had them when there is one (a skill that never ends: once).
                    double k = Math.Min(7, _events is { } fe ? fe.Casts.GetValueOrDefault(front) : info[front].SkillCasts);
                    stk += k * cov.Lv(Act(front));
                    Note(n, $"身前 {front} 开技能叠层（约 {k:0.#} 次/回合）");
                }
                if (nm == "铃兰" && front is not null && info[front].OnceAmount > 0)
                {
                    stk += info[front].OnceAmount * cov.Lv(Act(front));
                    Note(n, $"身前 {front} 每回合重复触发“获得时”");
                }
                if (nm == "塞雷娅" && front is not null && info[front].Trait.Contains("休整期结束时"))
                {
                    // The copy behaves as if 塞雷娅 had the trait: "自身" / "身前一格" / "同一行" are hers.
                    double copied = RoundEndValue(info[front], n, v.Cell, v.Facing, boardPl, atBoard, cov);
                    stk += copied;
                    Note(n, $"复制身前 {front} 的休整期结束时特质（叠层价值约 {copied:0.#}）");
                }
                if (nm == "浊心斯卡蒂")
                {
                    var cells = I.Offsets[(int)v.Facing].Select(o => new BoardCell(v.Cell.Col + o.Col, v.Cell.Row + o.Row)).ToHashSet();
                    int k = boardPl.Count(kv => kv.Key != n && cells.Contains(kv.Value.Cell));
                    stk += 2 * Math.Min(k, 10) * cov.Lv(["阿戈尔"]);
                    Note(n, $"范围内 {k} 名友方（每人 +2 阿戈尔）");
                }
                if (I.Around4)
                {
                    var nb = boardPl.Where(kv => kv.Key != n && Manhattan(kv.Value.Cell, v.Cell) == 1 && info[kv.Key].Role == PlacementRole.Damage)
                        .Select(kv => kv.Key).ToList();
                    comb += nb.Sum(m => 3 * info[m].Power);
                    if (nb.Count > 0) Note(n, "周围四格增益 " + string.Join("、", nb));
                }
                if (RowOfThree.Contains(nm) || nm == "罗比菈塔")
                {
                    int rowCount = boardPl.Count(kv => kv.Value.Cell.Row == v.Cell.Row);
                    if (nm == "罗比菈塔")
                    {
                        stk += 2 * rowCount * cov.Lv(new[] { "坚守", "助力" }.Where(cov.Active.Contains));
                        Note(n, $"同一行 {rowCount} 人");
                    }
                    else if (rowCount >= 3) { stk += 12; Note(n, $"同一行 {rowCount} 人，触发"); }
                    else Note(n, $"同一行只有 {rowCount} 人，未触发");
                }
                if (I.KillRange && _events is { } kev)
                {
                    double kills = kev.Kills.GetValueOrDefault(n);
                    double layers = Math.Min(I.Cap > 0 ? I.Cap : double.MaxValue, Math.Max(1, I.StackAmount) * kills);
                    stk += layers * cov.Lv(I.StackCovenants.Length > 0 ? I.StackCovenants.Where(cov.Active.Contains) : Act(n));
                    Note(n, $"模拟范围内击倒 {kills:0.#} 个，叠层 {layers:0.#}");
                }
                else if (I.KillRange)
                {
                    double c = 0;
                    foreach (var (dx, dy) in I.Offsets[(int)v.Facing])
                    {
                        var cell = new BoardCell(v.Cell.Col + dx, v.Cell.Row + dy);
                        c += kz.GetValueOrDefault(cell);
                        // Flyers shot down in range count as kills too (they pass through rather than pile up).
                        if (I.AntiAir) c += 0.3 * Board.Fly.GetValueOrDefault(cell);
                    }
                    stk += 1.8 * c;
                    Note(n, $"范围内击倒 / 控制叠层：罩住拦截点及前方 1~2 格（{c:0.0}）");
                }
            }

            // Operators' own stacking (not tied to where they stand); matters for choosing who goes on the field.
            foreach (var (n, v) in boardPl)
            {
                var I = info[n];
                if (I.KillRange || I.SelfFront > 0 || I.SelfBack > 0 || I.Name == "罗比菈塔" || I.StackKind is not ("covenant" or "self")) continue;
                double perRound = I.StackPerRound;
                // Stacking on casting a skill: as many casts as the simulated fight had (capped as the trait says).
                if (_events is { } ev && I.Trait.Contains("开启技能时") && I.StackAmount > 0)
                {
                    double c = ev.Casts.GetValueOrDefault(n);
                    perRound = Math.Min(I.Cap > 0 ? I.Cap : double.MaxValue, I.StackAmount * c);
                    if (notes is not null) Note(n, $"模拟开技能 {c:0.#} 次，叠层 {perRound:0.#}");
                }
                double own = I.Trait.Contains("休整期结束时") ? RoundEndValue(I, n, v.Cell, v.Facing, boardPl, atBoard, cov)
                    : perRound * cov.Lv(I.StackKind == "self" ? Act(n) : I.StackCovenants.Where(cov.Active.Contains));
                stk += own;
                if (own > 0 && I.Name == "涤火杰西卡") Note(n, $"按满层估算：场上 {boardPl.Count} 人都带装备，维多利亚 / 坚守各 +{2 * boardPl.Count} 层");
            }

            // 阿戈尔 devour: layers equal to the devoured operator's tier, every battle.
            stk += devourStack;
            bd.Devours = devours;

            // 【助力】 at the end of every preparation phase: all active covenants +2 layers (+4 with 3 different 助力).
            if (cov.Active.Contains("助力"))
            {
                int add = cov.Counts.GetValueOrDefault("助力") >= 3 ? 4 : 2;
                double helper = add * cov.Lv(cov.Active);
                stk += helper;
                if (boardPl.Keys.FirstOrDefault(n => info[n].Covenants.Contains("助力")) is { } first)
                    Note(first, $"助力：每回合所有已激活盟约 +{add} 层（叠层价值约 {helper:0.#}）");
            }

            // 【卡西米尔】 6: a ground 卡西米尔 knocked out is replaced by the right-most 卡西米尔 in the bench.
            if (cov.Counts.GetValueOrDefault("卡西米尔") >= 6)
            {
                var reserve = _pool.Where(n => !boardPl.ContainsKey(n) && info[n].Covenants.Contains("卡西米尔"))
                    .OrderByDescending(n => info[n].Power).FirstOrDefault();
                if (reserve is not null)
                {
                    comb += 6 * info[reserve].Power;
                    bd.KazimierzReserve = reserve;
                }
            }

            // Activating covenants is worth something in itself; bench-effective operators still stack.
            double activation = 0;
            foreach (var c in cov.Active)
            {
                bool core = _owner._covenants.TryGetValue(c, out var ci) && ci.BondType == "SEASON";
                activation += core ? 14 : 7;
                if (core && cov.Counts.GetValueOrDefault(c) >= 6) activation += 30;
            }
            double maxLv = cov.LayerValue.Values.DefaultIfEmpty(0).Max();
            foreach (var n in _pool.Where(n => !boardPl.ContainsKey(n)))
                if (info[n].BenchOk) stk += 6 * maxLv;

            bd.Combat = comb;
            bd.Stack = stk;
            bd.Activation = activation;
            bd.Total = comb + activation + _options.StackWeight * stk;
            return bd;
        }

        /// <summary>
        /// How strong each operator is with its covenants' current layers (<see cref="PlacementOptions.CovenantLayers"/>):
        /// a member of 炎 at 80 layers attacks 0.9 % × 80 harder than at none. Attack and attack-speed layers scale the
        /// damage power, HP and defence layers the effective HP; 突袭 at 50 gives everyone +50 attack speed.
        /// </summary>
        private Dictionary<string, OpInfo> WithCurrentLayers(Dictionary<string, OpInfo> info, IEnumerable<string> field, CovenantContext cov)
        {
            if (_options.CovenantLayers.Count == 0) return info;
            var buffs = cov.LayerBuffs ??= LayerBuffs(field, cov);
            if (buffs.Count == 0) return info;
            var copy = new Dictionary<string, OpInfo>(info);
            foreach (var (n, (atk, hp)) in buffs)
                if (copy.TryGetValue(n, out var I)) copy[n] = I.Buffed(I.Power * atk, I.Ehp * hp);
            return copy;
        }

        private Dictionary<string, (double Atk, double Hp)> LayerBuffs(IEnumerable<string> field, CovenantContext cov)
        {
            var names = field.ToList();
            var buffs = names.ToDictionary(n => n, _ => (Atk: 1.0, Hp: 1.0));
            foreach (var c in cov.Active)
            {
                int layers = _options.CovenantLayers.GetValueOrDefault(c);
                if (layers <= 0 || !CovenantLayers.TryGetValue(c, out var effect)) continue;
                foreach (var n in LayerMembers(c, effect, names, cov))
                {
                    var (atk, hp) = buffs[n];
                    foreach (var (stat, per) in effect.Parts)
                        if (stat is Stat.Atk or Stat.Aspd) atk *= 1 + per * layers;
                        else if (stat == Stat.Hp) hp *= 1 + per * layers;
                    buffs[n] = (atk, hp);
                }
            }
            if (cov.Active.Contains("突袭") && _options.CovenantLayers.GetValueOrDefault("突袭") >= 50)
                foreach (var n in names) buffs[n] = (buffs[n].Atk * 1.5, buffs[n].Hp);
            return buffs.Where(kv => Math.Abs(kv.Value.Atk - 1) > 1e-9 || Math.Abs(kv.Value.Hp - 1) > 1e-9)
                .ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        /// <summary>Who a covenant's layer buff reaches on this field.</summary>
        private IEnumerable<string> LayerMembers(string c, LayerEffect effect, IReadOnlyCollection<string> names, CovenantContext cov) =>
            effect.Scope switch
            {
                LayerScope.Everyone => names,
                LayerScope.PreciseRanged when cov.Counts.GetValueOrDefault(c) >= 3 =>
                    names.Where(n => Info[n].Covenants.Contains(c) || !Info[n].Melee),
                _ => names.Where(n => Info[n].Covenants.Contains(c)),
            };

        /// <summary>
        /// 【阿戈尔】 at battle start: each 阿戈尔 operator (left-most, then top-most first) devours the operator in front
        /// of it, dealing 5000 physical damage, and takes its base attack and block; the covenant gains layers equal to
        /// the devoured operator's tier. Whoever does not survive the 5000 damage leaves the fight, except that with
        /// 6 阿戈尔 the first three 阿戈尔 knocked out revive at once.
        /// </summary>
        private (Dictionary<string, (BoardCell Cell, Facing Facing)> Fight, Dictionary<string, OpInfo> Info,
            List<(string Aegir, string Eaten, bool Killed)> Devours, double Stack)
            Devour(Dictionary<string, (BoardCell Cell, Facing Facing)> board, Dictionary<BoardCell, string> at,
                CovenantContext cov, Action<string, string> note)
        {
            var devours = new List<(string, string, bool)>();
            if (!cov.Active.Contains("阿戈尔")) return (board, Info, devours, 0);
            Dictionary<string, OpInfo>? info = null;
            var eaten = new HashSet<string>();
            var killed = new HashSet<string>();
            int revived = 0;
            double stack = 0;
            foreach (var (n, v) in board.Where(kv => Info[kv.Key].Covenants.Contains("阿戈尔"))
                         .OrderBy(kv => kv.Value.Cell.Col).ThenBy(kv => kv.Value.Cell.Row))
            {
                if (eaten.Contains(n)) continue;
                var (fx, fy) = PlacementCells.Vector(v.Facing);
                if (!at.TryGetValue(new BoardCell(v.Cell.Col + fx, v.Cell.Row + fy), out var prey) || eaten.Contains(prey))
                {
                    note(n, "阿戈尔吞噬：身前没人，没吞到");
                    continue;
                }
                var (a, t) = (Info[n], Info[prey]);
                eaten.Add(prey);
                bool dies = t.Hp <= Math.Max(5000 - t.Def, 250);
                // 阿戈尔 6: the first 3 阿戈尔 operators knocked out revive at once, so a devoured 阿戈尔 stays.
                bool revives = dies && t.Covenants.Contains("阿戈尔") && cov.Counts.GetValueOrDefault("阿戈尔") >= 6 && revived < 3;
                if (revives) revived++;
                else if (dies) killed.Add(prey);
                dies &= !revives;
                info ??= new Dictionary<string, OpInfo>(Info);
                info[n] = info[n].Devoured(info[n].Power * (1 + t.Atk / Math.Max(1, a.Atk)), info[n].Block + t.Block);
                stack += t.Tier * cov.Lv(["阿戈尔"]);
                devours.Add((n, prey, dies));
                note(n, $"阿戈尔吞噬身前 {prey}：攻击 +{t.Atk:0}、阻挡 +{t.Block}、阿戈尔 +{t.Tier} 层");
                note(prey, dies ? $"开战被 {n} 吞噬（5000 物理伤害，撑不住，离场）"
                    : t.Hp <= Math.Max(5000 - t.Def, 250) ? $"开战被 {n} 吞噬，阿戈尔 6 立刻复活"
                    : $"开战被 {n} 吞噬（5000 物理伤害后还在场）");
            }
            var fight = killed.Count == 0 ? board : board.Where(kv => !killed.Contains(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
            return (fight, info ?? Info, devours, stack);
        }

        /// <summary>
        /// Layers one round-end trait of <paramref name="source"/> adds, as if held by <paramref name="holder"/>
        /// standing at <paramref name="cell"/> (塞雷娅 copies the trait of the operator in front of her).
        /// </summary>
        private double RoundEndValue(OpInfo source, string holder, BoardCell cell, Facing facing,
            Dictionary<string, (BoardCell Cell, Facing Facing)> pl, Dictionary<BoardCell, string> at, CovenantContext cov)
        {
            IEnumerable<string> ActOf(string n) => Info[n].Covenants.Where(cov.Active.Contains);
            var t = source.Trait;
            double amount = source.StackAmount > 0 ? source.StackAmount : Number(AmountPattern, t, 1);
            if (source.Name == "涤火杰西卡" || t.Contains("携带装备的干员"))
                return 2 * pl.Count * cov.Lv(new[] { "维多利亚", "坚守" }.Where(cov.Active.Contains)); // full: everyone equipped
            if (t.Contains("不同阶的【维多利亚】"))
            {
                int tiers = pl.Keys.Where(n => Info[n].Covenants.Contains("维多利亚")).Select(n => Info[n].Tier).Distinct().Count();
                return amount * tiers * cov.Lv(source.StackCovenants.Where(cov.Active.Contains));
            }
            if (t.Contains("同一行"))
                return amount * pl.Count(kv => kv.Value.Cell.Row == cell.Row) * cov.Lv(source.StackCovenants.Where(cov.Active.Contains));
            if (t.Contains("层数最多的盟约"))
                return amount * cov.LayerValue.Values.DefaultIfEmpty(0).Max();
            if (Regex.IsMatch(t, @"自身(以|及)?及?身前一格"))
            {
                var (fx, fy) = PlacementCells.Vector(facing);
                at.TryGetValue(new BoardCell(cell.Col + fx, cell.Row + fy), out var front);
                return amount * (cov.Lv(ActOf(holder)) + (front is null ? 0 : cov.Lv(ActOf(front))));
            }
            if (source.StackKind == "self" || t.Contains("自身已激活"))
                return (source.StackPerRound > 0 ? source.StackPerRound : amount) * cov.Lv(ActOf(holder));
            var named = source.StackCovenants.Length > 0 ? source.StackCovenants.Where(cov.Active.Contains)
                : Regex.Matches(t, "【(.+?)】").Select(m => m.Groups[1].Value).Where(cov.Active.Contains);
            return (source.StackPerRound > 0 ? source.StackPerRound : amount) * cov.Lv(named);
        }

        private (double Mult, string? Text) TileMultiplier(OpInfo I, BoardCell cell, Facing f)
        {
            if (Board.Tile is not { } tile || !Board.Special.Contains(cell)) return (1, null);
            double aff = Affinity(tile.Stat, I.Role);
            if (tile.Kind == TileKind.Facing)
            {
                int d = f == Facing.Down ? 1 : f == Facing.Up ? -1 : 0;
                return (1 + d * tile.Ally * aff, d switch
                {
                    1 => "橙色通道朝下：攻击提升",
                    -1 => "橙色通道朝上：攻击下降",
                    _ => "橙色通道：左右朝向无效果",
                });
            }
            string txt = tile.Kind == TileKind.Slow ? "站在沼泽上，攻速下降"
                : "地块加成：" + (tile.Drain ? "攻击 / 攻速提升，持续掉血" : "攻速提升");
            return (1 + tile.Ally * aff, txt);
        }

        private double RW(int k) => Board.Weights[k];
        private string RouteLabel(int k) => Board.Labels[k];
        private static int Manhattan(BoardCell a, BoardCell b) => Math.Abs(a.Col - b.Col) + Math.Abs(a.Row - b.Row);
        private static int Chebyshev(BoardCell a, BoardCell b) => Math.Max(Math.Abs(a.Col - b.Col), Math.Abs(a.Row - b.Row));

        // ── Search ─────────────────────────────────────────────────────────

        public Dictionary<string, (BoardCell Cell, Facing Facing)> Solve(CancellationToken cancel, bool simulate = false,
            Dictionary<string, (BoardCell Cell, Facing Facing)>? start = null)
        {
            Func<Dictionary<string, (BoardCell Cell, Facing Facing)>, double> Score = simulate ? SimScore : this.Score;
            var rnd = new Random(_options.Seed);
            var pins = _options.Pins.Where(kv => Info.ContainsKey(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
            // Kept on the field by the player (anywhere): placed first and never swapped out; kept on the bench: never placed.
            var must = _options.FieldPins.Where(k => Info.ContainsKey(k) && !pins.ContainsKey(k)).ToHashSet();
            var free = _pool.Where(n => !pins.ContainsKey(n) && (!_options.BenchPins.Contains(n) || must.Contains(n))).ToList();
            int field = Math.Max(FieldCount, Math.Min(pins.Count + must.Count, _pool.Count));
            Dictionary<string, (BoardCell, Facing)>? best = null;
            double bestScore = double.MinValue;
            if (_pool.Count == 0) return [];

            // With the simulation each step plays a whole fight: one run from the fast plan, at a lower temperature.
            int restarts = start is not null ? 1 : Math.Max(1, _options.Restarts);
            double heat = simulate ? 3.0 : 8.0;
            for (int restart = 0; restart < restarts; restart++)
            {
                var pl = start is not null ? new Dictionary<string, (BoardCell Cell, Facing Facing)>(start)
                    : new Dictionary<string, (BoardCell Cell, Facing Facing)>();
                foreach (var (n, p) in pins) pl[n] = (p.Cell, p.Facing);
                var used = pl.Values.Select(v => v.Cell).ToHashSet();
                var order = free.OrderBy(_ => rnd.Next()).ToList()
                    .OrderBy(n => !must.Contains(n)).ThenBy(n => Info[n].Role != PlacementRole.Tank).ThenBy(n => !Info[n].Melee).ToList();
                foreach (var n in order)
                {
                    if (pl.Count >= field) break;
                    var opts = Board.Deployable.Where(c => !used.Contains(c) && Board.Legal(Info[n], c)).ToList();
                    if (opts.Count == 0) continue;
                    var c = opts[rnd.Next(opts.Count)];
                    pl[n] = (c, Faces[rnd.Next(4)]);
                    used.Add(c);
                }
                double cur = Score(pl);
                if (cur > bestScore) { bestScore = cur; best = new(pl); }
                int iters = simulate ? Math.Max(50, _options.SimulationIterations) : Math.Max(100, _options.Iterations);
                for (int it = 0; it < iters; it++)
                {
                    if ((it & (simulate ? 15 : 255)) == 0 && cancel.IsCancellationRequested) break;
                    double T = Math.Max(0.05, heat * (1 - (double)it / iters));
                    var movable = pl.Keys.Where(k => !pins.ContainsKey(k)).ToList();
                    if (movable.Count == 0) break;
                    var cand = new Dictionary<string, (BoardCell Cell, Facing Facing)>(pl);
                    string n = movable[rnd.Next(movable.Count)];
                    double mv = rnd.NextDouble();
                    var bench = free.Where(m => !cand.ContainsKey(m)).ToList();
                    if (mv < 0.2 && bench.Count > 0)
                    {
                        if (must.Contains(n)) continue;
                        string m = bench[rnd.Next(bench.Count)];
                        var (cell, f) = cand[n];
                        cand.Remove(n);
                        if (!Board.Legal(Info[m], cell))
                        {
                            var occ = cand.Values.Select(v => v.Cell).ToHashSet();
                            var opts = Board.Deployable.Where(c => !occ.Contains(c) && Board.Legal(Info[m], c)).ToList();
                            if (opts.Count == 0) continue;
                            cell = opts[rnd.Next(opts.Count)];
                        }
                        cand[m] = (cell, f);
                    }
                    else if (mv < 0.5)
                    {
                        var occ = cand.Where(kv => kv.Key != n).Select(kv => kv.Value.Cell).ToHashSet();
                        var opts = Board.Deployable.Where(c => !occ.Contains(c) && Board.Legal(Info[n], c)).ToList();
                        if (opts.Count == 0) continue;
                        cand[n] = (opts[rnd.Next(opts.Count)], cand[n].Facing);
                    }
                    else if (mv < 0.75) cand[n] = (cand[n].Cell, Faces[rnd.Next(4)]);
                    else
                    {
                        string m = movable[rnd.Next(movable.Count)];
                        if (m == n || !Board.Legal(Info[n], cand[m].Cell) || !Board.Legal(Info[m], cand[n].Cell)) continue;
                        (cand[n], cand[m]) = ((cand[m].Cell, cand[n].Facing), (cand[n].Cell, cand[m].Facing));
                    }
                    double sc = Score(cand);
                    if (sc >= cur || rnd.NextDouble() < Math.Exp((sc - cur) / T)) { pl = cand; cur = sc; }
                    if (cur > bestScore) { bestScore = cur; best = new(pl); }
                }
            }
            return best!.ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        // ── Result ─────────────────────────────────────────────────────────

        public PlacementPlan Explain(Dictionary<string, (BoardCell Cell, Facing Facing)> pl)
        {
            var notes = pl.Keys.ToDictionary(k => k, _ => new List<string>());
            // The simulated fight: who killed how many, who went down.
            // With real streams: every stream at its real strength (not the search's calibrated one).
            var real = _options.Simulate && _calibrated && _streams is not null ? SimulateStreams(pl, StreamRuns(1)) : null;
            // The search's own value for this placement (before the events below are pinned for the notes).
            double? objective = _options.Simulate && _calibrated ? SimScore(pl) : null;
            // Stacking on skill casts as the real fights had them (the score and the layer estimate alike).
            _events = real is not null ? EventsOf(real) : null;
            try { return ExplainCore(pl, notes, real, objective); }
            finally { _events = null; }
        }

        private PlacementPlan ExplainCore(Dictionary<string, (BoardCell Cell, Facing Facing)> pl, Dictionary<string, List<string>> notes,
            List<(RoundStream Stream, double Weight, SimResult Result)>? real, double? objective)
        {
            var bd = Evaluate(pl, notes);
            SimResult? sim = real is not null ? SimResult.Average(real.Select(r => (r.Result, r.Weight)))
                : _options.Simulate && _scenario is not null ? Simulate(pl) : null;
            if (sim is not null)
                foreach (var (k, list) in notes)
                {
                    if (sim.Kills.TryGetValue(k, out int kills) && kills > 0) list.Add($"模拟击倒 {kills} 个敌人");
                    if (sim.Fallen.Contains(k)) list.Add("模拟中被打倒");
                }
            // The piece's numbers go last: the notes before them explain the cell.
            foreach (var (k, list) in notes)
                if (Info[k].EffectiveDps is { } eff)
                    list.Add($"有效输出 {eff:0}/秒" + (Info[k].SkillFactor > 1.005 ? $"（技能 ×{Info[k].SkillFactor:0.00}）" : "")
                        + $" · 有效生命 {Info[k].Ehp:0}");
            var placed = pl.OrderBy(kv => Order(kv.Key, kv.Value.Cell))
                .Select(kv => new PlacedOperator(Info[kv.Key].Name, kv.Value.Cell, kv.Value.Facing, Info[kv.Key].Role,
                    _options.Pins.ContainsKey(kv.Key), notes[kv.Key].Distinct().ToList(), kv.Key))
                .ToList();
            var bench = new List<BenchedOperator>();
            foreach (var n in _pool.Where(n => !pl.ContainsKey(n)))
            {
                var I = Info[n];
                var counted = I.Covenants.Where(BenchCountedCovenants.Contains).ToList();
                bench.Add(new BenchedOperator(n, I.BenchOk ? "在整备区也生效"
                    : counted.Count > 0 ? "整备区也算盟约人数（" + string.Join("、", counted) + "）" : ""));
            }
            bench.AddRange(_unplaceable.Select(n => new BenchedOperator(n, "缺少干员数据，没参与站位")));

            var checks = new List<PlacementCheck>();
            foreach (var c in bd.Cov.Active.Where(c => _owner._covenants.TryGetValue(c, out var ci) && ci.BondType == "SEASON")
                         .OrderByDescending(c => bd.Cov.Counts.GetValueOrDefault(c)))
                checks.Add(new PlacementCheck($"{c} {bd.Cov.Counts.GetValueOrDefault(c)}", true));
            // Lanes that meet before the line share one block point: one check for them.
            var lanes = new List<PlacementLane>();
            for (int k = 0; k < Board.Routes.Length; k++)
                lanes.Add(new PlacementLane(RouteLabel(k), Board.Weights[k] / Board.Weights.Sum(), Board.Routes[k],
                    bd.BlockAt[k] is int b ? Board.Routes[k][b] : null));
            foreach (var group in Enumerable.Range(0, lanes.Count).GroupBy(k => lanes[k].Block is { } cell ? cell.ToString() : $"none{k}"))
            {
                var ks = group.ToList();
                string label = string.Join("+", ks.Select(k => lanes[k].Label));
                int k0 = ks[0];
                checks.Add(bd.BlockAt[k0] is null
                    ? new PlacementCheck($"{label}没人拦", false)
                    : new PlacementCheck($"{label}{(ks.Count > 1 ? " " : "")}阻挡 {bd.BlockTotal[k0]:0.#}/{BlockNeed:0}", bd.BlockTotal[k0] >= BlockNeed));
            }
            if (Board.FlyTotal > 0)
                checks.Add(new PlacementCheck(
                    $"对空 {bd.AirGuards.Count} 人 {bd.AirCover:0.#}/{bd.AirNeed:0.#}" + (bd.AirEndCovered ? "" : " · 飞行终点没人"),
                    bd.AirCover >= bd.AirNeed && bd.AirEndCovered));
            if (Board.Tile is { Kind: TileKind.EnemyBuff } t)
            {
                int on = pl.Count(kv => Board.Special.Contains(kv.Value.Cell));
                string text = (on > 0 ? $"{on} 人站{t.Name} · " : "") + (bd.TilePenalty ? $"敌人会先走过{t.Name}" : $"防线在{t.Name}前");
                checks.Add(new PlacementCheck(text, !bd.TilePenalty, t.HintOnly));
            }
            if (Board.Tile is { Kind: TileKind.Slow } s)
                checks.Add(new PlacementCheck(bd.OnSlowTile ? $"有人站在{s.Name}上" : $"没人站在{s.Name}上", !bd.OnSlowTile));
            foreach (var c in bd.Cov.Active.Where(c => _options.CovenantLayers.GetValueOrDefault(c) > 0).OrderBy(c => c, StringComparer.Ordinal))
            {
                int layers = _options.CovenantLayers[c];
                string what = CovenantLayers.TryGetValue(c, out var e) && e.Parts.Length > 0
                    ? string.Join("、", e.Parts.Where(p => p.Stat != Stat.None).Select(p => $"{StatText(p.Stat)} +{p.PerLayer * layers:P0}"))
                    : "无属性加成";
                string whom = e?.Scope == LayerScope.Everyone ? "全场" : c == "精准" && bd.Cov.Counts.GetValueOrDefault(c) >= 3 ? "成员和远程" : "成员";
                checks.Add(new PlacementCheck($"{c} {layers} 层：{whom}{what}", true, IsHint: true));
            }
            if (bd.Cov.Active.Contains("灵巧") && _options.CovenantLayers.GetValueOrDefault("灵巧") >= 40)
                checks.Add(new PlacementCheck("灵巧 40 层：光环扩到周围八格", true, IsHint: true));
            if (bd.Cov.Active.Contains("突袭") && _options.CovenantLayers.GetValueOrDefault("突袭") >= 50)
                checks.Add(new PlacementCheck("突袭 50 层：所有干员攻速 +50", true, IsHint: true));
            if (real is not null)
            {
                // One line per enemy type of the run: expected handled / spawned and what gets through.
                int at = 0;
                foreach (var type in real.GroupBy(r => r.Stream.Type))
                {
                    double w = type.Sum(r => r.Weight);
                    double spawned = type.Sum(r => r.Weight * r.Result.Spawned) / w;
                    double killed = type.Sum(r => r.Weight * r.Result.Killed) / w;
                    double leaked = type.Sum(r => r.Weight * r.Result.Leaked) / w;
                    var names = type.SelectMany(r => r.Result.LeakedByName.Select(kv => (kv.Key, Count: r.Weight * kv.Value / w)))
                        .GroupBy(x => x.Key).Select(g => (Name: g.Key, Count: g.Sum(x => x.Count)))
                        .Where(x => x.Count >= 0.5).OrderByDescending(x => x.Count).Take(3).ToList();
                    string name = GarrisonSpawnStore.TypeNames.GetValueOrDefault(type.Key, type.Key == "BOSS" ? "首领回合" : type.Key);
                    // Short on the chip ("频次 11/21"); what gets through in its tooltip.
                    string text = $"{name} {killed:0.#}/{spawned:0.#}";
                    string detail = $"{name}：处理 {killed:0.#}/{spawned:0.#}";
                    double bosses = type.Sum(r => r.Weight * r.Result.Bosses) / w;
                    bool bossDown = true;
                    if (bosses > 0)
                    {
                        // How far the boss gets beaten: killed, or the share of its HP taken off.
                        double progress = type.Sum(r => r.Weight * r.Result.BossProgress) / w / bosses;
                        bossDown = progress >= 0.995;
                        text += bossDown ? " · 首领击杀" : $" · 首领 {progress:P0}";
                        detail += bossDown ? " · 首领击杀" : $" · 首领血量打掉 {progress:P0}";
                    }
                    if (leaked >= 0.5)
                        detail += $"\n漏 {leaked:0.#}" + (names.Count > 0 ? "：" + string.Join("、", names.Select(x => $"{x.Name}×{x.Count:0.#}")) : "");
                    checks.Insert(at++, new PlacementCheck(text, leaked < 0.5 && bossDown, Detail: detail));
                }
            }
            else if (sim is not null)
                checks.Insert(0, new PlacementCheck(
                    $"模拟：处理 {sim.Killed}/{sim.Spawned}" + (sim.Leaked > 0 || sim.Alive > 0 ? $"（漏 {sim.Leaked}，没打完 {sim.Alive}）" : ""),
                    sim.Leaked == 0));
            foreach (var (aegir, prey, killed) in bd.Devours)
                checks.Add(new PlacementCheck($"{aegir} 吞噬 {prey}" + (killed ? "（离场）" : ""), true, IsHint: true));
            if (bd.KazimierzReserve is { } reserve)
                checks.Add(new PlacementCheck($"整备区最右放 {reserve}（卡西米尔替补）", true, IsHint: true));
            foreach (var (_, first, lane, blocker) in SquishyBlocks(pl))
                if (first) checks.Add(new PlacementCheck($"{lane}最先由远程 {blocker} 阻挡", false));
            var gains = LayerGains(pl);
            if (gains.Count > 0)
            {
                var ordered = gains.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key} +{kv.Value:0.#}").ToList();
                checks.Add(new PlacementCheck("叠层 " + string.Join(" ", ordered), true, IsHint: true,
                    Detail: "预计每回合叠层：" + string.Join("、", ordered)));
            }
            if (bd.Healers.Count == 0)
                checks.Add(new PlacementCheck(_pool.Any(n => Info[n].Healer) ? "场上没有奶" : "干员池里没有奶", false));
            else
                checks.Add(new PlacementCheck(bd.HealerCoversLine ? "奶 " + string.Join("、", bd.Healers) : "奶够不到拦截点", bd.HealerCoversLine));

            return new PlacementPlan
            {
                Map = Board.Map,
                Placed = placed,
                Bench = bench,
                ActiveCovenants = [.. bd.Cov.Active.OrderBy(c => c, StringComparer.Ordinal)],
                Checks = checks,
                Combat = Math.Round(bd.Combat, 1),
                Stacking = Math.Round(bd.Stack, 1),
                Total = Math.Round(bd.Total, 1),
                Objective = objective is double o ? Math.Round(o, 1) : null,
                Lanes = lanes,
                Basis = (Board.Round is { } r
                    ? $"第 {r.Round} 回合路线 · 地面 {r.Walking}" + (Board.FlyTotal > 0 ? $" · 飞行 {r.Flying}" : r.Flying > 0 ? " · 按无飞行" : "")
                    : "地图默认路线（还没有本回合路线）")
                    + StreamBasis(),
                StackWeight = Math.Round(_options.StackWeight, 2),
                Simulation = sim,
                Scenario = sim is null || real is not null ? null : _scenario,
                StreamFactor = real is not null ? _streamFactor : null,
                LayerGains = gains,
                StreamResults = real is null ? [] : [.. real.Select(r => new StreamOutcome(r.Stream.Label, r.Stream.Type, r.Weight, r.Result))],
                TransformHints = TransformHints(pl),
                Shares = SharesOf(real is not null ? [.. real.Select(r => (r.Weight, r.Result))] : sim is not null ? [(1.0, sim)] : [], pl.Keys),
                Flying = Board.FlyTotal > 0,
            };
        }

        /// <summary>
        /// Each placed operator's share of damage dealt, taken and healed over the fights (weighted); a summon
        /// ("owner·name") counts for its operator.
        /// </summary>
        private static Dictionary<string, CombatShare> SharesOf(List<(double Weight, SimResult Result)> fights, IEnumerable<string> placed)
        {
            var keys = placed.ToList();
            if (fights.Count == 0 || keys.Count == 0) return [];
            static string Owner(string k) => k.IndexOf('·') is int i and > 0 ? k[..i] : k;
            Dictionary<string, double> Sum(Func<SimResult, IReadOnlyDictionary<string, double>> of)
            {
                var sum = keys.ToDictionary(k => k, _ => 0.0);
                foreach (var (w, r) in fights)
                    foreach (var (k, v) in of(r))
                        if (sum.ContainsKey(Owner(k))) sum[Owner(k)] += w * v;
                return sum;
            }
            var dealt = Sum(r => r.Damage);
            var taken = Sum(r => r.Taken);
            var healed = Sum(r => r.Healed);
            double Share(Dictionary<string, double> d, string k) => d.Values.Sum() is double t and > 0 ? d[k] / t : 0;
            return keys.ToDictionary(k => k, k => new CombatShare(Share(dealt, k), Share(taken, k), Share(healed, k)));
        }

        /// <summary>
        /// 转职 worth buying for this placement: covenants a 转职 item can give that are one operator short of activating,
        /// each tried on the field operators that lack it (the plan's own score, without the fight), best holder kept.
        /// Operators the player keeps on the field are preferred as holders: a 转职 leaves the field with its holder.
        /// </summary>
        private List<TransformHint> TransformHints(Dictionary<string, (BoardCell Cell, Facing Facing)> pl)
        {
            if (_options.TransformableCovenants.Count == 0 || pl.Count == 0) return [];
            var cov = Covenants(pl.Keys);
            double baseScore = Evaluate(pl, null).Total;
            var hints = new List<TransformHint>();
            foreach (var c in _options.TransformableCovenants.OrderBy(c => c, StringComparer.Ordinal))
            {
                if (cov.Active.Contains(c) || !_owner._covenants.TryGetValue(c, out var ci) || ci.ActivateCount is not int need) continue;
                int have = cov.Counts.GetValueOrDefault(c);
                if (have != need - 1) continue;
                TransformHint? best = null;
                // One 转职 per operator (暴躁铁皮 up to five): someone already holding one would lose it.
                foreach (var n in pl.Keys.Where(n => !Info[n].Covenants.Contains(c)
                    && Info[n].Transforms.Length < (Info[n].Name == LineupEquipmentService.MechName ? 5 : 1)))
                {
                    var old = Info[n];
                    Info[n] = old.WithCovenant(c);
                    _covCache.Clear();
                    double gain;
                    try { gain = Evaluate(pl, null).Total - baseScore; }
                    finally { Info[n] = old; _covCache.Clear(); }
                    // Keepers first: they stay on the field, so the 转职 keeps counting.
                    double rank = gain + (_options.FieldPins.Contains(n) || _options.Pins.ContainsKey(n) ? 1 : 0) + 0.1 * old.Tier;
                    if (best is null || rank > best.Gain + (_options.FieldPins.Contains(best.Holder) || _options.Pins.ContainsKey(best.Holder) ? 1 : 0) + 0.1 * Info[best.Holder].Tier)
                        best = new TransformHint(c, have, need, n, Math.Round(gain, 1));
                }
                if (best is { Gain: > 0 }) hints.Add(best);
            }
            return [.. hints.OrderByDescending(h => h.Gain).Take(3)];
        }

        /// <summary>Which real enemies the simulation used: the run's types, all of them when not read yet, or the boss.</summary>
        private string StreamBasis()
        {
            if (_streams is null) return "";
            var types = _streams.Select(s => s.Stream.Type).Distinct().ToList();
            if (types.SequenceEqual(["BOSS"])) return " · 按本回合首领关出怪";
            if (types.Count >= GarrisonSpawnStore.TypeNames.Count) return " · 按全部 9 类敌人平均（还没读到本局敌人）";
            return " · 按本局敌人：" + string.Join("、", types.Select(t => GarrisonSpawnStore.TypeNames.GetValueOrDefault(t, t)));
        }

        /// <summary>List order: lower route front to back, then upper route, then everyone else.</summary>
        private (int, int, int) Order(string n, BoardCell cell)
        {
            for (int k = Board.Routes.Length - 1; k >= 0; k--)
                if (Board.RouteIndex[k].TryGetValue(cell, out int j))
                    return (Board.Routes.Length - 1 - k, -j, 0);
            return (Board.Routes.Length, Info[n].Melee ? 0 : 1, -cell.Col);
        }
    }
}
