// Added for ASP Assistant Arknights on 2026-11-14; see NOTICE.
namespace ASPAssistant.Core.Models;

/// <summary>A point on the deployment grid; cell (c, r) has its centre at (c + 0.5, r + 0.5).</summary>
public sealed record RoutePoint(double Col, double Row);

/// <summary>
/// One enemy group's path this round. <see cref="Motion"/> is "walk" or "fly"; <see cref="Approx"/> = the level's
/// path was snapped to the grid; <see cref="Segments"/> are drawn one by one (a teleport splits a path).
/// </summary>
public sealed record EnemyRoute(string Motion, bool Boss, bool Approx, int Count,
    IReadOnlyList<IReadOnlyList<RoutePoint>> Segments, string? Side, bool Patrol)
{
    public bool Flying => Motion == "fly";
}

/// <summary>This round's enemy routes on one map.</summary>
public sealed record RoundRoutes(string MapId, int Round, string Level, string? BossEnemyId,
    IReadOnlyList<EnemyRoute> Routes, IReadOnlyList<RoutePoint> BossSpots)
{
    public int Walking => Routes.Where(r => !r.Flying).Sum(r => r.Count);
    public int Flying => Routes.Where(r => r.Flying).Sum(r => r.Count);
}
