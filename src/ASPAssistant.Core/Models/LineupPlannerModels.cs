namespace ASPAssistant.Core.Models;

/// <summary>
/// The run's strategy, as far as it changes planning (a run carries exactly one; every other strategy is
/// <see cref="Other"/>). See <see cref="PlannerStrategies"/>.
/// </summary>
public enum PlannerStrategy
{
    /// <summary>Any strategy that does not change which lineups are possible.</summary>
    Other,
    /// <summary>森蚺 “暴躁铁皮”: a piece that takes one place and can carry several transformations.</summary>
    Mech,
    /// <summary>优等生 Pith: a 调和 piece from round 1 that takes one place and is kept all run.</summary>
    Pith,
    /// <summary>变形同构: a free 变形同构体 every 5 rounds (at most 3 in 15 rounds); job-change gear is still bought.</summary>
    Shapeshifter,
}

public static class PlannerStrategies
{
    public const string MechId = "band_mech";
    public const string PithId = "band_pith";
    public const string ShapeshifterId = "band_damaztic";
    public const string PithName = "优等生 Pith";
    /// <summary>暴躁铁皮 holds one 变形同构体 plus job-change gear: 4 slots (3 transformations), 6 when elite (5).</summary>
    public static int MechTransformCap(bool elite) => elite ? 5 : 3;
    /// <summary>One free 变形同构体 every 5 rounds of a 15-round run.</summary>
    public const int ShapeshifterGifts = 3;

    /// <summary>Places the strategy's own piece takes (暴躁铁皮 and Pith each take one).</summary>
    public static int PiecePlaces(PlannerStrategy strategy) => strategy is PlannerStrategy.Mech or PlannerStrategy.Pith ? 1 : 0;

    public static string? StrategyId(PlannerStrategy strategy) => strategy switch
    {
        PlannerStrategy.Mech => MechId,
        PlannerStrategy.Pith => PithId,
        PlannerStrategy.Shapeshifter => ShapeshifterId,
        _ => null
    };

    /// <summary>Transformation counts worth planning for: one more when the strategy makes a third realistic.</summary>
    public static IReadOnlyList<int> TransformOptions(PlannerStrategy strategy)
        => strategy is PlannerStrategy.Mech or PlannerStrategy.Shapeshifter ? new[] { 0, 1, 2, 3 } : new[] { 0, 1, 2 };
}

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

    public PlannerStrategy Strategy { get; init; } = PlannerStrategy.Other;
    /// <summary>暴躁铁皮 is elite (5 transformations instead of 3).</summary>
    public bool MechElite { get; init; }
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
    public int FreeSlots => Math.Max(0, People - PlannerStrategies.PiecePlaces(Strategy) - Operators.Count);
    /// <summary>Score from non-target additional covenants: level I = 1, auto-confirmed level II = 3.</summary>
    public int AdditionalScore { get; init; }
    /// <summary>Additional covenants activated by this lineup and the level counted for scoring.</summary>
    public IReadOnlyDictionary<string, int> AdditionalCovenants { get; init; } = new Dictionary<string, int>();
    /// <summary>Transforms added only to reach the requested transform count; they do not help any unmet target.</summary>
    public int ForcedTransforms { get; init; }
    /// <summary>Extra core count contributed by 调和, per goal (already included in <see cref="Counts"/>).</summary>
    public IReadOnlyDictionary<string, int> HarmonyBonus { get; init; } = new Dictionary<string, int>();
    /// <summary>No 奶位 (healer or active 聚合): only returned when no lineup with one satisfies the targets.</summary>
    public bool MissingHealer { get; init; }
    public PlannerStrategy Strategy { get; init; } = PlannerStrategy.Other;
    /// <summary>Covenants 暴躁铁皮 is transformed into (part of <see cref="Transforms"/>; 暴躁铁皮 takes one of <see cref="People"/>).</summary>
    public IReadOnlyList<string> MechCovenants { get; init; } = [];
    public bool MechElite { get; init; }
    /// <summary>Pith's 调和 piece is part of the lineup (one of <see cref="People"/>).</summary>
    public bool HasPith => Strategy == PlannerStrategy.Pith;
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
