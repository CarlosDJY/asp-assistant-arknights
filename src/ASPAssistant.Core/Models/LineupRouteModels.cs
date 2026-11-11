namespace ASPAssistant.Core.Models;

/// <summary>
/// One stage of a route. A terminal at level <see cref="TerminalLevel"/> only offers operators up to
/// tier <see cref="MaxTier"/>; up to <see cref="TripleSlots"/> operators one tier higher can come from 三连.
/// </summary>
public sealed record RouteStageDefinition(string Name, int TerminalLevel, int MaxTier, int TripleSlots, int People)
{
    /// <summary>
    /// A stacking lineup (后期, six-terminal 叠层): keeps the 大成 covenants active and fields as many stackers for them as
    /// possible; swapped for the boss lineup before the boss fight. Its people count follows the 大成 lineup.
    /// </summary>
    public bool Stacking { get; init; }

    /// <summary>
    /// Default stages, modelled on the common “公式化运营” guides: 开局 → 前期 → 中期 (四本对对碰) → 后期 (六本叠层) → 大成.
    /// 大成 (the boss lineup) is the chosen final lineup itself and is not part of this list.
    /// </summary>
    public static IReadOnlyList<RouteStageDefinition> Defaults { get; } =
    [
        new("开局", 2, 2, 1, 6),
        new("前期", 3, 3, 1, 8),
        new("中期", 4, 4, 2, 8),
        new("后期", 6, 6, 0, 8) { Stacking = true },
    ];
}

/// <summary>Stage names saved by earlier versions (tracking entries keep the names of the stages that used them).</summary>
public static class RouteStageNames
{
    private static readonly Dictionary<string, string> Renamed = new(StringComparer.Ordinal)
    {
        ["四本对对碰"] = "中期",
        ["六本叠层"] = "后期",
    };

    /// <summary>“开局 · 四本对对碰 · 六本叠层” → “开局 · 中期 · 后期” (the old separate 中期 merges into 中期).</summary>
    public static string Normalize(string stages)
    {
        if (string.IsNullOrEmpty(stages)) return stages;
        var names = stages.Split(" · ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(n => Renamed.GetValueOrDefault(n, n)).Distinct(StringComparer.Ordinal);
        return string.Join(" · ", names);
    }
}

public sealed class RouteRequest
{
    /// <summary>The 大成 lineup the route leads to (normally a planner result).</summary>
    public required PlannedLineup Final { get; init; }
    public IReadOnlyList<PlannerGoal> Goals { get; init; } = [];
    public IReadOnlySet<string> BannedCovenants { get; init; } = new HashSet<string>();
    public IReadOnlySet<string> BannedOperators { get; init; } = new HashSet<string>();
    public bool ExcludeMixedBannedOperators { get; init; } = true;
    public IReadOnlyList<RouteStageDefinition> Stages { get; init; } = RouteStageDefinition.Defaults;
}

public enum RouteOperatorRole
{
    /// <summary>Stays until 大成 (part of the final lineup).</summary>
    Final,
    /// <summary>Only used on the way; replaced before 大成.</summary>
    Transition,
}

public sealed record RouteOperator(string Name, int Tier, string IconPath, RouteOperatorRole Role)
{
    /// <summary>Tier above what this stage's terminal offers: obtained by 三连.</summary>
    public bool ViaTriple { get; init; }
    /// <summary>Not in the previous stage.</summary>
    public bool IsNew { get; init; }
    /// <summary>Planned transformation (大成 only).</summary>
    public string? TransformedCovenant { get; init; }
    public IReadOnlyList<string> Covenants { get; init; } = [];
    public bool IsTank { get; init; }
    public bool IsHealer { get; init; }
    public bool IsStacker { get; init; }
}

/// <summary>Count of one covenant in a stage. <see cref="Level"/>: 0 = not active, 1 = Ⅰ, 2 = Ⅱ (by people count).</summary>
public sealed record RouteCovenantStatus(string Name, int Count, int Target, int Level, bool IsGoal, bool IsCore);

/// <summary>Strength and role check of one stage. <see cref="Points"/>: core 3 = 2, core 6 = 4, additional Ⅰ = 1, Ⅱ = 3.</summary>
public sealed record RouteStageSummary(int Points, int Tanks, int Healers, bool HealingCovenant, int Stackers,
    IReadOnlyList<string> Warnings)
{
    /// <summary>Estimated layers per round stacked on covenants the 大成 lineup activates.</summary>
    public double FinalLayers { get; init; }
    /// <summary>Per operator: estimated layers per round on 大成 covenants (stackers and amplifiers only).</summary>
    public IReadOnlyDictionary<string, double> OperatorLayers { get; init; } = new Dictionary<string, double>();
}

public sealed record RouteStage(RouteStageDefinition Definition, IReadOnlyList<RouteOperator> Operators,
    IReadOnlyList<RouteCovenantStatus> Covenants)
{
    public RouteStageSummary Summary { get; init; } = new(0, 0, 0, false, 0, []);
    /// <summary>Operators of the previous stage that are no longer used here.</summary>
    public IReadOnlyList<string> Removed { get; init; } = [];
    public bool IsFinal { get; init; }
}

public sealed class PlannedRoute
{
    public IReadOnlyList<RouteStage> Stages { get; init; } = [];

    /// <summary>
    /// Terminal level from which a stage is behind the player: the first higher terminal level of a later stage
    /// (开局 → 3, 前期 → 4, 中期 → 6). 0 = never (后期, 大成).
    /// </summary>
    public int ExpiresAt(int stageIndex)
    {
        var stage = Stages[stageIndex];
        if (stage.IsFinal) return 0;
        var own = stage.Definition.TerminalLevel;
        var later = Stages.Skip(stageIndex + 1).Select(s => s.Definition.TerminalLevel).Where(t => t > own).ToList();
        return later.Count == 0 ? 0 : later.Min();
    }

    public bool IsPast(int stageIndex, int terminalLevel)
        => ExpiresAt(stageIndex) is var at and > 0 && terminalLevel >= at;
}
