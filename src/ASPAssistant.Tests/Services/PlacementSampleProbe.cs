// Added for ASP Assistant Regulation on 2026-11-14; see NOTICE.
using System.Runtime.ExceptionServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ASPAssistant.App.Windows;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Tests.Services;

/// <summary>
/// Not a test: draws planned placements onto real game screenshots (data/samples) with the in-game overlay
/// (<see cref="OverlayWindow"/>), so the plans can be looked at as they would appear over the game. Each screenshot's
/// map and camera are recognised; saved lineups (「我的阵容」) are planned round by round (layers carried over) and the
/// chosen rounds rendered with a caption of what the simulation expects.
/// Run with: dotnet test --filter "FullyQualifiedName~PlacementSampleProbe" (PROBE_OUT = output directory).
/// </summary>
[Trait("Category", "Probe")]
public class PlacementSampleProbe
{
    private static readonly string Dir = Path.Combine(AppContext.BaseDirectory, "TestData", "Regulation");
    private static readonly (int Field, int Elite)[] Curve =
        [(3, 0), (4, 0), (5, 0), (6, 1), (7, 1), (8, 2), (8, 2), (8, 3), (8, 3), (8, 4), (8, 4), (8, 5), (8, 5), (8, 6), (8, 6)];
    private static readonly string Mode = Environment.GetEnvironmentVariable("PROBE_MODE") ?? "mode_multi_normal";
    private static string ModeName => Mode switch
    {
        "mode_multi_funny" => "标准", "mode_multi_hard" => "绝境", "mode_multi_abyss" => "终极", _ => "险境",
    };
    private static readonly string[] Run = ["飞行", "隐匿", "折射"];
    // Lineup id prefix → rounds to draw.
    private static readonly (string Id, int[] Rounds)[] Picks =
        [("a4835b55", [5, 9, 13]), ("627bf176", [13]), ("ed5a8ca5", [13]), ("5e48dbc4", [13]), ("99efceae", [13])];

    [Fact]
    public void Render()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var owns = Application.Current is null;
            var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            try
            {
                foreach (var resource in new[] { "Themes/Terminal.Dark.xaml", "Styles.xaml", "Logo.xaml" })
                    app.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri($"/ASPAssistant.App;component/Resources/{resource}", UriKind.Relative),
                    });
                RenderAll();
            }
            catch (Exception ex) { failure = ex; }
            finally { if (owns) app.Shutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void RenderAll()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        string samples = Path.Combine(root, "data", "samples");
        string lineupDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ASPAssistant", "lineups");
        string outDir = Environment.GetEnvironmentVariable("PROBE_OUT") ?? Path.Combine(root, "reports", "samples");
        Directory.CreateDirectory(outDir);

        var store = new JsonDataStore(Dir);
        var ops = store.LoadOperatorsAsync().GetAwaiter().GetResult().ToDictionary(o => o.Name);
        var (covenants, _) = store.LoadCovenantsAsync().GetAwaiter().GetResult();
        var board = GarrisonBoardStore.LoadAsync(Dir).GetAwaiter().GetResult()!;
        var routes = EnemyRouteStore.Load(Dir)!;
        var spawns = GarrisonSpawnStore.Load(Dir)!;
        var recognizer = new BoardRecognizer(board);

        // One screenshot per recognised map (the first found), with its camera.
        var shots = new Dictionary<string, (string Path, BitmapSource Image, BoardMatch Match)>();
        foreach (var file in Directory.GetFiles(samples, "*.png").Order())
        {
            var image = Load(file);
            if (recognizer.Recognize(Bmp(image, 400, 225)) is { } match && !shots.ContainsKey(match.Map.Id))
                shots[match.Map.Id] = (file, image, match);
        }
        File.WriteAllLines(Path.Combine(outDir, "maps.txt"),
            shots.Select(s => $"{s.Key} {s.Value.Match.Camera} {Path.GetFileName(s.Value.Path)}"));
        if (shots.Count == 0) return;
        var mapIds = shots.Keys.Order().ToList();

        // PROBE_ROUTES = "map,map:round" draws only those maps' routes for that round (for checking the route data).
        if (Environment.GetEnvironmentVariable("PROBE_ROUTES")?.Split(':') is { Length: 2 } only)
        {
            int round = int.Parse(only[1]);
            foreach (var mapId in only[0].Split(','))
            {
                var (_, shot, match) = shots[mapId];
                var rr = routes.Find(mapId, round, null, null)!;
                var caption = new List<string> { $"{match.Map.Name} · 第 {round} 回合路线（{rr.Level}）" };
                foreach (var r in rr.Routes.Where(r => !r.Flying))
                    caption.Add($"地面 ×{r.Count}：" + string.Join(" → ", r.Segments.SelectMany(s => s).Select(p => $"({p.Col - 0.5:0},{p.Row - 0.5:0})")));
                caption.Add("地图标的上路：" + string.Join(" → ", match.Map.Routes[0].Select(c => $"({c.Col},{c.Row})")));
                var png = Compose(shot, match.Map, board.Cameras[match.Camera], null, rr, caption);
                using var fs = File.Create(Path.Combine(outDir, $"routes_{mapId}_r{round:00}.png"));
                png.Save(fs);
            }
            return;
        }

        // PROBE_COMPARE = "name@CODE@facing;…" (CODE as the 站位 page names cells, facing 上/下/左/右) with PROBE_POOL
        // and PROBE_MAP: that layout against the planner's own, in the same scenarios; a report and a picture.
        if (Environment.GetEnvironmentVariable("PROBE_COMPARE") is { Length: > 0 } layoutText)
        {
            string mapId = Environment.GetEnvironmentVariable("PROBE_MAP") ?? "desert_steel";
            var (_, shot, match) = shots[mapId];
            var map = match.Map;
            var pool = (Environment.GetEnvironmentVariable("PROBE_POOL") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(n => new PlacementCandidate(n, false)).ToList();
            var mine = new Dictionary<string, (BoardCell Cell, Facing Facing)>();
            foreach (var part in layoutText.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var bits = part.Split('@');
                var cell = new BoardCell(int.Parse(bits[1][1..]) - 3, bits[1][0] - 'A');
                var facing = bits[2] switch { "上" => Facing.Up, "下" => Facing.Down, "左" => Facing.Left, _ => Facing.Right };
                mine[bits[0]] = (cell, facing);
            }
            var report = new System.Text.StringBuilder();
            void Line(string label, PlacementPlan p)
            {
                double k = p.StreamResults.Count > 0 ? p.StreamResults.Sum(r => r.Weight * r.Result.Killed) : p.Simulation?.Killed ?? 0;
                double n = p.StreamResults.Count > 0 ? p.StreamResults.Sum(r => r.Weight * r.Result.Spawned) : p.Simulation?.Spawned ?? 0;
                report.Append($"  {label}: 处理 {k:0.#}/{n:0.#} · 战斗分 {p.Combat:0} · 叠层分 {p.Stacking:0}×{p.StackWeight:0.##} · 总分 {p.Total:0} · 目标分 {p.Objective:0} · 叠层 "
                    + string.Join(" ", p.LayerGains.Select(kv => $"{kv.Key}+{kv.Value:0.#}")) + "\n");
                report.Append("    站位：" + string.Join(" ", p.Placed.Select(x => $"{x.Name}{x.CellCode}{PlacementCells.FacingText(x.Facing)}")) + "\n");
                report.Append("    检查：" + string.Join("；", p.Checks.Select(c => (c.Ok ? "✓" : "✗") + c.Text)) + "\n");
                if (p.StreamResults.Count > 0)
                    report.Append("    " + string.Join(" | ", p.StreamResults.Select(r => $"{r.Label}:{r.Result.Killed}/{r.Result.Spawned} 倒[{string.Join(",", r.Result.Fallen)}]")) + "\n");
                if (p.Simulation is { } sim)
                    report.Append("    伤害/击倒：" + string.Join(" ", sim.Damage.OrderByDescending(kv => kv.Value)
                        .Select(kv => $"{kv.Key} {kv.Value / 1000:0}k/{sim.Kills.GetValueOrDefault(kv.Key)}")) + "\n");
            }
            var scenarios = new (string Label, PlacementOptions Options)[]
            {
                ("叠层模式 · 未读到回合", new PlacementOptions { Mode = PlacementMode.Stacking, FieldCount = 8 }),
                ("平衡 · 险境 r9 · 飞行隐匿折射", new PlacementOptions { FieldCount = 8, Round = 9, Routes = routes.Find(mapId, 9, null, null), Streams = spawns.Streams(9, "mode_multi_normal", Run) }),
                ("平衡 · 险境 r9 · 复苏元素致命", new PlacementOptions { FieldCount = 8, Round = 9, Routes = routes.Find(mapId, 9, null, null), Streams = spawns.Streams(9, "mode_multi_normal", ["复苏", "元素", "致命"]) }),
                ("平衡 · 绝境 r13 · 飞行隐匿折射", new PlacementOptions { FieldCount = 8, Round = 13, Routes = routes.Find(mapId, 13, null, null), Streams = spawns.Streams(13, "mode_multi_hard", Run) }),
            };
            PlacementPlan? mineFirst = null;
            foreach (var (label, opt) in scenarios)
            {
                var planner = new PlacementPlanner(ops, covenants);
                var auto = planner.Plan(map, pool, opt);
                var judged = planner.Evaluate(map, pool, mine, opt with { StreamFactor = auto.StreamFactor, Scenario = auto.Scenario });
                mineFirst ??= judged;
                report.Append($"\n## {label}\n");
                Line("你的摆法", judged);
                Line("规划器", auto);
            }
            File.WriteAllText(Path.Combine(outDir, $"compare_{mapId}.md"), report.ToString());
            var caption = new List<string> { $"{map.Name} · 你的摆法（叠层模式 · 未读到回合）" };
            caption.Add("检查：" + string.Join("；", mineFirst!.Checks.Select(c => (c.Ok ? "✓" : "✗") + c.Text)));
            foreach (var p in mineFirst.Placed) caption.Add($"{p.Name}：" + string.Join("；", p.Notes.Take(4)));
            var png = Compose(shot, map, board.Cameras[match.Camera], mineFirst, null, caption);
            using var fs = File.Create(Path.Combine(outDir, $"compare_{mapId}.png"));
            png.Save(fs);
            return;
        }

        // PROBE_LABELS = map id: the map's cells with their codes (as the 站位 page names them) and its default routes.
        if (Environment.GetEnvironmentVariable("PROBE_LABELS") is { Length: > 0 } labelMap)
        {
            var (_, shot, match) = shots[labelMap];
            double w = shot.PixelWidth, h = shot.PixelHeight;
            var overlay = new OverlayWindow();
            overlay.UpdateBoard(match.Map, board.Cameras[match.Camera], showCells: true);
            double scale = w / 1920;
            var layer = overlay.SnapshotBoard(1920, h / scale);
            layer.LayoutTransform = new ScaleTransform(scale, scale);
            var labels = new Canvas();
            var proj = board.Cameras[match.Camera];
            foreach (var (cell, type) in match.Map.AllCells())
            {
                if (type is not (BoardCellType.Ground or BoardCellType.High or BoardCellType.Special)) continue;
                var (x, y) = proj.Project(cell.Col + 0.5, cell.Row + 0.5);
                var tag = new Border
                {
                    Background = new SolidColorBrush(Color.FromArgb(0xE0, 0x08, 0x0B, 0x0D)), CornerRadius = new CornerRadius(6 * scale),
                    Padding = new Thickness(8 * scale, 2 * scale, 8 * scale, 2 * scale),
                    BorderBrush = type == BoardCellType.High ? Brushes.DeepSkyBlue : Brushes.LimeGreen, BorderThickness = new Thickness(2 * scale),
                    Child = new TextBlock
                    {
                        Text = PlacementCells.Code(cell) + (type == BoardCellType.High ? " 高" : type == BoardCellType.Special ? " 特" : ""),
                        Foreground = Brushes.White, FontSize = 26 * scale, FontWeight = FontWeights.Bold,
                    },
                };
                tag.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
                Canvas.SetLeft(tag, x * w - tag.DesiredSize.Width / 2);
                Canvas.SetTop(tag, y * h - tag.DesiredSize.Height / 2);
                labels.Children.Add(tag);
            }
            var grid = new Grid { Width = w, Height = h };
            grid.Children.Add(new Image { Source = shot, Stretch = Stretch.Fill });
            grid.Children.Add(new Canvas { Children = { layer } });
            grid.Children.Add(labels);
            grid.Measure(new Size(w, h));
            grid.Arrange(new Rect(0, 0, w, h));
            grid.UpdateLayout();
            var rtb = new RenderTargetBitmap((int)w, (int)h, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(grid);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using var fs = File.Create(Path.Combine(outDir, $"cells_{labelMap}.png"));
            enc.Save(fs);
            overlay.Close();
            return;
        }

        // PROBE_POOL = "name,name,…" with PROBE_MAP and PROBE_PLAN_MODE (Combat/Balanced/Stacking): plans that pool the
        // way the app does before a round is read (the map's fixed routes, no enemy streams) and draws it.
        if (Environment.GetEnvironmentVariable("PROBE_POOL") is { Length: > 0 } poolText)
        {
            string mapId = Environment.GetEnvironmentVariable("PROBE_MAP") ?? "desert_steel";
            var mode = Enum.Parse<PlacementMode>(Environment.GetEnvironmentVariable("PROBE_PLAN_MODE") ?? "Balanced");
            var (_, shot, match) = shots[mapId];
            var pool = poolText.Split(',').Select(n => new PlacementCandidate(n, false)).ToList();
            // As the app plans before the round is read: round 8's real enemies (all 9 types), the map's fixed walking
            // routes, and the flying paths of the first round of this map that has flyers.
            var streams = spawns.Streams(8, null, []);
            var flyRoutes = Enumerable.Range(1, 13).Select(r => routes.Find(mapId, r, null, null)).FirstOrDefault(r => r is { Flying: > 0 });
            var plan = new PlacementPlanner(ops, covenants).Plan(match.Map, pool, new PlacementOptions
            {
                Mode = mode, FieldCount = 8, Streams = streams, Routes = flyRoutes, ExpectFlying = true,
            });
            var caption = new List<string>
            {
                $"{match.Map.Name} · 未读到回合（按第 8 回合真实出怪、9 类平均）· {mode} · 叠层权重 ×{plan.StackWeight:0.##}",
                "模拟：" + string.Join(" ", plan.StreamResults.GroupBy(r => r.Type).Select(g =>
                    $"{GarrisonSpawnStore.TypeNames.GetValueOrDefault(g.Key, g.Key)} {g.Sum(r => r.Weight * r.Result.Killed) / g.Sum(r => r.Weight):0.#}/{g.Sum(r => r.Weight * r.Result.Spawned) / g.Sum(r => r.Weight):0.#}"))
                    + $" · 预计叠层 " + string.Join(" ", plan.LayerGains.Select(kv => $"{kv.Key}+{kv.Value:0.#}")),
                "上场：" + string.Join(" ", plan.Placed.Select(p => p.Name)),
                "检查：" + string.Join("；", plan.Checks.Select(c => (c.Ok ? "✓" : "✗") + c.Text)),
            };
            foreach (var p in plan.Placed)
                caption.Add($"{p.Name}：" + string.Join("；", p.Notes.Take(4)));
            var png = Compose(shot, match.Map, board.Cameras[match.Camera], plan, null, caption);
            using var fs = File.Create(Path.Combine(outDir, $"pool_{mapId}_{mode}.png"));
            png.Save(fs);
            return;
        }

        // PROBE_PICK = "lineup:round:map" draws just that one.
        var one = Environment.GetEnvironmentVariable("PROBE_PICK")?.Split(':');
        var picks = one is { Length: 3 } ? [(one[0], new[] { int.Parse(one[1]) })] : Picks;
        if (one is { Length: 3 }) mapIds = [one[2]];
        int pick = 0;
        foreach (var (id, rounds) in picks)
        {
            var file = Directory.GetFiles(lineupDir, id + "*.json").FirstOrDefault();
            if (file is null) continue;
            var lineup = JsonSerializer.Deserialize<Lineup>(File.ReadAllText(file))!;
            // Spread the lineups over the recognised maps.
            var mapId = mapIds[pick++ % mapIds.Count];
            var (_, shot, match) = shots[mapId];
            var map = match.Map;
            var planner = new PlacementPlanner(ops, covenants);
            var layers = new Dictionary<string, double>();
            for (int round = 1; round <= rounds.Max(); round++)
            {
                var (field, elites) = Curve[round - 1];
                var slots = (round <= 4 ? lineup.EarlySlots : round <= 9 ? lineup.MidSlots : lineup.Slots)
                    .Where(s => ops.ContainsKey(s.OperatorName)).ToList();
                var eliteNames = slots.OrderByDescending(s => s.IsElite).Take(elites).Select(s => s.OperatorName).ToHashSet();
                var pool = slots.Select(s => s.OperatorName).Distinct().Select(n => new PlacementCandidate(n, eliteNames.Contains(n))).ToList();
                var roundRoutes = routes.Find(map.Id, round, null, null);
                var plan = planner.Plan(map, pool, new PlacementOptions
                {
                    Routes = roundRoutes, Round = round, FieldCount = field, Iterations = 1500, Restarts = 2,
                    SimulationIterations = 200, Streams = spawns.Streams(round, Mode, Run),
                    CovenantLayers = layers.ToDictionary(kv => kv.Key, kv => (int)kv.Value),
                });
                foreach (var (c, g) in plan.LayerGains) layers[c] = layers.GetValueOrDefault(c) + g;
                if (!rounds.Contains(round)) continue;

                var caption = new List<string>
                {
                    $"{lineup.Name} · 第 {round} 回合 · {ModeName} · 本局敌人 {string.Join("、", Run)} · {map.Name}",
                    "上场：" + string.Join(" ", plan.Placed.Select(p => p.Name + (eliteNames.Contains(p.Name) ? "★" : ""))),
                    "盟约（打完累计层）：" + string.Join(" ", plan.ActiveCovenants.Select(c => layers.TryGetValue(c, out var v) ? $"{c} {v:0}" : c)),
                };
                var warn = plan.Checks.Where(c => !c.Ok && !c.IsHint && !(c.Detail ?? c.Text).Contains("：处理")).Select(c => c.Text).ToList();
                if (warn.Count > 0) caption.Add("提示：" + string.Join("；", warn));
                var castNotes = plan.Placed.SelectMany(p => p.Notes.Where(n => n.StartsWith("模拟")).Select(n => $"{p.Name} {n}")).ToList();
                if (castNotes.Count > 0) caption.Add(string.Join("；", castNotes));
                foreach (var g in plan.StreamResults.GroupBy(r => r.Type))
                {
                    double w = g.Sum(r => r.Weight);
                    double k = g.Sum(r => r.Weight * r.Result.Killed) / w, n = g.Sum(r => r.Weight * r.Result.Spawned) / w;
                    double bosses = g.Sum(r => r.Weight * r.Result.Bosses) / w;
                    string boss = bosses > 0 ? $" · 首领血量打掉 {g.Sum(r => r.Weight * r.Result.BossProgress) / w / bosses:P0}" : "";
                    caption.Add($"{GarrisonSpawnStore.TypeNames.GetValueOrDefault(g.Key, g.Key == "BOSS" ? "首领回合" : g.Key)}：模拟处理 {k:0.#}/{n:0.#}{boss}");
                }
                var png = Compose(shot, map, board.Cameras[match.Camera], plan, roundRoutes, caption);
                string name = $"{id}_r{round:00}_{map.Id}_{Mode.Replace("mode_multi_", "")}.png";
                using var fs = File.Create(Path.Combine(outDir, name));
                png.Save(fs);
            }
        }
    }

    private static BitmapSource Load(string path)
    {
        var frame = BitmapDecoder.Create(new Uri(path), BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad).Frames[0];
        return new FormatConvertedBitmap(frame, PixelFormats.Bgr24, null, 0);
    }

    /// <summary>A 24-bit bottom-up BMP of the image scaled to <paramref name="w"/>×<paramref name="h"/> (the recognizer's input).</summary>
    private static byte[] Bmp(BitmapSource image, int w, int h)
    {
        var scaled = new FormatConvertedBitmap(new TransformedBitmap(image, new ScaleTransform((double)w / image.PixelWidth, (double)h / image.PixelHeight)), PixelFormats.Bgr24, null, 0);
        int stride = (w * 3 + 3) & ~3;
        var pixels = new byte[stride * h];
        scaled.CopyPixels(pixels, stride, 0);
        var bmp = new byte[54 + pixels.Length];
        bmp[0] = (byte)'B'; bmp[1] = (byte)'M';
        BitConverter.GetBytes(bmp.Length).CopyTo(bmp, 2);
        BitConverter.GetBytes(54).CopyTo(bmp, 10);
        BitConverter.GetBytes(40).CopyTo(bmp, 14);
        BitConverter.GetBytes(w).CopyTo(bmp, 18);
        BitConverter.GetBytes(h).CopyTo(bmp, 22);
        BitConverter.GetBytes((short)1).CopyTo(bmp, 26);
        BitConverter.GetBytes((short)24).CopyTo(bmp, 28);
        BitConverter.GetBytes(pixels.Length).CopyTo(bmp, 34);
        for (int y = 0; y < h; y++)
            Array.Copy(pixels, y * stride, bmp, 54 + (h - 1 - y) * stride, stride);
        return bmp;
    }

    private static PngBitmapEncoder Compose(BitmapSource shot, GarrisonMap map, BoardProjection projection, PlacementPlan? plan,
        RoundRoutes? routes, IReadOnlyList<string> caption)
    {
        double w = shot.PixelWidth, h = shot.PixelHeight;
        var overlay = new OverlayWindow();
        overlay.UpdateBoard(map, projection, showCells: false);
        overlay.UpdateRoutes(routes);
        overlay.UpdatePlacements(plan);
        // The overlay sizes its text for the client width; draw it at a 1920-wide client and scale up to the shot.
        double scale = w / 1920;
        var layer = overlay.SnapshotBoard(1920, h / scale);
        layer.LayoutTransform = new ScaleTransform(scale, scale);

        var text = new StackPanel { Margin = new Thickness(18 * scale) };
        foreach (var (line, i) in caption.Select((l, i) => (l, i)))
            text.Children.Add(new TextBlock
            {
                Text = line, Foreground = i == 0 ? new SolidColorBrush(Color.FromRgb(0x6F, 0xE0, 0xD3)) : Brushes.White,
                FontSize = (i == 0 ? 22 : 17) * scale, FontWeight = i == 0 ? FontWeights.Bold : FontWeights.Normal,
                Margin = new Thickness(0, 0, 0, 3 * scale),
            });
        var plate = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(0xD8, 0x08, 0x0B, 0x0D)), CornerRadius = new CornerRadius(8 * scale),
            HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(14 * scale),
            Child = text,
        };
        var grid = new Grid { Width = w, Height = h };
        grid.Children.Add(new Image { Source = shot, Stretch = Stretch.Fill });
        grid.Children.Add(new Canvas { Children = { layer } });
        grid.Children.Add(plate);
        grid.Measure(new Size(w, h));
        grid.Arrange(new Rect(0, 0, w, h));
        grid.UpdateLayout();
        var rtb = new RenderTargetBitmap((int)w, (int)h, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(grid);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(rtb));
        overlay.Close();
        return enc;
    }
}
