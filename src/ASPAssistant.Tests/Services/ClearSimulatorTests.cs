// Added for ASP Assistant Regulation on 2026-11-14; see NOTICE.
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

/// <summary>The steady-stream fight simulation and the planner's use of it.</summary>
public class ClearSimulatorTests
{
    private static readonly SimLane Straight = new([(7.5, 3.5), (-0.5, 3.5)], 1, false);
    private static readonly WaveScenario Small = new()
    {
        GroundCount = 6, SpawnSeconds = 12, TailSeconds = 15,
        Ground = new EnemyProfile(1500, 100, 0, 300, 1.5, 1.0),
    };

    private static SimUnit Guard(int col, int block = 2, double atk = 600) =>
        new("近卫", new BoardCell(col, 3), [new BoardCell(col, 3), new BoardCell(col + 1, 3)], true, block, false,
            atk, 1.0, false, 3000, 300, false);

    [Fact]
    public void WithNobodyEveryEnemyLeaks()
    {
        var r = ClearSimulator.Run([], [Straight], Small);
        r.Spawned.Should().Be(6);
        r.Leaked.Should().Be(6);
        r.Killed.Should().Be(0);
    }

    [Fact]
    public void ABlockerHoldsTheLineAndKills()
    {
        var r = ClearSimulator.Run([Guard(2)], [Straight], Small);
        r.Killed.Should().Be(6);
        r.Leaked.Should().Be(0);
        r.Kills["近卫"].Should().Be(6);
    }

    [Fact]
    public void AWeakBlockerFallsAndTheLineBreaks()
    {
        var tough = Small with { Ground = Small.Ground with { Hp = 20000, Atk = 1200 } };
        var r = ClearSimulator.Run([Guard(2, atk: 200)], [Straight], tough);
        r.Fallen.Should().Contain("近卫");
        r.Leaked.Should().BeGreaterThan(0);
    }

    [Fact]
    public void FlyersPassBlockersAndOnlyAntiAirHitsThem()
    {
        var air = new SimLane([(7.5, 3.5), (-0.5, 3.5)], 1, true);
        var w = Small with { GroundCount = 0, FlyCount = 4 };
        ClearSimulator.Run([Guard(2)], [air], w).Leaked.Should().Be(4, "a melee guard neither blocks nor hits flyers");
        var sniper = new SimUnit("狙击", new BoardCell(2, 2),
            [new BoardCell(1, 3), new BoardCell(2, 3), new BoardCell(3, 3), new BoardCell(4, 3)], false, 1, true,
            900, 1.0, false, 1200, 100, false);
        ClearSimulator.Run([sniper], [air], w).Killed.Should().Be(4);
    }

    // ── Enemy mechanics ───────────────────────────────────────────────

    private static WaveScenario Real(EnemyProfile p, int count = 4) => new()
    {
        TailSeconds = 30,
        Spawns = [.. Enumerable.Range(0, count).Select(k => new SimSpawn(1 + 3 * k, false, false, p))],
    };

    private static readonly EnemyProfile Grunt = new(1500, 100, 0, 300, 1.5, 1.0) { Name = "杂兵" };

    private static SimUnit Sniper(int col) => new("狙击", new BoardCell(col, 2),
        [.. Enumerable.Range(0, 8).Select(c => new BoardCell(c, 3))], false, 1, true, 900, 1.0, false, 1200, 100, false);

    [Fact]
    public void InvisibleEnemiesCanOnlyBeHitOnceBlocked()
    {
        var hidden = Grunt with { Traits = new EnemyTraits { Invisible = true } };
        ClearSimulator.Run([Sniper(3)], [Straight], Real(Grunt)).Killed.Should().Be(4);
        ClearSimulator.Run([Sniper(3)], [Straight], Real(hidden)).Leaked.Should().Be(4, "nobody blocks them");
        ClearSimulator.Run([Sniper(3), Guard(1, atk: 100)], [Straight], Real(hidden)).Killed.Should().Be(4);
    }

    [Fact]
    public void AnEmberNeedsHitsOrComesBack()
    {
        var ember = Grunt with { Traits = new EnemyTraits { EmberHits = 5, EmberSeconds = 10 } };
        var r = ClearSimulator.Run([Guard(2, block: 3)], [Straight], Real(ember, 1));
        r.Killed.Should().Be(1, "the ember lies on the guard's tile and takes its 5 hits in time");
        // A steady stream keeps the guard busy with whoever it blocks, so the embers come back again and again.
        var busy = ClearSimulator.Run([Guard(2, block: 3)], [Straight], Real(ember, 4));
        busy.Killed.Should().BeLessThan(4);
        // One slow hit every 3 s never lands 5 hits in 10 s: each time the ember comes back at full HP, is cut down
        // again and falls into an ember again, so it never dies.
        var slow = new SimUnit("近卫", new BoardCell(2, 3), [new BoardCell(2, 3)], true, 3, false, 5000, 3.0, false, 9000, 300, false);
        var never = ClearSimulator.Run([slow], [Straight], Real(ember, 1));
        never.Killed.Should().Be(0);
        never.Leaked.Should().Be(0, "it is still blocked");
        never.Damage["近卫"].Should().BeGreaterThan(2 * ember.Hp - 1);
    }

    [Fact]
    public void DeathSpawnsAddEnemies()
    {
        var child = Grunt with { Name = "大君之触", Hp = 500 };
        var parent = Grunt with { Traits = new EnemyTraits { OnDeath = [new DeathSpawn(child, 2, 1)] } };
        var r = ClearSimulator.Run([Guard(2)], [Straight], Real(parent, 2));
        r.Spawned.Should().Be(6);
        r.Killed.Should().Be(6);
    }

    [Fact]
    public void AShieldTakesWholeHits()
    {
        var shielded = Grunt with { Hp = 100, Traits = new EnemyTraits { Shield = 10_000 } };
        var r = ClearSimulator.Run([Guard(2)], [Straight], Real(shielded, 1));
        r.Killed.Should().Be(0);
        r.Leaked.Should().Be(0, "it is still blocked");
    }

    [Fact]
    public void RefractionBluntsArts()
    {
        // One walker crossing a caster's range: 5 hits kill it, unless refraction cuts them to 30 %.
        var caster = new SimUnit("术师", new BoardCell(3, 2), [.. Enumerable.Range(0, 8).Select(c => new BoardCell(c, 3))],
            false, 1, false, 700, 1.6, true, 1200, 100, false);
        ClearSimulator.Run([caster], [Straight], Real(Grunt, 1)).Killed.Should().Be(1);
        ClearSimulator.Run([caster], [Straight], Real(Grunt with { Traits = new EnemyTraits { RefractionRes = 70 } }, 1)).Leaked.Should().Be(1);
    }

    [Fact]
    public void NeuralBurstsStunTheBlockerAndLetEnemiesThrough()
    {
        var neural = Grunt with { Hp = 6000, Atk = 600, Traits = new EnemyTraits { Element = ElementKind.Neural, ElementRatio = 0.6 } };
        var tank = new SimUnit("重装", new BoardCell(2, 3), [new BoardCell(2, 3)], true, 3, false, 300, 1.2, false, 20000, 800, false);
        ClearSimulator.Run([tank], [Straight], Real(neural with { Traits = EnemyTraits.None }, 3)).Leaked.Should().Be(0);
        ClearSimulator.Run([tank], [Straight], Real(neural, 3)).Leaked.Should().BeGreaterThan(0);
    }

    [Fact]
    public void SomeEnemiesNeedABigBlock()
    {
        var brute = Grunt with { Traits = new EnemyTraits { MinBlock = 3 } };
        ClearSimulator.Run([Guard(2, block: 2, atk: 100)], [Straight], Real(brute)).Leaked.Should().Be(4);
        ClearSimulator.Run([Guard(2, block: 3, atk: 100)], [Straight], Real(brute, 3)).Leaked.Should().Be(0);
    }

    [Fact]
    public void AStunEveryFewAttacksLetsTheEnemyThrough()
    {
        // A blocker that outlasts the grunts; a third-hit 7 s stun releases them and they walk past.
        var tank = new SimUnit("重装", new BoardCell(2, 3), [new BoardCell(2, 3)], true, 3, false, 100, 1.2, false, 50000, 800, false);
        ClearSimulator.Run([tank], [Straight], Real(Grunt, 2)).Leaked.Should().Be(0);
        var stunner = Grunt with { Traits = new EnemyTraits { StunEvery = 3, StunSeconds = 7 } };
        ClearSimulator.Run([tank], [Straight], Real(stunner, 2)).Leaked.Should().BeGreaterThan(0);
    }

    [Fact]
    public void SplashAndChainsHitTheNeighbours()
    {
        // A guard blocks; a medic-less neighbour one cell above takes splash or chain hits too.
        var guard = Guard(2, block: 3, atk: 50) with { Hp = 50000 };
        var neighbour = new SimUnit("邻居", new BoardCell(2, 2), [new BoardCell(2, 2)], false, 0, false, 0, 1.0, false, 3000, 0, false);
        var hard = Grunt with { Hp = 1_000_000, Atk = 400 };
        ClearSimulator.Run([guard, neighbour], [Straight], Real(hard, 1)).Fallen.Should().NotContain("邻居");
        ClearSimulator.Run([guard, neighbour], [Straight], Real(hard with { Traits = new EnemyTraits { SplashRadius = 1.0 } }, 1))
            .Fallen.Should().Contain("邻居");
        ClearSimulator.Run([guard, neighbour], [Straight], Real(hard with { Traits = new EnemyTraits { ChainTargets = 2, ChainScale = 0.85 } }, 1))
            .Fallen.Should().Contain("邻居");
    }

    [Fact]
    public void ASkillCycleBurstsWhenItIsUp()
    {
        // Same average (factor 2): an auto skill ready from the start doubles... the burst kills before the walker leaves.
        var caster = new SimUnit("术师", new BoardCell(3, 2), [.. Enumerable.Range(2, 3).Select(c => new BoardCell(c, 3))],
            false, 1, false, 900, 1.6, true, 1200, 100, false) { SkillFactor = 1 };
        var walker = Grunt with { Hp = 3000, Res = 0 };
        ClearSimulator.Run([caster], [Straight], Real(walker, 1)).Leaked.Should().Be(1);
        var bursting = caster with { Skill = new SimSkill(4, 10, 10, 10, SkillCharge.Auto) };
        ClearSimulator.Run([bursting], [Straight], Real(walker, 1)).Killed.Should().Be(1);
    }

    [Fact]
    public void TheMireSlowsHeavyEnemiesTwiceAsFast()
    {
        // A sniper covering the cells after a 3-cell mire: slowed walkers spend longer in its reach.
        var mire = Enumerable.Range(4, 3).ToDictionary(c => new BoardCell(c, 3), _ => TerrainKind.Mire);
        var sniper = new SimUnit("狙击", new BoardCell(1, 2), [new BoardCell(1, 3), new BoardCell(2, 3)], false, 1, true, 400, 1.0, false, 1200, 100, false);
        var walker = Grunt with { Hp = 1500, Def = 0, Atk = 0, Speed = 1.0 };
        ClearSimulator.Run([sniper], [Straight], Real(walker, 1)).Leaked.Should().Be(1);
        ClearSimulator.Run([sniper], [Straight], Real(walker, 1), mire).Leaked.Should().Be(1, "a light walker keeps most of its speed");
        ClearSimulator.Run([sniper], [Straight], Real(walker with { Weight = 3 }, 1), mire).Killed.Should().Be(1, "a heavy one sinks twice as fast");
    }

    private static SimUnit Caster(int col, double atk) => new("术师", new BoardCell(col, 2), [new BoardCell(col - 1, 3), new BoardCell(col, 3), new BoardCell(col + 1, 3)],
        false, 1, false, atk, 1.0, true, 1200, 100, false);

    [Fact]
    public void ABossCountsByHowFarItIsBeaten()
    {
        // Four grunts and a boss too tough to kill: the boss is worth at least 8 enemies, by the HP taken off it.
        var boss = Grunt with { Name = "首领", Rank = "BOSS", Hp = 100_000, Atk = 0, Speed = 0.3 };
        var stream = new WaveScenario
        {
            TailSeconds = 40,
            Spawns = [.. Enumerable.Range(0, 4).Select(k => new SimSpawn(1 + k, false, false, Grunt)), new SimSpawn(6, false, false, boss)],
        };
        var weak = ClearSimulator.Run([Guard(2, block: 3, atk: 700)], [Straight], stream);
        var strong = ClearSimulator.Run([Guard(2, block: 3, atk: 2000)], [Straight], stream);
        weak.Bosses.Should().Be(1);
        weak.BossWorth.Should().Be(SimResult.MinBossWorth);
        weak.BossProgress.Should().BeInRange(0.01, 0.99);
        strong.BossProgress.Should().BeGreaterThan(weak.BossProgress);
        weak.Handled.Should().BeApproximately(weak.Killed - weak.BossesKilled + 8 * weak.BossProgress, 1e-9);
        // Without a boss, handled is just the kills.
        ClearSimulator.Run([Guard(2)], [Straight], Small).Handled.Should().Be(6);
    }

    [Fact]
    public void TalentRidersHitHarderAndDefencesHoldLonger()
    {
        // Against heavy armour, ignoring defence (and poison on top) deals more; a damage-reduced blocker lasts.
        var armoured = Real(Grunt with { Def = 600, Hp = 100_000 }, 1);
        double plain = ClearSimulator.Run([Guard(2, block: 3, atk: 700)], [Straight], armoured).Damage["近卫"];
        ClearSimulator.Run([Guard(2, block: 3, atk: 700) with { DefIgnore = 300 }], [Straight], armoured).Damage["近卫"]
            .Should().BeGreaterThan(plain * 1.5);
        ClearSimulator.Run([Guard(2, block: 3, atk: 700) with { DotDps = 200, DotSeconds = 3 }], [Straight], armoured).Damage["近卫"]
            .Should().BeGreaterThan(plain * 1.5);
        var brutes = Real(Grunt with { Hp = 100_000, Atk = 1200 }, 1);
        ClearSimulator.Run([Guard(2, atk: 100)], [Straight], brutes).Fallen.Should().Contain("近卫");
        ClearSimulator.Run([Guard(2, atk: 100) with { Sanctuary = 0.8, Regen = 300 }], [Straight], brutes).Fallen.Should().BeEmpty();
    }

    [Fact]
    public void SummonsFightOnlyWhileOnTheField()
    {
        // A summoned blocker that shows up after the walkers passed handles nothing; one there in time holds them.
        var wolf = Guard(2) with { Key = "狼群" };
        var early = wolf with { Summon = new SummonTiming(0, 0, 10) };
        var late = wolf with { Summon = new SummonTiming(60, 0, 10) };
        ClearSimulator.Run([early], [Straight], Small).Killed.Should().Be(6);
        var r = ClearSimulator.Run([late], [Straight], Small);
        r.Killed.Should().Be(0);
        r.Fallen.Should().BeEmpty("summons off the field are not operators knocked out");
        // A short-lived one leaves mid-fight.
        ClearSimulator.Run([wolf with { Summon = new SummonTiming(0, 4, 999) }], [Straight], Small).Leaked.Should().BeGreaterThan(0);
    }

    [Fact]
    public void TheFightCountsDamageTakenAndHealing()
    {
        // The blocker takes the hits; the medic behind it heals what it can.
        var medic = new SimUnit("医疗", new BoardCell(1, 3), [new BoardCell(1, 3), new BoardCell(2, 3), new BoardCell(3, 3)], false, 0, false,
            300, 1.0, true, 1500, 100, Healer: true);
        var r = ClearSimulator.Run([Guard(2), medic], [Straight], Small);
        r.Taken["近卫"].Should().BeGreaterThan(0);
        r.Taken["医疗"].Should().Be(0);
        r.Healed["医疗"].Should().BeGreaterThan(0);
        r.Healed["近卫"].Should().Be(0);
    }

    [Fact]
    public void ZonesWearDownAndPushesHoldBack()
    {
        // A skill-only caster whose skill is a damage zone kills walkers without attacking.
        var field = new SimSkill(1, 1, 1, 999, SkillCharge.Auto) { Zone = 1.5 };
        var caster = Caster(4, 600) with { Pattern = new AttackPattern { SkillOnly = true }, Skill = field };
        ClearSimulator.Run([caster], [Straight], Small).Killed.Should().BeGreaterThan(0);
        // Pushing walkers back keeps them in a ranged unit's reach for longer.
        var walkers = Real(Grunt with { Hp = 6000 }, 2);
        var sniper = Caster(4, 500);
        double kept = ClearSimulator.Run([sniper with { Push = new PushData { Cells = 0.8 } }], [Straight], walkers).Damage["术师"];
        kept.Should().BeGreaterThan(ClearSimulator.Run([sniper], [Straight], walkers).Damage["术师"]);
    }

    [Fact]
    public void ASkillCanHitSeveralEnemiesAndDealTrueDamage()
    {
        // Two walkers side by side: one target per hit vs two; and arts vs a 90-resistance walker vs true damage.
        var pair = new WaveScenario { TailSeconds = 30, Spawns = [new SimSpawn(1, false, false, Grunt with { Hp = 200_000 }), new SimSpawn(1.2, false, false, Grunt with { Hp = 200_000 })] };
        var always = new SimSkill(1, 1, 1, 999, SkillCharge.Auto);
        var guard = Guard(2, block: 3, atk: 700) with { Skill = always };
        ClearSimulator.Run([guard], [Straight], pair).Damage["近卫"].Should().BeLessThan(
            ClearSimulator.Run([guard with { Skill = always with { HitsBlocked = true } }], [Straight], pair).Damage["近卫"]);

        var hard = Real(Grunt with { Res = 90 }, 1);
        var caster = Caster(4, 1000) with { Skill = always };
        ClearSimulator.Run([caster], [Straight], hard).Leaked.Should().Be(1);
        ClearSimulator.Run([caster with { Skill = always with { Damage = SkillDamage.True } }], [Straight], hard).Killed.Should().Be(1);
    }

    [Fact]
    public void ASkillReachesItsOwnRange()
    {
        // It casts once a walker enters its normal reach (one cell), then shoots along the skill range (five cells).
        var sniper = new SimUnit("狙击", new BoardCell(4, 2), [new BoardCell(4, 3)], false, 1, true, 600, 1.0, false, 1200, 100, false)
        {
            Skill = new SimSkill(1, 1, 1, 999, SkillCharge.Auto),
        };
        ClearSimulator.Run([sniper], [Straight], Real(Grunt, 1)).Leaked.Should().Be(1);
        var wide = sniper with { SkillRange = [.. Enumerable.Range(0, 5).Select(c => new BoardCell(c, 3))] };
        ClearSimulator.Run([wide], [Straight], Real(Grunt, 1)).Killed.Should().Be(1);
    }

    [Fact]
    public void AHealingSkillKeepsTheBlockerUp()
    {
        var tank = Guard(2, block: 3, atk: 50) with { Hp = 4000, Def = 0 };
        var hard = Real(Grunt with { Hp = 1_000_000, Atk = 400 }, 1);
        ClearSimulator.Run([tank], [Straight], hard).Fallen.Should().Contain("近卫");
        var medic = new SimUnit("医疗", new BoardCell(2, 2), [new BoardCell(2, 3)], false, 1, false, 300, 1.0, false, 1500, 100, false)
        {
            Skill = new SimSkill(1, 5, 5, 20, SkillCharge.Auto) { HealPerSecond = 1.0 },
            SkillRange = [new BoardCell(2, 3), new BoardCell(2, 2)],
        };
        // Not a healer by role: only its skill heals (300 HP a second while it runs).
        ClearSimulator.Run([tank, medic with { Atk = 300 }], [Straight], hard).Fallen.Should().NotContain("近卫");
    }

    [Fact]
    public void ControlHoldsEnemiesInReach()
    {
        // A sniper reaching one cell: a 2 s stun on each hit keeps the walker there long enough to die.
        var sniper = new SimUnit("狙击", new BoardCell(4, 2), [new BoardCell(4, 3)], false, 1, true, 500, 1.0, false, 1200, 100, false);
        ClearSimulator.Run([sniper], [Straight], Real(Grunt, 1)).Leaked.Should().Be(1);
        var stunner = sniper with { Skill = new SimSkill(1, 1, 1, 999, SkillCharge.Auto) { ControlStuns = true, ControlSeconds = 2 } };
        var r = ClearSimulator.Run([stunner], [Straight], Real(Grunt, 1));
        r.Killed.Should().Be(1);
        r.StunsAround["狙击"].Should().BeGreaterThan(0, "the stuns happen in the cell next to it");
    }

    [Fact]
    public void ASkillCanRaiseItsBlock()
    {
        var wall = Guard(2, block: 1, atk: 10) with { Hp = 100_000 };
        ClearSimulator.Run([wall], [Straight], Real(Grunt with { Atk = 0 }, 3)).Leaked.Should().BeGreaterThan(0);
        var more = wall with { Skill = new SimSkill(1, 1, 1, 999, SkillCharge.Auto) { SelfBlock = 2 } };
        ClearSimulator.Run([more], [Straight], Real(Grunt with { Atk = 0 }, 3)).Leaked.Should().Be(0);
    }

    [Fact]
    public void SplashHitsTheEnemiesAroundTheTarget()
    {
        var pair = new WaveScenario { TailSeconds = 30, Spawns = [new SimSpawn(1, false, false, Grunt with { Hp = 200_000, Atk = 0 }), new SimSpawn(1.2, false, false, Grunt with { Hp = 200_000, Atk = 0 })] };
        var guard = Guard(2, block: 3, atk: 700);
        ClearSimulator.Run([guard with { Pattern = new AttackPattern { SplashRadius = 1 } }], [Straight], pair).Damage["近卫"]
            .Should().BeGreaterThan(1.5 * ClearSimulator.Run([guard], [Straight], pair).Damage["近卫"]);
    }

    [Fact]
    public void TilesActOnWhoeverIsOnThem()
    {
        var walkers = Real(Grunt with { Atk = 0 }, 1);
        // 活性源石 under the path: the walker loses 70 HP a second from then on.
        var infection = new Dictionary<BoardCell, TerrainKind> { [new BoardCell(6, 3)] = TerrainKind.Infection };
        ClearSimulator.Run([], [Straight], Real(Grunt with { Hp = 300, Atk = 0 }, 1), infection).Killed.Should().Be(1);
        // 隐匿模块 under a sniper's only reachable cell: it cannot see the walker there.
        var sniper = new SimUnit("狙击", new BoardCell(4, 2), [new BoardCell(4, 3)], false, 1, true, 2000, 0.5, false, 1200, 100, false);
        ClearSimulator.Run([sniper], [Straight], walkers).Killed.Should().Be(1);
        var hide = new Dictionary<BoardCell, TerrainKind> { [new BoardCell(4, 3)] = TerrainKind.Hide };
        ClearSimulator.Run([sniper], [Straight], walkers, hide).Leaked.Should().Be(1);
        // ...unless 奥术 reveals what is in its range.
        ClearSimulator.Run([sniper with { Reveals = true }], [Straight], walkers, hide).Killed.Should().Be(1);
    }

    [Fact]
    public async Task SummonsJoinThePlannedFight()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "TestData", "Regulation");
        var store = new JsonDataStore(dir);
        var ops = (await store.LoadOperatorsAsync()).ToDictionary(o => o.Name);
        var (covenants, _) = await store.LoadCovenantsAsync();
        var board = (await GarrisonBoardStore.LoadAsync(dir))!;
        var routes = EnemyRouteStore.Load(dir)!;
        ops["机械师"].Profile!.NormalStats!.Summon.Should().NotBeNull();
        ops["机械师"].Profile!.NormalStats!.Summon!.RangeShape.Should().NotBeNull();
        ops["斯卡蒂"].Profile!.NormalStats!.TalentAt(5)!.Auras.Should().Contain(a => a.Scope == "members" && a.Members!.Contains("歌蕾蒂娅"));
        var planner = new PlacementPlanner(ops, covenants);
        var map = board.Find("palace")!;
        string[] pool = ["机械师", "伺夜", "塔露拉", "维伊", "夏栎", "角峰"];
        var plan = planner.Plan(map, pool.Select(n => new PlacementCandidate(n, false)).ToList(),
            new PlacementOptions { Routes = routes.Find("palace", 6, null, null), Round = 6, FieldCount = 6, Iterations = 600, SimulationIterations = 60 });
        plan.Simulation!.Damage.Keys.Should().Contain(k => k.Contains('·'), "summons fight as units of their own");
        plan.Shares.Keys.Should().BeEquivalentTo(plan.Placed.Select(p => p.Key));
        plan.Shares.Values.Sum(v => v.Damage).Should().BeApproximately(1, 1e-6);
        plan.Shares.Values.Sum(v => v.Taken).Should().BeApproximately(1, 1e-6);
    }

    [Fact]
    public async Task PlanningBySimulationHandlesAtLeastAsManyAsTheOldScore()
    {
        var dir = Path.Combine(AppContext.BaseDirectory, "TestData", "Regulation");
        var store = new JsonDataStore(dir);
        var ops = (await store.LoadOperatorsAsync()).ToDictionary(o => o.Name);
        var (covenants, _) = await store.LoadCovenantsAsync();
        var board = (await GarrisonBoardStore.LoadAsync(dir))!;
        var routes = EnemyRouteStore.Load(dir)!;
        var planner = new PlacementPlanner(ops, covenants);
        var map = board.Find("palace")!;
        string[] pool = ["塔露拉", "Mon3tr", "机械师", "乌啾", "维伊", "冻河", "折桠", "奥尔佳", "烈夏", "夏栎", "角峰", "霜叶"];
        var cands = pool.Select(n => new PlacementCandidate(n, false)).ToList();
        var options = new PlacementOptions { Routes = routes.Find("palace", 8, null, null), Round = 8, Iterations = 2000, Restarts = 2, SimulationIterations = 300 };

        var simulated = planner.Plan(map, cands, options);
        simulated.Simulation.Should().NotBeNull();
        simulated.Checks[0].Text.Should().StartWith("模拟：处理");
        var old = planner.Plan(map, cands, options with { Simulate = false });
        var judged = planner.Evaluate(map, cands, old.Placed.ToDictionary(p => p.Key, p => (p.Cell, p.Facing)),
            options with { Scenario = simulated.Scenario });
        simulated.Simulation!.Killed.Should().BeGreaterThanOrEqualTo(judged.Simulation!.Killed);
        simulated.Placed.Should().Contain(p => p.Notes.Any(n => n.StartsWith("模拟击倒")));
    }
}
