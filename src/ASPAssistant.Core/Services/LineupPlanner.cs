using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

/// <summary>
/// Searches complete lineups (8/9 people × 0/1/2 transforms) that reach the selected covenant targets.
/// Feasible lineups are ranked by the number and level of additional covenants they activate.
/// </summary>
public sealed class LineupPlanner
{
    /// <summary>Covenants the 变形同构体 transform cannot grant.</summary>
    public static readonly IReadOnlySet<string> NoTransform = new HashSet<string> { "灵巧", "协防干员", "奇迹", "远见", "独行", "绝技", "调和" };
    /// <summary>Additional covenants whose level II is simply "3 operators".</summary>
    public static readonly IReadOnlySet<string> PeopleSecond = new HashSet<string> { "精准", "坚守", "助力", "不屈", "聚合", "奥术" };
    /// <summary>Covenants without a level II.</summary>
    public static readonly IReadOnlySet<string> NoSecond = new HashSet<string> { "独行", "调和", "协防干员" };
    /// <summary>These covenants apply in the preparation area and are not counted as lineup bonuses.</summary>
    public static readonly IReadOnlySet<string> NoAdditionalScore = new HashSet<string> { "奇迹", "投资人", "远见", "独行", "调和" };
    public const string SoloCovenant = "独行";
    public const int MaxLockedOperators = 9;
    /// <summary>Active 聚合 counts as a 奶位 like a healer.</summary>
    public const string HealingCovenant = "聚合";
    /// <summary>暴躁铁皮's own covenant.</summary>
    public const string MechCovenant = "协防干员";
    private const int MaxBranches = 48;

    private readonly IReadOnlyList<Operator> _operators;
    private readonly IReadOnlyList<CovenantInfo> _covenants;
    private readonly HashSet<string> _coreNames;

    public LineupPlanner(IEnumerable<Operator> operators, IEnumerable<CovenantInfo> covenants)
    {
        _operators = [.. operators.Where(o => o.RequiredStrategyId.Length == 0)];
        _covenants = [.. covenants];
        _coreNames = _covenants.Where(c => c.BondType == "SEASON").Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
    }

    public IReadOnlyList<CovenantInfo> Covenants => _covenants;

    // ── Goal helpers (shared with the UI) ───────────────────────────────────

    public static bool HasSecondLevel(CovenantInfo covenant) => !NoSecond.Contains(covenant.Name);

    /// <summary>True when level II depends on stacks / elites / other conditions the planner cannot verify.</summary>
    public static bool IsSecondLevelManual(CovenantInfo covenant)
        => covenant.BondType != "SEASON" && HasSecondLevel(covenant) && !PeopleSecond.Contains(covenant.Name);

    public static PlannerGoal CreateGoal(CovenantInfo covenant, int level)
    {
        var isCore = covenant.BondType == "SEASON";
        var basic = covenant.ActivateCount ?? (isCore ? 3 : 2);
        if (level < 2 || !HasSecondLevel(covenant)) return new PlannerGoal(covenant.Name, 1, basic, false, isCore);
        if (isCore) return new PlannerGoal(covenant.Name, 2, 6, false, true);
        if (PeopleSecond.Contains(covenant.Name)) return new PlannerGoal(covenant.Name, 2, 3, false, false);
        // The planner cannot verify stack/elite conditions for these II levels,
        // so a requested II behaves as the safely verifiable I level.
        return new PlannerGoal(covenant.Name, 1, basic, false, false);
    }

    public IReadOnlySet<string> DefaultTransformAllowed(IReadOnlySet<string>? bannedCovenants = null)
        => _covenants.Where(c => !NoTransform.Contains(c.Name) && bannedCovenants?.Contains(c.Name) != true)
            .Select(c => c.Name).ToHashSet(StringComparer.Ordinal);

    /// <summary>Operators that cannot appear because of covenant bans (see <see cref="CovenantBanRules"/>).</summary>
    public bool IsExcludedByBans(Operator op, IReadOnlySet<string> bannedCovenants)
        => CovenantBanRules.IsExcluded(op, bannedCovenants);

    // ── Search ──────────────────────────────────────────────────────────────

    /// <summary>Backwards-compatible flat list of every lineup found.</summary>
    public IReadOnlyList<PlannedLineup> Solve(PlannerRequest request, int maxResultsPerVariant = 3, int maxNodes = 40000)
        => Plan(request, CancellationToken.None, maxResultsPerVariant, maxNodes).AllLineups.ToList();

    public PlannerResult Plan(PlannerRequest request, CancellationToken cancellationToken = default,
        int maxResultsPerVariant = 3, int maxNodes = 40000)
    {
        if (request.Goals.Count == 0) return new PlannerResult();
        // A banned additional covenant only removes operators whose covenants are all banned; the rest can still
        // reach its activation count, so only banned core covenants are rejected as targets.
        var bannedGoal = request.Goals.FirstOrDefault(g => g.IsCore && request.BannedCovenants.Contains(g.Name));
        if (bannedGoal is not null) return new PlannerResult { Error = $"核心盟约“{bannedGoal.Name}”本局已被 Ban。" };
        if (request.LockedOperators.Count > MaxLockedOperators) return new PlannerResult { Error = $"必带干员不能超过 {MaxLockedOperators} 人。" };

        var candidates = _operators
            .Where(o => !request.BannedOperators.Contains(o.Name))
            .Where(o => !request.ExcludeMixedBannedOperators || !IsExcludedByBans(o, request.BannedCovenants))
            .ToList();
        var indexByName = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var i = 0; i < candidates.Count; i++) indexByName.TryAdd(candidates[i].Name, i);

        var missingLocked = request.LockedOperators.FirstOrDefault(n => !indexByName.ContainsKey(n));
        if (missingLocked is not null)
            return new PlannerResult { Error = $"必带干员“{missingLocked}”本局不可用（被禁用），请解除锁定。", CandidateCount = candidates.Count };

        var allowed = _covenants.Select(c => c.Name)
            .Where(n => request.TransformAllowed.Contains(n) && !request.BannedCovenants.Contains(n) && !NoTransform.Contains(n))
            .ToList();
        // Transform targets that are not in covenant data (tests, custom data) still count.
        allowed.AddRange(request.TransformAllowed.Where(n => !allowed.Contains(n) && !request.BannedCovenants.Contains(n) && !NoTransform.Contains(n) && _covenants.All(c => c.Name != n)));

        var ctx = new SearchContext(request, candidates, allowed, _covenants,
            [.. request.LockedOperators.Select(n => indexByName[n])], maxResultsPerVariant, maxNodes, cancellationToken);
        var variants = new List<PlannerVariantResult>();
        foreach (var transforms in request.TransformOptions)
            foreach (var people in request.PeopleOptions)
                variants.Add(ctx.SolveVariant(people, transforms));
        return new PlannerResult { Variants = variants, CandidateCount = candidates.Count };
    }

    /// <summary>
    /// Depth-first search over native covenant holders. Transforms are not branched on: once the remaining
    /// deficits can be covered by the transforms left, <see cref="TryFinish"/> assigns them to picked operators
    /// (adding a carrier operator only when nobody picked can take one).
    /// </summary>
    private sealed class SearchContext
    {
        private readonly PlannerRequest _request;
        private readonly List<Operator> _ops;
        private readonly IReadOnlyList<CovenantInfo> _covenants;
        private readonly HashSet<string> _coreNames;
        private readonly HashSet<string>[] _native;
        private readonly bool[] _harmony;
        private readonly List<string> _allowedOrdered;
        private readonly HashSet<string> _allowed;
        private readonly List<int> _locked;
        private readonly HashSet<int> _lockedSet;
        private readonly int _maxResults;
        private readonly int _maxNodes;
        private readonly CancellationToken _ct;
        private readonly Dictionary<string, int> _rarity;
        private readonly HashSet<string> _seen = new(StringComparer.Ordinal);
        /// <summary>暴躁铁皮 (takes one place): transformations it can carry (0 without the strategy).</summary>
        private readonly int _mechCap;
        /// <summary>Pith's 调和 piece (takes one place, not searched): counts like one more 调和 holder.</summary>
        private readonly int _extraHarmony;
        private readonly HashSet<string> _goalNames;
        private readonly HashSet<string> _additionalNames;
        private readonly bool _hasSoloGoal;
        // 奶位: every final lineup keeps at least one healer (or an active 聚合). Only enforced when the
        // operator data carries roles; if no lineup can have one, the search is repeated without it.
        private readonly bool[] _healer;
        private readonly bool _healerData;
        private readonly int _healingNeed;
        private bool _requireHealer;

        // per-variant state
        private int _people, _teamSize, _transforms, _nodes;
        private bool _limited;
        private List<PlannedLineup> _found = [];
        private readonly List<int> _picked = [];
        private readonly bool[] _used;
        private readonly HashSet<string> _visited = new(StringComparer.Ordinal);
        private static readonly IReadOnlyDictionary<int, string> NoTransforms = new Dictionary<int, string>();

        public SearchContext(PlannerRequest request, List<Operator> ops, List<string> allowed,
            IReadOnlyList<CovenantInfo> covenants,
            List<int> locked, int maxResults, int maxNodes, CancellationToken ct)
        {
            _request = request; _ops = ops; _covenants = covenants; _coreNames = covenants
                .Where(c => c.BondType == "SEASON").Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
            _allowedOrdered = allowed; _allowed = [.. allowed];
            _locked = locked; _lockedSet = [.. locked]; _maxResults = maxResults; _maxNodes = maxNodes; _ct = ct;
            _native = ops.Select(o => o.CoreCovenants.Concat(o.AdditionalCovenants).ToHashSet(StringComparer.Ordinal)).ToArray();
            _harmony = _native.Select(c => c.Contains(CovenantCalculator.HarmonyCovenant)).ToArray();
            _used = new bool[ops.Count];
            _rarity = request.Goals.ToDictionary(g => g.Name, g => _native.Count(n => n.Contains(g.Name)));
            _goalNames = request.Goals.Select(g => g.Name).ToHashSet(StringComparer.Ordinal);
            _additionalNames = _covenantNames.Where(n => !_goalNames.Contains(n)
                && !_coreNames.Contains(n) && !NoAdditionalScore.Contains(n)).ToHashSet(StringComparer.Ordinal);
            _hasSoloGoal = request.Goals.Any(g => g.Name == SoloCovenant);
            _healer = ops.Select(o => o.Profile?.IsHealer == true).ToArray();
            _healerData = _healer.Any(h => h);
            var healing = covenants.FirstOrDefault(c => c.Name == HealingCovenant);
            _healingNeed = healing is null ? 0 : CreateGoal(healing, 1).Count;
            _mechCap = request.Strategy == PlannerStrategy.Mech ? PlannerStrategies.MechTransformCap(request.MechElite) : 0;
            _extraHarmony = request.Strategy == PlannerStrategy.Pith ? 1 : 0;
        }

        private bool HasHealer(IReadOnlyList<int> team, Dictionary<string, int> counts)
            => !_requireHealer || team.Any(i => _healer[i])
               || _healingNeed > 0 && counts.GetValueOrDefault(HealingCovenant) >= _healingNeed;

        private IReadOnlyList<string> _covenantNames => _covenants.Select(c => c.Name).ToList();

        public PlannerVariantResult SolveVariant(int people, int transforms)
        {
            _ct.ThrowIfCancellationRequested();
            // 暴躁铁皮 / Pith take one of the places themselves.
            var places = people - PlannerStrategies.PiecePlaces(_request.Strategy);
            _people = people; _teamSize = Math.Min(places, _ops.Count); _transforms = transforms;
            _nodes = 0; _limited = false; _found = [];
            _picked.Clear(); _visited.Clear(); Array.Clear(_used);
            if (_locked.Count > places) return new PlannerVariantResult(people, transforms, [], false);
            foreach (var i in _locked) { _picked.Add(i); _used[i] = true; }
            _requireHealer = _healerData;
            Visit();
            if (_found.Count == 0 && _requireHealer)
            {
                // Targets leave no room for a healer: return the best lineups anyway, flagged.
                _requireHealer = false;
                _nodes = 0; _visited.Clear();
                Visit();
                _found = [.. _found.Select(f => f with { MissingHealer = true })];
            }
            var limited = _limited;
            return new PlannerVariantResult(people, transforms,
                _found.Select(f => f with { SearchLimited = limited }).ToList(), limited);
        }

        private (Dictionary<string, int> Counts, Dictionary<string, int> Bonus) Count(IReadOnlyList<int> team,
            IReadOnlyDictionary<int, string> tr, IReadOnlyCollection<string>? mech = null)
        {
            var counts = _covenants.Select(c => c.Name).Distinct(StringComparer.Ordinal)
                .ToDictionary(n => n, _ => 0, StringComparer.Ordinal);
            foreach (var target in tr.Values.Concat(mech ?? []))
                if (!counts.ContainsKey(target)) counts[target] = 0;
            foreach (var goal in _request.Goals)
                counts.TryAdd(goal.Name, 0);
            var present = new HashSet<string>(StringComparer.Ordinal);
            var harmony = 0;
            foreach (var i in team)
            {
                var op = _ops[i];
                foreach (var c in op.CoreCovenants)
                {
                    if (c == CovenantCalculator.HarmonyCovenant) continue;
                    if (counts.ContainsKey(c)) counts[c]++;
                    present.Add(c);
                }
                foreach (var c in op.AdditionalCovenants)
                    if (c != CovenantCalculator.HarmonyCovenant && counts.ContainsKey(c)) counts[c]++;
                if (_harmony[i])
                {
                    harmony++;
                    if (counts.ContainsKey(CovenantCalculator.HarmonyCovenant)) counts[CovenantCalculator.HarmonyCovenant]++;
                }
                if (tr.TryGetValue(i, out var t))
                {
                    if (counts.ContainsKey(t)) counts[t]++;
                    present.Add(t);
                }
            }
            // 暴躁铁皮 itself is a 协防干员 (another one activates it).
            if (_mechCap > 0 && counts.ContainsKey(MechCovenant)) counts[MechCovenant]++;
            foreach (var t in mech ?? [])
            {
                counts[t]++;
                present.Add(t);
            }
            if (_extraHarmony > 0)
            {
                harmony += _extraHarmony;
                if (counts.ContainsKey(CovenantCalculator.HarmonyCovenant)) counts[CovenantCalculator.HarmonyCovenant] += _extraHarmony;
            }
            var bonus = new Dictionary<string, int>(StringComparer.Ordinal);
            if (harmony > 0)
                foreach (var g in _request.Goals.Where(g => g.IsCore && present.Contains(g.Name)))
                {
                    counts[g.Name] += harmony;
                    bonus[g.Name] = harmony;
                }
            return (counts, bonus);
        }

        private bool Done => _nodes > _maxNodes;

        private int TransformDeficit(PlannerGoal goal, Dictionary<string, int> counts)
        {
            var deficit = Math.Max(0, goal.Count - counts[goal.Name]);
            return goal.IsCore && counts[goal.Name] == 0 && deficit > 0
                ? Math.Max(1, deficit - _picked.Count(i => _harmony[i]) - _extraHarmony)
                : deficit;
        }

        private bool Contributes(int i, PlannerGoal g, Dictionary<string, int> counts)
            => _native[i].Contains(g.Name) || (g.IsCore && _harmony[i]
                && (counts[g.Name] > 0 || _transforms > 0 && _allowed.Contains(g.Name)));

        private void Visit()
        {
            if (_found.Count >= _maxResults) return;
            if (++_nodes > _maxNodes) { _limited = true; return; }
            if ((_nodes & 63) == 0) _ct.ThrowIfCancellationRequested();

            var (counts, _) = Count(_picked, NoTransforms);
            if (_hasSoloGoal && counts[SoloCovenant] > 1) return;
            var missing = _request.Goals.Where(g => counts[g.Name] < g.Count).ToList();
            var slots = _teamSize - _picked.Count;
            var trLeft = _transforms;

            // Once targets are reachable, keep branching until the whole lineup is
            // filled. This is what lets the score see the operators in the free slots.
            var transformDeficit = missing.Sum(g => TransformDeficit(g, counts));
            var needHealer = !HasHealer(_picked, counts);
            if (slots <= 0)
            {
                if (missing.All(g => _allowed.Contains(g.Name)) && transformDeficit <= trLeft)
                    TryFinish(counts);
                return;
            }
            // Last free slot and still no 奶位: it has to be a healer (a transform into 聚合 is checked at finish).
            if (!CanStillReach(missing, counts, slots, trLeft)) return;
            if (!_visited.Add(StateKey())) return;

            // Branch on the hardest goal: ones that cannot be transformed first, then the rarest.
            // If transforms can cover every remaining deficit, the next operator
            // is selected for the score rather than for a target covenant.
            var pivot = missing.Count > 0 && !(missing.All(g => _allowed.Contains(g.Name)) && transformDeficit <= trLeft) ? missing
                .OrderBy(g => _allowed.Contains(g.Name) ? 1 : 0)
                .ThenBy(g => _rarity[g.Name])
                .ThenByDescending(g => g.Count - counts[g.Name])
                .First() : null;

            var branches = new List<(int Index, double Score)>();
            for (var i = 0; i < _ops.Count; i++)
            {
                if (_used[i]) continue;
                if (_hasSoloGoal && _native[i].Contains(SoloCovenant) && counts[ SoloCovenant ] >= 1) continue;
                if (pivot is not null && !Contributes(i, pivot, counts)) continue;
                if (needHealer && slots == 1 && !_healer[i] && trLeft == 0) continue;
                var gain = missing.Count(g => Contributes(i, g, counts));
                var extra = _native[i].Count(c => _additionalNames.Contains(c));
                var heal = needHealer && _healer[i] ? 5 : 0;
                branches.Add((i, 10 * gain + heal + 0.8 * extra + (i * 13 % 31) / 50.0));
            }

            // A solo covenant can make the requested people count impossible to
            // fill with valid operators. Keep the best partial lineup in that case.
            if (branches.Count == 0 && missing.Count == 0)
            {
                TryFinish(counts);
                return;
            }

            var expanded = 0;
            foreach (var b in branches.OrderByDescending(b => b.Score).ThenBy(b => b.Index))
            {
                if (Done) break;
                if (expanded++ >= MaxBranches) { if (pivot is not null) _limited = true; break; }
                _picked.Add(b.Index); _used[b.Index] = true;
                Visit();
                _picked.RemoveAt(_picked.Count - 1); _used[b.Index] = false;
            }
        }

        /// <summary>Optimistic bound: can the remaining slots and transforms still cover every deficit?</summary>
        private bool CanStillReach(List<PlannerGoal> missing, Dictionary<string, int> counts, int slots, int trLeft)
        {
            var maxCover = 0;
            var unusedNative = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < _ops.Count; i++)
            {
                if (_used[i]) continue;
                var cover = 0;
                foreach (var g in missing)
                {
                    if (!_native[i].Contains(g.Name) && !(g.IsCore && _harmony[i])) continue;
                    cover++;
                    unusedNative[g.Name] = unusedNative.GetValueOrDefault(g.Name) + 1;
                }
                if (cover > maxCover) maxCover = cover;
            }
            var totalDeficit = 0;
            foreach (var g in missing)
            {
                var deficit = TransformDeficit(g, counts);
                totalDeficit += deficit;
                var reach = Math.Min(slots, unusedNative.GetValueOrDefault(g.Name));
                if (_allowed.Contains(g.Name)) reach += trLeft;
                if (deficit > reach) return false;
            }
            return totalDeficit <= slots * maxCover + trLeft;
        }

        private string StateKey()
        {
            var ids = _picked.ToArray();
            Array.Sort(ids);
            return string.Join(',', ids);
        }

        /// <summary>Assigns transforms and records a complete, scored lineup.</summary>
        private bool TryFinish(Dictionary<string, int> nativeCounts)
        {
            var team = new List<int>(_picked);
            var tr = new Dictionary<int, string>();
            var mech = new List<string>();
            int Used() => tr.Count + mech.Count;

            // 暴躁铁皮 first: it carries several job-change gear on one 变形同构体.
            bool Assign(string covenant)
            {
                if (mech.Count < _mechCap && !mech.Contains(covenant)) { mech.Add(covenant); return true; }
                foreach (var i in team.OrderBy(i => _lockedSet.Contains(i)))
                    if (!tr.ContainsKey(i) && !_native[i].Contains(covenant)) { tr[i] = covenant; return true; }
                return false;
            }

            foreach (var g in _request.Goals)
            {
                var deficit = TransformDeficit(g, nativeCounts);
                for (var k = 0; k < deficit; k++)
                {
                    if (Used() >= _transforms || !_allowed.Contains(g.Name)) return false;
                    if (!Assign(g.Name)) return false;
                }
            }

            var forced = 0;
            var goalTargets = _request.Goals.Select(g => g.Name).Where(_allowed.Contains).ToList();
            while (Used() < _transforms && mech.Count < _mechCap)
            {
                var target = goalTargets.FirstOrDefault(n => !mech.Contains(n))
                    ?? _allowedOrdered.Where(n => !mech.Contains(n))
                        .OrderByDescending(n => TransformScore(n, team, tr, mech)).FirstOrDefault();
                if (target is null) break;
                mech.Add(target); forced++;
            }
            foreach (var i in team.OrderBy(i => _lockedSet.Contains(i)).ToList())
            {
                if (Used() >= _transforms) break;
                if (tr.ContainsKey(i)) continue;
                var target = goalTargets.FirstOrDefault(n => !_native[i].Contains(n));
                target ??= _allowedOrdered
                    .Where(n => !_native[i].Contains(n))
                    .OrderByDescending(n => TransformScore(n, team, tr, mech))
                    .FirstOrDefault();
                if (target is null) continue;
                tr[i] = target; forced++;
            }
            if (Used() != _transforms) return false;

            var (counts, bonus) = Count(team, tr, mech);
            if (_request.Goals.Any(g => counts[g.Name] < g.Count)) return false;
            if (!HasHealer(team, counts)) return false;
            if (_hasSoloGoal && counts[SoloCovenant] != 1) return false;

            var key = string.Join(',', team.Select(i => _ops[i].Name).Order(StringComparer.Ordinal)) + '|' +
                      string.Join(',', tr.Select(x => _ops[x.Key].Name + ':' + x.Value).Order(StringComparer.Ordinal)) + '|' +
                      string.Join(',', mech.Order(StringComparer.Ordinal));
            if (!_seen.Add(key)) return true;

            var (additionalScore, additionalLevels) = Score(counts);

            var operators = team
                .OrderBy(i => _lockedSet.Contains(i) ? 0 : 1)
                .Select(i => new PlannedOperator(_ops[i].Name, tr.GetValueOrDefault(i), [.. _native[i]])
                {
                    IsLocked = _lockedSet.Contains(i),
                    IsHarmony = _harmony[i],
                    IconPath = _ops[i].IconPath
                }).ToList();
            var lineup = new PlannedLineup(_people, _transforms, operators, counts, false)
            {
                Goals = _request.Goals,
                ForcedTransforms = forced,
                HarmonyBonus = bonus,
                AdditionalScore = additionalScore,
                AdditionalCovenants = additionalLevels,
                Strategy = _request.Strategy,
                MechCovenants = mech,
                MechElite = _request.MechElite
            };
            _found.Add(lineup);
            _found = _found
                .OrderByDescending(x => x.AdditionalScore)
                .ThenByDescending(x => x.AdditionalCovenants.Count)
                .ThenBy(x => x.Operators.Sum(o => o.IsLocked ? 0 : 1))
                .Take(_maxResults)
                .ToList();
            return true;
        }

        private int TransformScore(string covenant, IReadOnlyList<int> team, IReadOnlyDictionary<int, string> tr,
            IReadOnlyCollection<string> mech)
        {
            if (!_additionalNames.Contains(covenant)) return 0;
            var (counts, _) = Count(team, tr, mech);
            var current = counts.GetValueOrDefault(covenant);
            var info = _covenants.FirstOrDefault(c => c.Name == covenant);
            var threshold = info?.ActivateCount ?? 2;
            if (current + 1 < threshold) return 0;
            return IsKnownSecond(covenant) && current + 1 >= 3 ? 3 : 1;
        }

        private (int Score, IReadOnlyDictionary<string, int> Levels) Score(IReadOnlyDictionary<string, int> counts)
        {
            var levels = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var covenant in _covenants)
            {
                if (!_additionalNames.Contains(covenant.Name)) continue;
                var count = counts.GetValueOrDefault(covenant.Name);
                var threshold = covenant.ActivateCount ?? 2;
                if (count < threshold) continue;
                var level = IsKnownSecond(covenant.Name) && count >= 3 ? 3 : 1;
                levels[covenant.Name] = level;
            }
            return (levels.Values.Sum(), levels);
        }

        private static bool IsKnownSecond(string covenant)
            => PeopleSecond.Contains(covenant);
    }
}
