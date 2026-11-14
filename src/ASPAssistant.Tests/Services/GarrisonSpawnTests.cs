// Added for ASP Assistant Regulation on 2026-11-14; see NOTICE.
using System.Diagnostics;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using FluentAssertions;
using Xunit.Abstractions;

namespace ASPAssistant.Tests.Services;

/// <summary>The real round streams (路标档案馆 data) and planning against them.</summary>
public class GarrisonSpawnTests(ITestOutputHelper output)
{
    private static readonly string Dir = Path.Combine(AppContext.BaseDirectory, "TestData", "Regulation");
    private static readonly GarrisonSpawnStore Store = GarrisonSpawnStore.Load(Dir)!;

    [Fact]
    public void OnlyTheRunsTypesComeAndEachTypeCountsTheSame()
    {
        var streams = Store.Streams(5, null, ["飞行", "隐匿", "复苏"]);
        streams.Select(s => s.Type).Distinct().Should().BeEquivalentTo(["FLY", "INVISIBLE", "REBORN"]);
        streams.Sum(s => s.Weight).Should().BeApproximately(1, 1e-6);
        foreach (var type in streams.GroupBy(s => s.Type))
            type.Sum(s => s.Weight).Should().BeApproximately(1.0 / 3, 1e-6);
    }

    [Fact]
    public void ARoundIsAllFlyingOrAllGround()
    {
        // Round 1's template also has 2 fixed 源石虫 on the ground, flying round or not.
        for (int round = 2; round <= 13; round++)
            foreach (var s in Store.Streams(round, null, []))
            {
                s.Spawns.Should().NotBeEmpty();
                s.Spawns.Select(x => x.Flying).Distinct().Should().ContainSingle($"{s.Label} in round {round}");
                s.Spawns.All(x => x.Flying).Should().Be(s.Type == "FLY", s.Label);
            }
    }

    [Fact]
    public void EnemiesCarryTheModesBuff()
    {
        // 宿主士兵 in 险境 round 5: HP 5616, attack 333 (as the site shows).
        var host = Store.Streams(5, "mode_multi_abyss", []).SelectMany(s => s.Spawns).First(s => s.Profile.Name == "宿主士兵");
        host.Profile.Hp.Should().BeApproximately(5616, 1);
        host.Profile.Atk.Should().BeApproximately(333, 1);
        var normal = Store.Streams(5, "mode_multi_normal", []).SelectMany(s => s.Spawns).First(s => s.Profile.Name == "宿主士兵");
        normal.Profile.Hp.Should().BeLessThan(host.Profile.Hp);
    }

    [Fact]
    public async Task MapsCarryTheirSpecialTiles()
    {
        var board = (await GarrisonBoardStore.LoadAsync(Dir))!;
        board.Find("desert_laser")!.Terrain.Should().Contain(new BoardCell(3, 1), TerrainKind.Infection);
        board.Find("snow")!.Terrain.Values.Should().AllBeEquivalentTo(TerrainKind.Hide).And.HaveCount(4);
        board.Find("desert_camo")!.Terrain.Values.Should().AllBeEquivalentTo(TerrainKind.Mire);
        board.Find("palace_arrows")!.Terrain.Values.Should().AllBeEquivalentTo(TerrainKind.Speedup);
        board.Find("palace")!.Terrain.Should().BeEmpty();
    }

    [Fact]
    public void TheLastRoundsBringTheirEndgame()
    {
        Store.Streams(14, null, ["飞行"]).Should().NotBeEmpty();
        Store.Streams(15, null, ["飞行"]).Should().NotBeEmpty();
        GarrisonSpawnStore.TypeCode("本局敌人：飞行").Should().Be("FLY");
        Store.Streams(14, null, [], "enemy_9013_acstmk").Should().ContainSingle();
        Store.Streams(15, null, [], "enemy_9013_acstmk").Should().ContainSingle("round 15 brings the boss's second form");
    }

    private static async Task<(PlacementPlanner Planner, GarrisonMap Map, List<PlacementCandidate> Pool, PlacementOptions Options)> Palace(
        int round, params string[] types)
    {
        var store = new JsonDataStore(Dir);
        var ops = (await store.LoadOperatorsAsync()).ToDictionary(o => o.Name);
        var (covenants, _) = await store.LoadCovenantsAsync();
        var board = (await GarrisonBoardStore.LoadAsync(Dir))!;
        var routes = EnemyRouteStore.Load(Dir)!;
        string[] pool = ["塔露拉", "Mon3tr", "机械师", "乌啾", "维伊", "冻河", "折桠", "奥尔佳", "烈夏", "夏栎", "角峰", "霜叶"];
        var options = new PlacementOptions
        {
            Routes = routes.Find("palace", round, null, null), Round = round, Iterations = 2000, Restarts = 2,
            SimulationIterations = 300, Streams = Store.Streams(round, null, types),
        };
        return (new PlacementPlanner(ops, covenants), board.Find("palace")!, [.. pool.Select(n => new PlacementCandidate(n, false))], options);
    }

    private static double Expected(PlacementPlan plan) => plan.StreamResults.Sum(r => r.Weight * r.Result.Killed);

    [Fact]
    public async Task PlansAgainstTheRunsRealEnemies()
    {
        var (planner, map, pool, options) = await Palace(8, "飞行", "元素", "特异");
        var watch = Stopwatch.StartNew();
        var plan = planner.Plan(map, pool, options);
        output.WriteLine($"{watch.ElapsedMilliseconds} ms, factor {plan.StreamFactor:0.00}");
        foreach (var c in plan.Checks) output.WriteLine(c.Text);
        foreach (var r in plan.StreamResults) output.WriteLine($"{r.Label} {r.Weight:0.00}: {r.Result.Killed}/{r.Result.Spawned}");

        plan.Flying.Should().BeTrue("飞行 is one of the run's types");
        plan.StreamResults.Select(r => r.Type).Distinct().Should().BeEquivalentTo(["FLY", "ELEMENT", "SPECIAL"]);
        plan.Checks.Take(3).Select(c => c.Text[..2]).Should().BeEquivalentTo(["飞行", "元素", "特异"]);
        plan.Basis.Should().Contain("按本局敌人");
        plan.Checks.Should().Contain(c => c.Detail != null && c.Detail.StartsWith("预计每回合叠层：") && c.IsHint);
        plan.LayerGains.Should().NotBeEmpty();

        // The old hand-tuned score's placement, judged against the same streams at the same scale, handles no more.
        var old = planner.Plan(map, pool, options with { Simulate = false });
        var judged = planner.Evaluate(map, pool, old.Placed.ToDictionary(p => p.Key, p => (p.Cell, p.Facing)),
            options with { StreamFactor = plan.StreamFactor });
        output.WriteLine($"sim plan {Expected(plan):0.0} vs old {Expected(judged):0.0}");
        Expected(plan).Should().BeGreaterThanOrEqualTo(Expected(judged) - 0.5);
    }

    [Fact]
    public async Task NoFlyingTypeMeansNoAntiAirPlanning()
    {
        var (planner, map, pool, options) = await Palace(6, "元素", "特异", "持续");
        var plan = planner.Plan(map, pool, options with { SimulationIterations = 100 });
        plan.Flying.Should().BeFalse();
        plan.Checks.Should().NotContain(c => c.Text.StartsWith("对空"));
    }
}
