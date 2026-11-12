// Added for ASP Assistant Arknights on 2026-11-13; see NOTICE.
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

/// <summary>How one target covenant stands for a lineup (with this run's bans taken out).</summary>
public sealed record TargetState(string Name, int Tier, bool IsCore, int Count, int Needed, bool CovenantBanned)
{
    public bool Reached => !CovenantBanned && Count >= Needed;
    public int Missing => Math.Max(0, Needed - Count);
    public string Label => $"{Name} {(Tier >= 2 ? "Ⅱ" : "Ⅰ")}";
}

/// <summary>本局推荐里一套阵容的状态，按好坏排序。</summary>
public enum RecommendationKind
{
    /// <summary>没人被禁，目标都满足。</summary>
    Usable,
    /// <summary>有人被禁，但目标照样满足。</summary>
    BannedButFine,
    /// <summary>目标会掉档：可以修补。</summary>
    NeedsRepair,
    /// <summary>目标里有本局不完整的核心盟约，原目标不可能凑齐。</summary>
    Broken,
}

public sealed record LineupRecommendation(
    Lineup Lineup,
    RecommendationKind Kind,
    IReadOnlyList<string> Banned,
    IReadOnlyList<TargetState> Targets,
    string Note)
{
    public string StatusText => Kind switch
    {
        RecommendationKind.Usable => "可用",
        RecommendationKind.BannedButFine => $"被禁 {Banned.Count} 人",
        RecommendationKind.NeedsRepair => Banned.Count > 0 ? $"被禁 {Banned.Count} 人 · 需替补" : "需替补",
        _ => "盟约断档",
    };
}

/// <summary>
/// Target covenants of saved lineups: deriving them from the late-game slots, checking them against this run's
/// bans, and ranking the lineups for 本局推荐.
/// </summary>
public static class LineupTargets
{
    /// <summary>The late-game slots' active covenants as targets (Ⅱ where the people count reaches it), core first.</summary>
    public static List<TargetCovenant> Derive(Lineup lineup, CovenantCalculator calculator)
    {
        var infos = calculator.AllCovenants.ToDictionary(c => c.Name, StringComparer.Ordinal);
        return [.. calculator.Compute(lineup).Where(s => s.Activated && infos.ContainsKey(s.Name))
            .OrderByDescending(s => s.BondType == "SEASON").ThenByDescending(s => s.Count).ThenBy(s => s.Name, StringComparer.Ordinal)
            .Select(s =>
            {
                var info = infos[s.Name];
                var second = LineupPlanner.CreateGoal(info, 2);
                var tier = second.Level == 2 && s.Count >= second.Count ? 2 : 1;
                return new TargetCovenant { Name = s.Name, Tier = tier };
            })];
    }

    /// <summary>The lineup's own targets, or the derived ones for lineups saved before targets existed.</summary>
    public static IReadOnlyList<TargetCovenant> Of(Lineup lineup, CovenantCalculator calculator)
        => lineup.TargetCovenants.Count > 0 ? lineup.TargetCovenants : Derive(lineup, calculator);

    /// <summary>
    /// Each target with the late-game people still available: <paramref name="unavailable"/> operators are taken out
    /// (their transformations go with them). A banned core covenant cannot be reached at all.
    /// </summary>
    public static IReadOnlyList<TargetState> States(Lineup lineup, CovenantCalculator calculator,
        Func<string, bool> unavailable, IReadOnlySet<string> bannedCovenants)
    {
        var infos = calculator.AllCovenants.ToDictionary(c => c.Name, StringComparer.Ordinal);
        var remaining = new Lineup { Slots = [.. lineup.Slots.Where(s => !unavailable(s.OperatorName))] };
        var counts = calculator.Compute(remaining).ToDictionary(s => s.Name, s => s.Count, StringComparer.Ordinal);
        return [.. Of(lineup, calculator).Where(t => infos.ContainsKey(t.Name)).Select(t =>
        {
            var info = infos[t.Name];
            var isCore = info.BondType == "SEASON";
            var goal = LineupPlanner.CreateGoal(info, t.Tier);
            return new TargetState(t.Name, t.Tier, isCore, counts.GetValueOrDefault(t.Name), goal.Count,
                isCore && bannedCovenants.Contains(t.Name));
        })];
    }

    public static LineupRecommendation Recommend(Lineup lineup, CovenantCalculator calculator,
        Func<string, bool> unavailable, IReadOnlySet<string> bannedCovenants)
    {
        var banned = lineup.Slots.Select(s => s.OperatorName).Where(n => n.Length > 0 && unavailable(n)).Distinct().ToList();
        var targets = States(lineup, calculator, unavailable, bannedCovenants);
        var broken = targets.Where(t => t.CovenantBanned).ToList();
        var short_ = targets.Where(t => !t.CovenantBanned && !t.Reached).ToList();
        var kind = broken.Count > 0 ? RecommendationKind.Broken
            : short_.Count > 0 ? RecommendationKind.NeedsRepair
            : banned.Count > 0 ? RecommendationKind.BannedButFine
            : RecommendationKind.Usable;
        var bannedText = banned.Count > 0 ? $"{string.Join("、", banned)}被禁" : "";
        var note = kind switch
        {
            RecommendationKind.Broken => $"本局{string.Join("、", broken.Select(t => t.Name))}不完整，原目标凑不齐",
            RecommendationKind.NeedsRepair => Join(bannedText,
                string.Join("；", short_.Select(t => $"{t.Label} 差 {t.Missing} 人"))),
            RecommendationKind.BannedButFine => Join(bannedText, "目标盟约不受影响"),
            _ => "",
        };
        return new LineupRecommendation(lineup, kind, banned, targets, note);
    }

    /// <summary>Best first: kind, then fewer banned people, then more targets reached.</summary>
    public static IReadOnlyList<LineupRecommendation> Rank(IEnumerable<Lineup> lineups, CovenantCalculator calculator,
        Func<string, bool> unavailable, IReadOnlySet<string> bannedCovenants)
        => [.. lineups.Where(l => l.Slots.Any(s => s.OperatorName.Length > 0))
            .Select(l => Recommend(l, calculator, unavailable, bannedCovenants))
            .OrderBy(r => r.Kind).ThenBy(r => r.Banned.Count).ThenByDescending(r => r.Targets.Count(t => t.Reached))
            .ThenByDescending(r => r.Lineup.UpdatedAt)];

    /// <summary>Filter: every / any of <paramref name="covenants"/> is a target (at <paramref name="tier"/> if set).</summary>
    public static bool Matches(Lineup lineup, CovenantCalculator calculator, IReadOnlyCollection<string> covenants, int tier, bool matchAll)
    {
        if (covenants.Count == 0) return tier == 0 || Of(lineup, calculator).Any(t => t.Tier == tier);
        var targets = Of(lineup, calculator);
        bool Has(string name) => targets.Any(t => t.Name == name && (tier == 0 || t.Tier == tier));
        return matchAll ? covenants.All(Has) : covenants.Any(Has);
    }

    private static string Join(string a, string b) => a.Length == 0 ? b : b.Length == 0 ? a : $"{a}，{b}";
}
