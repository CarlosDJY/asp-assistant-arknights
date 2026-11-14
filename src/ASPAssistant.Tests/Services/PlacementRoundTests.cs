// Added for ASP Assistant Regulation on 2026-11-14; see NOTICE.
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

/// <summary>Placement with this round's real routes: ground lanes, flying enemies, stacking against the fight.</summary>
public class PlacementRoundTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "TestData", "Regulation");

    private static readonly string[] Pool =
        ["塔露拉", "Mon3tr", "机械师", "乌啾", "维伊", "冻河", "折桠", "奥尔佳", "烈夏", "夏栎", "角峰", "霜叶"];

    private static async Task<(PlacementPlanner Planner, GarrisonBoardData Board, EnemyRouteStore Routes)> LoadAsync()
    {
        var store = new JsonDataStore(DataDir);
        var ops = (await store.LoadOperatorsAsync()).ToDictionary(o => o.Name);
        var (covenants, _) = await store.LoadCovenantsAsync();
        var board = (await GarrisonBoardStore.LoadAsync(DataDir))!;
        return (new PlacementPlanner(ops, covenants), board, EnemyRouteStore.Load(DataDir)!);
    }

    private static List<PlacementCandidate> Candidates() => [.. Pool.Select(n => new PlacementCandidate(n, false))];

    private static PlacementOptions Fast(RoundRoutes? routes, bool flying = true, int seed = 1) =>
        new() { Iterations = 3000, Restarts = 3, Seed = seed, Routes = routes, ExpectFlying = flying };

    [Fact]
    public async Task RoundOneHasOnlyTheLowerLane()
    {
        var (planner, board, routes) = await LoadAsync();
        var map = board.Find("palace")!;
        var r1 = routes.Find("palace", 1, null, null)!;
        r1.Routes.Where(r => !r.Flying).Should().NotBeEmpty();

        var plan = planner.Plan(map, Candidates(), Fast(r1));

        plan.Lanes.Should().NotBeEmpty();
        plan.Lanes.Should().OnlyContain(l => l.Label.StartsWith("下路"), "round 1 sends every ground enemy from the lower spawn");
        plan.Checks.Should().NotContain(c => c.Text.Contains("上路"));
        plan.Basis.Should().StartWith("第 1 回合路线");
        plan.Lanes.Sum(l => l.Share).Should().BeApproximately(1, 1e-6);
    }

    [Fact]
    public async Task LanesFollowTheRealPathsNotTheFixedOnes()
    {
        var (planner, board, routes) = await LoadAsync();
        var map = board.Find("palace")!;
        var r5 = routes.Find("palace", 5, null, null)!;

        var plan = planner.Plan(map, Candidates(), Fast(r5));

        // Round 5's upper lane walks along row 0 to column 1 and down it (checked in game; the generated data cut
        // through (3,1) and is corrected by build_garrison_routes.py).
        var upper = plan.Lanes.Where(l => l.Label.StartsWith("上路")).ToList();
        upper.Should().NotBeEmpty();
        upper.Should().OnlyContain(l => l.Cells.Contains(new BoardCell(1, 0)) && l.Cells.Contains(new BoardCell(1, 2)) && !l.Cells.Contains(new BoardCell(3, 1)));
        // Weights follow the enemy counts: the lower lanes carry most of the walkers in this round.
        plan.Lanes.Where(l => l.Label.StartsWith("下路")).Sum(l => l.Share).Should().BeGreaterThan(0.5);
    }

    [Fact]
    public async Task FlyingRoundsAskForAntiAirAndGroundOnlyRoundsDoNot()
    {
        var (planner, board, routes) = await LoadAsync();
        var map = board.Find("palace")!;
        var r8 = routes.Find("palace", 8, null, null)!;
        r8.Flying.Should().BeGreaterThan(0);

        var withAir = planner.Plan(map, Candidates(), Fast(r8, flying: true));
        var groundOnly = planner.Plan(map, Candidates(), Fast(r8, flying: false));

        withAir.Flying.Should().BeTrue();
        withAir.Checks.Should().Contain(c => c.Text.StartsWith("对空"));
        withAir.Placed.Should().Contain(p => p.Notes.Any(n => n.StartsWith("对空覆盖")));
        groundOnly.Flying.Should().BeFalse();
        groundOnly.Checks.Should().NotContain(c => c.Text.StartsWith("对空"));
        groundOnly.Basis.Should().Contain("按无飞行");

        // The same arrangement scores lower once flyers are expected and nobody can reach them.
        var meleeOnly = Pool.Where(planner.IsMelee).Take(6).ToList();
        var cells = map.AllCells().Where(c => c.Type is BoardCellType.Ground or BoardCellType.Special).Select(c => c.Cell).Take(meleeOnly.Count).ToList();
        var placement = meleeOnly.Zip(cells).ToDictionary(x => x.First, x => (x.Second, Facing.Right));
        var meleePool = meleeOnly.Select(n => new PlacementCandidate(n, false)).ToList();
        var air = planner.Evaluate(map, meleePool, placement, Fast(r8, flying: true));
        var ground = planner.Evaluate(map, meleePool, placement, Fast(r8, flying: false));
        air.Combat.Should().BeLessThan(ground.Combat);
        // Melee only reach flyers when they attack at range (the 领主 branch), so the coverage falls short.
        air.Checks.Should().Contain(c => c.Text.StartsWith("对空") && !c.Ok);
        var lords = (await new JsonDataStore(DataDir).LoadOperatorsAsync())
            .Where(o => o.Profile?.SubProfession == "领主").Select(o => o.Name).ToHashSet();
        air.Placed.Where(p => p.Notes.Any(n => n.StartsWith("对空覆盖"))).Select(p => p.Name)
            .Should().OnlyContain(n => lords.Contains(n));
    }

    [Fact]
    public async Task WithoutRoundRoutesThePlanUsesTheMapsFixedRoutes()
    {
        var (planner, board, _) = await LoadAsync();
        var map = board.Find("palace_arrows")!;

        var plan = planner.Plan(map, Candidates(), Fast(null));

        plan.Flying.Should().BeFalse();
        plan.Basis.Should().StartWith("地图默认路线");
        plan.Lanes.Select(l => l.Label).Should().Equal("上路", "下路");
        plan.Lanes[1].Share.Should().BeApproximately(2.0 / 3, 1e-6);
    }

    [Fact]
    public void StackingWeighsTheSameInEveryRound()
    {
        new PlacementOptions { Round = 1 }.StackWeight.Should().Be(2.5);
        new PlacementOptions { Round = 15 }.StackWeight.Should().Be(2.5, "layers are what beats the bosses at the end");
        new PlacementOptions { Mode = PlacementMode.Stacking, Round = 8 }.StackWeight.Should().Be(5);
        new PlacementOptions { Mode = PlacementMode.Combat }.StackWeight.Should().Be(0.25);
    }

    [Fact]
    public async Task EveryMapPlansWithEveryRoundsRoutes()
    {
        var (planner, board, routes) = await LoadAsync();
        foreach (var map in board.Maps)
            foreach (int round in new[] { 1, 6, 12 })
            {
                var rr = routes.Find(map.Id, round, null, null);
                rr.Should().NotBeNull($"{map.Id} round {round}");
                var plan = planner.Plan(map, Candidates(), new PlacementOptions { Iterations = 600, Restarts = 1, Routes = rr });
                plan.Placed.Should().HaveCount(8, $"{map.Id} round {round}");
                plan.Lanes.Should().NotBeEmpty($"{map.Id} round {round}");
            }
    }

    [Fact]
    public void RouteStoreKnowsHowManyRoundsAModeHas()
    {
        var routes = EnemyRouteStore.Load(DataDir)!;
        routes.RoundCount(null).Should().Be(15);
    }
}
