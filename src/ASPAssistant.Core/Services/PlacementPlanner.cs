using System.Text.RegularExpressions;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

public sealed record PlacementOptions
{
    /// <summary>Operators on the field (8 unless the player says 9).</summary>
    public int FieldCount { get; init; } = 8;
    public PlacementMode Mode { get; init; } = PlacementMode.Balanced;
    /// <summary>Operators the player fixed by hand.</summary>
    public IReadOnlyDictionary<string, PlacementPin> Pins { get; init; } = new Dictionary<string, PlacementPin>();
    public int Seed { get; init; } = 1;
    public int Iterations { get; init; } = 5000;
    public int Restarts { get; init; } = 5;

    public double StackWeight => Mode switch
    {
        PlacementMode.Combat => 0.25,
        PlacementMode.Stacking => 2.5,
        _ => 1.0,
    };
}

/// <summary>
/// Picks which operators of the pool go on the field and where they stand, by simulated annealing over a
/// hand-tuned score. Ported from the positioning prototype; the weights are estimates, not game numbers:
/// <list type="bullet">
/// <item>Each route's block point is the first operator on it. Tanks belong there, melee damage dealers right
/// behind (they also refill the line), ranged operators and healers further back. The lower route gets about
/// twice the enemies of the upper one.</item>
/// <item>Damage is the time enemies spend in an operator's range, weighted towards the block point and the
/// 1–2 cells in front of it, times how much the operator gains from damage buffs.</item>
/// <item>Tiles that buff enemies (红地板, 蓝箭头) buff them whenever they walk over, so the defence line
/// should be in front of those cells; 沼泽 slows both sides, so operators avoid standing on it.</item>
/// <item>Stacking from positional traits (身前 / 身后一格, 周围四格 / 八格, 同一行, 攻击范围内), worth more the
/// more the stacked covenant's stat helps the lineup.</item>
/// </list>
/// 阿戈尔's devour and the snow map's stealth cells are not modelled.
/// </summary>
public sealed class PlacementPlanner
{
    private readonly IReadOnlyDictionary<string, Operator> _operators;
    private readonly IReadOnlyDictionary<string, CovenantInfo> _covenants;

    public PlacementPlanner(IReadOnlyDictionary<string, Operator> operators, IEnumerable<CovenantInfo> covenants)
    {
        _operators = operators;
        _covenants = covenants.GroupBy(c => c.Name).ToDictionary(g => g.Key, g => g.First());
    }

    /// <summary>Operators the planner can use (they need class, block and range data).</summary>
    public bool CanPlace(string name) =>
        _operators.TryGetValue(name, out var op) && op.Profile is { } p && p.NormalRange?.BaseRange is not null;

    public bool IsMelee(string name) => _operators.TryGetValue(name, out var op) && op.Profile?.IsMelee == true;

    /// <summary>Chooses the field operators from <paramref name="pool"/> and arranges them.</summary>
    public PlacementPlan Plan(GarrisonMap map, IReadOnlyList<PlacementCandidate> pool, PlacementOptions options,
        CancellationToken cancel = default)
    {
        var job = new Job(this, map, pool, options);
        var best = job.Solve(cancel);
        return job.Explain(best);
    }

    /// <summary>Scores a given arrangement (e.g. after the player dragged someone), without moving anyone.</summary>
    public PlacementPlan Evaluate(GarrisonMap map, IReadOnlyList<PlacementCandidate> pool,
        IReadOnlyDictionary<string, (BoardCell Cell, Facing Facing)> placement, PlacementOptions options)
    {
        var job = new Job(this, map, pool, options);
        return job.Explain(job.Normalise(placement));
    }

    /// <summary>The best other cells for <paramref name="name"/> with everyone else where they are.</summary>
    public IReadOnlyList<PlacementAlternative> Alternatives(GarrisonMap map, IReadOnlyList<PlacementCandidate> pool,
        IReadOnlyDictionary<string, (BoardCell Cell, Facing Facing)> placement, string name, PlacementOptions options,
        int count = 2)
    {
        var job = new Job(this, map, pool, options);
        var pl = job.Normalise(placement);
        if (!pl.TryGetValue(name, out var here)) return [];
        double current = job.Score(pl);
        var occupied = pl.Where(kv => kv.Key != name).Select(kv => kv.Value.Cell).ToHashSet();
        var result = new List<PlacementAlternative>();
        foreach (var cell in job.Board.Deployable)
        {
            if (cell == here.Cell || occupied.Contains(cell) || !job.Board.Legal(job.Info[name], cell)) continue;
            (double Score, Facing Facing) best = (double.MinValue, Facing.Right);
            foreach (var f in Faces)
            {
                pl[name] = (cell, f);
                double s = job.Score(pl);
                if (s > best.Score) best = (s, f);
            }
            pl[name] = (cell, best.Facing);
            var notes = job.NotesFor(pl, name);
            result.Add(new PlacementAlternative(cell, best.Facing, Math.Round(best.Score - current, 1),
                notes.FirstOrDefault() ?? PlacementCells.FacingText(best.Facing)));
        }
        pl[name] = here;
        return [.. result.OrderByDescending(a => a.Delta).Take(count)];
    }

    // ── Static tables ──────────────────────────────────────────────────────

    private static readonly Facing[] Faces = [Facing.Right, Facing.Down, Facing.Left, Facing.Up];
    private static readonly Dictionary<int, double> TierPower = new() { [1] = 0.6, [2] = 0.75, [3] = 0.9, [4] = 1.1, [5] = 1.35, [6] = 1.6 };
    private static readonly Dictionary<string, double> ProfessionDps = new()
    {
        ["近卫"] = 1.0, ["狙击"] = 1.0, ["术师"] = 1.0, ["特种"] = 0.85, ["先锋"] = 0.7, ["辅助"] = 0.5, ["医疗"] = 0.15, ["重装"] = 0.35,
    };

    private enum Stat { Atk, Aspd, Hp, Heal, None }

    /// <summary>How much each role gains from a kind of buff (0–1).</summary>
    private static double Affinity(Stat stat, PlacementRole role) => stat switch
    {
        Stat.Atk or Stat.Aspd => role switch { PlacementRole.Damage => 1.0, PlacementRole.Tank => 0.3, PlacementRole.Support => stat == Stat.Aspd ? 0.5 : 0.45, _ => 0.45 },
        Stat.Hp => role switch { PlacementRole.Tank => 1.0, PlacementRole.Damage => 0.35, _ => 0.3 },
        Stat.Heal => role switch { PlacementRole.Support => 1.0, PlacementRole.Tank => 0.2, _ => 0.1 },
        _ => 0,
    };

    /// <summary>What a covenant's layers mainly add (covenant_effects.json).</summary>
    private static readonly Dictionary<string, Stat> CovenantStat = new()
    {
        ["炎"] = Stat.Atk, ["萨尔贡"] = Stat.Aspd, ["维多利亚"] = Stat.Atk, ["谢拉格"] = Stat.Atk, ["阿戈尔"] = Stat.Hp,
        ["叙拉古"] = Stat.Aspd, ["卡西米尔"] = Stat.Atk, ["乌萨斯"] = Stat.Atk, ["伊比利亚"] = Stat.Atk, ["莱塔尼亚"] = Stat.Atk,
        ["精准"] = Stat.Atk, ["迅捷"] = Stat.Aspd, ["灵巧"] = Stat.Aspd, ["坚守"] = Stat.Hp, ["助力"] = Stat.Hp, ["突袭"] = Stat.Atk,
        ["奥术"] = Stat.Atk, ["聚合"] = Stat.Heal, ["协防干员"] = Stat.Hp, ["独行"] = Stat.Atk, ["远见"] = Stat.None,
        ["奇迹"] = Stat.None, ["投资人"] = Stat.None, ["不屈"] = Stat.Hp, ["调和"] = Stat.None, ["绝技"] = Stat.Atk,
    };

    /// <summary>Covenants whose buff applies to every operator on the field.</summary>
    private static readonly HashSet<string> GlobalCovenants = ["坚守", "助力"];
    /// <summary>Covenants that count operators on the bench too.</summary>
    private static readonly HashSet<string> BenchCountedCovenants = ["远见", "奇迹", "投资人"];
    private static readonly HashSet<string> PositionalStackers = ["凛御银灰", "Mon3tr", "阿米娅", "华法琳", "铃兰", "塞雷娅", "罗比菈塔", "浊心斯卡蒂"];
    private static readonly HashSet<string> RowOfThree = ["机械师", "歌蕾蒂娅"];
    private static readonly double[] RouteWeight = [1.0, 2.0];
    private const double BlockNeed = 5;

    private enum TileKind { Slow, EnemyBuff, Facing }

    /// <summary><paramref name="EnemyPenalty"/>: per route, when enemies walk the tile before reaching the line.</summary>
    private sealed record TileEffect(TileKind Kind, Stat Stat, double Ally, bool Drain, string Name, double EnemyPenalty = 0, bool HintOnly = false);

    private static readonly Dictionary<string, TileEffect> Tiles = new()
    {
        ["desert_camo"] = new(TileKind.Slow, Stat.Aspd, -0.45, false, "沼泽"),
        ["desert_laser"] = new(TileKind.EnemyBuff, Stat.Atk, 0.3, true, "红地板", EnemyPenalty: 8),
        // Arrows only make enemies faster, so standing on them / keeping the line in front is a preference, not a must.
        ["palace_arrows"] = new(TileKind.EnemyBuff, Stat.Aspd, 0.25, false, "蓝箭头", EnemyPenalty: 3, HintOnly: true),
        ["city"] = new(TileKind.Facing, Stat.Atk, 0.3, false, "橙色通道"),
    };

    private static readonly (BoardCell From, BoardCell To) PalaceTeleport = (new(3, 0), new(3, 3));

    private static readonly Regex CapPattern = new(@"至多叠加(\d+)层");
    private static readonly Regex AmountPattern = new(@"层数\+(\d+)");
    private static readonly Regex OncePattern = new(@"<获得时>.*?层数\+(\d+)");
    private static readonly Regex SelfFrontPattern = new(@"自身(以|及)?及?身前一格干员已激活的盟约层数\+");
    private static readonly Regex SelfFrontAmount = new(@"身前一格干员已激活的盟约层数\+(\d+)");
    private static readonly Regex SkillBlockPattern = new(@"(?<!身前一格[^。；]*)阻挡数\+");
    private static readonly Dictionary<string, double> SelfFrontOverrides = new() { ["旅骨"] = 3 };
    private static readonly Dictionary<string, double> SelfBack = new() { ["青枳"] = 3, ["佩佩"] = 3 };

    private static double Number(Regex pattern, string text, double fallback = 0)
    {
        var m = pattern.Match(text);
        return m.Success && double.TryParse(m.Groups[1].Value, out var v) ? v : fallback;
    }

    // ── Per-operator facts ─────────────────────────────────────────────────

    private sealed class OpInfo
    {
        public required string Name;
        public required PlacementRole Role;
        public required double Power;
        public required bool Melee;
        public required int Block;
        public required (int Col, int Row)[][] Offsets; // by Facing
        public required string[] Covenants;
        public required int Tier;
        public required bool Healer;
        public required string Trait;
        public required double Cap;
        public required bool StacksLayers;
        public required double OnceAmount;
        public required double TriggersPerRound;
        public required int SkillCasts;
        public required double SelfFront;
        public required double SelfBack;
        public required bool KillRange;
        public required bool Around4;
        public required bool BenchOk;
        public required bool SkillBlock;
        /// <summary>Own stacking from the profile: covenant (named covenants) / self (own covenants) / other.</summary>
        public required string StackKind;
        public required string[] StackCovenants;
        public required double StackPerRound;
        public required double StackAmount;
    }

    private OpInfo? BuildInfo(PlacementCandidate c)
    {
        if (!_operators.TryGetValue(c.Name, out var op) || op.Profile is not { } p) return null;
        var range = p.RangeFor(c.IsElite);
        if (range is null) return null;
        var variant = c.IsElite && op.Elite.Traits.Count > 0 ? op.Elite : op.Normal;
        string trait = string.Join(" ", variant.Traits.Select(t => t.TraitDescription));
        double cap = Number(CapPattern, trait);
        double amount = Number(AmountPattern, trait, 1);
        if (amount <= 0) amount = 1;
        bool once = trait.Contains("<获得时>");
        var skill = c.IsElite ? p.EliteSkill ?? p.NormalSkill : p.NormalSkill;
        int casts = skill is null || skill.SpType == "被动" || skill.SpCost == 0 ? 0
            : skill.SpCost <= 8 ? 7 : skill.SpCost <= 20 ? 4 : skill.SpCost <= 40 ? 2 : 1;
        double selfFront = SelfFrontOverrides.TryGetValue(c.Name, out var sf) ? sf
            : SelfFrontPattern.IsMatch(trait) ? Number(SelfFrontAmount, trait) : 0;
        var offsets = Faces.Select(f => range.Offsets(f).ToArray()).ToArray();
        var info = new OpInfo
        {
            Name = c.Name,
            Role = PlacementRole.Damage,
            Power = TierPower.GetValueOrDefault(op.Tier, 1) * ProfessionDps.GetValueOrDefault(p.Profession, 0.6) * (c.IsElite ? 1.15 : 1),
            Melee = p.IsMelee,
            Block = p.BlockCount,
            Offsets = offsets,
            Covenants = [.. op.CoreCovenants, .. op.AdditionalCovenants],
            Tier = op.Tier,
            Healer = p.IsHealer || p.Profession == "医疗",
            Trait = trait,
            Cap = cap,
            StacksLayers = !once && Regex.IsMatch(trait, @"层数\+\d|叠加"),
            OnceAmount = once ? Number(OncePattern, trait) : 0,
            TriggersPerRound = cap > 0 ? cap / amount : trait.Contains("<战斗中>") ? 4 : 1,
            SkillCasts = casts,
            SelfFront = selfFront,
            SelfBack = SelfBack.GetValueOrDefault(c.Name),
            KillRange = trait.Contains("攻击范围内每有一名敌人") && trait.Contains("层数"),
            Around4 = trait.Contains("周围四格"),
            BenchOk = trait.Contains("整备区也生效"),
            SkillBlock = c.Name != "帕拉斯" && skill is not null && SkillBlockPattern.IsMatch(skill.Description),
            StackKind = p.Stack?.Kind ?? "",
            StackCovenants = [.. p.Stack?.Covenants ?? []],
            StackPerRound = p.Stack?.PerRound ?? 0,
            StackAmount = p.Stack?.Amount ?? 0,
        };
        info.Role = RoleOf(info, p, op.Tier);
        return info;
    }

    private static PlacementRole RoleOf(OpInfo i, OperatorProfile p, int tier)
    {
        if (p.Profession == "重装") return PlacementRole.Tank;
        if (p.Profession == "医疗" || p.IsHealer) return PlacementRole.Support;
        bool positional = i.SelfFront > 0 || i.SelfBack > 0 || PositionalStackers.Contains(i.Name);
        bool stacker = positional || p.Stack is { } s && s.Kind != "once" && !string.IsNullOrEmpty(s.Kind) && s.PerRound >= 3;
        if (stacker && (tier <= 3 || p.Profession is "辅助" or "先锋") && i.Name != "凛御银灰") return PlacementRole.Stacker;
        if (p.Profession == "辅助") return PlacementRole.Support;
        return PlacementRole.Damage;
    }

    // ── Board ──────────────────────────────────────────────────────────────

    private sealed class BoardInfo
    {
        public BoardInfo(GarrisonMap map)
        {
            Map = map;
            Routes = map.Routes.Select(r => r.ToArray()).ToArray();
            RouteIndex = Routes.Select(r =>
            {
                var d = new Dictionary<BoardCell, int>();
                for (int j = 0; j < r.Length; j++) d.TryAdd(r[j], j);
                return d;
            }).ToArray();
            Deployable = map.AllCells().Where(c => c.Type is BoardCellType.Ground or BoardCellType.Special or BoardCellType.High)
                .Select(c => c.Cell).ToArray();
            Special = map.AllCells().Where(c => c.Type == BoardCellType.Special).Select(c => c.Cell).ToHashSet();
            Tile = Tiles.GetValueOrDefault(map.Id);
            if (map.Id == "palace")
                foreach (var r in Routes)
                {
                    int k = Array.IndexOf(r, PalaceTeleport.To);
                    if (k >= 0) { Extra = [r[k..]]; break; }
                }
            ExtraIndex = Extra.Select(r =>
            {
                var d = new Dictionary<BoardCell, int>();
                for (int j = 0; j < r.Length; j++) d.TryAdd(r[j], j);
                return d;
            }).ToArray();
        }

        public GarrisonMap Map { get; }
        public BoardCell[][] Routes { get; }
        public Dictionary<BoardCell, int>[] RouteIndex { get; }
        public BoardCell[][] Extra { get; } = [];
        public Dictionary<BoardCell, int>[] ExtraIndex { get; }
        public BoardCell[] Deployable { get; }
        public HashSet<BoardCell> Special { get; }
        public TileEffect? Tile { get; }

        public bool Legal(OpInfo op, BoardCell cell) => GarrisonMap.CanDeploy(Map.At(cell), op.Melee);
        public bool OnMainRoute(BoardCell c) => RouteIndex.Any(d => d.ContainsKey(c));
    }

    // ── One planning job ───────────────────────────────────────────────────

    private sealed class CovenantContext
    {
        public required HashSet<string> Active;
        public required Dictionary<string, double> LayerValue;
        public required Dictionary<string, int> Counts;
        public double Lv(IEnumerable<string> covs) => covs.Sum(c => LayerValue.GetValueOrDefault(c));
    }

    private sealed class Job
    {
        private readonly PlacementPlanner _owner;
        private readonly PlacementOptions _options;
        private readonly List<string> _pool;
        private readonly List<string> _unplaceable;
        private readonly Dictionary<string, CovenantContext> _covCache = [];

        public Job(PlacementPlanner owner, GarrisonMap map, IReadOnlyList<PlacementCandidate> pool, PlacementOptions options)
        {
            _owner = owner;
            _options = options;
            Board = new BoardInfo(map);
            Info = [];
            _unplaceable = [];
            foreach (var c in pool.DistinctBy(c => c.Key))
            {
                var info = owner.BuildInfo(c);
                if (info is null) { if (!_unplaceable.Contains(c.Name)) _unplaceable.Add(c.Name); }
                else Info[c.Key] = info;
            }
            _pool = [.. Info.Keys];
        }

        public BoardInfo Board { get; }
        public Dictionary<string, OpInfo> Info { get; }

        private int FieldCount => Math.Min(Math.Max(1, _options.FieldCount), _pool.Count);

        public Dictionary<string, (BoardCell Cell, Facing Facing)> Normalise(
            IReadOnlyDictionary<string, (BoardCell Cell, Facing Facing)> placement) =>
            placement.Where(kv => Info.ContainsKey(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);

        private CovenantContext Covenants(IEnumerable<string> field)
        {
            var names = field.OrderBy(n => n, StringComparer.Ordinal).ToList();
            string key = string.Join("|", names);
            if (_covCache.TryGetValue(key, out var cached)) return cached;
            // Covenants count different operators: a second copy of the same operator adds nothing.
            var byCovenant = new Dictionary<string, HashSet<string>>();
            void Add(string c, string op) { if (!byCovenant.TryGetValue(c, out var set)) byCovenant[c] = set = []; set.Add(op); }
            var fieldSet = names.ToHashSet();
            foreach (var n in names)
                foreach (var c in Info[n].Covenants) Add(c, Info[n].Name);
            foreach (var n in _pool.Where(n => !fieldSet.Contains(n)))
                foreach (var c in Info[n].Covenants.Where(BenchCountedCovenants.Contains)) Add(c, Info[n].Name);
            var counts = byCovenant.ToDictionary(kv => kv.Key, kv => kv.Value.Count);
            var active = counts.Where(kv => kv.Value >= (_owner._covenants.TryGetValue(kv.Key, out var ci) ? ci.ActivateCount ?? 99 : 99))
                .Select(kv => kv.Key).ToHashSet();
            var lv = new Dictionary<string, double>();
            foreach (var c in active)
            {
                var stat = CovenantStat.GetValueOrDefault(c, Stat.Atk);
                var members = GlobalCovenants.Contains(c) ? names : names.Where(n => Info[n].Covenants.Contains(c));
                double v = members.Sum(n => Affinity(stat, Info[n].Role) * (stat is Stat.Atk or Stat.Aspd ? Info[n].Power : 1));
                lv[c] = 0.25 * v;
            }
            var ctx = new CovenantContext { Active = active, LayerValue = lv, Counts = counts };
            _covCache[key] = ctx;
            return ctx;
        }

        public double Score(Dictionary<string, (BoardCell Cell, Facing Facing)> pl) => Evaluate(pl, null).Total;

        public List<string> NotesFor(Dictionary<string, (BoardCell Cell, Facing Facing)> pl, string name)
        {
            var notes = pl.Keys.ToDictionary(k => k, _ => new List<string>());
            Evaluate(pl, notes);
            return notes[name].Distinct().ToList();
        }

        private sealed class Breakdown
        {
            public double Combat, Stack, Activation, Total;
            public double[] BlockTotal = [];
            public int?[] BlockAt = [];
            public bool TilePenalty;
            public bool OnSlowTile;
            public List<string> Healers = [];
            public bool HealerCoversLine;
            public CovenantContext Cov = null!;
        }

        private Breakdown Evaluate(Dictionary<string, (BoardCell Cell, Facing Facing)> pl,
            Dictionary<string, List<string>>? notes)
        {
            void Note(string n, string text) { if (notes is not null) notes[n].Add(text); }
            var cov = Covenants(pl.Keys);
            var at = new Dictionary<BoardCell, string>();
            foreach (var (n, v) in pl) at[v.Cell] = n;
            double comb = 0, stk = 0;
            var dwell = new Dictionary<BoardCell, double>();
            var kz = new Dictionary<BoardCell, double>();
            var blockNames = new HashSet<string>();
            int routes = Board.Routes.Length;
            var blockIdx = new int?[routes];
            var bd = new Breakdown { Cov = cov, BlockTotal = new double[routes], BlockAt = blockIdx };

            int? LayRoute(BoardCell[] rt, Dictionary<BoardCell, int> index, double rw, string label)
            {
                var idx = pl.Where(kv => index.ContainsKey(kv.Value.Cell))
                    .Select(kv => (J: index[kv.Value.Cell], N: kv.Key)).OrderBy(x => x.J).ThenBy(x => x.N, StringComparer.Ordinal).ToList();
                if (idx.Count == 0)
                {
                    comb -= 40 * rw;
                    foreach (var c in rt) dwell[c] = dwell.GetValueOrDefault(c) + 0.15 * rw;
                    return null;
                }
                var (b, first) = idx[0];
                blockNames.Add(first);
                for (int j = 0; j < rt.Length; j++)
                {
                    var c = rt[j];
                    double w = j <= b ? 0.15 + (b - j) switch { 0 => 1.9, 1 => 1.2, 2 => 0.6, _ => 0 } : 0.12;
                    dwell[c] = dwell.GetValueOrDefault(c) + w * rw;
                    if (j <= b && b - j <= 2) kz[c] = kz.GetValueOrDefault(c) + (b - j) switch { 0 => 2.0, 1 => 1.2, _ => 0.5 } * rw;
                }
                var I = Info[first];
                var t = I.Melee || I.Role == PlacementRole.Support ? I.Role : PlacementRole.Support;
                double frontBonus = t switch { PlacementRole.Tank => 25, PlacementRole.Damage => 6, PlacementRole.Stacker => 3, _ => -15 };
                comb += rw * (frontBonus + Math.Min(I.Block, 3) * 1.5 + 1.2 * Math.Min(rt.Length - 1 - b, 5));
                foreach (var (_, m) in idx.Skip(1)) comb += 0.35 * Info[m].Block * rw;
                Note(first, $"{label}拦截点（离保护点 {rt.Length - 1 - b} 格）");
                return b;
            }

            for (int k = 0; k < routes; k++)
                blockIdx[k] = LayRoute(Board.Routes[k], Board.RouteIndex[k], RW(k), RouteLabel(k));
            for (int e = 0; e < Board.Extra.Length; e++)
            {
                LayRoute(Board.Extra[e], Board.ExtraIndex[e], 0.6, "D6 传送");
                foreach (var (n, v) in pl)
                    if (Math.Max(Math.Abs(v.Cell.Col - 3), Math.Abs(v.Cell.Row - 3)) <= 1)
                    {
                        comb -= Info[n].Role == PlacementRole.Tank ? 2 : 4;
                        Note(n, "在 D6 传送点周围，会周期性受伤");
                    }
            }

            // Tiles that affect enemies: they are buffed whenever they walk over, so the line should be in front.
            if (Board.Tile is { } tile)
                for (int k = 0; k < routes; k++)
                {
                    var rt = Board.Routes[k];
                    int? b = blockIdx[k];
                    for (int j = 0; j < rt.Length; j++)
                    {
                        if (!Board.Special.Contains(rt[j]) || b is not null && j > b) continue;
                        if (tile.Kind == TileKind.EnemyBuff) { comb -= tile.EnemyPenalty * RW(k); bd.TilePenalty = true; }
                        if (tile.Kind == TileKind.Slow) dwell[rt[j]] = dwell.GetValueOrDefault(rt[j]) * 1.4;
                    }
                }

            foreach (var (n, v) in pl)
            {
                var I = Info[n];
                if (I.Role == PlacementRole.Tank && !Board.OnMainRoute(v.Cell) && !Board.ExtraIndex.Any(d => d.ContainsKey(v.Cell)))
                    comb -= 12;
                // Depth: tanks in front, melee next, ranged at the back.
                (int Dist, int Route, int J)? best = null;
                for (int k = 0; k < routes; k++)
                {
                    if (blockIdx[k] is null) continue;
                    var rt = Board.Routes[k];
                    int dist = int.MaxValue, jj = 0;
                    for (int j = 0; j < rt.Length; j++)
                    {
                        int d = Math.Max(Math.Abs(v.Cell.Col - rt[j].Col), Math.Abs(v.Cell.Row - rt[j].Row));
                        if (d < dist) { dist = d; jj = j; }
                    }
                    if (dist <= 2 && (best is null || dist < best.Value.Dist)) best = (dist, k, jj);
                }
                if (best is { } bb)
                {
                    int ahead = blockIdx[bb.Route]!.Value - bb.J;
                    double rw = RW(bb.Route);
                    if (!I.Melee && ahead > 0) comb -= 6 * ahead * rw;
                    else if (I.Role == PlacementRole.Damage && ahead > 0 && !blockNames.Contains(n)) comb -= 3 * ahead * rw;
                    if (!I.Melee && ahead <= -1) comb += 1.5 * rw;
                }
                if (Board.Tile is { Kind: TileKind.Slow } && Board.Special.Contains(v.Cell)) bd.OnSlowTile = true;
            }

            // Damage: time enemies spend in range × damage power × tile buff.
            foreach (var (n, v) in pl)
            {
                var I = Info[n];
                double c = 0;
                foreach (var (dx, dy) in I.Offsets[(int)v.Facing]) c += dwell.GetValueOrDefault(new BoardCell(v.Cell.Col + dx, v.Cell.Row + dy));
                var (mult, txt) = TileMultiplier(I, v.Cell, v.Facing);
                if (txt is not null) Note(n, txt);
                if (txt is not null && Board.Tile is { Drain: true } && blockNames.Contains(n)) comb -= 4;
                comb += 2.2 * I.Power * c * mult;
                if (c > 0 && I.Role == PlacementRole.Damage) Note(n, $"输出区覆盖 {c * mult:0.0}");
            }

            // Healers: cover whoever holds the line; a second healer on the same person adds less.
            var healed = new Dictionary<string, List<string>>();
            foreach (var (n, v) in pl)
            {
                var I = Info[n];
                if (!I.Healer) continue;
                var cells = I.Offsets[(int)v.Facing].Select(o => new BoardCell(v.Cell.Col + o.Col, v.Cell.Row + o.Row)).ToHashSet();
                var hit = pl.Where(kv => cells.Contains(kv.Value.Cell) && Board.OnMainRoute(kv.Value.Cell)).Select(kv => kv.Key).ToList();
                foreach (var m in hit)
                {
                    if (!healed.TryGetValue(m, out var list)) healed[m] = list = [];
                    list.Add(n);
                }
                if (hit.Count > 0) Note(n, "奶到 " + string.Join("、", hit));
            }
            foreach (var (m, hs) in healed)
                comb += (blockNames.Contains(m) ? 9 : 3) * (1 + 0.25 * (hs.Count - 1));
            // Composition: keep at least one healer on the field when the pool has one, and make it reach
            // whoever holds a line (a healer that heals nobody blocking is close to no healer).
            bd.Healers = pl.Keys.Where(n => Info[n].Healer).ToList();
            bd.HealerCoversLine = healed.Keys.Any(blockNames.Contains);
            if (bd.Healers.Count == 0 && _pool.Any(n => Info[n].Healer)) comb -= 45;
            else if (bd.Healers.Count > 0 && !bd.HealerCoversLine) comb -= 12;

            // Block count at each block point and the two cells behind it.
            for (int k = 0; k < routes; k++)
            {
                if (blockIdx[k] is not int b) continue;
                var rt = Board.Routes[k];
                double total = 0;
                var who = new List<string>();
                for (int j = b; j < Math.Min(b + 3, rt.Length); j++)
                {
                    if (!at.TryGetValue(rt[j], out var m)) continue;
                    double bl = Info[m].Block * (Info[m].Melee ? 1 : 0.5);
                    total += bl;
                    who.Add($"{m}{bl:0.#}");
                }
                bd.BlockTotal[k] = total;
                if (total < BlockNeed) comb -= 10 * (BlockNeed - total) * RW(k);
                if (at.TryGetValue(rt[b], out var firstName))
                    Note(firstName, $"{RouteLabel(k)}阻挡 {total:0.#}/{BlockNeed:0}（{string.Join("、", who)}）");
            }

            // Second contact: 1–2 cells behind a block point is where enemies go next when it breaks.
            for (int k = 0; k < routes; k++)
            {
                if (blockIdx[k] is not int b) continue;
                var rt = Board.Routes[k];
                foreach (int j in new[] { b + 1, b + 2 })
                {
                    if (j >= rt.Length || !at.TryGetValue(rt[j], out var m)) continue;
                    var I = Info[m];
                    double w = RW(k) * (j == b + 1 ? 1.0 : 0.6);
                    if (I.SkillBlock) { comb += 3 * w; Note(m, "第二接敌位（技能可阻挡，能顶前排）"); }
                    else if (I.Role == PlacementRole.Support || !I.Melee && I.Role != PlacementRole.Tank)
                    { comb -= 8 * w; Note(m, "站在第二接敌位，前排被突破就会先挨打"); }
                    else if (I.Melee && I.Role is PlacementRole.Damage or PlacementRole.Tank)
                    { comb += 5 * w * (I.Role == PlacementRole.Damage ? I.Power : 0.6); Note(m, "第二接敌位（补位 + 输出）"); }
                }
            }

            // 【灵巧】: the operator and the 4 cells around it get attack speed.
            if (cov.Active.Contains("灵巧"))
                foreach (var (n, v) in pl)
                {
                    if (!Info[n].Covenants.Contains("灵巧")) continue;
                    var nb = pl.Where(kv => kv.Key != n && Manhattan(kv.Value.Cell, v.Cell) == 1).Select(kv => kv.Key).ToList();
                    comb += nb.Sum(m => 3 * Affinity(Stat.Aspd, Info[m].Role) * Info[m].Power);
                    if (nb.Count > 0) Note(n, "灵巧光环（周围四格攻速）：" + string.Join("、", nb));
                }

            // Positional traits.
            IEnumerable<string> Act(string n) => Info[n].Covenants.Where(cov.Active.Contains);
            foreach (var (n, v) in pl)
            {
                var I = Info[n];
                string nm = I.Name;
                var (fx, fy) = PlacementCells.Vector(v.Facing);
                at.TryGetValue(new BoardCell(v.Cell.Col + fx, v.Cell.Row + fy), out var front);
                at.TryGetValue(new BoardCell(v.Cell.Col - fx, v.Cell.Row - fy), out var back);
                if (I.SelfFront > 0 && front is not null)
                {
                    var tg = Act(front).ToList();
                    stk += I.SelfFront * cov.Lv(tg);
                    Note(n, $"身前 {front}：{(tg.Count > 0 ? string.Join("、", tg) : "无已激活盟约")} 各 +{I.SelfFront:0.#} 层/回合");
                }
                if (I.SelfBack > 0 && back is not null)
                {
                    var tg = Act(back).ToList();
                    stk += I.SelfBack * cov.Lv(tg);
                    Note(n, $"身后 {back}：{(tg.Count > 0 ? string.Join("、", tg) : "无已激活盟约")} 各 +{I.SelfBack:0.#} 层");
                }
                if (nm == "帕拉斯" && front is not null && Info[front].Melee)
                {
                    comb += 6 + (blockNames.Contains(front) ? 4 : 0);
                    Note(n, $"技能：身前 {front} 阻挡 +1、攻击 / 防御提升");
                }
                if (nm == "凛御银灰")
                {
                    if (front is not null && Info[front].Covenants.Any(c => c is "谢拉格" or "灵巧") && Info[front].Cap > 0)
                    {
                        stk += Info[front].Cap * 0.75 * 0.8 * cov.Lv(Act(front));
                        Note(n, $"身前 {front} 叠层上限 {Info[front].Cap:0.#}→{Info[front].Cap * 1.75:0.#}");
                    }
                    else Note(n, "身前没有带上限的谢拉格 / 灵巧叠层干员，特质没用上");
                }
                if (nm == "Mon3tr")
                {
                    var tg = pl.Where(kv => kv.Key != n && Chebyshev(kv.Value.Cell, v.Cell) == 1 && Info[kv.Key].Cap > 0).Select(kv => kv.Key).ToList();
                    stk += tg.Sum(m => Info[m].Cap * 0.3 * 0.8 * cov.Lv(Act(m)));
                    if (tg.Count > 0) Note(n, "周围八格 " + string.Join("、", tg) + " 叠层上限 +30%");
                }
                if (nm == "阿米娅" && front is not null && Info[front].StacksLayers)
                {
                    stk += Info[front].TriggersPerRound * cov.Lv(Act(front)) * 0.8;
                    Note(n, $"身前 {front} 每次叠层额外 +1");
                }
                if (nm == "华法琳" && front is not null)
                {
                    int k = Math.Min(7, Info[front].SkillCasts);
                    stk += k * cov.Lv(Act(front));
                    Note(n, $"身前 {front} 开技能叠层（约 {k} 次/回合）");
                }
                if (nm == "铃兰" && front is not null && Info[front].OnceAmount > 0)
                {
                    stk += Info[front].OnceAmount * cov.Lv(Act(front));
                    Note(n, $"身前 {front} 每回合重复触发“获得时”");
                }
                if (nm == "塞雷娅" && front is not null && Info[front].Trait.Contains("休整期结束时"))
                {
                    // The copy behaves as if 塞雷娅 had the trait: "自身" / "身前一格" / "同一行" are hers.
                    double copied = RoundEndValue(Info[front], n, v.Cell, v.Facing, pl, at, cov);
                    stk += copied;
                    Note(n, $"复制身前 {front} 的休整期结束时特质（叠层价值约 {copied:0.#}）");
                }
                if (nm == "浊心斯卡蒂")
                {
                    var cells = I.Offsets[(int)v.Facing].Select(o => new BoardCell(v.Cell.Col + o.Col, v.Cell.Row + o.Row)).ToHashSet();
                    int k = pl.Count(kv => kv.Key != n && cells.Contains(kv.Value.Cell));
                    stk += 2 * Math.Min(k, 10) * cov.Lv(["阿戈尔"]);
                    Note(n, $"范围内 {k} 名友方（每人 +2 阿戈尔）");
                }
                if (I.Around4)
                {
                    var nb = pl.Where(kv => kv.Key != n && Manhattan(kv.Value.Cell, v.Cell) == 1 && Info[kv.Key].Role == PlacementRole.Damage)
                        .Select(kv => kv.Key).ToList();
                    comb += nb.Sum(m => 3 * Info[m].Power);
                    if (nb.Count > 0) Note(n, "周围四格增益 " + string.Join("、", nb));
                }
                if (RowOfThree.Contains(nm) || nm == "罗比菈塔")
                {
                    int rowCount = pl.Count(kv => kv.Value.Cell.Row == v.Cell.Row);
                    if (nm == "罗比菈塔")
                    {
                        stk += 2 * rowCount * cov.Lv(new[] { "坚守", "助力" }.Where(cov.Active.Contains));
                        Note(n, $"同一行 {rowCount} 人");
                    }
                    else if (rowCount >= 3) { stk += 12; Note(n, $"同一行 {rowCount} 人，触发"); }
                    else Note(n, $"同一行只有 {rowCount} 人，未触发");
                }
                if (I.KillRange)
                {
                    double c = 0;
                    foreach (var (dx, dy) in I.Offsets[(int)v.Facing]) c += kz.GetValueOrDefault(new BoardCell(v.Cell.Col + dx, v.Cell.Row + dy));
                    stk += 1.8 * c;
                    Note(n, $"范围内击倒 / 控制叠层：罩住拦截点及前方 1~2 格（{c:0.0}）");
                }
            }

            // Operators' own stacking (not tied to where they stand); matters for choosing who goes on the field.
            foreach (var (n, v) in pl)
            {
                var I = Info[n];
                if (I.KillRange || I.SelfFront > 0 || I.SelfBack > 0 || I.Name == "罗比菈塔" || I.StackKind is not ("covenant" or "self")) continue;
                double own = I.Trait.Contains("休整期结束时") ? RoundEndValue(I, n, v.Cell, v.Facing, pl, at, cov)
                    : I.StackPerRound * cov.Lv(I.StackKind == "self" ? Act(n) : I.StackCovenants.Where(cov.Active.Contains));
                stk += own;
                if (own > 0 && I.Name == "涤火杰西卡") Note(n, $"按满层估算：场上 {pl.Count} 人都带装备，维多利亚 / 坚守各 +{2 * pl.Count} 层");
            }

            // Activating covenants is worth something in itself; bench-effective operators still stack.
            double activation = 0;
            foreach (var c in cov.Active)
            {
                bool core = _owner._covenants.TryGetValue(c, out var ci) && ci.BondType == "SEASON";
                activation += core ? 14 : 7;
                if (core && cov.Counts.GetValueOrDefault(c) >= 6) activation += 30;
            }
            double maxLv = cov.LayerValue.Values.DefaultIfEmpty(0).Max();
            foreach (var n in _pool.Where(n => !pl.ContainsKey(n)))
                if (Info[n].BenchOk) stk += 6 * maxLv;

            bd.Combat = comb;
            bd.Stack = stk;
            bd.Activation = activation;
            bd.Total = comb + activation + _options.StackWeight * stk;
            return bd;
        }

        /// <summary>
        /// Layers one round-end trait of <paramref name="source"/> adds, as if held by <paramref name="holder"/>
        /// standing at <paramref name="cell"/> (塞雷娅 copies the trait of the operator in front of her).
        /// </summary>
        private double RoundEndValue(OpInfo source, string holder, BoardCell cell, Facing facing,
            Dictionary<string, (BoardCell Cell, Facing Facing)> pl, Dictionary<BoardCell, string> at, CovenantContext cov)
        {
            IEnumerable<string> ActOf(string n) => Info[n].Covenants.Where(cov.Active.Contains);
            var t = source.Trait;
            double amount = source.StackAmount > 0 ? source.StackAmount : Number(AmountPattern, t, 1);
            if (source.Name == "涤火杰西卡" || t.Contains("携带装备的干员"))
                return 2 * pl.Count * cov.Lv(new[] { "维多利亚", "坚守" }.Where(cov.Active.Contains)); // full: everyone equipped
            if (t.Contains("不同阶的【维多利亚】"))
            {
                int tiers = pl.Keys.Where(n => Info[n].Covenants.Contains("维多利亚")).Select(n => Info[n].Tier).Distinct().Count();
                return amount * tiers * cov.Lv(source.StackCovenants.Where(cov.Active.Contains));
            }
            if (t.Contains("同一行"))
                return amount * pl.Count(kv => kv.Value.Cell.Row == cell.Row) * cov.Lv(source.StackCovenants.Where(cov.Active.Contains));
            if (t.Contains("层数最多的盟约"))
                return amount * cov.LayerValue.Values.DefaultIfEmpty(0).Max();
            if (Regex.IsMatch(t, @"自身(以|及)?及?身前一格"))
            {
                var (fx, fy) = PlacementCells.Vector(facing);
                at.TryGetValue(new BoardCell(cell.Col + fx, cell.Row + fy), out var front);
                return amount * (cov.Lv(ActOf(holder)) + (front is null ? 0 : cov.Lv(ActOf(front))));
            }
            if (source.StackKind == "self" || t.Contains("自身已激活"))
                return (source.StackPerRound > 0 ? source.StackPerRound : amount) * cov.Lv(ActOf(holder));
            var named = source.StackCovenants.Length > 0 ? source.StackCovenants.Where(cov.Active.Contains)
                : Regex.Matches(t, "【(.+?)】").Select(m => m.Groups[1].Value).Where(cov.Active.Contains);
            return (source.StackPerRound > 0 ? source.StackPerRound : amount) * cov.Lv(named);
        }

        private (double Mult, string? Text) TileMultiplier(OpInfo I, BoardCell cell, Facing f)
        {
            if (Board.Tile is not { } tile || !Board.Special.Contains(cell)) return (1, null);
            double aff = Affinity(tile.Stat, I.Role);
            if (tile.Kind == TileKind.Facing)
            {
                int d = f == Facing.Down ? 1 : f == Facing.Up ? -1 : 0;
                return (1 + d * tile.Ally * aff, d switch
                {
                    1 => "橙色通道朝下：攻击提升",
                    -1 => "橙色通道朝上：攻击下降",
                    _ => "橙色通道：左右朝向无效果",
                });
            }
            string txt = tile.Kind == TileKind.Slow ? "站在沼泽上，攻速下降"
                : "地块加成：" + (tile.Drain ? "攻击 / 攻速提升，持续掉血" : "攻速提升");
            return (1 + tile.Ally * aff, txt);
        }

        private double RW(int k) => k < RouteWeight.Length ? RouteWeight[k] : 1;
        private string RouteLabel(int k) => Board.Routes.Length == 2 ? (k == 1 ? "下路" : "上路") : $"路线 {k + 1} ";
        private static int Manhattan(BoardCell a, BoardCell b) => Math.Abs(a.Col - b.Col) + Math.Abs(a.Row - b.Row);
        private static int Chebyshev(BoardCell a, BoardCell b) => Math.Max(Math.Abs(a.Col - b.Col), Math.Abs(a.Row - b.Row));

        // ── Search ─────────────────────────────────────────────────────────

        public Dictionary<string, (BoardCell Cell, Facing Facing)> Solve(CancellationToken cancel)
        {
            var rnd = new Random(_options.Seed);
            var pins = _options.Pins.Where(kv => Info.ContainsKey(kv.Key)).ToDictionary(kv => kv.Key, kv => kv.Value);
            var free = _pool.Where(n => !pins.ContainsKey(n)).ToList();
            int field = Math.Max(FieldCount, Math.Min(pins.Count, _pool.Count));
            Dictionary<string, (BoardCell, Facing)>? best = null;
            double bestScore = double.MinValue;
            if (_pool.Count == 0) return [];

            for (int restart = 0; restart < Math.Max(1, _options.Restarts); restart++)
            {
                var pl = new Dictionary<string, (BoardCell Cell, Facing Facing)>();
                foreach (var (n, p) in pins) pl[n] = (p.Cell, p.Facing);
                var used = pl.Values.Select(v => v.Cell).ToHashSet();
                var order = free.OrderBy(_ => rnd.Next()).ToList()
                    .OrderBy(n => Info[n].Role != PlacementRole.Tank).ThenBy(n => !Info[n].Melee).ToList();
                foreach (var n in order)
                {
                    if (pl.Count >= field) break;
                    var opts = Board.Deployable.Where(c => !used.Contains(c) && Board.Legal(Info[n], c)).ToList();
                    if (opts.Count == 0) continue;
                    var c = opts[rnd.Next(opts.Count)];
                    pl[n] = (c, Faces[rnd.Next(4)]);
                    used.Add(c);
                }
                double cur = Score(pl);
                if (cur > bestScore) { bestScore = cur; best = new(pl); }
                int iters = Math.Max(100, _options.Iterations);
                for (int it = 0; it < iters; it++)
                {
                    if ((it & 255) == 0 && cancel.IsCancellationRequested) break;
                    double T = Math.Max(0.05, 8.0 * (1 - (double)it / iters));
                    var movable = pl.Keys.Where(k => !pins.ContainsKey(k)).ToList();
                    if (movable.Count == 0) break;
                    var cand = new Dictionary<string, (BoardCell Cell, Facing Facing)>(pl);
                    string n = movable[rnd.Next(movable.Count)];
                    double mv = rnd.NextDouble();
                    var bench = free.Where(m => !cand.ContainsKey(m)).ToList();
                    if (mv < 0.2 && bench.Count > 0)
                    {
                        string m = bench[rnd.Next(bench.Count)];
                        var (cell, f) = cand[n];
                        cand.Remove(n);
                        if (!Board.Legal(Info[m], cell))
                        {
                            var occ = cand.Values.Select(v => v.Cell).ToHashSet();
                            var opts = Board.Deployable.Where(c => !occ.Contains(c) && Board.Legal(Info[m], c)).ToList();
                            if (opts.Count == 0) continue;
                            cell = opts[rnd.Next(opts.Count)];
                        }
                        cand[m] = (cell, f);
                    }
                    else if (mv < 0.5)
                    {
                        var occ = cand.Where(kv => kv.Key != n).Select(kv => kv.Value.Cell).ToHashSet();
                        var opts = Board.Deployable.Where(c => !occ.Contains(c) && Board.Legal(Info[n], c)).ToList();
                        if (opts.Count == 0) continue;
                        cand[n] = (opts[rnd.Next(opts.Count)], cand[n].Facing);
                    }
                    else if (mv < 0.75) cand[n] = (cand[n].Cell, Faces[rnd.Next(4)]);
                    else
                    {
                        string m = movable[rnd.Next(movable.Count)];
                        if (m == n || !Board.Legal(Info[n], cand[m].Cell) || !Board.Legal(Info[m], cand[n].Cell)) continue;
                        (cand[n], cand[m]) = ((cand[m].Cell, cand[n].Facing), (cand[n].Cell, cand[m].Facing));
                    }
                    double sc = Score(cand);
                    if (sc >= cur || rnd.NextDouble() < Math.Exp((sc - cur) / T)) { pl = cand; cur = sc; }
                    if (cur > bestScore) { bestScore = cur; best = new(pl); }
                }
            }
            return best!.ToDictionary(kv => kv.Key, kv => kv.Value);
        }

        // ── Result ─────────────────────────────────────────────────────────

        public PlacementPlan Explain(Dictionary<string, (BoardCell Cell, Facing Facing)> pl)
        {
            var notes = pl.Keys.ToDictionary(k => k, _ => new List<string>());
            var bd = Evaluate(pl, notes);
            var placed = pl.OrderBy(kv => Order(kv.Key, kv.Value.Cell))
                .Select(kv => new PlacedOperator(Info[kv.Key].Name, kv.Value.Cell, kv.Value.Facing, Info[kv.Key].Role,
                    _options.Pins.ContainsKey(kv.Key), notes[kv.Key].Distinct().ToList(), kv.Key))
                .ToList();
            var bench = new List<BenchedOperator>();
            foreach (var n in _pool.Where(n => !pl.ContainsKey(n)))
            {
                var I = Info[n];
                var counted = I.Covenants.Where(BenchCountedCovenants.Contains).ToList();
                bench.Add(new BenchedOperator(n, I.BenchOk ? "在整备区也生效"
                    : counted.Count > 0 ? "整备区也算盟约人数（" + string.Join("、", counted) + "）" : ""));
            }
            bench.AddRange(_unplaceable.Select(n => new BenchedOperator(n, "缺少干员数据，没参与站位")));

            var checks = new List<PlacementCheck>();
            foreach (var c in bd.Cov.Active.Where(c => _owner._covenants.TryGetValue(c, out var ci) && ci.BondType == "SEASON")
                         .OrderByDescending(c => bd.Cov.Counts.GetValueOrDefault(c)))
                checks.Add(new PlacementCheck($"{c} {bd.Cov.Counts.GetValueOrDefault(c)}", true));
            for (int k = 0; k < Board.Routes.Length; k++)
                checks.Add(bd.BlockAt[k] is null
                    ? new PlacementCheck($"{RouteLabel(k)}没人拦", false)
                    : new PlacementCheck($"{RouteLabel(k)}阻挡 {bd.BlockTotal[k]:0.#}/{BlockNeed:0}", bd.BlockTotal[k] >= BlockNeed));
            if (Board.Tile is { Kind: TileKind.EnemyBuff } t)
            {
                int on = pl.Count(kv => Board.Special.Contains(kv.Value.Cell));
                string text = (on > 0 ? $"{on} 人站{t.Name} · " : "") + (bd.TilePenalty ? $"敌人会先走过{t.Name}" : $"防线在{t.Name}前");
                checks.Add(new PlacementCheck(text, !bd.TilePenalty, t.HintOnly));
            }
            if (Board.Tile is { Kind: TileKind.Slow } s)
                checks.Add(new PlacementCheck(bd.OnSlowTile ? $"有人站在{s.Name}上" : $"没人站在{s.Name}上", !bd.OnSlowTile));
            if (bd.Healers.Count == 0)
                checks.Add(new PlacementCheck(_pool.Any(n => Info[n].Healer) ? "场上没有奶" : "干员池里没有奶", false));
            else
                checks.Add(new PlacementCheck(bd.HealerCoversLine ? "奶 " + string.Join("、", bd.Healers) : "奶够不到拦截点", bd.HealerCoversLine));

            return new PlacementPlan
            {
                Map = Board.Map,
                Placed = placed,
                Bench = bench,
                ActiveCovenants = [.. bd.Cov.Active.OrderBy(c => c, StringComparer.Ordinal)],
                Checks = checks,
                Combat = Math.Round(bd.Combat, 1),
                Stacking = Math.Round(bd.Stack, 1),
                Total = Math.Round(bd.Total, 1),
            };
        }

        /// <summary>List order: lower route front to back, then upper route, then everyone else.</summary>
        private (int, int, int) Order(string n, BoardCell cell)
        {
            for (int k = Board.Routes.Length - 1; k >= 0; k--)
                if (Board.RouteIndex[k].TryGetValue(cell, out int j))
                    return (Board.Routes.Length - 1 - k, -j, 0);
            return (Board.Routes.Length, Info[n].Melee ? 0 : 1, -cell.Col);
        }
    }
}
