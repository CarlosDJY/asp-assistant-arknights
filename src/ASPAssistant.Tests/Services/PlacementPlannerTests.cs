using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

public class PlacementPlannerTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "TestData", "Regulation");

    private static readonly string[] UrsusPool =
        ["塔露拉", "Mon3tr", "机械师", "乌啾", "维伊", "冻河", "折桠", "奥尔佳", "烈夏", "夏栎", "角峰", "霜叶"];

    private static async Task<(PlacementPlanner Planner, GarrisonBoardData Board)> LoadAsync()
    {
        var store = new JsonDataStore(DataDir);
        var ops = (await store.LoadOperatorsAsync()).ToDictionary(o => o.Name);
        var (covenants, _) = await store.LoadCovenantsAsync();
        var board = (await GarrisonBoardStore.LoadAsync(DataDir))!;
        return (new PlacementPlanner(ops, covenants), board);
    }

    private static PlacementOptions Fast(int seed = 1) => new() { Iterations = 3000, Restarts = 3, Seed = seed };

    [Fact]
    public async Task Plan_PicksEightFromTwelveAndHoldsBothRoutes()
    {
        var (planner, board) = await LoadAsync();
        var map = board.Find("palace_arrows")!;
        var plan = planner.Plan(map, UrsusPool.Select(n => new PlacementCandidate(n, false)).ToList(), Fast());

        plan.Placed.Should().HaveCount(8);
        plan.Bench.Should().HaveCount(4);
        plan.Placed.Select(p => p.Cell).Should().OnlyHaveUniqueItems();
        foreach (var p in plan.Placed)
            GarrisonMap.CanDeploy(map.At(p.Cell), planner.IsMelee(p.Name)).Should().BeTrue(p.Name);
        plan.ActiveCovenants.Should().Contain("乌萨斯");
        // Both routes have someone blocking.
        foreach (var route in map.Routes)
            plan.Placed.Should().Contain(p => route.Contains(p.Cell));
        plan.Checks.Should().Contain(c => c.Text.StartsWith("下路阻挡"));
    }

    [Fact]
    public async Task Plan_KeepsPinnedOperators()
    {
        var (planner, board) = await LoadAsync();
        var map = board.Find("palace_arrows")!;
        var pin = new PlacementPin(new BoardCell(2, 3), Facing.Up);
        var options = Fast() with { Pins = new Dictionary<string, PlacementPin> { ["塔露拉"] = pin } };
        var plan = planner.Plan(map, UrsusPool.Select(n => new PlacementCandidate(n, false)).ToList(), options);

        var t = plan.Find("塔露拉");
        t.Should().NotBeNull();
        t!.Cell.Should().Be(pin.Cell);
        t.Facing.Should().Be(Facing.Up);
        t.IsPinned.Should().BeTrue();
        plan.Placed.Should().HaveCount(8);
    }

    [Fact]
    public async Task Plan_KeepsFieldAndBenchPins()
    {
        // Whoever the plan would pick: the field pins go on, the bench pins stay off.
        var (planner, board) = await LoadAsync();
        var map = board.Find("palace_arrows")!;
        var pool = UrsusPool.Select(n => new PlacementCandidate(n, false)).ToList();
        var free = planner.Plan(map, pool, Fast());
        var benched = free.Bench.Select(b => b.Name).ToList();
        var placed = free.Placed.Select(p => p.Key).ToList();
        var up = new HashSet<string> { benched[0], benched[1] };
        var down = new HashSet<string> { placed[0], placed[1] };
        var plan = planner.Plan(map, pool, Fast() with { FieldPins = up, BenchPins = down });
        plan.Placed.Should().HaveCount(8);
        plan.Placed.Select(p => p.Key).Should().Contain(up).And.NotContain(down);
        plan.Bench.Select(b => b.Name).Should().Contain(down);
    }

    [Fact]
    public async Task TransformsCountOnlyOnTheFieldAndHintsFillTheGap()
    {
        // 炎 takes 3: two field operators carrying a 炎 转职 leave it one short (a hint); a third activates it.
        var (planner, board) = await LoadAsync();
        var map = board.Find("palace_arrows")!;
        var plain = UrsusPool.Select(n => new PlacementCandidate(n, false)).ToList();
        var free = planner.Plan(map, plain, Fast());
        var at = free.Placed.ToDictionary(p => p.Key, p => (p.Cell, p.Facing));
        var holders = free.Placed.Take(3).Select(p => p.Key).ToList();
        List<PlacementCandidate> With(int count) =>
            [.. plain.Select(c => holders.Take(count).Contains(c.Key) ? c with { Transforms = ["炎"] } : c)];
        var options = Fast() with { TransformableCovenants = new HashSet<string> { "炎" } };

        var two = planner.Evaluate(map, With(2), at, options);
        two.ActiveCovenants.Should().NotContain("炎");
        two.TransformHints.Should().ContainSingle(h => h.Covenant == "炎" && h.Have == 2 && h.Need == 3)
            .Which.Holder.Should().NotBe(holders[0]).And.NotBe(holders[1]);
        planner.Evaluate(map, With(3), at, options).ActiveCovenants.Should().Contain("炎");
        // A holder benched takes its 转职 with it.
        var benched = at.Where(kv => kv.Key != holders[0]).ToDictionary(kv => kv.Key, kv => kv.Value);
        planner.Evaluate(map, With(3), benched, options).ActiveCovenants.Should().NotContain("炎");
    }

    [Fact]
    public async Task Evaluate_AndAlternatives_ScoreAGivenArrangement()
    {
        var (planner, board) = await LoadAsync();
        var map = board.Find("palace_arrows")!;
        var pool = UrsusPool.Select(n => new PlacementCandidate(n, false)).ToList();
        var plan = planner.Plan(map, pool, Fast());
        var placement = plan.Placed.ToDictionary(p => p.Name, p => (p.Cell, p.Facing));

        planner.Evaluate(map, pool, placement, Fast()).Total.Should().BeApproximately(plan.Total, 0.2);
        var alts = planner.Alternatives(map, pool, placement, plan.Placed[0].Name, Fast());
        alts.Should().NotBeEmpty();
        alts.Should().OnlyContain(a => !placement.Values.Select(v => v.Cell).Contains(a.Cell));
    }

    [Fact]
    public async Task Plan_WorksOnEveryMap()
    {
        var (planner, board) = await LoadAsync();
        var pool = UrsusPool.Select(n => new PlacementCandidate(n, false)).ToList();
        foreach (var map in board.Maps)
        {
            var plan = planner.Plan(map, pool, new PlacementOptions { Iterations = 800, Restarts = 1 });
            plan.Placed.Should().HaveCount(8, map.Id);
        }
    }

    [Fact]
    public async Task PoolBuilder_GroupsTrackedExtensionAndOther()
    {
        var ops = (await new JsonDataStore(DataDir).LoadOperatorsAsync()).ToDictionary(o => o.Name);
        var tracked = new List<TrackingEntry>
        {
            new() { Name = "塔露拉", Type = TrackingType.Operator },
            new() { Name = "冻河", Type = TrackingType.Operator },
            new() { Name = "折桠", Type = TrackingType.Operator, Role = TrackingRole.Transition, Stages = "前期", ExpiresAt = 3 },
        };
        var state = new DeployPoolState { Owned = ["塔露拉", "夏栎"], Extra = ["夏栎"] };
        var groups = DeployPoolBuilder.Build(tracked, ops, state);

        groups.Select(g => g.Kind).Should().Equal(DeployPoolGroupKind.Final, DeployPoolGroupKind.Transition,
            DeployPoolGroupKind.Extension, DeployPoolGroupKind.Other);
        groups[0].Items.Should().Contain(i => i.Name == "塔露拉" && i.Owned);
        groups[0].Items.Should().Contain(i => i.Name == "冻河" && !i.Owned);
        groups[2].Items.Should().Contain(i => i.Name == "维伊");          // 乌萨斯, not tracked
        groups[2].Items.Should().Contain(i => i.Name == "涤火杰西卡", "every other operator is in it, sharing a covenant or not");
        groups[2].Items.Should().HaveCount(ops.Count - 3 - 1, "all but the 3 tracked and the 1 added by hand");
        groups[2].Items.Should().NotContain(i => i.Name == "折桠");
        groups[2].Items.Select(i => i.Operator!.Tier).Should().BeInAscendingOrder();   // banded by tier in the UI
        groups[3].Items.Should().ContainSingle(i => i.Name == "夏栎" && i.Removable);
    }

    [Fact]
    public async Task Plan_KeepsAHealerOnTheField()
    {
        var (planner, board) = await LoadAsync();
        // Damage-heavy pool with a single healer: the healer still goes on and reaches a block point.
        string[] pool = ["塔露拉", "机械师", "维伊", "冻河", "折桠", "奥尔佳", "烈夏", "角峰", "霜叶", "赫拉格", "乌啾"];
        foreach (var id in new[] { "palace_arrows", "desert_crates", "city" })
        {
            // The fast score (the planner's first stage) keeps the healer on; with layers weighted ×2.5 it may stand
            // where it stacks rather than by the block point — the simulation against the round's real enemies puts
            // it in reach (the test below).
            var plan = planner.Plan(board.Find(id)!, pool.Select(n => new PlacementCandidate(n, false)).ToList(), Fast() with { Simulate = false });
            plan.Placed.Should().Contain(p => p.Name == "乌啾", id);
        }
    }

    [Fact]
    public async Task Plan_KeepsAHealerOnTheField_AgainstRealEnemies()
    {
        var (planner, board) = await LoadAsync();
        var dir = Path.Combine(AppContext.BaseDirectory, "TestData", "Regulation");
        var spawns = ASPAssistant.Core.Data.GarrisonSpawnStore.Load(dir)!;
        var routes = EnemyRouteStore.Load(dir)!;
        string[] pool = ["塔露拉", "机械师", "维伊", "冻河", "折桠", "奥尔佳", "烈夏", "角峰", "霜叶", "赫拉格", "乌啾"];
        foreach (var id in new[] { "palace_arrows", "desert_crates", "city" })
        {
            var options = Fast() with
            {
                Routes = routes.Find(id, 8, null, null), Round = 8, SimulationIterations = 300,
                Streams = spawns.Streams(8, null, ["特异", "致命", "元素"]),
            };
            var plan = planner.Plan(board.Find(id)!, pool.Select(n => new PlacementCandidate(n, false)).ToList(), options);
            plan.Placed.Should().Contain(p => p.Name == "乌啾", id);
        }
    }

    private static readonly string[] VictoriaPool =
        ["涤火杰西卡", "塞雷娅", "维娜·维多利亚", "白铁", "凯瑟琳", "哈洛德", "酒神", "慕斯", "罗比菈塔", "云迹", "空弦", "星源"];

    [Fact]
    public void ClearPool_ForgetsOwnedEliteCountsAndHandAdded_KeepsSettings()
    {
        var state = new DeployPoolState { Owned = ["塔露拉", "夏栎"], Extra = ["夏栎"], FieldCount = 9, ManualMap = "palace" };
        state.SetElite("塔露拉", true);
        state.SetCount("塔露拉", 2);
        state.ClearPool();
        state.IsPoolEmpty.Should().BeTrue();
        state.Elite.Should().BeEmpty();
        state.Counts.Should().BeEmpty();
        state.CountOf("塔露拉").Should().Be(0);
        (state.FieldCount, state.ManualMap).Should().Be((9, "palace"));
    }

    [Fact]
    public async Task Plan_PutsJessicaInFrontOfSaria()
    {
        var (planner, board) = await LoadAsync();
        // On 沙漠（中央木箱） both routes meet at one cell and 杰西卡 holds it alone, so 塞雷娅 stands behind her and
        // copies her round-end trait.
        // Fast score only: see Plan_KeepsAHealerOnTheField on the simulation's generic enemy.
        var crates = planner.Plan(board.Find("desert_crates")!, VictoriaPool.Select(n => new PlacementCandidate(n, false)).ToList(), Fast(2) with { Simulate = false });
        crates.Find("塞雷娅")!.Notes.Should().Contain(n => n.StartsWith("复制身前 涤火杰西卡"));
        // With two separate routes 塞雷娅 (a defender with about 7 500 effective HP) is worth more holding the second
        // line than copying a trait; either way she must have a job on the field, not just stand somewhere.
        var plan = planner.Plan(board.Find("palace")!, VictoriaPool.Select(n => new PlacementCandidate(n, false)).ToList(), new PlacementOptions { Simulate = false });
        var saria = plan.Find("塞雷娅");
        saria.Should().NotBeNull();
        saria!.Notes.Should().Contain(n => n.StartsWith("复制身前") || n.Contains("拦截点"));
    }

    [Fact]
    public async Task Plan_PlacesDuplicateCopiesButCountsCovenantsOnce()
    {
        var (planner, board) = await LoadAsync();
        var map = board.Find("palace_arrows")!;
        // Two 冻河 and two 维伊: both copies can go on the field, but 乌萨斯 counts different operators.
        var pool = new List<PlacementCandidate>
        {
            new("塔露拉", false), new("机械师", false), new("Mon3tr", false), new("折桠", false),
            new("冻河", false), new("冻河", false, 1), new("维伊", false), new("维伊", false, 1),
        };
        var plan = planner.Plan(map, pool, Fast());
        plan.Placed.Should().HaveCount(8);
        plan.Placed.Select(p => p.Key).Should().OnlyHaveUniqueItems().And.Contain(["冻河", "冻河#2", "维伊#2"]);
        plan.Placed.Where(p => p.Name == "冻河").Should().HaveCount(2);
        // 塔露拉, 冻河, 维伊, 折桠 → 4 different 乌萨斯 operators: not 6, so no "乌萨斯 6" check.
        plan.Checks.Should().NotContain(c => c.Text == "乌萨斯 6");
        plan.Checks.Should().Contain(c => c.Text.StartsWith("乌萨斯 4"));
    }

    [Fact]
    public void PoolState_TracksCopies()
    {
        var state = new DeployPoolState();
        state.SetCount("冻河", 2);
        state.CountOf("冻河").Should().Be(2);
        state.IsOwned("冻河").Should().BeTrue();
        state.SetCount("冻河", 1);
        state.Counts.Should().NotContainKey("冻河");
        state.SetCount("冻河", 0);
        state.IsOwned("冻河").Should().BeFalse();
    }
}
