// Added for ASP Assistant Regulation on 2026-11-14; see NOTICE.
using System.Text;
using System.Text.Json;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Tests.Services;

/// <summary>
/// Not a test: plays the user's saved lineups (「我的阵容」, %APPDATA%\ASPAssistant\lineups) round by round against the
/// real streams and writes a report. Early slots for rounds 1–4, mid for 5–9, late for 10–13, cut to an assumed
/// growth curve (field size, elites); covenant layers carry over from the plans' layer gains.
/// Run with: dotnet test --filter "FullyQualifiedName~PresetProgressionProbe" (PROBE_OUT = report directory,
/// PROBE_LINEUPS = comma-separated id prefixes, PROBE_MODES = comma-separated mode ids, PROBE_MAP = map id).
/// </summary>
[Trait("Category", "Probe")]
public class PresetProgressionProbe
{
    private static readonly string Dir = Path.Combine(AppContext.BaseDirectory, "TestData", "Regulation");

    // Round → (field size, elites): an assumed middle-of-the-road growth (shop odds and income are server-side).
    private static readonly (int Field, int Elite)[] Curve =
        [(3, 0), (4, 0), (5, 0), (6, 1), (7, 1), (8, 2), (8, 2), (8, 3), (8, 3), (8, 4), (8, 4), (8, 5), (8, 5)];

    private static readonly string[][] Runs = [["飞行", "隐匿", "折射"], ["复苏", "元素", "致命"], ["频次", "持续", "特异"]];

    /// <summary>Where a plan's time goes: the app's default search, fights simulated and time per fight.</summary>
    [Fact]
    public async Task Timing()
    {
        string lineupDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ASPAssistant", "lineups");
        if (!Directory.Exists(lineupDir)) return;
        string outDir = Environment.GetEnvironmentVariable("PROBE_OUT") ?? Path.GetTempPath();
        var store = new JsonDataStore(Dir);
        var ops = (await store.LoadOperatorsAsync()).ToDictionary(o => o.Name);
        var (covenants, _) = await store.LoadCovenantsAsync();
        var board = (await GarrisonBoardStore.LoadAsync(Dir))!;
        var routes = EnemyRouteStore.Load(Dir)!;
        var spawns = GarrisonSpawnStore.Load(Dir)!;
        var map = board.Find("palace")!;
        var lineup = JsonSerializer.Deserialize<Lineup>(File.ReadAllText(Directory.GetFiles(lineupDir, "a4835b55*.json")[0]))!;
        var sb = new StringBuilder($"cores {Environment.ProcessorCount}\n");
        foreach (int round in new[] { 5, 9, 13 })
        {
            var pool = lineup.Slots.Concat(lineup.MidSlots).Select(s => s.OperatorName).Where(ops.ContainsKey).Distinct().Take(10)
                .Select(n => new PlacementCandidate(n, false)).ToList();
            var options = new PlacementOptions
            {
                Routes = routes.Find("palace", round, null, null), Round = round,
                Streams = spawns.Streams(round, "mode_multi_normal", Runs[0]),
            };
            foreach (var (label, o) in new[] { ("fast only", options with { Simulate = false }), ("app default", options) })
            {
                long runs0 = ClearSimulator.RunCount;
                var simTime0 = ClearSimulator.RunTime;
                var watch = System.Diagnostics.Stopwatch.StartNew();
                var plan = new PlacementPlanner(ops, covenants).Plan(map, pool, o);
                watch.Stop();
                long runs = ClearSimulator.RunCount - runs0;
                int enemies = plan.StreamResults.Count > 0 ? (int)plan.StreamResults.Average(r => r.Result.Spawned) : 0;
                sb.Append($"r{round} {label}: {watch.ElapsedMilliseconds} ms, {runs} fights"
                    + (runs > 0 ? $", {watch.Elapsed.TotalMilliseconds / runs:0.00} ms/fight, sim CPU {(ClearSimulator.RunTime - simTime0).TotalMilliseconds:0} ms" : "") + $", ~{enemies} enemies/stream\n");
                // What the plan is, to check a speed-up changes nothing.
                File.AppendAllText(Path.Combine(outDir, "fingerprint.txt"), $"r{round} {label}: "
                    + string.Join(" ", plan.Placed.Select(p => $"{p.Name}@{p.Cell.Col},{p.Cell.Row},{p.Facing}"))
                    + " | " + string.Join(" ", plan.StreamResults.Select(r => $"{r.Label}:{r.Result.Killed}/{r.Result.Spawned}/{r.Result.Leaked}/{string.Join(",", r.Result.Fallen)}"))
                    + "\n");
            }
        }
        File.WriteAllText(Path.Combine(outDir, "timing.txt"), sb.ToString());
    }

    /// <summary>The same lineup and round planned in each mode (战斗 / 平衡 / 叠层): enemies handled vs layers stacked.</summary>
    [Fact]
    public async Task Balance()
    {
        string lineupDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ASPAssistant", "lineups");
        if (!Directory.Exists(lineupDir)) return;
        string outDir = Environment.GetEnvironmentVariable("PROBE_OUT") ?? Path.GetTempPath();
        var store = new JsonDataStore(Dir);
        var ops = (await store.LoadOperatorsAsync()).ToDictionary(o => o.Name);
        var (covenants, _) = await store.LoadCovenantsAsync();
        var board = (await GarrisonBoardStore.LoadAsync(Dir))!;
        var routes = EnemyRouteStore.Load(Dir)!;
        var spawns = GarrisonSpawnStore.Load(Dir)!;
        var map = board.Find("palace")!;
        var sb = new StringBuilder("# 同一阵容同一回合，三种模式（险境 · 宫殿 · 飞行、隐匿、折射）\n");
        foreach (var id in new[] { "a4835b55", "627bf176", "ed5a8ca5", "5e48dbc4" })
        {
            var lineup = JsonSerializer.Deserialize<Lineup>(File.ReadAllText(Directory.GetFiles(lineupDir, id + "*.json")[0]))!;
            sb.Append($"\n## {lineup.Name}\n");
            foreach (int round in new[] { 3, 6, 9, 12 })
            {
                var (field, elites) = Curve[round - 1];
                var slots = (round <= 4 ? lineup.EarlySlots : round <= 9 ? lineup.MidSlots : lineup.Slots)
                    .Where(s => ops.ContainsKey(s.OperatorName)).ToList();
                var eliteNames = slots.OrderByDescending(s => s.IsElite).Take(elites).Select(s => s.OperatorName).ToHashSet();
                // A bigger pool than the field, so the mode can also change who goes on.
                var extra = lineup.SupportSlots.Concat(lineup.MidSlots).Concat(lineup.Slots).Where(s => ops.ContainsKey(s.OperatorName));
                var pool = slots.Concat(extra).Select(s => s.OperatorName).Distinct().Take(field + 3)
                    .Select(n => new PlacementCandidate(n, eliteNames.Contains(n))).ToList();
                foreach (var mode in new[] { PlacementMode.Combat, PlacementMode.Balanced, PlacementMode.Stacking })
                {
                    var plan = new PlacementPlanner(ops, covenants).Plan(map, pool, new PlacementOptions
                    {
                        Routes = routes.Find("palace", round, null, null), Round = round, FieldCount = field, Mode = mode,
                        Iterations = 1500, Restarts = 2, SimulationIterations = 200, Streams = spawns.Streams(round, "mode_multi_normal", Runs[0]),
                    });
                    double k = plan.StreamResults.Sum(r => r.Weight * r.Result.Killed), n = plan.StreamResults.Sum(r => r.Weight * r.Result.Spawned);
                    sb.Append($"- r{round} {mode,-8} 叠层权重 {plan.StackWeight:0.00} · 处理 {k:0.#}/{n:0.#} · 叠层 "
                        + string.Join(" ", plan.LayerGains.Select(kv => $"{kv.Key}+{kv.Value:0.#}"))
                        + $" · 叠层分 {plan.StackWeight * plan.Stacking:0} vs 击杀分 {10 * k:0} · "
                        + string.Join(" ", plan.Placed.Select(p => p.Name)) + "\n");
                }
            }
        }
        File.WriteAllText(Path.Combine(outDir, "balance.md"), sb.ToString());
    }

    [Fact]
    public async Task Report()
    {
        string lineupDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ASPAssistant", "lineups");
        if (!Directory.Exists(lineupDir)) return;
        string outDir = Environment.GetEnvironmentVariable("PROBE_OUT") ?? Path.GetTempPath();
        var only = (Environment.GetEnvironmentVariable("PROBE_LINEUPS") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries);
        var modes = (Environment.GetEnvironmentVariable("PROBE_MODES") ?? "mode_multi_normal,mode_multi_hard").Split(',');
        string mapId = Environment.GetEnvironmentVariable("PROBE_MAP") ?? "palace";

        var store = new JsonDataStore(Dir);
        var ops = (await store.LoadOperatorsAsync()).ToDictionary(o => o.Name);
        var (covenants, _) = await store.LoadCovenantsAsync();
        var board = (await GarrisonBoardStore.LoadAsync(Dir))!;
        var routes = EnemyRouteStore.Load(Dir)!;
        var spawns = GarrisonSpawnStore.Load(Dir)!;
        var map = board.Find(mapId)!;
        var lineups = Directory.GetFiles(lineupDir, "*.json")
            .Where(f => only.Length == 0 || only.Any(p => Path.GetFileName(f).StartsWith(p)))
            .Select(f => (Id: Path.GetFileName(f)[..8], L: JsonSerializer.Deserialize<Lineup>(File.ReadAllText(f))!))
            .ToList();

        foreach (var mode in modes)
        {
            var sb = new StringBuilder($"# {mode} · {map.Name}\n");
            foreach (var (id, lineup) in lineups)
                foreach (var run in Runs)
                {
                    var planner = new PlacementPlanner(ops, covenants);
                    var layers = new Dictionary<string, double>();
                    sb.Append($"\n## {lineup.Name} ({id}) · 本局敌人 {string.Join("、", run)}\n");
                    for (int round = 1; round <= Curve.Length; round++)
                    {
                        var (field, elites) = Curve[round - 1];
                        var slots = (round <= 4 ? lineup.EarlySlots : round <= 9 ? lineup.MidSlots : lineup.Slots)
                            .Where(s => ops.ContainsKey(s.OperatorName)).ToList();
                        if (slots.Count == 0) slots = lineup.Slots.Where(s => ops.ContainsKey(s.OperatorName)).ToList();
                        // The lineup's own ★ first, then the slots in order, up to this round's elite count.
                        var eliteNames = slots.OrderByDescending(s => s.IsElite).Take(elites).Select(s => s.OperatorName).ToHashSet();
                        var pool = slots.Select(s => s.OperatorName).Distinct()
                            .Select(n => new PlacementCandidate(n, eliteNames.Contains(n))).ToList();
                        var options = new PlacementOptions
                        {
                            Routes = routes.Find(mapId, round, null, null), Round = round, FieldCount = field,
                            Iterations = 1500, Restarts = 2, SimulationIterations = 200,
                            Streams = spawns.Streams(round, mode, run),
                            CovenantLayers = layers.ToDictionary(kv => kv.Key, kv => (int)kv.Value),
                        };
                        var plan = planner.Plan(map, pool, options);
                        foreach (var (c, g) in plan.LayerGains) layers[c] = layers.GetValueOrDefault(c) + g;
                        double killed = plan.StreamResults.Sum(r => r.Weight * r.Result.Killed);
                        double spawned = plan.StreamResults.Sum(r => r.Weight * r.Result.Spawned);
                        string board1 = string.Join(" ", plan.Placed.Select(p => p.Name + (pool.First(c => c.Name == p.Name).IsElite ? "★" : "")));
                        sb.Append($"- r{round} 上场{field} 精锐{elites} · 处理 {killed:0.#}/{spawned:0.#} ({killed / Math.Max(1e-9, spawned):0%}) · {board1}\n");
                        sb.Append("  - 盟约（打完累计层）：" + string.Join(" ", plan.ActiveCovenants.Select(c =>
                            layers.TryGetValue(c, out var v) ? $"{c}({v:0})" : c)) + "\n");
                        var types = plan.StreamResults.GroupBy(r => r.Type).Select(g =>
                        {
                            double w = g.Sum(r => r.Weight);
                            double k = g.Sum(r => r.Weight * r.Result.Killed) / w, n = g.Sum(r => r.Weight * r.Result.Spawned) / w;
                            var leaks = g.SelectMany(r => r.Result.LeakedByName.Select(kv => (kv.Key, V: r.Weight / w * kv.Value)))
                                .GroupBy(x => x.Key).Select(x => (x.Key, V: x.Sum(y => y.V))).Where(x => x.V >= 0.5)
                                .OrderByDescending(x => x.V).Take(3).ToList();
                            string label = GarrisonSpawnStore.TypeNames.GetValueOrDefault(g.Key, g.Key);
                            return $"{label}：处理 {k:0.#}/{n:0.#}" + (leaks.Count > 0
                                ? $"（漏 {n - k:0.#}：{string.Join("、", leaks.Select(x => $"{x.Key}×{x.V:0.#}"))}）" : "");
                        });
                        sb.Append("  - " + string.Join(" | ", types) + "\n");
                    }
                }
            File.WriteAllText(Path.Combine(outDir, $"presets_{mode}_{mapId}.md"), sb.ToString());
        }
    }
}
