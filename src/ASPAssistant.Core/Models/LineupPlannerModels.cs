namespace ASPAssistant.Core.Models;

/// <summary>A covenant target for the planner. <c>Count</c> is the number of operators required.</summary>
public sealed record PlannerGoal(string Name, int Level, int Count, bool Manual, bool IsCore);

public sealed class PlannerRequest
{
    public IReadOnlyList<PlannerGoal> Goals { get; init; } = [];
    public IReadOnlySet<string> BannedCovenants { get; init; } = new HashSet<string>();
    public IReadOnlySet<string> BannedOperators { get; init; } = new HashSet<string>();
    public IReadOnlySet<string> LockedOperators { get; init; } = new HashSet<string>();

    /// <summary>Covenants an operator may be transformed into. Callers normally pass <c>LineupPlanner.DefaultTransformAllowed</c>.</summary>
    public IReadOnlySet<string> TransformAllowed { get; init; } = new HashSet<string>();

    /// <summary>Exclude operators made unusable by covenant bans (only one covenant and it is banned, or banned core + banned additional).</summary>
    public bool ExcludeMixedBannedOperators { get; init; } = true;

    public IReadOnlyList<int> PeopleOptions { get; init; } = [8, 9];
    public IReadOnlyList<int> TransformOptions { get; init; } = [0, 1, 2];
}

public sealed record PlannedOperator(string Name, string? TransformedCovenant, IReadOnlyList<string> NativeCovenants)
{
    public bool IsLocked { get; init; }
    public bool IsHarmony { get; init; }
    public string IconPath { get; init; } = "";
}

/// <summary>
/// One complete candidate lineup.
/// </summary>
public sealed record PlannedLineup(int People, int Transforms, IReadOnlyList<PlannedOperator> Operators,
    IReadOnlyDictionary<string, int> Counts, bool SearchLimited)
{
    public IReadOnlyList<PlannerGoal> Goals { get; init; } = [];
    /// <summary>Only nonzero when the available candidate pool cannot fill the requested size.</summary>
    public int FreeSlots => Math.Max(0, People - Operators.Count);
    /// <summary>Score from non-target additional covenants: level I = 1, auto-confirmed level II = 3.</summary>
    public int AdditionalScore { get; init; }
    /// <summary>Additional covenants activated by this lineup and the level counted for scoring.</summary>
    public IReadOnlyDictionary<string, int> AdditionalCovenants { get; init; } = new Dictionary<string, int>();
    /// <summary>Transforms added only to reach the requested transform count; they do not help any unmet target.</summary>
    public int ForcedTransforms { get; init; }
    /// <summary>Extra core count contributed by 调和, per goal (already included in <see cref="Counts"/>).</summary>
    public IReadOnlyDictionary<string, int> HarmonyBonus { get; init; } = new Dictionary<string, int>();
}

/// <summary>Search result of one people × transforms combination.</summary>
public sealed record PlannerVariantResult(int People, int Transforms, IReadOnlyList<PlannedLineup> Lineups, bool SearchLimited);

public sealed class PlannerResult
{
    public IReadOnlyList<PlannerVariantResult> Variants { get; init; } = [];
    public string? Error { get; init; }
    public int CandidateCount { get; init; }
    public IEnumerable<PlannedLineup> AllLineups => Variants.SelectMany(v => v.Lineups);
}
