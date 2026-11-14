// Added for ASP Assistant Regulation on 2026-11-14; see NOTICE.
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using FluentAssertions;

namespace ASPAssistant.Tests.Services;

/// <summary>Covenant layers by their formulas, and the covenant rules that depend on where operators stand.</summary>
public class PlacementCovenantTests
{
    private static readonly string DataDir = Path.Combine(AppContext.BaseDirectory, "TestData", "Regulation");

    private static async Task<(PlacementPlanner Planner, GarrisonMap Map)> LoadAsync()
    {
        var store = new JsonDataStore(DataDir);
        var ops = (await store.LoadOperatorsAsync()).ToDictionary(o => o.Name);
        var (covenants, _) = await store.LoadCovenantsAsync();
        var board = (await GarrisonBoardStore.LoadAsync(DataDir))!;
        return (new PlacementPlanner(ops, covenants), board.Find("palace")!);
    }

    private static List<PlacementCandidate> Pool(params string[] names) => [.. names.Select(n => new PlacementCandidate(n, false))];

    private static Dictionary<string, (BoardCell, Facing)> At(params (string Name, int Col, int Row, Facing Facing)[] cells) =>
        cells.ToDictionary(c => c.Name, c => (new BoardCell(c.Col, c.Row), c.Facing));

    [Fact]
    public void LayersAreWorthWhatTheirFormulaAdds()
    {
        // 精准 +1.2 % attack per layer, 炎 +0.9 %, 奥术 +0.4 % extra magic damage; economy covenants add no stats.
        PlacementPlanner.PerLayer("精准").Should().BeApproximately(0.012, 1e-9);
        PlacementPlanner.PerLayer("炎").Should().BeApproximately(0.009, 1e-9);
        PlacementPlanner.PerLayer("乌萨斯").Should().BeApproximately(0.0105, 1e-9, "attack and HP together");
        PlacementPlanner.PerLayer("精准").Should().BeGreaterThan(PlacementPlanner.PerLayer("炎"));
        PlacementPlanner.PerLayer("炎").Should().BeGreaterThan(PlacementPlanner.PerLayer("奥术"));
        PlacementPlanner.PerLayer("远见").Should().Be(0);
        PlacementPlanner.PerLayer("协防干员").Should().Be(0);
    }

    [Fact]
    public async Task AegirDevoursTheOperatorInFrontAndStacksItsTier()
    {
        var (planner, map) = await LoadAsync();
        var pool = Pool("幽灵鲨", "深巡", "斯卡蒂", "卡达");
        var fed = planner.Evaluate(map, pool, At(("幽灵鲨", 3, 1, Facing.Right), ("卡达", 4, 1, Facing.Right),
            ("深巡", 2, 3, Facing.Up), ("斯卡蒂", 4, 3, Facing.Left)), new PlacementOptions());
        var hungry = planner.Evaluate(map, pool, At(("幽灵鲨", 3, 1, Facing.Left), ("卡达", 4, 1, Facing.Right),
            ("深巡", 2, 3, Facing.Up), ("斯卡蒂", 4, 3, Facing.Left)), new PlacementOptions());

        fed.Find("幽灵鲨")!.Notes.Should().Contain(n => n.StartsWith("阿戈尔吞噬身前 卡达"));
        fed.Find("卡达")!.Notes.Should().Contain(n => n.Contains("被 幽灵鲨 吞噬") && n.Contains("离场"), "a 1-tier caster has far less than 5000 HP");
        fed.Checks.Should().Contain(c => c.Text.StartsWith("幽灵鲨 吞噬 卡达") && c.IsHint);
        fed.Stacking.Should().BeGreaterThan(hungry.Stacking, "the devour adds 阿戈尔 layers equal to the prey's tier");
        hungry.Find("幽灵鲨")!.Notes.Should().Contain("阿戈尔吞噬：身前没人，没吞到");
    }

    [Fact]
    public async Task WithSixAegirTheFirstThreeDevouredAegirRevive()
    {
        // A fourth devour would be one knock-out too many: only the first three revive.
        var (planner, map) = await LoadAsync();
        var pool = Pool("幽灵鲨", "深巡", "斯卡蒂", "歌蕾蒂娅", "归溟幽灵鲨", "乌尔比安", "卡达");
        // A column of 阿戈尔 each facing up: the lower one devours the one above it.
        var plan = planner.Evaluate(map, pool, At(("深巡", 3, 3, Facing.Up), ("乌尔比安", 3, 2, Facing.Up),
            ("幽灵鲨", 3, 1, Facing.Up), ("歌蕾蒂娅", 2, 2, Facing.Left), ("归溟幽灵鲨", 2, 3, Facing.Up),
            ("斯卡蒂", 4, 3, Facing.Right), ("卡达", 1, 3, Facing.Right)), new PlacementOptions());
        plan.ActiveCovenants.Should().Contain("阿戈尔");
        plan.Find("乌尔比安")!.Notes.Should().Contain(n => n.Contains("阿戈尔 6 立刻复活"));
        plan.Checks.Count(c => c.Text.Contains("吞噬")).Should().Be(3);
        plan.Checks.Where(c => c.Text.Contains("吞噬")).Should().OnlyContain(c => !c.Text.Contains("离场"),
            "every devoured operator here is a 阿戈尔 and at most three are knocked out");
    }

    [Fact]
    public async Task ASkillThatNeverEndsIsCastOnce()
    {
        // 乌有's skill lasts for the rest of the fight: 华法琳 behind it gets one cast's stacking, not seven.
        var (planner, map) = await LoadAsync();
        var plan = planner.Evaluate(map, Pool("华法琳", "乌有", "折桠"),
            At(("华法琳", 2, 3, Facing.Right), ("乌有", 3, 3, Facing.Right), ("折桠", 4, 3, Facing.Right)), new PlacementOptions());
        var note = plan.Find("华法琳")!.Notes.Single(n => n.StartsWith("身前 乌有 开技能叠层"));
        note.Should().MatchRegex(@"约 (0|1)(\.\d)? 次");
    }

    [Fact]
    public async Task HelperStacksEveryActiveCovenant()
    {
        var (planner, map) = await LoadAsync();
        var plan = planner.Evaluate(map, Pool("野鬃", "夏栎", "烈夏", "折桠", "奥尔佳"),
            At(("野鬃", 3, 3, Facing.Right), ("夏栎", 2, 2, Facing.Right), ("烈夏", 4, 3, Facing.Right),
                ("折桠", 5, 3, Facing.Right), ("奥尔佳", 3, 2, Facing.Right)), new PlacementOptions());
        plan.ActiveCovenants.Should().Contain("助力").And.Contain("乌萨斯");
        plan.Placed.SelectMany(p => p.Notes).Should().Contain(n => n.StartsWith("助力：每回合所有已激活盟约 +2"));
    }

    [Fact]
    public async Task ConvergenceHealsAlliesInItsRange()
    {
        var (planner, map) = await LoadAsync();
        var plan = planner.Evaluate(map, Pool("乌啾", "风絮", "折桠"),
            At(("乌啾", 2, 2, Facing.Down), ("风絮", 3, 2, Facing.Down), ("折桠", 2, 3, Facing.Right)), new PlacementOptions());
        plan.ActiveCovenants.Should().Contain("聚合");
        plan.Placed.SelectMany(p => p.Notes).Should().Contain(n => n.StartsWith("聚合：范围内回血") && n.Contains("折桠"));
    }

    [Fact]
    public async Task KazimierzSixSuggestsTheBenchReserve()
    {
        var (planner, map) = await LoadAsync();
        var pool = Pool("砾", "野鬃", "瑕光", "远牙", "玛恩纳", "历阵锐枪芬", "焰尾", "锏");
        var plan = planner.Evaluate(map, pool,
            At(("砾", 3, 3, Facing.Right), ("野鬃", 4, 3, Facing.Right), ("瑕光", 2, 3, Facing.Right),
                ("远牙", 2, 1, Facing.Right), ("玛恩纳", 3, 1, Facing.Right), ("历阵锐枪芬", 3, 2, Facing.Right)),
            new PlacementOptions());
        plan.Checks.Should().Contain(c => c.Text.StartsWith("整备区最右放") && c.Text.Contains("卡西米尔替补") && c.IsHint);
        plan.Checks.Single(c => c.Text.StartsWith("整备区最右放")).Text.Should().ContainAny("焰尾", "锏");
    }

    [Fact]
    public async Task CurrentLayersStrengthenTheMembers()
    {
        var (planner, map) = await LoadAsync();
        var pool = Pool("烈夏", "折桠", "奥尔佳", "维伊", "夏栎");
        var placement = At(("折桠", 3, 3, Facing.Right), ("烈夏", 3, 2, Facing.Down), ("奥尔佳", 4, 3, Facing.Right),
            ("维伊", 2, 2, Facing.Right), ("夏栎", 2, 3, Facing.Up));
        var none = planner.Evaluate(map, pool, placement, new PlacementOptions());
        var stacked = planner.Evaluate(map, pool, placement,
            new PlacementOptions { CovenantLayers = new Dictionary<string, int> { ["乌萨斯"] = 100 } });

        stacked.Combat.Should().BeGreaterThan(none.Combat, "乌萨斯 members attack 75 % harder at 100 layers");
        stacked.Checks.Should().Contain(c => c.IsHint && c.Text == "乌萨斯 100 层：成员攻击 +75%、生存 +30%");
        // Layers of a covenant that is not active change nothing.
        var inactive = planner.Evaluate(map, pool, placement,
            new PlacementOptions { CovenantLayers = new Dictionary<string, int> { ["炎"] = 100 } });
        inactive.Combat.Should().Be(none.Combat);
    }

    [Fact]
    public async Task SafetyPenaltiesGrowWithTheTeamsStrength()
    {
        var (planner, map) = await LoadAsync();
        // Nobody blocks the lower lane: the penalty must not shrink relative to the stronger team.
        var pool = Pool("烈夏", "奥尔佳", "维伊");
        var placement = At(("烈夏", 4, 0, Facing.Down), ("奥尔佳", 5, 0, Facing.Left), ("维伊", 3, 0, Facing.Right));
        var weak = planner.Evaluate(map, pool, placement, new PlacementOptions());
        var strong = planner.Evaluate(map, pool, placement,
            new PlacementOptions { CovenantLayers = new Dictionary<string, int> { ["乌萨斯"] = 100 } });
        weak.Checks.Should().Contain(c => c.Text.Contains("没人拦"));
        (strong.Combat - weak.Combat).Should().BeLessThan(Math.Abs(weak.Combat),
            "more damage from layers does not buy the unblocked lane back");
    }

    [Fact]
    public async Task DexterityReachesEightCellsAtFortyLayers()
    {
        var (planner, map) = await LoadAsync();
        var pool = Pool("霜叶", "奥尔佳", "烈夏");
        var placement = At(("霜叶", 3, 2, Facing.Right), ("奥尔佳", 4, 3, Facing.Right), ("烈夏", 4, 1, Facing.Right));
        var four = planner.Evaluate(map, pool, placement, new PlacementOptions());
        var eight = planner.Evaluate(map, pool, placement,
            new PlacementOptions { CovenantLayers = new Dictionary<string, int> { ["灵巧"] = 40 } });

        four.Placed.SelectMany(p => p.Notes).Should().NotContain(n => n.StartsWith("灵巧光环"), "both neighbours are diagonal");
        eight.Placed.SelectMany(p => p.Notes).Should().Contain(n => n.StartsWith("灵巧光环（周围八格攻速）"));
    }
}
