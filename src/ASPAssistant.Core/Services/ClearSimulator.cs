// Added for ASP Assistant Regulation on 2026-11-14; see NOTICE.
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

/// <summary>A kind of enemy in the simulated stream (speed in cells per second, range in cells; 0 = melee).</summary>
public sealed record EnemyProfile(double Hp, double Def, double Res, double Atk, double Interval, double Speed)
{
    public string Name { get; init; } = "";
    /// <summary>Attack radius in cells; ranged enemies shoot operators in reach while they walk.</summary>
    public double Range { get; init; }
    /// <summary>Its attacks deal arts damage (reduced by the operator's arts resistance).</summary>
    public bool Arts { get; init; }
    public string Rank { get; init; } = "NORMAL";
    /// <summary>Protection-point HP lost when it gets through.</summary>
    public int LifePoints { get; init; } = 1;
    /// <summary>Weight (massLevel); 3 or more sinks twice as fast in 沼泽地段.</summary>
    public int Weight { get; init; } = 1;
    /// <summary>Its special mechanics (none for the generic stream).</summary>
    public EnemyTraits Traits { get; init; } = EnemyTraits.None;

    /// <summary>
    /// The enemy <paramref name="factor"/> times as tough: HP × factor and attack × √factor (harder enemies also hit
    /// harder, or blockers and healers would never matter); what it spawns on death too.
    /// </summary>
    public EnemyProfile Tougher(double factor) => this with
    {
        Hp = Hp * factor,
        Atk = Atk * Math.Sqrt(factor),
        Traits = Traits.OnDeath.Count == 0 ? Traits
            : Traits with { OnDeath = [.. Traits.OnDeath.Select(d => d with { Profile = d.Profile.Tougher(factor) })] },
    };
}

public enum ElementKind { None, Burning, Apoptosis, Neural, Erosion }

/// <summary>
/// An enemy's special mechanics, read from its talents (garrison_spawns.json enemies.traits). Rules from the game's
/// terms: 隐匿 = cannot be targeted until blocked; 折射 = +70 arts resistance (on for the whole fight here); element
/// injury on operators bursts at 1000 (神经: 1000 true damage + 10 s stun; 侵蚀: −100 defence + 800 physical; 灼燃:
/// 1200 arts and −20 resistance for 10 s; 凋亡: no skills and 100 arts damage per second for 15 s).
/// </summary>
public sealed record EnemyTraits
{
    public static readonly EnemyTraits None = new();
    public bool Invisible { get; init; }
    public double RefractionRes { get; init; }
    /// <summary>Hits it shrugs off whatever their damage (damage-count shield).</summary>
    public int Shield { get; init; }
    /// <summary>On death the enemies around it get this many shield hits.</summary>
    public int DeathShieldAura { get; init; }
    /// <summary>Every fall: an ember (unblockable, still) that needs this many hits, or it comes back at full HP (and falls into an ember again).</summary>
    public int EmberHits { get; init; }
    public double EmberSeconds { get; init; }
    /// <summary>First death: untargetable for this long, then back at full HP.</summary>
    public double RebornSeconds { get; init; }
    public IReadOnlyList<DeathSpawn> OnDeath { get; init; } = [];
    public ElementKind Element { get; init; }
    /// <summary>Element injury per hit as a share of its attack.</summary>
    public double ElementRatio { get; init; }
    public double BleedDps { get; init; }
    public double BleedSeconds { get; init; }
    /// <summary>A cloud left on death: true damage per second (or attack share) to operators within range.</summary>
    public double CloudDps { get; init; }
    public double CloudAtkScale { get; init; }
    public double CloudRange { get; init; }
    public double CloudSeconds { get; init; }
    /// <summary>The cloud lands on whoever killed it rather than where it fell.</summary>
    public bool CloudAtSource { get; init; }
    /// <summary>First hit on each operator × this, and that operator cannot be healed for <see cref="CritNoHeal"/> s.</summary>
    public double CritScale { get; init; } = 1;
    public double CritNoHeal { get; init; }
    /// <summary>Defence / arts resistance it gives the enemies within <see cref="AuraRange"/>.</summary>
    public double AuraDef { get; init; }
    public double AuraRes { get; init; }
    public double AuraRange { get; init; }
    /// <summary>Attack-speed multiplier on operators within <see cref="SlowRange"/> (0.5 = half as fast).</summary>
    public double SlowFactor { get; init; } = 1;
    public double SlowRange { get; init; }
    /// <summary>Damage it takes while not blocked (0.6 = 40 % less).</summary>
    public double UnblockedTaken { get; init; } = 1;
    /// <summary>Block slots it takes, and the least block count that can stop it.</summary>
    public int BlockCost { get; init; } = 1;
    public int MinBlock { get; init; }
    /// <summary>HP regenerated per second.</summary>
    public double Regen { get; init; }
    /// <summary>
    /// Every this many attacks the hit also stuns for <see cref="StunSeconds"/>: its target, or every operator within
    /// <see cref="StunRadius"/> of the enemy.
    /// </summary>
    public int StunEvery { get; init; }
    public double StunSeconds { get; init; }
    public double StunRadius { get; init; }
    /// <summary>
    /// Its hits also land (× <see cref="SplashScale"/>) on the operators within this radius of the target: on every
    /// attack, every <see cref="SplashEvery"/>-th, or only the first (<see cref="SplashFirst"/>).
    /// </summary>
    public double SplashRadius { get; init; }
    public int SplashEvery { get; init; } = 1;
    public bool SplashFirst { get; init; }
    public double SplashScale { get; init; } = 1;
    /// <summary>Each hit jumps on to the nearest operator until this many are hit, × <see cref="ChainScale"/> per jump.</summary>
    public int ChainTargets { get; init; }
    public double ChainScale { get; init; } = 1;
    /// <summary>Every <see cref="LineCooldown"/> s an attack sweeps the line through its target (× <see cref="LineScale"/> each).</summary>
    public double LineCooldown { get; init; }
    public double LineScale { get; init; }
}

/// <summary>Enemies that appear where one died (after <see cref="Delay"/> s).</summary>
public sealed record DeathSpawn(EnemyProfile Profile, int Count, double Delay);

/// <summary>One enemy of a real stream: when it appears, at which gate, flying or not, and what it is.</summary>
public sealed record SimSpawn(double Time, bool Upper, bool Flying, EnemyProfile Profile);

/// <summary>
/// A steady stream of enemies: <see cref="GroundCount"/> walkers and <see cref="FlyCount"/> flyers spread evenly
/// over <see cref="SpawnSeconds"/>, then <see cref="TailSeconds"/> to finish them.
/// </summary>
public sealed record WaveScenario
{
    public EnemyProfile Ground { get; init; } = new(4000, 250, 20, 450, 1.6, 0.85);
    public EnemyProfile Flying { get; init; } = new(2200, 120, 20, 0, 0, 1.0);
    public int GroundCount { get; init; } = 24;
    public int FlyCount { get; init; }
    public double SpawnSeconds { get; init; } = 45;
    public double TailSeconds { get; init; } = 25;
    public double Step { get; init; } = 0.2;
    /// <summary>A real stream (round data): replaces the counts and the two profiles above when set.</summary>
    public IReadOnlyList<SimSpawn>? Spawns { get; init; }

    /// <summary>
    /// The same stream with enemies <paramref name="factor"/> times as tough: HP × factor and attack × √factor
    /// (harder enemies also hit harder, or blockers and healers would never matter).
    /// </summary>
    public WaveScenario Tougher(double factor) => this with
    {
        Ground = Ground with { Hp = Ground.Hp * factor, Atk = Ground.Atk * Math.Sqrt(factor) },
        Flying = Flying with { Hp = Flying.Hp * factor, Atk = Flying.Atk * Math.Sqrt(factor) },
        Spawns = Spawns?.Select(s => s with { Profile = s.Profile.Tougher(factor) }).ToList(),
    };
}

/// <summary>How a skill gains SP: one per second, per attack, or per hit taken.</summary>
public enum SkillCharge { Auto, Attack, Hit }

/// <summary>What damage a skill's hits deal while it runs: the operator's own type, arts, or true damage.</summary>
public enum SkillDamage { Same, Arts, True }

/// <summary>
/// A skill on its SP cycle: SP from <see cref="Init"/> up to <see cref="Cost"/>, then attack (or healing) × <see cref="Gain"/>
/// for <see cref="Duration"/> s (0: one strong action). Auto skills wait for a target; no SP is gained while one runs.
/// While it runs the operator uses its skill range (<see cref="SimUnit.SkillRange"/>) and the effects below.
/// </summary>
public sealed record SimSkill(double Gain, double Cost, double Init, double Duration, SkillCharge Charge)
{
    /// <summary>Enemies hit per attack (99: every enemy in range).</summary>
    public int Targets { get; init; } = 1;
    /// <summary>Strikes per skill action ("持续进行五次").</summary>
    public int Hits { get; init; } = 1;
    /// <summary>Hits every enemy it blocks.</summary>
    public bool HitsBlocked { get; init; }
    public SkillDamage Damage { get; init; }
    /// <summary>Arts resistance its hits ignore.</summary>
    public double ResIgnore { get; init; }
    /// <summary>Enemies in range: defence × this and arts resistance + <see cref="EnemyResAdd"/> (a debuff).</summary>
    public double EnemyDefScale { get; init; } = 1;
    public double EnemyResAdd { get; init; }
    /// <summary>On cast, allies in range healed by this × attack; while it runs, this × attack per second.</summary>
    public double HealBurst { get; init; }
    public double HealPerSecond { get; init; }
    public bool Debuffs => EnemyDefScale < 1 || EnemyResAdd < 0;
    /// <summary>Its hits stun (no moving or attacking) or stop (no moving) enemies for this long, at this chance.</summary>
    public bool ControlStuns { get; init; }
    public double ControlSeconds { get; init; }
    public double ControlChance { get; init; } = 1;
    /// <summary>What it gives itself while it runs: defence × (1 + this), max HP × (1 + ratio) or + flat, block + this.</summary>
    public double SelfDef { get; init; }
    public double SelfHp { get; init; }
    public int SelfBlock { get; init; }
    /// <summary>While it runs: damage per second (× attack, arts) to every ground enemy in reach, and their slow.</summary>
    public double Zone { get; init; }
    public double Slow { get; init; }
    /// <summary>Its hits push enemies away or drag them towards the operator.</summary>
    public PushData? Push { get; init; }
}

/// <summary>A summon's time on the field: first appears at <see cref="Appear"/> s, stays <see cref="Duration"/> s
/// (0: until beaten), comes back <see cref="Cooldown"/> s after it leaves or falls.</summary>
public sealed record SummonTiming(double Appear, double Duration, double Cooldown);

/// <summary>
/// How an operator's normal attack lands (its branch's trait): on one target, on several, on everything it blocks,
/// splashing around the target, jumping between enemies, or twice per attack; healers healing several at once;
/// operators that only act through their skill; or a healing aura instead of attacks.
/// </summary>
public sealed record AttackPattern
{
    public static readonly AttackPattern Single = new();
    /// <summary>Enemies hit per attack (99: every enemy in range).</summary>
    public int Targets { get; init; } = 1;
    public bool HitsBlocked { get; init; }
    public double SplashRadius { get; init; }
    /// <summary>Hits jump on to this many enemies in all (× 0.85 per jump).</summary>
    public int ChainTargets { get; init; }
    public int HitsPerAttack { get; init; } = 1;
    public int HealTargets { get; init; } = 1;
    /// <summary>Does not attack unless its skill runs (阵法术师).</summary>
    public bool SkillOnly { get; init; }
    /// <summary>No attacks; heals everyone in range by this × attack per second (吟游者).</summary>
    public double HealAura { get; init; }
    /// <summary>Attacks normally, heals instead while its skill runs (护佑者).</summary>
    public bool HealsDuringSkill { get; init; }
}

/// <summary>An operator in the simulation: where it stands, what it reaches and its combat numbers.</summary>
public sealed record SimUnit(
    string Key, BoardCell Cell, IReadOnlyCollection<BoardCell> Range, bool CanBlock, int Block, bool AntiAir,
    double Atk, double Interval, bool Arts, double Hp, double Def, bool Healer)
{
    /// <summary>Arts resistance (0–100).</summary>
    public double Res { get; init; }
    /// <summary>How much of <see cref="Atk"/> comes from the skill (lost while 凋亡 locks skills).</summary>
    public double SkillFactor { get; init; } = 1;
    /// <summary>
    /// The carried skill on its SP cycle; <see cref="Atk"/> ÷ <see cref="SkillFactor"/> is then the attack without it.
    /// Null: <see cref="Atk"/> is the skill's average over a fight.
    /// </summary>
    public SimSkill? Skill { get; init; }
    /// <summary>Enemies in its range lose invisibility (奥术 with 3 different 奥术 operators on the field).</summary>
    public bool Reveals { get; init; }
    /// <summary>Casting its skill strips invisibility from the enemies in its range for this long (凛御银灰).</summary>
    public double SkillRevealSeconds { get; init; }
    /// <summary>The cells it reaches while its skill runs (null: its normal range).</summary>
    public IReadOnlyCollection<BoardCell>? SkillRange { get; init; }
    public AttackPattern Pattern { get; init; } = AttackPattern.Single;

    // Talents (data/talent_effects.py), already scaled by how often they hold.
    /// <summary>Defence ignored: flat, then a share; arts resistance ignored (flat).</summary>
    public double DefIgnore { get; init; }
    public double DefIgnoreRatio { get; init; }
    public double ResIgnore { get; init; }
    /// <summary>Damage dealt × this.</summary>
    public double DamageScale { get; init; } = 1;
    /// <summary>Extra arts damage per hit, × the hit's attack.</summary>
    public double ExtraArts { get; init; }
    /// <summary>Poison its hits leave: arts damage per second for <see cref="DotSeconds"/>.</summary>
    public double DotDps { get; init; }
    public double DotSeconds { get; init; }
    /// <summary>Its hits stun (or stop) enemies for this long, at this chance.</summary>
    public bool HitStuns { get; init; }
    public double HitControlSeconds { get; init; }
    public double HitControlChance { get; init; }
    /// <summary>Enemies in its range take this much more damage and move this much slower.</summary>
    public double Fragile { get; init; }
    public double EnemySlow { get; init; }
    /// <summary>Damage it takes × (1 − this); share of physical hits dodged.</summary>
    public double Sanctuary { get; init; }
    public double Dodge { get; init; }
    /// <summary>HP it regains per second; SP it gains per second on top of its skill's charging.</summary>
    public double Regen { get; init; }
    public double SpPerSecond { get; init; }
    /// <summary>Its normal attacks push (or drag) their target.</summary>
    public PushData? Push { get; init; }
    /// <summary>A summon: when it is on the field (null: an operator, there from the start).</summary>
    public SummonTiming? Summon { get; init; }
}

/// <summary>An enemy path: points in grid coordinates (cell centres at +0.5), spawn → defence point.</summary>
public sealed record SimLane(IReadOnlyList<(double Col, double Row)> Points, double Weight, bool Flying)
{
    /// <summary>Starts at the upper gate (spawn row 0–1) rather than the lower one.</summary>
    public bool Upper => Points.Count > 0 && Points[0].Row < 2;
}

public sealed record SimResult(
    int Spawned, int Killed, int Leaked, int Alive,
    IReadOnlyDictionary<string, int> Kills, IReadOnlyDictionary<string, double> Damage,
    IReadOnlyList<string> Fallen, IReadOnlyDictionary<int, int> LeaksByLane)
{
    /// <summary>Enemies that got through, by name (what the line could not handle).</summary>
    public IReadOnlyDictionary<string, int> LeakedByName { get; init; } = new Dictionary<string, int>();
    /// <summary>Skills cast per operator (only for operators given a <see cref="SimUnit.Skill"/>).</summary>
    public IReadOnlyDictionary<string, int> Casts { get; init; } = new Dictionary<string, int>();
    /// <summary>Per operator: stuns (enemies or operators) in the eight cells around it.</summary>
    public IReadOnlyDictionary<string, int> StunsAround { get; init; } = new Dictionary<string, int>();
    /// <summary>Per operator: enemies that fell inside its attack range (whoever killed them).</summary>
    public IReadOnlyDictionary<string, int> KillsInRange { get; init; } = new Dictionary<string, int>();
    /// <summary>Per operator (summons apart): damage it took, and HP it restored to others or itself through healing.</summary>
    public IReadOnlyDictionary<string, double> Taken { get; init; } = new Dictionary<string, double>();
    public IReadOnlyDictionary<string, double> Healed { get; init; } = new Dictionary<string, double>();
    /// <summary>Bosses (rank BOSS) in the stream.</summary>
    public int Bosses { get; init; }
    /// <summary>
    /// How far the bosses were beaten, summed: 1 per boss killed, otherwise the share of its HP (all its lives)
    /// taken off before it got through or the fight ended.
    /// </summary>
    public double BossProgress { get; init; }
    /// <summary>Bosses killed (also counted in <see cref="Killed"/>).</summary>
    public int BossesKilled { get; init; }
    /// <summary>
    /// What a boss is worth in enemies handled: the round is about the boss, so it counts as much as everything else
    /// in its stream put together (at least <see cref="MinBossWorth"/>).
    /// </summary>
    public double BossWorth => Bosses == 0 ? 0 : Math.Max(MinBossWorth, (double)(Spawned - Bosses) / Bosses);
    public const double MinBossWorth = 8;
    /// <summary>
    /// Enemies handled with the bosses at their worth: kills of the others, plus each boss's worth times how far
    /// it was beaten (so damage on a boss that survives still counts). Equals <see cref="Killed"/> without bosses.
    /// </summary>
    public double Handled => Killed - BossesKilled + BossWorth * BossProgress;
    /// <summary>The most <see cref="Handled"/> can be.</summary>
    public double Worth => Spawned - Bosses + BossWorth * Bosses;
    public double KillRate => Spawned == 0 ? 1 : (double)Killed / Spawned;

    /// <summary>The weighted average of several fights (counts rounded; fallen = down in at least half of them).</summary>
    public static SimResult Average(IEnumerable<(SimResult Result, double Weight)> runs)
    {
        var list = runs.ToList();
        double total = list.Sum(r => r.Weight);
        if (list.Count == 0 || total <= 0) return new SimResult(0, 0, 0, 0, new Dictionary<string, int>(), new Dictionary<string, double>(), [], new Dictionary<int, int>());
        int Avg(Func<SimResult, double> f) => (int)Math.Round(list.Sum(r => r.Weight * f(r.Result)) / total);
        Dictionary<TKey, double> Sum<TKey>(Func<SimResult, IEnumerable<KeyValuePair<TKey, double>>> f) where TKey : notnull =>
            list.SelectMany(r => f(r.Result).Select(kv => (kv.Key, V: r.Weight * kv.Value / total)))
                .GroupBy(x => x.Key).ToDictionary(g => g.Key, g => g.Sum(x => x.V));
        var kills = Sum(r => r.Kills.Select(kv => new KeyValuePair<string, double>(kv.Key, kv.Value)));
        var leaks = Sum(r => r.LeaksByLane.Select(kv => new KeyValuePair<int, double>(kv.Key, kv.Value)));
        var names = Sum(r => r.LeakedByName.Select(kv => new KeyValuePair<string, double>(kv.Key, kv.Value)));
        var fallen = list.SelectMany(r => r.Result.Fallen.Select(k => (k, r.Weight / total))).GroupBy(x => x.k)
            .Where(g => g.Sum(x => x.Item2) >= 0.5).Select(g => g.Key).ToList();
        return new SimResult(Avg(r => r.Spawned), Avg(r => r.Killed), Avg(r => r.Leaked), Avg(r => r.Alive),
            kills.Where(kv => kv.Value >= 0.5).ToDictionary(kv => kv.Key, kv => (int)Math.Round(kv.Value)),
            Sum(r => r.Damage), fallen,
            leaks.Where(kv => kv.Value >= 0.5).ToDictionary(kv => kv.Key, kv => (int)Math.Round(kv.Value)))
        {
            LeakedByName = names.Where(kv => kv.Value >= 0.5).ToDictionary(kv => kv.Key, kv => (int)Math.Round(kv.Value)),
        };
    }
}

/// <summary>
/// Time-step simulation of an enemy stream against a placement: walkers follow their lane and stop at the first
/// operator with free block capacity on their cell, fight it, and leak when they reach the end; flyers are never
/// blocked and only anti-air operators reach them. Operators attack every <see cref="SimUnit.Interval"/> (enemies
/// they block first, then the one furthest along), healers heal the most hurt ally in range. The enemies' special
/// mechanics (<see cref="EnemyTraits"/>) are played out: invisibility, refraction, damage-count shields, embers and
/// rebirth, death spawns, element injury with its bursts, bleeding, death clouds, first-hit crits, auras, block
/// requirements, regeneration, stuns every few attacks, splash, chains and line attacks. Operators' skills run on
/// their SP cycle when given (<see cref="SimUnit.Skill"/>), and the map's special tiles (<see cref="TerrainKind"/>)
/// act on whoever stands on or walks over them.
/// </summary>
public static class ClearSimulator
{
    private const double BurstAt = 1000;
    private const double ElementCooldown = 10;
    // Tiles (level blackboards): 活性源石 attack +20 %, attack speed +20, 70 HP lost per second (the rate is assumed);
    // 优化模块 operators' attack speed +25, enemies' move speed × 1.5. 沼泽地段 (陷入沼泽): each trigger adds a stack of
    // −5 % attack speed (enemies also −5 % move speed; weight ≥ 3 adds two stacks), up to 10. Assumed: it triggers on
    // stepping in and every second on it, and the stacks stay.
    private const double InfectionAtk = 1.2, InfectionSpeed = 1.2, InfectionDps = 70;
    private const double SpeedupSpeed = 1.25, SpeedupMove = 1.5;
    private const double MireStack = 0.05, MireInterval = 1;
    private const int MireMaxStacks = 10, MireHeavy = 3;

    private sealed class Enemy
    {
        public required EnemyProfile Profile;
        public int Lane;
        public double S;
        public double Hp;
        public double MaxHp;
        public int BlockedBy = -1;
        public double Cooldown;
        public bool Flying;
        public bool Done;
        public int Shield;
        public bool Ember, UsedReborn;
        public int EmberHits;
        public double StateTimer;   // ember / rebirth countdown
        public bool Reviving;
        public double X, Y;
        public BoardCell Cell;      // the cell under it (kept with X, Y)
        public int CellBit = -1;
        public double AuraDef, AuraRes;
        public double DefScale = 1, ResAdd;   // skill debuffs on it this step
        public double StunUntil = -1, StopUntil = -1;   // controlled by an operator's skill
        public HashSet<int>? Struck; // operators already hit once (first-hit crits)
        public int Attacks;
        public bool Infected;       // walked over 活性源石: stronger, losing HP
        public TerrainKind Tile;
        public double LineReady;
        public double RevealedUntil = -1;
        public int MireStacks;
        public double MireNext = double.NegativeInfinity;   // next 沼泽 trigger while on it
        public bool Killed;
        public int LivesLost;       // embers and rebirths it went through
        public double Fragile, SlowBy;   // from operators' talents and zones this step
        public double PoisonDps, PoisonUntil = -1;
        public int PoisonBy = -1;
        public double PushedAt = double.NegativeInfinity;
        public double Mire => 1 - MireStack * MireStacks;
        public double Atk => Profile.Atk * (Infected ? InfectionAtk : 1);
        public EnemyTraits T => Profile.Traits;
        public bool Still => Ember || Reviving;
    }

    private sealed class Unit
    {
        public required SimUnit Spec;
        public required HashSet<BoardCell> Range;
        public UInt128 Mask;        // Range as cell bits
        public HashSet<BoardCell> SkillRange = [];
        public UInt128 SkillMask;
        public int[] SkillAllies = [];  // allies in the skill range (its healing)
        public double HealUntil = -1;
        public int[] Patients = []; // healers: the allies in range
        public double Hp;
        public double Cooldown;
        public int Blocking;
        public bool Down;
        public int Kills;
        public int Casts, StunsSeen, KillsSeen;
        public double MaxHp;
        public int BlockCap;
        public bool BuffOn;          // the skill's self buffs applied
        public double Damage, Taken, Healed;
        public double StunUntil = -1, NoHealUntil = -1, SkillLockUntil = -1, BurnUntil = -1, ApoptosisUntil = -1;
        public double BleedDps, BleedUntil = -1;
        public double DefLoss;
        public double Slow = 1;
        public readonly double[] Gauge = new double[5];
        public readonly double[] GaugeReady = new double[5];
        public TerrainKind Tile;
        public double Sp, ActiveUntil = -1;
        public bool Empowered;
        public double NextUp = double.PositiveInfinity, UpUntil = double.PositiveInfinity;   // summons
        public double X => Spec.Cell.Col + 0.5;
        public double Y => Spec.Cell.Row + 0.5;
        public bool Stunned(double t) => StunUntil > t;
        public double Res(double t) => Math.Max(0, Spec.Res - (BurnUntil > t ? 20 : 0));
        public double Def => Math.Max(0, Spec.Def * (BuffOn ? 1 + (Spec.Skill?.SelfDef ?? 0) : 1) - DefLoss);
    }

    private sealed record Cloud(double X, double Y, double Range, double Dps, double Until);

    private sealed class Path
    {
        public required (double X, double Y)[] Points;
        public required double[] Cumulative;
        public double Length => Cumulative[^1];

        public (double X, double Y) At(double s)
        {
            if (s <= 0) return Points[0];
            int i = Array.BinarySearch(Cumulative, s);
            if (i < 0) i = ~i;
            if (i >= Points.Length) return Points[^1];
            if (i == 0) return Points[0];
            double seg = Cumulative[i] - Cumulative[i - 1];
            double t = seg <= 0 ? 0 : (s - Cumulative[i - 1]) / seg;
            return (Points[i - 1].X + (Points[i].X - Points[i - 1].X) * t, Points[i - 1].Y + (Points[i].Y - Points[i - 1].Y) * t);
        }

        /// <summary>The distance along the path of its point nearest to (x, y).</summary>
        public double Nearest(double x, double y)
        {
            double best = double.MaxValue, at = 0;
            for (int i = 1; i < Points.Length; i++)
            {
                var (ax, ay) = Points[i - 1];
                double dx = Points[i].X - ax, dy = Points[i].Y - ay, len2 = dx * dx + dy * dy;
                double f = len2 <= 0 ? 0 : Math.Clamp(((x - ax) * dx + (y - ay) * dy) / len2, 0, 1);
                double px = ax + f * dx - x, py = ay + f * dy - y, d = px * px + py * py;
                if (d < best) { best = d; at = Cumulative[i - 1] + f * (Cumulative[i] - Cumulative[i - 1]); }
            }
            return at;
        }

        public static Path Of(IReadOnlyList<(double Col, double Row)> points)
        {
            var pts = points.Select(p => (p.Col, p.Row)).ToArray();
            var cum = new double[pts.Length];
            for (int i = 1; i < pts.Length; i++)
                cum[i] = cum[i - 1] + Math.Sqrt(Math.Pow(pts[i].Col - pts[i - 1].Col, 2) + Math.Pow(pts[i].Row - pts[i - 1].Row, 2));
            return new Path { Points = pts, Cumulative = cum };
        }
    }

    private const int MaskCol0 = -4, MaskCols = 16, MaskRow0 = -2, MaskRows = 8;

    /// <summary>The cell's bit in a range mask, or −1 outside the masked area.</summary>
    private static int Bit(BoardCell c)
    {
        int col = c.Col - MaskCol0, row = c.Row - MaskRow0;
        return col < 0 || col >= MaskCols || row < 0 || row >= MaskRows ? -1 : row * MaskCols + col;
    }

    private static UInt128 MaskOf(IEnumerable<BoardCell> cells)
    {
        UInt128 m = 0;
        foreach (var c in cells) if (Bit(c) is int b and >= 0) m |= UInt128.One << b;
        return m;
    }

    private static bool Has(UInt128 mask, int bit) => ((mask >> bit) & UInt128.One) != UInt128.Zero;

    private static double Dist(double ax, double ay, double bx, double by) => Math.Sqrt((ax - bx) * (ax - bx) + (ay - by) * (ay - by));

    /// <summary>Fights simulated so far in this process (for profiling the planner).</summary>
    public static long RunCount => Interlocked.Read(ref _runs);
    private static long _runs;

    public static SimResult Run(IReadOnlyList<SimUnit> specs, IReadOnlyList<SimLane> lanes, WaveScenario w,
        IReadOnlyDictionary<BoardCell, TerrainKind>? terrain = null)
    {
        Interlocked.Increment(ref _runs);
        long t0 = System.Diagnostics.Stopwatch.GetTimestamp();
        try { return RunCore(specs, lanes, w, terrain); }
        finally { Interlocked.Add(ref _ticks, System.Diagnostics.Stopwatch.GetTimestamp() - t0); }
    }

    /// <summary>Time spent simulating so far in this process, summed over threads.</summary>
    public static TimeSpan RunTime => TimeSpan.FromSeconds((double)Interlocked.Read(ref _ticks) / System.Diagnostics.Stopwatch.Frequency);
    private static long _ticks;

    private static SimResult RunCore(IReadOnlyList<SimUnit> specs, IReadOnlyList<SimLane> lanes, WaveScenario w,
        IReadOnlyDictionary<BoardCell, TerrainKind>? terrain)
    {
        var paths = lanes.Select(l => Path.Of(l.Points)).ToArray();
        TerrainKind TileAt(BoardCell c) => terrain is not null && terrain.TryGetValue(c, out var k) ? k : TerrainKind.None;
        var units = specs.Select(s => new Unit
        {
            Spec = s, Range = [.. s.Range], Hp = s.Hp, Tile = TileAt(s.Cell), Sp = s.Skill?.Init ?? 0,
        }).ToList();
        foreach (var u in units)
        {
            if (u.Spec.Summon is { } sm) { u.Down = true; u.NextUp = sm.Appear; }
            u.MaxHp = u.Spec.Hp;
            u.BlockCap = u.Spec.Block;
            u.Mask = MaskOf(u.Range);
            u.SkillRange = u.Spec.SkillRange is { } sr ? [.. sr] : u.Range;
            u.SkillMask = MaskOf(u.SkillRange);
            if (u.Spec.Skill is { } hs && (hs.HealBurst > 0 || hs.HealPerSecond > 0) || u.Spec.Pattern.HealAura > 0)
                u.SkillAllies = [.. Enumerable.Range(0, units.Count).Where(j => u.SkillRange.Contains(units[j].Spec.Cell))];
            if (u.Spec.Healer || u.Spec.Pattern.HealsDuringSkill)
                u.Patients = [.. Enumerable.Range(0, units.Count).Where(j => u.Range.Contains(units[j].Spec.Cell))];
        }
        UInt128 revealedMask = 0;
        var sources = new List<Enemy>();
        var hits = new List<(double Priority, Enemy Enemy)>();
        var revealed = new HashSet<BoardCell>();
        bool anyReveal = units.Any(u => u.Spec.Reveals);
        bool anyDebuff = units.Any(u => u.Spec.Skill is { Debuffs: true });
        bool anyHot = units.Any(u => u.Spec.Skill is { HealPerSecond: > 0 } || u.Spec.Pattern.HealAura > 0);
        bool anyField = units.Any(u => u.Spec.Fragile > 0 || u.Spec.EnemySlow > 0 || u.Spec.Skill is { Zone: > 0 } or { Slow: > 0 });
        bool anyRegen = units.Any(u => u.Spec.Regen > 0);
        bool anySummon = units.Any(u => u.Spec.Summon is not null);
        var rng = new Random(20261114);   // control chances: fixed, so a fight plays the same every time
        var byCell = new Dictionary<BoardCell, List<int>>();
        for (int i = 0; i < units.Count; i++)
        {
            if (!units[i].Spec.CanBlock || units[i].Spec.Block + (units[i].Spec.Skill?.SelfBlock ?? 0) <= 0) continue;
            if (!byCell.TryGetValue(units[i].Spec.Cell, out var list)) byCell[units[i].Spec.Cell] = list = [];
            list.Add(i);
        }
        var spawns = Schedule(lanes, w);
        var enemies = new List<Enemy>(spawns.Count);
        var pending = new List<(double Time, int Lane, double S, EnemyProfile Profile)>();
        var clouds = new List<Cloud>();
        var leaksByLane = new Dictionary<int, int>();
        var leakedByName = new Dictionary<string, int>();
        var bosses = new List<Enemy>();
        int next = 0, killed = 0, leaked = 0, spawned = spawns.Count;
        double end = (w.Spawns is { Count: > 0 } real ? real.Max(s => s.Time) : w.SpawnSeconds) + w.TailSeconds;
        bool anyAura = spawns.Any(s => HasAura(s.Profile)), anySlow = spawns.Any(s => s.Profile.Traits.SlowFactor < 1);
        double t = 0;

        Enemy Add(EnemyProfile p, int lane, double s)
        {
            var e = new Enemy
            {
                Profile = p, Lane = lane, S = s, Hp = p.Hp, MaxHp = p.Hp, Flying = lanes[lane].Flying,
                Cooldown = p.Interval, Shield = p.Traits.Shield,
            };
            (e.X, e.Y) = paths[lane].At(s);
            Locate(e);
            enemies.Add(e);
            if (p.Rank == "BOSS") bosses.Add(e);
            return e;
        }

        static void Locate(Enemy e)
        {
            e.Cell = new BoardCell((int)Math.Floor(e.X), (int)Math.Floor(e.Y));
            e.CellBit = Bit(e.Cell);
        }

        void Release(Enemy e)
        {
            if (e.BlockedBy >= 0) units[e.BlockedBy].Blocking = Math.Max(0, units[e.BlockedBy].Blocking - e.T.BlockCost);
            e.BlockedBy = -1;
        }

        void Fall(Unit u, int index)
        {
            u.Down = true;
            if (u.Spec.Summon is { } sm) { u.NextUp = t + sm.Cooldown; u.UpUntil = double.PositiveInfinity; }
            u.Blocking = 0;
            foreach (var e in enemies)
                if (e.BlockedBy == index) e.BlockedBy = -1;
        }

        // A stun at (x, y): every operator whose eight surrounding cells include it saw one.
        void SawStun(double x, double y)
        {
            int col = (int)Math.Floor(x), row = (int)Math.Floor(y);
            foreach (var o in units)
                if (!o.Down && Math.Abs(o.Spec.Cell.Col - col) <= 1 && Math.Abs(o.Spec.Cell.Row - row) <= 1) o.StunsSeen++;
        }

        void Stun(Unit u, int index, double seconds)
        {
            SawStun(u.X, u.Y);
            u.StunUntil = Math.Max(u.StunUntil, t + seconds);
            u.Blocking = 0;
            foreach (var e in enemies)
                if (e.BlockedBy == index) e.BlockedBy = -1;
        }

        void Hurt(Unit u, int index, double damage)
        {
            u.Taken += Math.Max(0, Math.Min(damage, u.Hp));
            u.Hp -= damage;
            if (u.Hp <= 0 && !u.Down) Fall(u, index);
        }

        void Burst(Unit u, int index, ElementKind kind)
        {
            switch (kind)
            {
                case ElementKind.Neural: Hurt(u, index, 1000); if (!u.Down) Stun(u, index, 10); break;
                case ElementKind.Erosion: u.DefLoss += 100; Hurt(u, index, Math.Max(800 - u.Def, 40)); break;
                case ElementKind.Burning: u.BurnUntil = t + 10; Hurt(u, index, 1200 * (1 - u.Res(t) / 100)); break;
                case ElementKind.Apoptosis: u.SkillLockUntil = t + 15; u.ApoptosisUntil = t + 15; break;
            }
        }

        // One enemy hit on an operator, with its crit, element injury and bleeding.
        void Strike(Enemy e, int index, double scale = 1)
        {
            var u = units[index];
            var p = e.Profile;
            double atk = e.Atk * scale;
            double dmg = p.Arts ? Math.Max(atk * (1 - u.Res(t) / 100), 0.05 * atk) : Math.Max(atk - u.Def, 0.05 * atk);
            if (!p.Arts) dmg *= 1 - u.Spec.Dodge;
            dmg *= 1 - u.Spec.Sanctuary;
            if (u.Spec.Skill is { Charge: SkillCharge.Hit } hs && u.ActiveUntil <= t && !u.Empowered) u.Sp = Math.Min(hs.Cost, u.Sp + 1);
            if (e.T.CritScale > 1 && (e.Struck ??= []).Add(index))
            {
                dmg *= e.T.CritScale;
                u.NoHealUntil = Math.Max(u.NoHealUntil, t + e.T.CritNoHeal);
            }
            Hurt(u, index, dmg);
            if (u.Down) return;
            if (e.T.Element != ElementKind.None && e.T.ElementRatio > 0)
            {
                int k = (int)e.T.Element;
                if (u.GaugeReady[k] <= t)
                {
                    u.Gauge[k] += atk * e.T.ElementRatio;
                    if (u.Gauge[k] >= BurstAt)
                    {
                        u.Gauge[k] = 0;
                        u.GaugeReady[k] = t + ElementCooldown;
                        Burst(u, index, e.T.Element);
                    }
                }
            }
            if (e.T.BleedDps > 0)
            {
                u.BleedDps = Math.Max(u.BleedDps, e.T.BleedDps);
                u.BleedUntil = t + e.T.BleedSeconds;
            }
        }

        // One enemy attack on an operator: the hit, then its splash, chain and stun; or, when ready, its line sweep.
        void Attack(Enemy e, int index)
        {
            var T = e.T;
            e.Attacks++;
            var target = units[index];
            double tx = target.X, ty = target.Y;
            if (T.LineCooldown > 0 && e.LineReady <= t)
            {
                e.LineReady = t + T.LineCooldown;
                double dx = tx - e.X, dy = ty - e.Y, len = Math.Sqrt(dx * dx + dy * dy);
                if (len < 1e-6) { dx = -1; dy = 0; len = 1; }  // on its blocker's tile: towards the defence point
                dx /= len; dy /= len;
                for (int j = 0; j < units.Count; j++)
                {
                    if (units[j].Down) continue;
                    double px = units[j].X - e.X, py = units[j].Y - e.Y;
                    double along = px * dx + py * dy, off = Math.Abs(px * dy - py * dx);
                    if (along >= -0.5 && along <= 4 && off <= 0.5) Strike(e, j, T.LineScale);
                }
                return;
            }
            Strike(e, index);
            if (T.SplashRadius > 0 && (T.SplashFirst ? e.Attacks == 1 : T.SplashEvery <= 1 || e.Attacks % T.SplashEvery == 0))
                for (int j = 0; j < units.Count; j++)
                    if (j != index && !units[j].Down && Dist(units[j].X, units[j].Y, tx, ty) <= T.SplashRadius)
                        Strike(e, j, T.SplashScale);
            if (T.ChainTargets > 1)
            {
                var hit = new HashSet<int> { index };
                double scale = 1, cx = tx, cy = ty;
                for (int k = 1; k < T.ChainTargets; k++)
                {
                    int jump = -1;
                    double best = 1.6;
                    for (int j = 0; j < units.Count; j++)
                    {
                        if (units[j].Down || hit.Contains(j)) continue;
                        double d = Dist(units[j].X, units[j].Y, cx, cy);
                        if (d <= best) { best = d; jump = j; }
                    }
                    if (jump < 0) break;
                    hit.Add(jump);
                    scale *= T.ChainScale;
                    Strike(e, jump, scale);
                    (cx, cy) = (units[jump].X, units[jump].Y);
                }
            }
            if (T.StunEvery > 0 && e.Attacks % T.StunEvery == 0)
            {
                if (T.StunRadius > 0)
                {
                    for (int j = 0; j < units.Count; j++)
                        if (!units[j].Down && Dist(units[j].X, units[j].Y, e.X, e.Y) <= T.StunRadius) Stun(units[j], j, T.StunSeconds);
                }
                else if (!target.Down) Stun(target, index, T.StunSeconds);
            }
        }

        // While a skill runs (or a strong action is armed) the operator reaches its skill range.
        bool SkillOn(Unit u) => u.ActiveUntil > t || u.Empowered;
        bool InRange(Unit u, Enemy e)
        {
            bool on = SkillOn(u);
            return e.CellBit >= 0 ? Has(on ? u.SkillMask : u.Mask, e.CellBit) : (on ? u.SkillRange : u.Range).Contains(e.Cell);
        }
        bool Revealed(Enemy e) => e.CellBit >= 0 ? Has(revealedMask, e.CellBit) : revealed.Contains(e.Cell);

        // Skills: ready once SP reaches the cost; casting starts the boosted window (or arms one strong action).
        bool Ready(Unit u) => u.Spec.Skill is { } sk && u.ActiveUntil <= t && !u.Empowered && u.Sp >= sk.Cost && u.SkillLockUntil <= t;

        void Cast(Unit u)
        {
            var sk = u.Spec.Skill!;
            u.Sp = 0;
            u.Casts++;
            if (sk.Duration > 0) u.ActiveUntil = t + sk.Duration; else u.Empowered = true;
            double baseAtk = u.Spec.Atk / Math.Max(1e-9, u.Spec.SkillFactor);
            if (sk.HealBurst > 0)
                foreach (int j in u.SkillAllies)
                    if (!units[j].Down) Heal(u, units[j], sk.HealBurst * baseAtk);
            if (sk.HealPerSecond > 0) u.HealUntil = t + (sk.Duration > 0 ? sk.Duration : 10);
            if (u.Spec.SkillRevealSeconds > 0)
                foreach (var e in enemies)
                    if (!e.Done && InRange(u, e)) e.RevealedUntil = t + u.Spec.SkillRevealSeconds;
        }

        // The skill's multiplier on this action (casting first if ready); attack-charged skills gain SP otherwise.
        // skillHit: this action is a skill action (the one that casts included), so the skill's effects apply.
        double SkillBoost(Unit u, out bool skillHit)
        {
            skillHit = false;
            if (u.Spec.Skill is not { } sk) return 1;
            if (Ready(u)) Cast(u);
            skillHit = true;
            if (u.ActiveUntil > t) return sk.Gain;
            if (u.Empowered) { u.Empowered = false; return sk.Gain; }
            skillHit = false;
            if (sk.Charge == SkillCharge.Attack) u.Sp = Math.Min(sk.Cost, u.Sp + 1);
            return 1;
        }

        // One operator hit on an enemy: embers take hits, shields absorb them, else damage by type.
        void Hit(Unit u, int i, Enemy target, double atk, bool skillHit)
        {
            if (target.Done) return;
            if (target.Ember)
            {
                // An ember takes a number of hits, whatever their damage.
                if (--target.EmberHits <= 0) { target.Ember = false; Die(target, i, final: true); }
                return;
            }
            if (target.Shield > 0) { target.Shield--; return; }
            var p = target.Profile;
            var sk = skillHit ? u.Spec.Skill : null;
            var kind = sk is { Damage: not SkillDamage.Same } ? sk.Damage : u.Spec.Arts ? SkillDamage.Arts : SkillDamage.Same;
            double dmg;
            if (kind == SkillDamage.True) dmg = atk;
            else if (kind == SkillDamage.Arts)
            {
                double res = Math.Clamp(p.Res + target.T.RefractionRes + target.AuraRes + target.ResAdd - (sk?.ResIgnore ?? 0) - u.Spec.ResIgnore, 0, 100);
                dmg = Math.Max(atk * (1 - res / 100), 0.05 * atk);
            }
            else
            {
                double def = Math.Max(0, (p.Def + target.AuraDef) * target.DefScale * (1 - u.Spec.DefIgnoreRatio) - u.Spec.DefIgnore);
                dmg = Math.Max(atk - def, 0.05 * atk);
            }
            if (u.Spec.ExtraArts > 0) dmg += ArtsOn(target, u.Spec.ExtraArts * atk, u.Spec.ResIgnore);
            dmg *= u.Spec.DamageScale * (1 + target.Fragile);
            if (target.BlockedBy < 0) dmg *= target.T.UnblockedTaken;
            dmg = Math.Min(dmg, target.Hp);
            target.Hp -= dmg;
            u.Damage += dmg;
            if (target.Hp <= 0) { Die(target, i); return; }
            if (u.Spec.DotDps > 0)
            {
                target.PoisonDps = Math.Max(target.PoisonDps, u.Spec.DotDps);
                target.PoisonUntil = t + u.Spec.DotSeconds;
                target.PoisonBy = i;
            }
            if (u.Spec.HitControlSeconds > 0 && rng.NextDouble() < u.Spec.HitControlChance)
            {
                if (u.Spec.HitStuns)
                {
                    if (target.StunUntil <= t) SawStun(target.X, target.Y);
                    target.StunUntil = Math.Max(target.StunUntil, t + u.Spec.HitControlSeconds);
                }
                else target.StopUntil = Math.Max(target.StopUntil, t + u.Spec.HitControlSeconds);
            }
            if ((sk?.Push ?? (skillHit ? null : u.Spec.Push)) is { } push) Displace(target, u, push);
            if (sk is { ControlSeconds: > 0 } c && (c.ControlChance >= 1 || rng.NextDouble() < c.ControlChance))
            {
                if (c.ControlStuns)
                {
                    if (target.StunUntil <= t) SawStun(target.X, target.Y);
                    target.StunUntil = Math.Max(target.StunUntil, t + c.ControlSeconds);
                }
                else target.StopUntil = Math.Max(target.StopUntil, t + c.ControlSeconds);
            }
        }

        // Healing by an operator: what actually filled the patient's HP counts as its healing.
        void Heal(Unit by, Unit to, double amount)
        {
            double before = to.Hp;
            to.Hp = Math.Min(to.MaxHp, to.Hp + amount);
            by.Healed += Math.Max(0, to.Hp - before);
        }

        // Arts damage on an enemy after its resistance (talent riders, poison, zones).
        double ArtsOn(Enemy e, double amount, double ignore)
        {
            double res = Math.Clamp(e.Profile.Res + e.T.RefractionRes + e.AuraRes + e.ResAdd - ignore, 0, 100);
            return Math.Max(amount * (1 - res / 100), 0.05 * amount);
        }

        // Damage over time from an operator (poison, zones): no hit, just HP lost.
        void Wear(Enemy e, int by, double dmg)
        {
            if (e.Done || e.Still || e.Shield > 0) return;
            dmg = Math.Min(dmg * (1 + e.Fragile), e.Hp);
            e.Hp -= dmg;
            if (by >= 0) units[by].Damage += dmg;
            if (e.Hp <= 0) Die(e, by);
        }

        // Pushed away from the operator or dragged towards it, less the heavier it is; at most once a second.
        void Displace(Enemy e, Unit u, PushData push)
        {
            if (e.Done || e.Still || t - e.PushedAt < 1) return;
            double cells = push.Cells * Math.Max(0, 1 - 0.15 * (e.Profile.Weight - 1));
            if (cells < 0.05) return;
            e.PushedAt = t;
            var path = paths[e.Lane];
            double s = e.S;
            if (push.Pull)
            {
                double to = path.Nearest(u.X, u.Y);
                s = to > s ? Math.Min(to, s + cells) : Math.Max(to, s - cells);
            }
            else s = Math.Max(0, s - cells);
            if (Math.Abs(s - e.S) < 1e-6) return;
            Release(e);
            e.S = s;
            (e.X, e.Y) = path.At(s);
            Locate(e);
        }

        // An enemy brought to 0 HP: it may turn into an ember (every time) or be reborn (once) first; otherwise it
        // dies and its death effects play out. An ember beaten down by its hits is final.
        void Die(Enemy e, int killer, bool final = false)
        {
            if (e.T.EmberHits > 0 && !final)
            {
                if (!e.Ember) e.LivesLost++;
                e.Ember = true;
                e.EmberHits = e.T.EmberHits;
                e.StateTimer = e.T.EmberSeconds;
                // A blocked enemy stands on its blocker's tile, so the ember lies there, within the blocker's reach.
                if (e.BlockedBy >= 0)
                {
                    var cell = units[e.BlockedBy].Spec.Cell;
                    e.X = cell.Col + 0.5;
                    e.Y = cell.Row + 0.5;
                    Locate(e);
                }
                Release(e);
                return;
            }
            if (e.T.RebornSeconds > 0 && !e.UsedReborn)
            {
                e.UsedReborn = true;
                e.LivesLost++;
                e.Reviving = true;
                e.StateTimer = e.T.RebornSeconds;
                Release(e);
                return;
            }
            e.Done = true;
            e.Ember = false;
            e.Killed = true;
            killed++;
            foreach (var o in units)
                if (!o.Down && (e.CellBit >= 0 ? Has(o.Mask, e.CellBit) : o.Range.Contains(e.Cell))) o.KillsSeen++;
            if (killer >= 0) units[killer].Kills++;
            Release(e);
            foreach (var child in e.T.OnDeath)
                for (int k = 0; k < child.Count; k++)
                {
                    pending.Add((t + child.Delay, e.Lane, e.S, child.Profile));
                    spawned++;
                }
            if (e.T.CloudSeconds > 0)
            {
                bool atSource = e.T.CloudAtSource && killer >= 0;
                double dps = e.T.CloudDps > 0 ? e.T.CloudDps : e.T.CloudAtkScale * e.Profile.Atk;
                clouds.Add(new Cloud(atSource ? units[killer].X : e.X, atSource ? units[killer].Y : e.Y, e.T.CloudRange, dps, t + e.T.CloudSeconds));
            }
            if (e.T.DeathShieldAura > 0)
                foreach (var o in enemies)
                    if (!o.Done && o != e && Dist(o.X, o.Y, e.X, e.Y) <= 1.5) o.Shield = Math.Max(o.Shield, e.T.DeathShieldAura);
        }

        for (; t < end; t += w.Step)
        {
            if (anySummon)
                for (int i = 0; i < units.Count; i++)
                {
                    var u = units[i];
                    if (u.Spec.Summon is not { } sm) continue;
                    if (u.Down && t >= u.NextUp)
                    {
                        u.Down = false;
                        u.Hp = u.MaxHp;
                        u.Cooldown = 0;
                        u.UpUntil = sm.Duration > 0 ? t + sm.Duration : double.PositiveInfinity;
                    }
                    else if (!u.Down && t >= u.UpUntil) Fall(u, i);
                }
            while (next < spawns.Count && spawns[next].Time <= t)
            {
                var (_, lane, p) = spawns[next++];
                Add(p, lane, 0);
            }
            for (int k = pending.Count - 1; k >= 0; k--)
                if (pending[k].Time <= t)
                {
                    Add(pending[k].Profile, pending[k].Lane, pending[k].S);
                    pending.RemoveAt(k);
                }

            if (anyReveal)
            {
                revealed.Clear();
                revealedMask = 0;
                foreach (var u in units)
                    if (u.Spec.Reveals && !u.Down) { revealed.UnionWith(u.Range); revealedMask |= u.Mask; }
            }

            // Auras: enemies buff the enemies around them and slow the operators around them.
            // (The few enemies that carry one are listed first, so each enemy checks only those.)
            if (anyAura)
            {
                sources.Clear();
                foreach (var s in enemies)
                    if (!s.Done && !s.Still && HasAura(s.Profile)) sources.Add(s);
                foreach (var e in enemies)
                {
                    if (e.Done) continue;
                    e.AuraDef = e.AuraRes = 0;
                    foreach (var s in sources)
                        if (s != e && Dist(s.X, s.Y, e.X, e.Y) <= s.T.AuraRange)
                        {
                            e.AuraDef = Math.Max(e.AuraDef, s.T.AuraDef);
                            e.AuraRes = Math.Max(e.AuraRes, s.T.AuraRes);
                        }
                }
            }
            if (anySlow)
            {
                sources.Clear();
                foreach (var s in enemies)
                    if (!s.Done && !s.Still && s.T.SlowFactor < 1) sources.Add(s);
                foreach (var u in units)
                {
                    u.Slow = 1;
                    foreach (var s in sources)
                        if (Dist(s.X, s.Y, u.X, u.Y) <= s.T.SlowRange) u.Slow = Math.Min(u.Slow, s.T.SlowFactor);
                }
            }

            // Enemies move, get blocked, fight their blocker, or leak.
            foreach (var e in enemies)
            {
                if (e.Done) continue;
                var prof = e.Profile;
                if (e.Still)
                {
                    // Embers and the reborn wait where they fell; an ember left alone comes back at full HP.
                    e.StateTimer -= w.Step;
                    if (e.StateTimer <= 0)
                    {
                        e.Ember = e.Reviving = false;
                        e.Hp = e.MaxHp;
                    }
                    continue;
                }
                if (e.T.Regen > 0) e.Hp = Math.Min(e.MaxHp, e.Hp + e.T.Regen * w.Step);
                if (e.PoisonUntil > t)
                {
                    Wear(e, e.PoisonBy, ArtsOn(e, e.PoisonDps * w.Step, 0));
                    if (e.Done) continue;
                }
                e.Tile = e.Flying ? TerrainKind.None : TileAt(e.Cell);
                if (e.Tile == TerrainKind.Infection) e.Infected = true;
                if (e.Tile == TerrainKind.Mire)
                {
                    if (t >= e.MireNext)
                    {
                        e.MireStacks = Math.Min(MireMaxStacks, e.MireStacks + (prof.Weight >= MireHeavy ? 2 : 1));
                        e.MireNext = t + MireInterval;
                    }
                }
                else e.MireNext = double.NegativeInfinity;
                if (e.Infected)
                {
                    e.Hp -= InfectionDps * w.Step;
                    if (e.Hp <= 0) { Die(e, -1); continue; }
                }
                double haste = (e.Infected ? InfectionSpeed : 1) * e.Mire;
                bool stunned = e.StunUntil > t;
                if (e.BlockedBy >= 0)
                {
                    var u = units[e.BlockedBy];
                    if (u.Down || u.Stunned(t)) e.BlockedBy = -1;
                    else
                    {
                        if (stunned) continue;
                        e.Cooldown -= w.Step * haste;
                        if (e.Cooldown <= 0 && prof.Atk > 0)
                        {
                            Attack(e, e.BlockedBy);
                            e.Cooldown += prof.Interval;
                        }
                        continue;
                    }
                }
                var path = paths[e.Lane];
                if (stunned) continue;
                // Ranged enemies shoot the nearest operator in reach while they walk (the back line too).
                if (prof.Range > 0 && prof.Atk > 0)
                {
                    e.Cooldown -= w.Step * haste;
                    if (e.Cooldown <= 0)
                    {
                        int target = -1;
                        double nearest = double.MaxValue;
                        for (int i = 0; i < units.Count; i++)
                        {
                            // Operators on 隐匿模块 are invisible to enemy fire.
                            if (units[i].Down || units[i].Tile == TerrainKind.Hide) continue;
                            double d = Dist(units[i].X, units[i].Y, e.X, e.Y);
                            if (d <= prof.Range && d < nearest) { nearest = d; target = i; }
                        }
                        if (target >= 0)
                        {
                            Attack(e, target);
                            e.Cooldown += prof.Interval;
                        }
                        else e.Cooldown = 0;
                    }
                }
                if (e.StopUntil > t) continue;
                double move = e.Mire * (e.Tile == TerrainKind.Speedup ? SpeedupMove : 1) * (1 - Math.Min(0.8, e.SlowBy));
                e.S += prof.Speed * move * w.Step;
                if (e.S >= path.Length)
                {
                    e.Done = true;
                    leaked++;
                    leaksByLane[e.Lane] = leaksByLane.GetValueOrDefault(e.Lane) + 1;
                    leakedByName[prof.Name] = leakedByName.GetValueOrDefault(prof.Name) + 1;
                    continue;
                }
                (e.X, e.Y) = path.At(e.S);
                Locate(e);
                if (!e.Flying)
                {
                    var cell = e.Cell;
                    e.Tile = TileAt(cell);   // the tile it now stands on (hidden there before anyone aims)
                    if (byCell.TryGetValue(cell, out var blockers))
                        foreach (var i in blockers)
                        {
                            var u = units[i];
                            if (u.Down || u.Stunned(t) || u.BlockCap < e.T.MinBlock || u.Blocking + e.T.BlockCost > u.BlockCap) continue;
                            u.Blocking += e.T.BlockCost;
                            e.BlockedBy = i;
                            e.Cooldown = prof.Interval;
                            break;
                        }
                }
            }

            // Damage over time on operators: bleeding (cured by healing), apoptosis, death clouds.
            clouds.RemoveAll(c => c.Until <= t);
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (u.Down) continue;
                double dot = 0;
                if (u.BleedUntil > t) dot += u.BleedDps * (1 - u.Res(t) / 100);
                if (u.ApoptosisUntil > t) dot += 100 * (1 - u.Res(t) / 100);
                if (u.Tile == TerrainKind.Infection) dot += InfectionDps;
                foreach (var c in clouds)
                    if (Dist(c.X, c.Y, u.X, u.Y) <= c.Range) dot += c.Dps;
                if (dot > 0) Hurt(u, i, dot * w.Step);
            }

            if (anyDebuff)
            {
                foreach (var e in enemies) { e.DefScale = 1; e.ResAdd = 0; }
                foreach (var u in units)
                {
                    if (u.Down || u.Spec.Skill is not { Debuffs: true } ds || !SkillOn(u)) continue;
                    foreach (var e in enemies)
                        if (!e.Done && InRange(u, e))
                        {
                            e.DefScale = Math.Min(e.DefScale, ds.EnemyDefScale);
                            e.ResAdd = Math.Min(e.ResAdd, ds.EnemyResAdd);
                        }
                }
            }
            if (anyField)
            {
                foreach (var e in enemies) { e.Fragile = 0; e.SlowBy = 0; }
                for (int i = 0; i < units.Count; i++)
                {
                    var u = units[i];
                    if (u.Down) continue;
                    var zs = u.Spec.Skill is { } zk && SkillOn(u) ? zk : null;
                    double zone = zs?.Zone ?? 0, slow = Math.Max(u.Spec.EnemySlow, zs?.Slow ?? 0);
                    if (zone <= 0 && slow <= 0 && u.Spec.Fragile <= 0) continue;
                    double perStep = zone * u.Spec.Atk / Math.Max(1e-9, u.Spec.SkillFactor) * w.Step;
                    foreach (var e in enemies)
                    {
                        if (e.Done || e.Still || !InRange(u, e)) continue;
                        e.Fragile = Math.Max(e.Fragile, u.Spec.Fragile);
                        if (e.Flying) continue;
                        e.SlowBy = Math.Max(e.SlowBy, slow);
                        if (zone > 0) Wear(e, i, ArtsOn(e, perStep, u.Spec.ResIgnore));
                    }
                }
            }
            if (anyRegen)
                foreach (var u in units)
                    if (!u.Down && u.Spec.Regen > 0) u.Hp = Math.Min(u.MaxHp, u.Hp + u.Spec.Regen * w.Step);
            if (anyHot)
                foreach (var u in units)
                {
                    if (u.Down) continue;
                    double rate = u.Spec.Pattern.HealAura
                        + (u.HealUntil > t && u.Spec.Skill is { } hs2 ? hs2.HealPerSecond : 0);
                    if (rate <= 0) continue;
                    double heal = rate * u.Spec.Atk / Math.Max(1e-9, u.Spec.SkillFactor) * w.Step;
                    foreach (int j in u.SkillAllies)
                        if (!units[j].Down) Heal(u, units[j], heal);
                }

            UInt128 groundAt = 0, airAt = 0;
            bool stray = false;     // an enemy outside the masked area: no shortcut this step
            foreach (var e in enemies)
            {
                if (e.Done || e.Reviving) continue;
                if (e.CellBit < 0) { stray = true; continue; }
                if (e.Flying) airAt |= UInt128.One << e.CellBit; else groundAt |= UInt128.One << e.CellBit;
            }

            // Operators act.
            for (int i = 0; i < units.Count; i++)
            {
                var u = units[i];
                if (u.Down) continue;
                var sk = u.Spec.Skill;
                if (sk is not null)
                {
                    // 凋亡 cuts a running skill short; auto skills gain SP every second while none runs.
                    if (u.SkillLockUntil > t) { u.ActiveUntil = Math.Min(u.ActiveUntil, t); u.Empowered = false; }
                    if (sk.Charge == SkillCharge.Auto && u.ActiveUntil <= t && !u.Empowered) u.Sp = Math.Min(sk.Cost, u.Sp + w.Step);
                    if (u.Spec.SpPerSecond > 0 && u.ActiveUntil <= t && !u.Empowered) u.Sp = Math.Min(sk.Cost, u.Sp + u.Spec.SpPerSecond * w.Step);
                }
                // The skill's self buffs come and go with it (extra block released when it ends).
                bool buff = sk is not null && SkillOn(u);
                if (buff != u.BuffOn)
                {
                    u.BuffOn = buff;
                    double hp = sk!.SelfHp > 10 ? u.Spec.Hp + sk.SelfHp : u.Spec.Hp * (1 + sk.SelfHp);
                    if (buff) { u.Hp += hp - u.MaxHp; u.MaxHp = hp; u.BlockCap = u.Spec.Block + sk.SelfBlock; }
                    else
                    {
                        u.MaxHp = u.Spec.Hp;
                        u.Hp = Math.Min(u.Hp, u.MaxHp);
                        u.BlockCap = u.Spec.Block;
                        foreach (var e in enemies)
                            if (u.Blocking > u.BlockCap && e.BlockedBy == i) Release(e);
                    }
                }
                u.Cooldown -= w.Step;
                if (u.Cooldown > 0 || u.Stunned(t)) continue;
                var pat = u.Spec.Pattern;
                if (pat.HealAura > 0) continue;   // heals through its aura, never attacks
                double tileSpeed = u.Tile switch
                {
                    TerrainKind.Infection => InfectionSpeed,
                    TerrainKind.Speedup => SpeedupSpeed,
                    TerrainKind.Mire => 1 - MireStack * Math.Min(MireMaxStacks, 1 + Math.Floor(t / MireInterval)),
                    _ => 1,
                };
                double interval = u.Spec.Interval / u.Slow / tileSpeed;
                double atk = sk is not null ? u.Spec.Atk / Math.Max(1e-9, u.Spec.SkillFactor)
                    : u.SkillLockUntil > t ? u.Spec.Atk / Math.Max(1, u.Spec.SkillFactor) : u.Spec.Atk;
                if (u.Tile == TerrainKind.Infection) atk *= InfectionAtk;
                if (u.Spec.Healer || pat.HealsDuringSkill && SkillOn(u))
                {
                    Unit? patient = null;
                    foreach (int j in u.Patients)
                    {
                        var o = units[j];
                        if (!o.Down && o.Hp < o.MaxHp && o.NoHealUntil <= t
                            && (patient is null || o.Hp / o.MaxHp < patient.Hp / patient.MaxHp))
                            patient = o;
                    }
                    if (patient is null) { u.Cooldown = 0.2; continue; }
                    atk *= SkillBoost(u, out _);
                    Heal(u, patient, atk);
                    patient.BleedUntil = -1;
                    // Healers that heal several at once: the next most hurt too (each once).
                    var healed = pat.HealTargets > 1 ? new List<Unit> { patient } : null;
                    for (int extra = 1; extra < pat.HealTargets; extra++)
                    {
                        Unit? other = null;
                        foreach (int j in u.Patients)
                        {
                            var o = units[j];
                            if (!o.Down && o.Hp < o.MaxHp && o.NoHealUntil <= t && !healed!.Contains(o) && (other is null || o.Hp / o.MaxHp < other.Hp / other.MaxHp))
                                other = o;
                        }
                        if (other is null) break;
                        healed!.Add(other);
                        Heal(u, other, atk);
                        other.BleedUntil = -1;
                    }
                    u.Cooldown += interval;
                    continue;
                }
                // Nothing it blocks and nothing in reach: no target (the same outcome as the scan below).
                if (!stray && u.Blocking == 0 && ((SkillOn(u) ? u.SkillMask : u.Mask) & (groundAt | (u.Spec.AntiAir ? airAt : UInt128.Zero))) == UInt128.Zero)
                {
                    u.Cooldown = 0;
                    continue;
                }
                // A revealing skill is cast as soon as anything (seen or not) is in range.
                if (u.Spec.SkillRevealSeconds > 0 && Ready(u) && enemies.Any(e => !e.Done && !e.Reviving && InRange(u, e))) Cast(u);
                // Skill-only attackers stay idle until their skill runs (cast as soon as anything is in reach).
                if (pat.SkillOnly && !SkillOn(u))
                {
                    if (Ready(u) && enemies.Any(e => !e.Done && !e.Reviving && InRange(u, e))) Cast(u);
                    else { u.Cooldown = 0; continue; }
                }
                Enemy? target = null;
                double best = double.MinValue;
                int ownBit = Bit(u.Spec.Cell);
                bool on = sk is not null && SkillOn(u);
                int take = on && sk!.Targets > 1 ? sk.Targets : pat.Targets;
                bool allBlocked = on && sk!.HitsBlocked || pat.HitsBlocked;
                bool multi = take > 1 || allBlocked;
                hits.Clear();
                foreach (var e in enemies)
                {
                    if (e.Done || e.Reviving || e.Flying && !u.Spec.AntiAir) continue;
                    // Invisible enemies (and those on 隐匿模块) can only be hit once blocked or revealed; embers always.
                    if ((e.T.Invisible || e.Tile == TerrainKind.Hide) && !e.Ember && e.BlockedBy < 0 && e.RevealedUntil <= t
                        && !Revealed(e)) continue;
                    double priority;
                    // What it blocks stands on its own cell: first in line, unless the range it uses now leaves
                    // that cell out (a skill that only strikes the cell in front).
                    if (e.BlockedBy == i && (!SkillOn(u) || Has(u.SkillMask, ownBit))) priority = 1e6 + e.S;
                    else
                    {
                        if (!InRange(u, e)) continue;
                        priority = e.S;
                    }
                    if (multi) hits.Add((priority, e));
                    if (priority > best) { best = priority; target = e; }
                }
                if (target is null)
                {
                    // An armed strike with nothing where it lands (a wall in front) is spent on nothing.
                    if (u.Empowered && sk is not null) { u.Empowered = false; u.Cooldown += interval; }
                    else u.Cooldown = 0;
                    continue;
                }
                u.Cooldown += interval;
                atk *= SkillBoost(u, out bool skillHit);
                int strikes = Math.Max(1, pat.HitsPerAttack) * (skillHit && sk is { Hits: > 1 } ? sk.Hits : 1);
                for (int rep = 0; rep < strikes; rep++)
                {
                    if (!multi) Hit(u, i, target, atk, skillHit);
                    else
                    {
                        // Several at once: the furthest-along enemies in reach (and every enemy it blocks, if it says so).
                        hits.Sort((a, b) => b.Priority.CompareTo(a.Priority));
                        for (int h = 0; h < hits.Count; h++)
                        {
                            var (_, e) = hits[h];
                            if (h < take || allBlocked && e.BlockedBy == i) Hit(u, i, e, atk, skillHit);
                        }
                    }
                }
                // Splash around the target, or hits jumping on from it.
                if (pat.SplashRadius > 0)
                    foreach (var e in enemies)
                        if (e != target && !e.Done && !e.Reviving && (!e.Flying || u.Spec.AntiAir)
                            && Dist(e.X, e.Y, target.X, target.Y) <= pat.SplashRadius) Hit(u, i, e, atk, skillHit);
                if (pat.ChainTargets > 1)
                {
                    var chained = new HashSet<Enemy> { target };
                    double scale = 1, cx = target.X, cy = target.Y;
                    for (int k = 1; k < pat.ChainTargets; k++)
                    {
                        Enemy? jump = null;
                        double nearest = 1.6;
                        foreach (var e in enemies)
                        {
                            if (e.Done || e.Reviving || chained.Contains(e) || e.Flying && !u.Spec.AntiAir) continue;
                            double d = Dist(e.X, e.Y, cx, cy);
                            if (d <= nearest) { nearest = d; jump = e; }
                        }
                        if (jump is null) break;
                        chained.Add(jump);
                        scale *= 0.85;
                        Hit(u, i, jump, atk * scale, skillHit);
                        (cx, cy) = (jump.X, jump.Y);
                    }
                }
            }

            // Finished enemies leave the list (order kept, so targeting ties break the same way).
            enemies.RemoveAll(e => e.Done);
            // Nothing left to come or fight and nothing still hurting an operator: the rest of the clock changes nothing.
            if (next == spawns.Count && pending.Count == 0 && enemies.Count == 0 && clouds.Count == 0
                && !units.Any(u => !u.Down && (u.Tile == TerrainKind.Infection || u.BleedUntil > t || u.ApoptosisUntil > t)))
                break;
        }

        int alive = enemies.Count(e => !e.Done) + pending.Count + (spawns.Count - next);
        // A boss not yet spawned counts as untouched; one with a second life (rebirth) has twice the HP to beat.
        int bossCount = bosses.Count + spawns.Skip(next).Count(s => s.Profile.Rank == "BOSS");
        double bossProgress = bosses.Sum(b =>
        {
            if (b.Killed) return 1.0;
            int lives = 1 + (b.T.RebornSeconds > 0 ? 1 : 0) + (b.T.EmberHits > 0 ? 1 : 0);
            double part = b.Still ? 0 : 1 - Math.Clamp(b.Hp / Math.Max(1, b.MaxHp), 0, 1);
            return Math.Min(1, (b.LivesLost + part) / lives);
        });
        return new SimResult(spawned, killed, leaked, alive,
            units.ToDictionary(u => u.Spec.Key, u => u.Kills), units.ToDictionary(u => u.Spec.Key, u => u.Damage),
            [.. units.Where(u => u.Down && u.Spec.Summon is null).Select(u => u.Spec.Key)], leaksByLane)
        {
            LeakedByName = leakedByName,
            Casts = units.Where(u => u.Spec.Skill is not null).ToDictionary(u => u.Spec.Key, u => u.Casts),
            StunsAround = units.ToDictionary(u => u.Spec.Key, u => u.StunsSeen),
            KillsInRange = units.ToDictionary(u => u.Spec.Key, u => u.KillsSeen),
            Bosses = bossCount, BossProgress = bossProgress, BossesKilled = bosses.Count(b => b.Killed),
            Taken = units.ToDictionary(u => u.Spec.Key, u => u.Taken),
            Healed = units.ToDictionary(u => u.Spec.Key, u => u.Healed),
        };
    }

    private static bool HasAura(EnemyProfile p) => p.Traits.AuraRange > 0 && (p.Traits.AuraDef > 0 || p.Traits.AuraRes > 0);

    /// <summary>Spawn times and lanes: each kind spread evenly over the spawn window, lanes in proportion to their weight.</summary>
    private static List<(double Time, int Lane, EnemyProfile Profile)> Schedule(IReadOnlyList<SimLane> lanes, WaveScenario w)
    {
        if (w.Spawns is { } real)
        {
            // A real stream keeps its times; each enemy takes a lane of its own gate and motion, shared by weight.
            var share = new double[lanes.Count];
            var list = new List<(double, int, EnemyProfile)>();
            foreach (var s in real)
            {
                var idx = Enumerable.Range(0, lanes.Count).Where(i => lanes[i].Flying == s.Flying && lanes[i].Upper == s.Upper && lanes[i].Weight > 0).ToList();
                if (idx.Count == 0) idx = [.. Enumerable.Range(0, lanes.Count).Where(i => lanes[i].Flying == s.Flying && lanes[i].Weight > 0)];
                if (idx.Count == 0) idx = [.. Enumerable.Range(0, lanes.Count).Where(i => !lanes[i].Flying)];
                if (idx.Count == 0) continue;
                double total = idx.Sum(i => lanes[i].Weight);
                foreach (var i in idx) share[i] += lanes[i].Weight / total;
                int lane = idx.MaxBy(i => share[i]);
                share[lane] -= 1;
                list.Add((s.Time, lane, s.Profile));
            }
            list.Sort((a, b) => a.Item1.CompareTo(b.Item1));
            return list;
        }
        var result = new List<(double, int, EnemyProfile)>();
        foreach (bool flying in new[] { false, true })
        {
            int count = flying ? w.FlyCount : w.GroundCount;
            var idx = Enumerable.Range(0, lanes.Count).Where(i => lanes[i].Flying == flying && lanes[i].Weight > 0).ToList();
            if (count <= 0 || idx.Count == 0) continue;
            double total = idx.Sum(i => lanes[i].Weight);
            var credit = idx.ToDictionary(i => i, _ => 0.0);
            for (int k = 0; k < count; k++)
            {
                // Largest remainder: the lane furthest behind its share gets the next enemy.
                foreach (var i in idx) credit[i] += lanes[i].Weight / total;
                int lane = idx.MaxBy(i => credit[i]);
                credit[lane] -= 1;
                result.Add((w.SpawnSeconds * (k + 0.5) / count, lane, flying ? w.Flying : w.Ground));
            }
        }
        result.Sort((a, b) => a.Item1.CompareTo(b.Item1));
        return result;
    }
}
