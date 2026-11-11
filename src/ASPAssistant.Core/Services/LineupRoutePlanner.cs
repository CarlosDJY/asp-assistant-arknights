using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

/// <summary>
/// Derives the transition lineups (开局 → 前期 → 中期 → 后期 (叠层)) that lead to a chosen 大成 lineup.
/// <para>
/// Stages are solved backwards from 大成: each earlier stage is the best lineup the terminal can offer
/// (tier ≤ terminal level, plus a few one-tier-higher operators from 三连) that builds the target covenants
/// and keeps as many operators of the following stage as possible, so the route needs few replacements.
/// Operators that are not in the final lineup are reported as 过渡 (transition).
/// </para>
/// </summary>
public sealed class LineupRoutePlanner
{
    // Objective weights (one covenant point = PointWeight).
    // Strength: core 3 = 2 points, core 6 = 4; additional Ⅰ = 1, three-person Ⅱ = 3, or 2 while 本家 is below 3 (奇迹/投资人/远见/独行/调和 score 0).
    private const double PointWeight = 60;
    /// <summary>Each target-covenant member keeps the route heading for the final lineup.</summary>
    private const double GoalMember = 20;
    private const int AdditionalSecondPoints = 3;
    /// <summary>Additional covenant Ⅱ before every target core covenant has 3 people.</summary>
    private const int AdditionalSecondBeforeCore = 2;
    /// <summary>Target covenants' points count 2×, so the route builds 本家 rather than any covenant worth the same.</summary>
    private const double GoalPointFactor = 2;
    // Roles (from operator_profiles.json).
    private const double MissingTank = 600;
    private const double MissingHealer = 600;
    /// <summary>Healers are required from this terminal level on (三本以后).</summary>
    public const int HealerFromTerminal = 3;
    /// <summary>Value of one layer per round on a covenant the 大成 lineup activates (layers are kept).</summary>
    private const double LayerFinal = 6;
    /// <summary>Value of one layer per round on a covenant only active in this stage.</summary>
    private const double LayerTransition = 2;
    /// <summary>Temporary 乌萨斯 layers vanish unless converted.</summary>
    private const double TemporaryLayerFactor = 0.5;
    /// <summary>Mon3tr's 周围八格: how many capped stackers it can realistically cover.</summary>
    private const int AroundCoverage = 3;
    /// <summary>Share of a raised cap that is actually reached.</summary>
    private const double CapReach = 0.75;
    private const double ConsumerGoal = 30;
    private const double ConsumerOther = 10;
    // Route continuity.
    private const double KeepForNextStage = 14;
    /// <summary>Keeping what the previous stage bought (fewer sells).</summary>
    private const double KeepFromPreviousStage = 8;
    /// <summary>Per target-covenant member lost compared with the previous stage.</summary>
    private const double GoalRegression = 60;
    private const double FinalMember = 5;
    private const double PerTier = 1.5;
    private const double SpeculativeTriple = 6;
    private const double SoloConflict = 60;
    /// <summary>后期 (叠层): each 大成 covenant kept active (needed to stack it at all).</summary>
    private const double StackingKeepCovenantActive = 150;
    /// <summary>后期 (叠层): per member a target core covenant (or the best target additional covenant) is short of its plan.</summary>
    private const double StackingGoalMissing = 400;
    /// <summary>后期 (叠层): layers count 3× the normal stage weight.</summary>
    private const double StackingLayerWeight = 3;
    private const int MaxImprovementRounds = 60;

    private readonly IReadOnlyList<Operator> _operators;
    private readonly Dictionary<string, Operator> _byName;
    private readonly Dictionary<string, CovenantInfo> _covenants;
    private readonly LineupPlanner _planner;
    /// <summary>Distinct covenants of each operator and whether it is a 调和 holder (Score is called a lot).</summary>
    private readonly Dictionary<string, (string[] Covenants, bool Harmony)> _opCovenants;

    public LineupRoutePlanner(IEnumerable<Operator> operators, IEnumerable<CovenantInfo> covenants)
    {
        _operators = [.. operators];
        _byName = _operators.GroupBy(o => o.Name).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        _covenants = covenants.GroupBy(c => c.Name).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        _planner = new LineupPlanner(_operators, _covenants.Values);
        _opCovenants = _byName.ToDictionary(kv => kv.Key, kv =>
        {
            var all = kv.Value.CoreCovenants.Concat(kv.Value.AdditionalCovenants).Distinct(StringComparer.Ordinal).ToArray();
            return (all, all.Contains(CovenantCalculator.HarmonyCovenant));
        }, StringComparer.Ordinal);
    }

    // The run's strategy pieces for the route being planned (Plan runs synchronously on one thread).
    /// <summary>Pith's 调和 piece is there from round 1 in every stage.</summary>
    [ThreadStatic] private static bool _pith;
    /// <summary>暴躁铁皮's transformations, counted where the 大成 transformations are (后期 (叠层), 大成).</summary>
    [ThreadStatic] private static IReadOnlyList<string>? _mech;

    public PlannedRoute Plan(RouteRequest request, CancellationToken cancellationToken = default)
    {
        _pith = request.Final.HasPith;
        _mech = request.Final.MechCovenants;
        try { return PlanCore(request, cancellationToken); }
        finally { _pith = false; _mech = null; }
    }

    private PlannedRoute PlanCore(RouteRequest request, CancellationToken cancellationToken)
    {
        var goals = request.Goals.Count > 0 ? request.Goals : request.Final.Goals;
        var finalNames = request.Final.Operators.Select(o => o.Name).ToHashSet(StringComparer.Ordinal);
        var finalTransforms = request.Final.Operators.Where(o => o.TransformedCovenant is not null)
            .ToDictionary(o => o.Name, o => o.TransformedCovenant!, StringComparer.Ordinal);
        var finalCounts = Count(finalNames, finalTransforms);
        // Layers are kept for the rest of the run, so stacking what 大成 activates is an investment.
        var finalActive = finalCounts.Where(kv => kv.Value >= ActivateCount(kv.Key)).Select(kv => kv.Key)
            .ToHashSet(StringComparer.Ordinal);
        // A stacking stage fields as many people as the boss lineup; 暴躁铁皮 / Pith take one place in every stage.
        var piece = PlannerStrategies.PiecePlaces(request.Final.Strategy);
        var stageDefs = request.Stages.Select(d =>
        {
            var def = d.Stacking ? d with { People = request.Final.People } : d;
            return def with { People = Math.Max(1, def.People - piece) };
        }).ToList();
        request = new RouteRequest
        {
            Final = request.Final, Goals = request.Goals, BannedCovenants = request.BannedCovenants,
            BannedOperators = request.BannedOperators, ExcludeMixedBannedOperators = request.ExcludeMixedBannedOperators,
            Stages = stageDefs
        };
        var stageCount = request.Stages.Count;
        // Earlier layers accumulate for more rounds: 开局 1.3 … 四本 0.85 with the default four stages.
        double StageWeight(int s) => 0.7 + 0.15 * (stageCount - s);
        var pool = _operators
            .Where(o => !request.BannedOperators.Contains(o.Name))
            .Where(o => !request.ExcludeMixedBannedOperators || !_planner.IsExcludedByBans(o, request.BannedCovenants))
            .ToList();

        // Backwards: 大成 → 后期 → 中期 → 前期 → 开局.
        var teams = new List<string>[request.Stages.Count];
        IReadOnlySet<string> next = finalNames;
        for (var s = request.Stages.Count - 1; s >= 0; s--)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var team = SolveStage(request.Stages[s], pool, goals, next, finalNames, previous: null, teams[s], finalActive, StageWeight(s),
                request.Stages[s].Stacking ? finalTransforms : NoTransforms);
            teams[s] = team;
            // A stacking stage is a detour before the boss: earlier stages still head for the boss lineup.
            if (!request.Stages[s].Stacking) next = team.ToHashSet(StringComparer.Ordinal);
        }

        // Then forwards: re-solve each stage knowing the previous one, so the route does not sell what it
        // just built or let a target covenant shrink again (开局 3 莱塔尼亚 → 前期 1 would be a bad route).
        for (var s = 0; s < request.Stages.Count; s++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var after = s + 1 < request.Stages.Count && !request.Stages[s + 1].Stacking
                ? teams[s + 1].ToHashSet(StringComparer.Ordinal) : finalNames;
            var before = s > 0 ? teams[s - 1] : null;
            teams[s] = SolveStage(request.Stages[s], pool, goals, after, finalNames, before, teams[s], finalActive, StageWeight(s),
                request.Stages[s].Stacking ? finalTransforms : NoTransforms);
        }

        // Forwards: annotate roles, new arrivals and replacements.
        var stages = new List<RouteStage>();
        IReadOnlySet<string> previous = new HashSet<string>();
        for (var s = 0; s < request.Stages.Count; s++)
        {
            var def = request.Stages[s];
            var team = teams[s];
            var ops = team.Select(n => _byName[n])
                .OrderBy(o => finalNames.Contains(o.Name) ? 0 : 1).ThenByDescending(o => o.Tier).ThenBy(o => o.Name, StringComparer.Ordinal)
                .Select(o => ToRouteOperator(o, finalNames, previous, viaTriple: o.Tier > def.MaxTier, transform: null))
                .ToList();
            // Transformations planned for 大成 are already done by 后期 (叠层).
            var stageTransforms = def.Stacking ? finalTransforms : NoTransforms;
            if (def.Stacking)
                ops = [.. ops.Select(o => o with { TransformedCovenant = finalTransforms.GetValueOrDefault(o.Name) })];
            var summary = Summarize(team, stageTransforms, goals, def.TerminalLevel, finalActive);
            if (def.Stacking)
                summary = summary with { Warnings = [.. summary.Warnings, .. StackingGoalWarnings(Count(team, stageTransforms), goals)] };
            stages.Add(new RouteStage(def, ops, Status(team, stageTransforms, goals))
            {
                Summary = summary,
                Removed = previous.Where(n => !team.Contains(n)).Order(StringComparer.Ordinal).ToList()
            });
            previous = team.ToHashSet(StringComparer.Ordinal);
        }

        var finalTeam = request.Final.Operators.Select(o => o.Name).ToList();
        var transforms = request.Final.Operators.Where(o => o.TransformedCovenant is not null)
            .ToDictionary(o => o.Name, o => o.TransformedCovenant!, StringComparer.Ordinal);
        var finalDef = new RouteStageDefinition("大成（打 Boss）", 6, 6, 0, request.Final.People);
        var finalOps = request.Final.Operators
            .Where(o => _byName.ContainsKey(o.Name))
            .Select(o => ToRouteOperator(_byName[o.Name], finalNames, previous, false, o.TransformedCovenant))
            .OrderByDescending(o => o.Tier).ThenBy(o => o.Name, StringComparer.Ordinal)
            .ToList();
        stages.Add(new RouteStage(finalDef, finalOps, Status(finalTeam, transforms, goals))
        {
            Summary = Summarize(finalTeam, transforms, goals, finalDef.TerminalLevel, finalActive),
            Removed = previous.Where(n => !finalNames.Contains(n)).Order(StringComparer.Ordinal).ToList(),
            IsFinal = true
        });
        return new PlannedRoute { Stages = stages };
    }

    private RouteOperator ToRouteOperator(Operator o, IReadOnlySet<string> finalNames, IReadOnlySet<string> previous,
        bool viaTriple, string? transform)
        => new(o.Name, o.Tier, o.IconPath, finalNames.Contains(o.Name) ? RouteOperatorRole.Final : RouteOperatorRole.Transition)
        {
            ViaTriple = viaTriple,
            IsNew = !previous.Contains(o.Name),
            TransformedCovenant = transform,
            Covenants = [.. o.CoreCovenants.Concat(o.AdditionalCovenants).Distinct()],
            IsTank = o.Profile?.IsTank == true,
            IsHealer = o.Profile?.IsHealer == true,
            IsStacker = o.Profile?.IsStacker == true
        };

    // ── One stage: greedy fill + swap improvement ───────────────────────────

    private List<string> SolveStage(RouteStageDefinition def, List<Operator> allPool, IReadOnlyList<PlannerGoal> goals,
        IReadOnlySet<string> next, IReadOnlySet<string> finalNames, IReadOnlyList<string>? previous, IReadOnlyList<string>? current,
        IReadOnlySet<string> finalActive, double stageWeight, IReadOnlyDictionary<string, string> transforms)
    {
        var pool = allPool.Where(o => o.Tier <= def.MaxTier || (def.TripleSlots > 0 && o.Tier == def.MaxTier + 1))
            .OrderBy(o => o.Name, StringComparer.Ordinal).ToList();
        var size = Math.Min(def.People, pool.Count);
        var prevSet = previous?.ToHashSet(StringComparer.Ordinal) ?? new HashSet<string>(StringComparer.Ordinal);
        var prevCounts = previous is null ? null : Count(previous, NoTransforms);
        var ctx = new StageContext(this, def, goals, next, finalNames, prevSet, prevCounts, finalActive, stageWeight, transforms);

        var seeds = new List<List<Operator>>
        {
            new List<Operator>(),
            // Start from what the next stage already uses (within this stage's tier limit).
            pool.Where(o => next.Contains(o.Name)).OrderByDescending(o => o.Tier).ToList(),
            pool.Where(o => prevSet.Contains(o.Name)).OrderByDescending(o => o.Tier).ToList(),
        };
        if (current is not null)
            seeds.Add(pool.Where(o => current.Contains(o.Name)).ToList());
        // Single swaps cannot activate a covenant that needs several new holders at once,
        // so also start from “this goal is already activated” for each goal, and for all of them
        // (core covenants first), preferring holders the next stage keeps.
        IEnumerable<Operator> Holders(string covenant) => pool
            .Where(o => o.CoreCovenants.Contains(covenant) || o.AdditionalCovenants.Contains(covenant))
            .OrderByDescending(o => next.Contains(o.Name)).ThenByDescending(o => finalNames.Contains(o.Name))
            .ThenBy(o => o.Tier > def.MaxTier).ThenByDescending(o => o.Tier).ThenBy(o => o.Name, StringComparer.Ordinal);
        var combined = new List<Operator>();
        foreach (var g in goals.OrderByDescending(g => IsCore(g.Name)))
        {
            var need = ActivateCount(g.Name);
            seeds.Add(Holders(g.Name).Take(need).ToList());
            var have = combined.Count(o => o.CoreCovenants.Contains(g.Name) || o.AdditionalCovenants.Contains(g.Name));
            foreach (var o in Holders(g.Name).Where(o => !combined.Contains(o)).Take(Math.Max(0, need - have)))
                combined.Add(o);
        }
        seeds.Add(combined);

        // Seeds are independent: solve them in parallel, then pick in seed order so the result stays deterministic.
        var solved = new (List<Operator> Team, double Score)[seeds.Count];
        Parallel.For(0, seeds.Count, i =>
        {
            var team = new List<Operator>();
            foreach (var o in seeds[i])
                if (team.Count < size && ctx.CanAdd(team, o)) team.Add(o);
            Fill(team, pool, size, ctx);
            Improve(team, pool, ctx);
            solved[i] = (team, ctx.Score(team));
        });
        List<Operator>? best = null;
        var bestScore = double.NegativeInfinity;
        foreach (var (team, score) in solved)
            if (score > bestScore + 1e-9) { best = team; bestScore = score; }
        return best!.Select(o => o.Name).ToList();
    }

    private static void Fill(List<Operator> team, List<Operator> pool, int size, StageContext ctx)
    {
        while (team.Count < size)
        {
            Operator? pick = null;
            var pickScore = double.NegativeInfinity;
            foreach (var o in pool)
            {
                if (team.Contains(o) || !ctx.CanAdd(team, o)) continue;
                team.Add(o);
                var score = ctx.Score(team);
                team.RemoveAt(team.Count - 1);
                if (score > pickScore + 1e-9) { pick = o; pickScore = score; }
            }
            if (pick is null) break;
            team.Add(pick);
        }
    }

    private static void Improve(List<Operator> team, List<Operator> pool, StageContext ctx)
    {
        var current = ctx.Score(team);
        for (var round = 0; round < MaxImprovementRounds; round++)
        {
            var bestGain = 1e-9;
            (int Slot, Operator In)? bestSwap = null;
            for (var slot = 0; slot < team.Count; slot++)
            {
                var outOp = team[slot];
                foreach (var inOp in pool)
                {
                    if (team.Contains(inOp)) continue;
                    team[slot] = inOp;
                    if (ctx.IsValid(team))
                    {
                        var gain = ctx.Score(team) - current;
                        if (gain > bestGain) { bestGain = gain; bestSwap = (slot, inOp); }
                    }
                    team[slot] = outOp;
                }
            }
            if (bestSwap is not { } swap) break;
            team[swap.Slot] = swap.In;
            current += bestGain;
        }
    }

    // ── Counting (same rules as the planner: Harmony adds +1 to present core covenants) ──

    private static readonly IReadOnlyDictionary<string, string> NoTransforms = new Dictionary<string, string>();

    private Dictionary<string, int> Count(IEnumerable<string> team, IReadOnlyDictionary<string, string> transforms)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        var present = new HashSet<string>(StringComparer.Ordinal);
        var harmony = 0;
        foreach (var name in team)
        {
            if (!_opCovenants.TryGetValue(name, out var info)) continue;
            foreach (var c in info.Covenants)
            {
                counts[c] = counts.GetValueOrDefault(c) + 1;
                if (IsCore(c)) present.Add(c);
            }
            if (info.Harmony)
                harmony++;
            if (transforms.TryGetValue(name, out var t))
            {
                counts[t] = counts.GetValueOrDefault(t) + 1;
                if (IsCore(t)) present.Add(t);
            }
        }
        if (_mech is { Count: > 0 } mech && !ReferenceEquals(transforms, NoTransforms))
            foreach (var t in mech)
            {
                counts[t] = counts.GetValueOrDefault(t) + 1;
                if (IsCore(t)) present.Add(t);
            }
        if (_pith) harmony++;
        if (harmony > 0)
            foreach (var c in present)
                counts[c] += harmony;
        return counts;
    }

    /// <summary>Lineup strength: core 3 = 2, core 6 = 4; additional Ⅰ = 1, three-person Ⅱ = 3.</summary>
    /// <remarks>
    /// Until every target core covenant (本家) has reached 3 people, an additional covenant's Ⅱ is worth only
    /// <see cref="AdditionalSecondBeforeCore"/> points, so a transition lineup does not trade 本家 for another
    /// covenant's three-person Ⅱ.
    /// </remarks>
    public int Points(IReadOnlyDictionary<string, int> counts, IEnumerable<PlannerGoal> goals, IReadOnlySet<string>? only = null)
    {
        var coreReady = goals.Where(g => IsCore(g.Name)).All(g => counts.GetValueOrDefault(g.Name) >= ActivateCount(g.Name));
        var additionalSecond = coreReady ? AdditionalSecondPoints : AdditionalSecondBeforeCore;
        var points = 0;
        foreach (var (name, c) in counts)
        {
            if (only is not null && !only.Contains(name)) continue;
            if (LineupPlanner.NoAdditionalScore.Contains(name) || c < ActivateCount(name)) continue;
            if (IsCore(name)) points += c >= 6 ? 4 : 2;
            else points += c >= SecondCount(name) ? additionalSecond : 1;
        }
        return points;
    }

    internal bool IsActive(IReadOnlyDictionary<string, int> counts, string covenant)
        => counts.GetValueOrDefault(covenant) >= ActivateCount(covenant);

    /// <summary>聚合 heals allies in range once active, so it covers the healer slot.</summary>
    private bool HasHealing(IEnumerable<Operator> team, IReadOnlyDictionary<string, int> counts)
        => team.Any(o => o.Profile?.IsHealer == true) || IsActive(counts, HealingCovenant);

    public const string HealingCovenant = "聚合";

    /// <summary>Tank / healer requirements plus the value of the layers stacked per round (see <see cref="StackLayers"/>).</summary>
    private double RoleScore(List<Operator> team, IReadOnlyDictionary<string, int> counts, IReadOnlySet<string> finalActive,
        double stageWeight, int terminal, bool transitionLayers = true)
    {
        double score = 0;
        if (!team.Any(o => o.Profile?.IsTank == true)) score -= MissingTank;
        if (terminal >= HealerFromTerminal && !HasHealing(team, counts)) score -= MissingHealer;
        var layers = StackLayers(team, counts, finalActive);
        score += stageWeight * layers.Values.Sum(l => LayerFinal * l.Final + (transitionLayers ? LayerTransition * l.Transition : 0));
        foreach (var o in team)
        {
            if (o.Profile is not { Consumes.Count: > 0 } p) continue;
            if (p.Consumes.Any(c => IsActive(counts, c) && finalActive.Contains(c))) score += ConsumerGoal;
            else if (p.Consumes.Any(c => IsActive(counts, c))) score += ConsumerOther;
        }
        return score;
    }

    /// <summary>Layers per round one operator adds: on covenants 大成 activates, and on ones only active now.</summary>
    public readonly record struct StackContribution(double Final, double Transition)
    {
        public static StackContribution operator +(StackContribution a, StackContribution b)
            => new(a.Final + b.Final, a.Transition + b.Transition);
        public StackContribution Scale(double f) => new(Final * f, Transition * f);
        public double Value => LayerFinal * Final + LayerTransition * Transition;
    }

    /// <summary>
    /// Estimated layers per round each operator adds (normal-version traits; positions are free, so adjacent
    /// stackers and front-only amplifiers take the best neighbour). Only active covenants can be stacked
    /// (“使已激活的…”); 获得时 stacking is ignored.
    /// </summary>
    public Dictionary<string, StackContribution> StackLayers(IReadOnlyList<Operator> team, IReadOnlyDictionary<string, int> counts,
        IReadOnlySet<string> finalActive)
    {
        StackContribution OnCovenants(IEnumerable<string> covenants, double perRound)
        {
            double final = 0, transition = 0;
            foreach (var c in covenants.Distinct())
            {
                if (!IsActive(counts, c)) continue;
                if (finalActive.Contains(c)) final += perRound; else transition += perRound;
            }
            return new StackContribution(final, transition);
        }
        IEnumerable<string> Own(Operator o) => _opCovenants.TryGetValue(o.Name, out var info) ? info.Covenants : o.CoreCovenants.Concat(o.AdditionalCovenants);
        double Temp(OperatorStackInfo st) => st.Temporary ? TemporaryLayerFactor : 1;
        Operator? BestNeighbour(Operator self, double perRound)
            => team.Where(o => o != self).OrderByDescending(o => OnCovenants(Own(o), perRound).Value).FirstOrDefault();

        // 1. Direct stacking.
        var result = new Dictionary<string, StackContribution>(StringComparer.Ordinal);
        foreach (var o in team)
        {
            if (o.Profile?.Stack is not { } st) continue;
            var per = st.PerRound * Temp(st);
            result[o.Name] = st.Kind switch
            {
                "covenant" => OnCovenants(st.Covenants, per),
                "self" => OnCovenants(Own(o), per),
                "adjacent" => OnCovenants(Own(o), per) + (BestNeighbour(o, per) is { } n ? OnCovenants(Own(n), per) : default),
                "grant" => BestNeighbour(o, per) is { } g ? OnCovenants(Own(g), per) : default,
                _ => default, // once / amplifier
            };
        }

        // 2. Amplifiers work on the direct stacking above.
        var capped = team.Where(o => o.Profile?.Stack is { Cap: not null, Kind: not ("once" or "amplifier") }).ToList();
        foreach (var o in team)
        {
            if (o.Profile?.Stack is not { Kind: "amplifier", Amplify: { } amp } st) continue;
            StackContribution extra = default;
            switch (amp.Type)
            {
                case "capPercent":
                {
                    // Raises the per-battle cap: capped stackers gain that share of their (capped) stacking.
                    var eligible = capped.Where(c => c != o
                        && (st.Covenants.Count == 0 || Own(c).Any(st.Covenants.Contains)))
                        .Select(c => result.GetValueOrDefault(c.Name).Scale(amp.Value))
                        .OrderByDescending(c => c.Value)
                        .Take(amp.Scope == "front" ? 1 : AroundCoverage);
                    foreach (var e in eligible) extra += e;
                    break;
                }
                case "capFlat":
                {
                    // 薇薇安娜: other operators sharing a covenant with her get +N cap this battle.
                    foreach (var c in capped.Where(c => c != o && Own(c).Intersect(Own(o)).Any()))
                    {
                        var stc = c.Profile!.Stack!;
                        var targets = stc.Kind == "covenant" ? stc.Covenants : Own(c);
                        extra += OnCovenants(targets, amp.Value * CapReach);
                    }
                    break;
                }
                case "perEvent":
                {
                    // 阿米娅: the operator in front gains +N per stacking event (not capped).
                    var best = team.Where(c => c != o && result.ContainsKey(c.Name) && c.Profile!.Stack!.Amount > 0)
                        .Select(c =>
                        {
                            var stc = c.Profile!.Stack!;
                            var events = stc.PerRound / stc.Amount;
                            var share = result[c.Name].Scale(1 / stc.PerRound * events * amp.Value);
                            return share;
                        })
                        .OrderByDescending(c => c.Value).FirstOrDefault();
                    extra = best;
                    break;
                }
                case "tempBoost":
                {
                    // 塔露拉: kills stack more 乌萨斯 temporary layers (normal 2 → value).
                    var gain = (amp.Value - 2) * 2 * TemporaryLayerFactor;
                    extra = OnCovenants(st.Covenants, gain);
                    break;
                }
            }
            result[o.Name] = extra;
        }
        return result;
    }

    private RouteStageSummary Summarize(IEnumerable<string> names, IReadOnlyDictionary<string, string> transforms,
        IReadOnlyList<PlannerGoal> goals, int terminal, IReadOnlySet<string> finalActive)
    {
        var team = names.Where(_byName.ContainsKey).Select(n => _byName[n]).ToList();
        var counts = Count(team.Select(o => o.Name), transforms);
        var warnings = new List<string>();
        var tanks = team.Count(o => o.Profile?.IsTank == true);
        var healers = team.Count(o => o.Profile?.IsHealer == true);
        var healingCovenant = IsActive(counts, HealingCovenant);
        if (tanks == 0) warnings.Add("缺少盾位（重装）");
        if (terminal >= HealerFromTerminal && healers == 0 && !healingCovenant) warnings.Add("缺少奶位（医疗 / 聚合）");
        var layers = StackLayers(team, counts, finalActive);
        return new RouteStageSummary(Points(counts, goals), tanks, healers, healingCovenant,
            team.Count(o => o.Profile?.IsStacker == true), warnings)
        {
            FinalLayers = Math.Round(layers.Values.Sum(l => l.Final), 1),
            OperatorLayers = layers.Where(kv => kv.Value.Final + kv.Value.Transition > 0)
                .ToDictionary(kv => kv.Key, kv => Math.Round(kv.Value.Final, 1))
        };
    }

    /// <summary>
    /// 后期 (叠层) requirement: every target core covenant at its planned count, and at least one target
    /// additional covenant at its planned count. Returns the missing members (0 = satisfied).
    /// </summary>
    internal (int Core, int Additional, int Total) StackingGoalShortfall(IReadOnlyDictionary<string, int> counts, IReadOnlyList<PlannerGoal> goals)
    {
        var core = goals.Where(g => IsCore(g.Name)).Sum(g => Math.Max(0, g.Count - counts.GetValueOrDefault(g.Name)));
        var extras = goals.Where(g => !IsCore(g.Name)).Select(g => Math.Max(0, g.Count - counts.GetValueOrDefault(g.Name))).ToList();
        var additional = extras.Count == 0 ? 0 : extras.Min();
        return (core, additional, core + additional);
    }

    private IEnumerable<string> StackingGoalWarnings(IReadOnlyDictionary<string, int> counts, IReadOnlyList<PlannerGoal> goals)
    {
        foreach (var g in goals.Where(g => IsCore(g.Name) && counts.GetValueOrDefault(g.Name) < g.Count))
            yield return $"{g.Name} 未达规划（{counts.GetValueOrDefault(g.Name)}/{g.Count}）";
        if (StackingGoalShortfall(counts, goals).Additional > 0)
            yield return "没有小盟约达到规划人数";
    }

    private bool IsCore(string covenant) => _covenants.TryGetValue(covenant, out var c) && c.BondType == "SEASON";
    private int ActivateCount(string covenant) => _covenants.TryGetValue(covenant, out var c) ? c.ActivateCount ?? (c.BondType == "SEASON" ? 3 : 2) : 2;
    private int SecondCount(string covenant) => IsCore(covenant) ? 6 : LineupPlanner.PeopleSecond.Contains(covenant) ? 3 : int.MaxValue;

    private IReadOnlyList<RouteCovenantStatus> Status(IEnumerable<string> team, IReadOnlyDictionary<string, string> transforms,
        IReadOnlyList<PlannerGoal> goals)
    {
        var counts = Count(team, transforms);
        var goalNames = goals.Select(g => g.Name).ToHashSet(StringComparer.Ordinal);
        int Level(string name, int count) => count >= SecondCount(name) ? 2 : count >= ActivateCount(name) ? 1 : 0;

        var list = goals.Select(g =>
        {
            var count = counts.GetValueOrDefault(g.Name);
            return new RouteCovenantStatus(g.Name, count, g.Count, Level(g.Name, count), true, IsCore(g.Name));
        }).ToList();
        list.AddRange(counts
            .Where(kv => !goalNames.Contains(kv.Key) && kv.Value >= ActivateCount(kv.Key))
            .OrderByDescending(kv => IsCore(kv.Key)).ThenByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .Select(kv => new RouteCovenantStatus(kv.Key, kv.Value, ActivateCount(kv.Key), Level(kv.Key, kv.Value), false, IsCore(kv.Key))));
        return list;
    }

    private sealed class StageContext(LineupRoutePlanner owner, RouteStageDefinition def, IReadOnlyList<PlannerGoal> goals,
        IReadOnlySet<string> next, IReadOnlySet<string> finalNames, IReadOnlySet<string> previous,
        IReadOnlyDictionary<string, int>? previousCounts, IReadOnlySet<string> finalActive, double stageWeight,
        IReadOnlyDictionary<string, string> transforms)
    {
        private readonly HashSet<string> _goalNames = goals.Select(g => g.Name).ToHashSet(StringComparer.Ordinal);

        private int TripleCount(IEnumerable<Operator> team) => team.Count(o => o.Tier > def.MaxTier);

        /// <summary>
        /// 后期 (叠层): every 大成 covenant must stay active (only active covenants gain layers), then as many
        /// layers per round on them as possible. Tank / healer still apply; overlap with the boss lineup is kept.
        /// </summary>
        private double StackingScore(List<Operator> team, Dictionary<string, int> counts)
        {
            double score = -StackingGoalMissing * owner.StackingGoalShortfall(counts, goals).Total;
            foreach (var c in finalActive)
                if (owner.IsActive(counts, c)) score += StackingKeepCovenantActive;
            score += owner.RoleScore(team, counts, finalActive, StackingLayerWeight, def.TerminalLevel, transitionLayers: false);
            var solo = counts.GetValueOrDefault(LineupPlanner.SoloCovenant);
            if (solo > 1) score -= SoloConflict * (solo - 1);
            foreach (var o in team)
            {
                if (next.Contains(o.Name)) score += KeepForNextStage;
                if (previous.Contains(o.Name)) score += KeepFromPreviousStage;
            }
            return score;
        }

        public bool CanAdd(List<Operator> team, Operator o)
            => o.Tier <= def.MaxTier || TripleCount(team) < def.TripleSlots;

        public bool IsValid(List<Operator> team) => TripleCount(team) <= def.TripleSlots;

        public double Score(List<Operator> team)
        {
            var counts = owner.Count(team.Select(o => o.Name), def.Stacking ? transforms : NoTransforms);
            if (def.Stacking) return StackingScore(team, counts);
            double score = PointWeight * (owner.Points(counts, goals) + (GoalPointFactor - 1) * owner.Points(counts, goals, _goalNames));
            foreach (var g in goals)
                score += GoalMember * Math.Min(counts.GetValueOrDefault(g.Name), g.Count);
            score += owner.RoleScore(team, counts, finalActive, stageWeight, def.TerminalLevel);
            if (previousCounts is not null)
                foreach (var g in goals)
                {
                    var drop = Math.Min(previousCounts.GetValueOrDefault(g.Name), g.Count) - Math.Min(counts.GetValueOrDefault(g.Name), g.Count);
                    if (drop > 0) score -= GoalRegression * drop;
                }
            var solo = counts.GetValueOrDefault(LineupPlanner.SoloCovenant);
            if (solo > 1) score -= SoloConflict * (solo - 1);
            foreach (var o in team)
            {
                var keep = next.Contains(o.Name);
                var final = finalNames.Contains(o.Name);
                if (keep) score += KeepForNextStage;
                if (previous.Contains(o.Name)) score += KeepFromPreviousStage;
                if (final) score += FinalMember;
                score += PerTier * o.Tier;
                if (o.Tier > def.MaxTier && !keep && !final) score -= SpeculativeTriple;
            }
            return score;
        }
    }
}
