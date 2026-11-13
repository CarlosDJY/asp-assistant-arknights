// Added for ASP Assistant Arknights on 2026-11-14; see NOTICE.
using ASPAssistant.Core.Data;
using FluentAssertions;

namespace ASPAssistant.Tests.Data;

/// <summary>本回合敌人路线: the level each round plays and its routes on the recognised map.</summary>
public class EnemyRouteStoreTests
{
    private static readonly EnemyRouteStore Store = EnemyRouteStore.Load(Path.Combine(AppContext.BaseDirectory, "TestData", "Regulation"))!;

    [Fact]
    public void OrdinaryRoundsPlayTheSameLevelInEveryMode()
    {
        var palace = Store.Find("palace", 1, "规程等级5", null)!;
        palace.Level.Should().Be("01");
        Store.Find("palace", 1, "险境模拟", null)!.Level.Should().Be("01");
        Store.Find("palace", 1, null, null)!.Level.Should().Be("01", "the mode is not needed outside boss rounds");
        // Ground enemies come from the lower spawn (7, 3) and end at the defence point (-1, 3).
        var walk = palace.Routes.First(r => !r.Flying);
        walk.Segments[0][0].Should().Be(new Core.Models.RoutePoint(7.5, 3.5));
        walk.Segments[0][^1].Should().Be(new Core.Models.RoutePoint(-0.5, 3.5));
        palace.Walking.Should().BeGreaterThan(0);
    }

    [Fact]
    public void BossRoundsNeedTheBossAndTheMode()
    {
        Store.Find("palace", 14, "险境模拟", null, out var reason).Should().BeNull();
        reason.Should().Be("首领还没识别");
        Store.Find("palace", 14, null, "enemy_9013_acstmk", out reason).Should().BeNull();
        reason.Should().Be("模式还没识别");
        Store.Find("palace", 14, "险境模拟", "enemy_9013_acstmk")!.Level.Should().Be("h07_01");
        Store.Find("palace", 14, "规程等级5", "enemy_9013_acstmk")!.Level.Should().Be("h07_01_s");
    }

    [Fact]
    public void ModeNamesFromTheHud()
    {
        Store.IsSolo("规程等级 12").Should().BeTrue();
        Store.IsSolo("极速模拟").Should().BeTrue();
        Store.IsSolo("终极模拟").Should().BeFalse();
        Store.IsSolo(null).Should().BeNull();
    }

    [Fact]
    public void EveryRecognisedMapHasRoutes()
    {
        foreach (var id in new[] { "desert_crates", "desert_cactus", "desert_steel", "desert_laser", "city", "desert_camo", "snow", "palace", "snow_ice", "palace_arrows" })
            Store.Find(id, 3, null, null).Should().NotBeNull(id);
        Store.Find("unknown", 3, null, null, out var reason).Should().BeNull();
        reason.Should().Be("这张地图没有数据");
    }
}
