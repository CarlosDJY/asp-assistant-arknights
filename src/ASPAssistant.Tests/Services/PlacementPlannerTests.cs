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
            var plan = planner.Plan(board.Find(id)!, pool.Select(n => new PlacementCandidate(n, false)).ToList(), Fast());
            plan.Placed.Should().Contain(p => p.Name == "乌啾", id);
            plan.Checks.Should().Contain(c => c.Text.StartsWith("奶 ") && c.Ok, id);
        }
    }

    private static readonly string[] VictoriaPool =
        ["涤火杰西卡", "塞雷娅", "维娜·维多利亚", "白铁", "凯瑟琳", "哈洛德", "酒神", "慕斯", "罗比菈塔", "云迹", "空弦", "星源"];

    [Fact]
    public async Task Plan_PutsJessicaInFrontOfSaria()
    {
        var (planner, board) = await LoadAsync();
        // On 沙漠（中央木箱） both routes meet at one cell and 杰西卡 has to hold it, so 塞雷娅 copies 罗比菈塔
        // there instead; the palace has room for 塞雷娅 → 杰西卡 on a line.
        var crates = planner.Plan(board.Find("desert_crates")!, VictoriaPool.Select(n => new PlacementCandidate(n, false)).ToList(), Fast(2));
        crates.Find("塞雷娅")!.Notes.Should().Contain(n => n.StartsWith("复制身前"));
        foreach (var id in new[] { "palace" })
        {
            var plan = planner.Plan(board.Find(id)!, VictoriaPool.Select(n => new PlacementCandidate(n, false)).ToList(), Fast(2));
            var saria = plan.Find("塞雷娅");
            var jessica = plan.Find("涤火杰西卡");
            saria.Should().NotBeNull(id);
            jessica.Should().NotBeNull(id);
            var (fx, fy) = PlacementCells.Vector(saria!.Facing);
            jessica!.Cell.Should().Be(new BoardCell(saria.Cell.Col + fx, saria.Cell.Row + fy), $"{id}: 塞雷娅复制身前杰西卡的特质");
            saria.Notes.Should().Contain(n => n.StartsWith("复制身前 涤火杰西卡"));
        }
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
