// Added for ASP Assistant Arknights on 2026-11-12; modified 2026-11-14; see NOTICE.
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using UniformGrid = System.Windows.Controls.Primitives.UniformGrid;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using ASPAssistant.App.Controls;
using ASPAssistant.App.Windows;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;

namespace ASPAssistant.App.Views;

/// <summary>
/// 追踪 · 站位: picks which owned operators go on the field and where, for the recognised (or chosen) map.
/// The player can drag an operator on the mini board, fix it there, and let the rest be re-planned around it.
/// </summary>
public partial class DeployView : UserControl
{
    private const double S = 23, L = 12, T = 12;
    private static readonly Brush TankBrush = Frozen(Color.FromRgb(0x4C, 0x8D, 0xFF));
    private static readonly Brush StackerBrush = Frozen(Color.FromRgb(0xB0, 0x7C, 0xFF));
    private static readonly Brush HighBrush = Frozen(Color.FromArgb(0x38, 0x4C, 0x8D, 0xFF));
    private static readonly Brush DefenseBrush = Frozen(Color.FromRgb(0x39, 0xD0, 0xFF));
    private static readonly Brush PinBrush = Frozen(Color.FromRgb(0xE8, 0xC0, 0x60));

    private IReadOnlyDictionary<string, Operator> _operators = new Dictionary<string, Operator>();
    private PlacementPlanner? _planner;
    private GarrisonBoardData? _board;
    private TrackingViewModel? _tracking;
    private SettingsManager? _settings;
    private DeployPoolState _state = new();

    private BoardMatch? _recognized;
    private PlacementPlan? _recommended;   // last planner output
    private PlacementPlan? _plan;          // what is shown (recommended, or after the player moved someone)
    private Dictionary<string, (BoardCell Cell, Facing Facing)> _working = [];
    private readonly Dictionary<string, PlacementPin> _pins = [];
    private string? _pinsMap;
    private string? _selected;
    private bool _dirty;                   // the shown arrangement differs from the planner's
    private CancellationTokenSource? _planCts;
    private readonly DispatcherTimer _replanTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private int? _activeTier;   // 扩展池: the tier shown (tiles switch between tiers)
    private string _poolQuery = "";
    private bool _ownedOnly;
    private bool _ready;

    // drag state
    private string? _dragName;
    private Point _dragStart;
    private FrameworkElement? _dragElement;
    private bool _dragMoved;

    public DeployView()
    {
        InitializeComponent();
        _replanTimer.Tick += (_, _) => { _replanTimer.Stop(); _ = ReplanAsync(); };
        ModeList.SelectedIndex = 1;
        FieldCountList.SelectedIndex = 0;
        FlyingList.SelectedIndex = 0;
        IsVisibleChanged += (_, _) => { if (IsVisible && _ready && _plan is null) ScheduleReplan(); };
    }

    /// <summary>The plan to show in game (null when there is none, or the in-game display is off).</summary>
    public event Action<PlacementPlan?>? PlanChanged;
    public event Action<bool>? ShowInGameChanged;

    public bool ShowInGame => InGameToggle.IsChecked == true;

    /// <summary>
    /// The round to plan for on a map: its real routes (null when unknown, with why), the round number and how many
    /// rounds the mode has. Set by the app from the shop HUD and the information screen.
    /// </summary>
    public Func<string, (RoundRoutes? Routes, int? Round, int TotalRounds, string? Why)>? RoundProvider { get; set; }
    /// <summary>
    /// The real enemy streams a round may bring (this run's 3 enemy types from the information screen, the HUD's
    /// mode and the boss), from garrison_spawns.json; null without the data. Set by the app.
    /// </summary>
    public Func<int, IReadOnlyList<ASPAssistant.Core.Data.RoundStream>?>? StreamProvider { get; set; }
    /// <summary>
    /// Flying paths for a map when this round's routes are unknown: a round of that map that has flyers. Set by the app.
    /// </summary>
    public Func<string, RoundRoutes?>? FlyingRoutesProvider { get; set; }
    /// <summary>This run's covenant layers as read in game (empty until a battle shows them). Set by the app.</summary>
    public Func<IReadOnlyDictionary<string, int>>? LayersProvider { get; set; }
    private string? _roundKey;
    /// <summary>The round whose real enemies stand in while the round is not read yet (mid-run).</summary>
    private const int AssumedRound = 8;
    /// <summary>The player's 有飞行 / 无飞行 for this round (null: decided from this run's enemy types).</summary>
    private bool? _flyingOverride;

    /// <summary>Round, mode or boss changed: replan when the round's routes for the current map are different.</summary>
    public void RoundContextChanged()
    {
        if (!_ready) return;
        var key = RoundKey();
        if (key == _roundKey) return;
        _roundKey = key;
        _flyingOverride = null;   // a new round: back to deciding from the run's enemy types
        ScheduleReplan();
    }

    private (RoundRoutes? Routes, int? Round, int TotalRounds, string? Why) RoundContext()
    {
        var ctx = CurrentMap is { } map && RoundProvider is { } provider ? provider(map.Id) : (null, null, 15, null);
        // A round set by hand holds until the game reads a different one.
        if (_roundFix is { } fix)
        {
            if (fix.ReadThen != ctx.Round) _roundFix = null;
            else ctx.Round = fix.Value;
        }
        return ctx;
    }

    /// <summary>The round set by hand, with the round the game had read then.</summary>
    private (int Value, int? ReadThen)? _roundFix;
    private bool _editingRound;

    /// <summary>The round chip at the start of the layer row: 第 N 回合 (or 回合未读); click to set it by hand.</summary>
    private void AddRoundChip()
    {
        var (_, round, total, _) = RoundContext();
        bool byHand = _roundFix is not null;
        if (_editingRound)
        {
            var box = new TextBox
            {
                Text = round?.ToString() ?? "", Width = 34, FontSize = 10.5, Padding = new Thickness(2, 0, 2, 0),
                Margin = new Thickness(0, 0, 6, 3), VerticalContentAlignment = VerticalAlignment.Center,
                Background = (Brush)FindResource("InputBackgroundBrush"), Foreground = (Brush)FindResource("TextPrimaryBrush"),
                BorderBrush = (Brush)FindResource("AccentSolidBrush"), CaretBrush = (Brush)FindResource("TextPrimaryBrush"),
            };
            var pair = new StackPanel { Orientation = Orientation.Horizontal };
            var label = Text("第 ", 10.5, "TextPrimaryBrush", bold: true);
            label.VerticalAlignment = VerticalAlignment.Center;
            pair.Children.Add(label);
            pair.Children.Add(box);
            bool done = false;
            void Commit()
            {
                if (done) return;
                done = true;
                _editingRound = false;
                int? read = CurrentMap is { } m && RoundProvider is { } p ? p(m.Id).Round : null;
                if (int.TryParse(box.Text.Trim(), out int r) && r >= 1 && r <= total) _roundFix = r == read ? null : (r, read);
                else if (box.Text.Trim().Length == 0) _roundFix = null;
                ShowRoundBasis();
                RoundContextChanged();
            }
            box.KeyDown += (_, e) =>
            {
                if (e.Key == Key.Enter) Commit();
                else if (e.Key == Key.Escape) { done = true; _editingRound = false; ShowRoundBasis(); }
            };
            box.LostKeyboardFocus += (_, _) => Commit();
            LayerRow.Children.Add(pair);
            Dispatcher.BeginInvoke(() => { box.Focus(); box.SelectAll(); }, System.Windows.Threading.DispatcherPriority.Input);
            return;
        }
        var tb = new TextBlock
        {
            Text = round is int n ? $"第 {n} 回合" : $"回合未读（按第 {AssumedRound} 回合）", FontSize = 10.5, FontWeight = FontWeights.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = (Brush)FindResource(byHand ? "EliteBrush" : round is null ? "TextSecondaryBrush" : "TextPrimaryBrush"),
        };
        var chip = new Border
        {
            CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 1, 5, 1), Margin = new Thickness(0, 0, 6, 3),
            Background = (Brush)FindResource("SectionBackgroundBrush"), Cursor = Cursors.Hand, Child = tb,
            ToolTip = byHand ? "回合已手动设置（点一下再改，清空恢复读取值；游戏读到新回合会覆盖）" : "点一下手动设置回合",
        };
        chip.MouseLeftButtonUp += (_, e) => { e.Handled = true; _editingRound = true; ShowRoundBasis(); };
        LayerRow.Children.Add(chip);
    }

    private string RoundKey()
    {
        var (routes, round, _, why) = RoundContext();
        var streams = Streams(round ?? AssumedRound);
        // The run's types and the mode's buff (first enemy's HP) change the streams without changing the routes.
        string spawn = streams is null ? "-"
            : string.Join(",", streams.Select(s => s.Type).Distinct()) + "|" + streams.Sum(s => s.Spawns.Count)
              + "|" + streams.Select(s => s.Spawns.FirstOrDefault()?.Profile.Hp ?? 0).FirstOrDefault().ToString("0");
        return $"{CurrentMap?.Id}|{round}|{routes?.Level}|{why}|{spawn}";
    }
    public PlacementPlan? CurrentPlan => _plan;
    /// <summary>转职 items and the covenant each gives (变形同构体 + the item). Set by the app before <see cref="Initialize"/>.</summary>
    public IReadOnlyDictionary<string, string> JobChangeCovenants { get; set; } = new Dictionary<string, string>();

    public void Initialize(IReadOnlyDictionary<string, Operator> operators, IEnumerable<CovenantInfo> covenants,
        GarrisonBoardData? board, TrackingViewModel tracking, SettingsManager settings, DeployPoolState state, bool showInGame)
    {
        _operators = operators;
        _planner = new PlacementPlanner(operators, covenants);
        _board = board;
        _tracking = tracking;
        _settings = settings;
        _state = state;
        InGameToggle.IsChecked = showInGame;
        _suppressSelection = true;
        ModeList.SelectedIndex = (int)state.Mode;
        FieldCountList.SelectedIndex = state.FieldCount >= 9 ? 1 : 0;
        FlyingList.SelectedIndex = state.ExpectFlying ? 0 : 1;
        _suppressSelection = false;
        tracking.PropertyChanged += OnTrackingPropertyChanged;
        HookTracking();
        DrawLegend();
        BuildMapChoices();
        _ready = true;
        RefreshAll();
        ScheduleReplan();
    }

    /// <summary>Recognition result from the shop / board reader.</summary>
    /// <remarks>
    /// The board is not visible all the time (shop menus, battle effects, operators standing on cells), so the
    /// last recognised map is kept: losing sight of the board must not throw away the plan or the fixed operators.
    /// Only a different map replaces it.
    /// </remarks>
    public void SetRecognizedBoard(BoardMatch? match)
    {
        string? before = CurrentMap?.Id;
        _recognized = match;
        if (match is not null) _lastSeen = match.Map;
        if (CurrentMap?.Id != before) OnMapChanged();
        else RefreshHeader();
    }

    private GarrisonMap? _lastSeen;

    /// <summary>The map being planned: the one picked by hand, else the recognised (or last recognised) one.</summary>
    public GarrisonMap? CurrentMap =>
        _state.ManualMap is { } id && _board?.Find(id) is { } manual ? manual : _recognized?.Map ?? _lastSeen;

    private void OnMapChanged()
    {
        _selected = null;
        _dirty = false;
        if (_pinsMap != CurrentMap?.Id) { _pins.Clear(); _pinsMap = CurrentMap?.Id; }
        _plan = _recommended = null;
        _working = [];
        RefreshAll();
        ScheduleReplan();
    }

    // ── Tracking / pool ────────────────────────────────────────────────────

    private void OnTrackingPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TrackingViewModel.TrackedOperators)) HookTracking();
        if (e.PropertyName is nameof(TrackingViewModel.TrackedOperators) or nameof(TrackingViewModel.TerminalLevel))
            Dispatcher.BeginInvoke(() => { RefreshAll(); ScheduleReplan(); });
    }

    private NotifyCollectionChangedEventHandler? _trackingHandler;
    private System.Collections.ObjectModel.ObservableCollection<TrackingEntry>? _hooked;

    private void HookTracking()
    {
        if (_hooked is not null && _trackingHandler is not null) _hooked.CollectionChanged -= _trackingHandler;
        _hooked = _tracking?.TrackedOperators;
        _trackingHandler = (_, _) => Dispatcher.BeginInvoke(() => { RefreshAll(); ScheduleReplan(); });
        if (_hooked is not null) _hooked.CollectionChanged += _trackingHandler;
    }

    private IReadOnlyList<DeployPoolGroup> Groups() =>
        DeployPoolBuilder.Build(_tracking?.TrackedOperators ?? [], _operators, _state);

    /// <summary>Owned operators; a duplicated operator gives one candidate per copy (keys 冻河, 冻河#2…).</summary>
    private List<PlacementCandidate> Candidates() =>
        [.. _state.Owned.Distinct().Where(_operators.ContainsKey)
            .SelectMany(n => Enumerable.Range(0, _state.CountOf(n)).Select(k => new PlacementCandidate(n, _state.IsElite(n), k)))
            .Select(c => _state.Transforms.TryGetValue(c.Key, out var t) && t.Count > 0 ? c with { Transforms = [.. t] } : c)];

    private static string NameOf(string key) => PlacementKeys.NameOf(key);
    /// <summary>Display name of a key: 冻河#2 → 冻河②.</summary>
    private static string Display(string key)
    {
        string name = NameOf(key);
        return key.Length > name.Length && int.TryParse(key[(name.Length + 1)..], out int k) && k is >= 2 and <= 9
            ? name + (char)('①' + k - 1) : name;
    }

    private void SaveState()
    {
        if (_settings is { } s) _ = s.SaveDeployPoolAsync(_state);
    }

    // ── Planning ───────────────────────────────────────────────────────────

    private void ScheduleReplan()
    {
        if (!_ready) return;
        _replanTimer.Stop();
        _replanTimer.Start();
    }

    private PlacementOptions Options()
    {
        var (routes, round, total, _) = RoundContext();
        // Round not read yet: plan against a mid-run round's real enemies rather than a made-up stream.
        var streams = Streams(round ?? AssumedRound);
        bool flying = _flyingOverride ?? (streams is { Count: > 0 } ? streams.Any(s => s.Flying) : _state.ExpectFlying);
        // The player said whether this round flies: keep only the streams that match (none match: the generic stream).
        if (_flyingOverride is bool chosen && streams is not null)
        {
            var matching = streams.Where(s => s.Flying == chosen).ToList();
            streams = matching.Count > 0 ? matching : null;
        }
        // No routes for this round yet but flyers expected: a round of this map that has flyers gives their paths.
        if (routes is null && flying && CurrentMap is { } m && FlyingRoutesProvider?.Invoke(m.Id) is { } flyRoutes)
            routes = flyRoutes;
        // Flyers need flying paths: without any, plan against the ground streams alone (their shares rescaled).
        if (streams is not null && (routes is null || routes.Flying == 0) && streams.Any(s => s.Flying))
        {
            var ground = streams.Where(s => !s.Flying).ToList();
            double sum = ground.Sum(s => s.Weight);
            streams = ground.Count > 0 && sum > 0 ? [.. ground.Select(s => s with { Weight = s.Weight / sum })] : null;
        }
        return new()
        {
            FieldCount = _state.FieldCount,
            Mode = _state.Mode,
            Pins = new Dictionary<string, PlacementPin>(_pins),
            FieldPins = _state.FieldPins.ToHashSet(),
            BenchPins = _state.BenchPins.ToHashSet(),
            TransformableCovenants = JobChangeCovenants.Values.ToHashSet(),
            Routes = routes,
            ExpectFlying = flying,
            Round = round,
            TotalRounds = total,
            Streams = streams,
            // The layers read from the battle screen's badge strip (the last battle's; empty before the first).
            CovenantLayers = CurrentLayers(),
        };
    }

    /// <summary>The line under the toolbar: what the plan is based on and how much stacking counts this round.</summary>
    private void ShowRoundBasis()
    {
        var (routes, round, _, why) = RoundContext();
        string text = _plan?.Basis ?? (routes is null ? "地图默认路线" : "");
        if (routes is null && why is not null) text += $"（{(round is int r ? $"第 {r} 回合：" : "")}{why}）";
        if (round is null && Streams(AssumedRound) is { Count: > 0 }) text += $" · 按第 {AssumedRound} 回合的真实出怪估算";
        if (_plan is { } p) text += $" · 叠层权重 ×{p.StackWeight:0.##}";
        var layers = LayersProvider?.Invoke();
        text += layers is { Count: > 0 }
            ? " · 层数 " + string.Join(" ", layers.OrderByDescending(kv => kv.Value).Select(kv => $"{kv.Key}{kv.Value}"))
            : " · 层数未读到（战斗中会读取顶部盟约栏）";
        // The line itself stays short; the full basis is in its tooltip.
        _basisTip = ShortBasis(routes, round, layers) + "\n" + text;
        RefreshLayerRow();
        bool byStreams = Streams(round) is { Count: > 0 };
        FlyingList.IsEnabled = true;
        FlyingList.ToolTip = (_flyingOverride is not null ? "已手动指定这回合是否飞行波次（换回合后恢复自动）。"
                : byStreams ? "自动：按本局敌人类型判断。点选可手动指定这回合是飞行波次还是地面波次。"
                : "点选这回合是否飞行波次。")
            + (routes is null ? "还没读到本回合路线时，有飞行按这张图常见的飞行路线规划。" : "");
        if (_plan is { } fp)
        {
            _suppressSelection = true;
            FlyingList.SelectedIndex = fp.Flying ? 0 : 1;
            _suppressSelection = false;
        }
    }

    private string _basisTip = "";
    /// <summary>Layers set by hand, with what the game had read for that covenant then (a newer read replaces them).</summary>
    private readonly Dictionary<string, (int Value, int? ReadThen)> _layerFixes = [];
    private string? _editingLayer;

    /// <summary>The covenants' layers the plan uses: read in battle, corrected by hand where the player did.</summary>
    private Dictionary<string, int> CurrentLayers()
    {
        var read = LayersProvider?.Invoke() ?? new Dictionary<string, int>();
        var layers = new Dictionary<string, int>(read);
        foreach (var (cov, fix) in _layerFixes.ToList())
        {
            int? now = read.TryGetValue(cov, out var v) ? v : null;
            if (now != fix.ReadThen) { _layerFixes.Remove(cov); continue; }   // the game read a newer value
            layers[cov] = fix.Value;
        }
        return layers;
    }

    /// <summary>
    /// The layer row next to 有飞行 / 无飞行: each covenant the plan activates (and any other with layers) as "炎 46";
    /// click one to type the right number. Hand-set values are marked and last until the game reads a new value.
    /// </summary>
    private void RefreshLayerRow()
    {
        LayerRow.Children.Clear();
        LayerRow.ToolTip = _basisTip + "\n点回合或层数可以手动修正（游戏读到新的会覆盖）";
        AddRoundChip();
        var layers = CurrentLayers();
        var shown = (_plan?.ActiveCovenants ?? []).Concat(layers.Where(kv => kv.Value > 0).Select(kv => kv.Key))
            .Distinct().OrderByDescending(c => layers.GetValueOrDefault(c)).ThenBy(c => c, StringComparer.Ordinal).ToList();
        if (shown.Count == 0)
        {
            LayerRow.Children.Add(Text("层数未读（战斗中读取顶部盟约栏）", 10.5, "TextSecondaryBrush"));
            return;
        }
        foreach (var cov in shown)
        {
            bool fixedByHand = _layerFixes.ContainsKey(cov);
            if (_editingLayer == cov)
            {
                var box = new TextBox
                {
                    Text = layers.TryGetValue(cov, out var cur) ? cur.ToString() : "", Width = 42, FontSize = 10.5, Padding = new Thickness(2, 0, 2, 0),
                    Margin = new Thickness(0, 0, 4, 3), VerticalContentAlignment = VerticalAlignment.Center,
                    Background = (Brush)FindResource("InputBackgroundBrush"), Foreground = (Brush)FindResource("TextPrimaryBrush"),
                    BorderBrush = (Brush)FindResource("AccentSolidBrush"), CaretBrush = (Brush)FindResource("TextPrimaryBrush"),
                };
                var label = Text(cov + " ", 10.5, "TextPrimaryBrush", bold: true);
                label.VerticalAlignment = VerticalAlignment.Center;
                var pair = new StackPanel { Orientation = Orientation.Horizontal };
                pair.Children.Add(label);
                pair.Children.Add(box);
                bool done = false;
                void Commit()
                {
                    if (done) return;
                    done = true;
                    _editingLayer = null;
                    var read = LayersProvider?.Invoke();
                    int? readNow = read is not null && read.TryGetValue(cov, out var rv) ? rv : null;
                    if (int.TryParse(box.Text.Trim(), out int value) && value >= 0) _layerFixes[cov] = (Math.Min(value, 9999), readNow);
                    else if (box.Text.Trim().Length == 0) _layerFixes.Remove(cov);
                    RefreshLayerRow();
                    ScheduleReplan();
                }
                box.KeyDown += (_, e) =>
                {
                    if (e.Key == Key.Enter) Commit();
                    else if (e.Key == Key.Escape) { done = true; _editingLayer = null; RefreshLayerRow(); }
                };
                box.LostKeyboardFocus += (_, _) => Commit();
                LayerRow.Children.Add(pair);
                Dispatcher.BeginInvoke(() => { box.Focus(); box.SelectAll(); }, System.Windows.Threading.DispatcherPriority.Input);
                continue;
            }
            string value2 = layers.TryGetValue(cov, out var n) ? n.ToString() : "–";
            var tb = new TextBlock { FontSize = 10.5, VerticalAlignment = VerticalAlignment.Center };
            tb.Inlines.Add(new System.Windows.Documents.Run(cov + " ") { Foreground = (Brush)FindResource("TextSecondaryBrush") });
            tb.Inlines.Add(new System.Windows.Documents.Run(value2)
            {
                FontWeight = FontWeights.Bold,
                Foreground = (Brush)FindResource(fixedByHand ? "EliteBrush" : "TextPrimaryBrush"),
            });
            var chip = new Border
            {
                CornerRadius = new CornerRadius(3), Padding = new Thickness(5, 1, 5, 1), Margin = new Thickness(0, 0, 4, 3),
                Background = (Brush)FindResource("SectionBackgroundBrush"), Cursor = Cursors.Hand, Child = tb,
                ToolTip = fixedByHand ? $"{cov} 层数已手动改为 {value2}（点一下再改，清空恢复读取值）" : $"点一下手动修正{cov}的层数",
            };
            string c = cov;
            chip.MouseLeftButtonUp += (_, e) => { e.Handled = true; _editingLayer = c; RefreshLayerRow(); };
            LayerRow.Children.Add(chip);
        }
    }

    /// <summary>The basis in a few words: 第 8 回合路线 · 本局 3 类敌人 · 叠层 ×2.5 · 层数未读.</summary>
    private string ShortBasis(RoundRoutes? routes, int? round, IReadOnlyDictionary<string, int>? layers)
    {
        var parts = new List<string> { routes is not null && round is int r ? $"第 {r} 回合路线" : "默认路线" };
        var streams = Streams(round ?? (Streams(AssumedRound) is { Count: > 0 } ? AssumedRound : null));
        if (streams is { Count: > 0 })
        {
            var types = streams.Select(s => s.Type).Distinct().ToList();
            parts.Add(types.SequenceEqual(["BOSS"]) ? "首领关"
                : types.Count >= ASPAssistant.Core.Data.GarrisonSpawnStore.TypeNames.Count ? "9 类敌人平均" : $"本局 {types.Count} 类敌人");
            if (round is null) parts.Add($"按第 {AssumedRound} 回合");
        }
        if (_plan is { } p) parts.Add($"叠层 ×{p.StackWeight:0.##}");
        parts.Add(layers is { Count: > 0 } ? "层数已读" : "层数未读");
        return string.Join(" · ", parts);
    }

    private IReadOnlyList<ASPAssistant.Core.Data.RoundStream>? Streams(int? round) =>
        round is int r && StreamProvider is { } provider ? provider(r) : null;

    private async Task ReplanAsync()
    {
        _planCts?.Cancel();
        var map = CurrentMap;
        var pool = Candidates();
        if (_planner is not { } planner || map is null || pool.Count == 0)
        {
            _plan = _recommended = null;
            _working = [];
            RefreshAll();
            PublishPlan();
            return;
        }
        foreach (var key in _pins.Keys.Where(k => !pool.Any(c => c.Key == k)).ToList()) _pins.Remove(key);
        _state.FieldPins.RemoveAll(k => !pool.Any(c => c.Key == k));
        _state.BenchPins.RemoveAll(k => !pool.Any(c => c.Key == k));
        foreach (var key in _state.Transforms.Keys.Where(k => !pool.Any(c => c.Key == k)).ToList()) _state.Transforms.Remove(key);
        // One 转职 per operator (暴躁铁皮 up to five): older saves may hold more.
        foreach (var (key, list) in _state.Transforms)
            while (list.Count > (NameOf(key) == LineupEquipmentService.MechName ? 5 : 1)) list.RemoveAt(0);
        _roundKey = RoundKey();
        var cts = _planCts = new CancellationTokenSource();
        var options = Options();
        SetStatus("正在计算站位…");
        SetComputing(true);
        RecomputeButton.IsEnabled = false;
        try
        {
            var plan = await Task.Run(() => planner.Plan(map, pool, options, cts.Token), cts.Token);
            if (cts.IsCancellationRequested) return;
            _recommended = _plan = plan;
            _working = plan.Placed.ToDictionary(p => p.Key, p => (p.Cell, p.Facing));
            _dirty = false;
            if (_selected is not null && plan.Find(_selected) is null) _selected = null;
            SetStatus(null);
        }
        catch (OperationCanceledException) { return; }
        catch (Exception ex)
        {
            Core.AppLogger.Warn("Deploy", $"Placement planning failed: {ex.Message}");
            SetStatus("站位计算出错：" + ex.Message);
        }
        finally
        {
            if (_planCts == cts) { RecomputeButton.IsEnabled = true; SetComputing(false); }
        }
        RefreshAll();
        PublishPlan();
    }

    private void Reevaluate()
    {
        if (_planner is null || CurrentMap is not { } map) return;
        // The same tougher / easier copy as the plan, so a dragged placement is judged on the same streams.
        _plan = _planner.Evaluate(map, Candidates(), _working, Options() with { StreamFactor = _plan?.StreamFactor });
        RefreshAll();
        PublishPlan();
    }

    private void PublishPlan() => PlanChanged?.Invoke(ShowInGame ? _plan : null);

    private void SetStatus(string? text)
    {
        StatusText.Text = text ?? "";
        StatusText.Visibility = text is null ? Visibility.Collapsed : Visibility.Visible;
    }

    // ── Rendering ──────────────────────────────────────────────────────────

    private enum Page { Main, Pool, NoMap }
    private Page? _shownPage;
    private PlacementPlan? _listedPlan;   // plan the operator cards were last animated for
    private string? _shownDetail;
    private string? _bouncePool;          // pool card to bounce after a toggle
    private int? _justOpenedTier;
    private Page _page = Page.Main;
    private bool _pickingMap;

    private void RefreshAll()
    {
        if (!_ready) return;
        var page = _page == Page.Pool ? Page.Pool : CurrentMap is null || _pickingMap ? Page.NoMap : Page.Main;
        if (page != _shownPage)
        {
            // Pool / map picker come in from the right, the main view from the left.
            FrameworkElement panel = page switch { Page.Pool => PoolPanel, Page.NoMap => NoMapPanel, _ => MainPanel };
            if (_shownPage is not null) PageTransition.SlideIn(panel, fromX: page == Page.Main ? -14 : 14);
            _shownPage = page;
        }
        MainPanel.Visibility = page == Page.Main ? Visibility.Visible : Visibility.Collapsed;
        PoolPanel.Visibility = page == Page.Pool ? Visibility.Visible : Visibility.Collapsed;
        ShowRoundBasis();
        NoMapPanel.Visibility = page == Page.NoMap ? Visibility.Visible : Visibility.Collapsed;
        if (page == Page.Pool) { RefreshPool(); return; }
        if (page == Page.NoMap) { RefreshNoMap(); return; }
        RefreshHeader();
        DrawBoard();
        RefreshBench();
        RefreshPlanList();
        RefreshDetail();
    }

    private void RefreshHeader()
    {
        var map = CurrentMap;
        MapName.Text = map?.Name ?? "未识别地图";
        bool manual = _state.ManualMap is not null;
        bool stale = !manual && _recognized is null && _lastSeen is not null;
        MapSourceText.Text = manual ? "手动" : stale ? "上次识别" : "自动";
        MapSourceDot.Fill = (Brush)FindResource(manual ? "TextSecondaryBrush" : stale ? "WarningBrush" : "SuccessBrush");
        MapThumb.Source = map is null ? null : Thumbnail(map.Id);
        var finals = _tracking?.TrackedOperators.Where(e => e.Type == TrackingType.Operator && !e.IsTransition).ToList() ?? [];
        var covs = finals.Select(e => _operators.GetValueOrDefault(e.Name)).Where(o => o is not null)
            .SelectMany(o => o!.CoreCovenants.Concat(o.AdditionalCovenants))
            .GroupBy(c => c).OrderByDescending(g => g.Count()).Take(2).Select(g => $"{g.Key} {g.Count()}");
        string level = _tracking is { TerminalLevel: > 0 } t ? $"终端 {t.TerminalLevel} 级" : "终端未识别";
        string camera = _recognized?.Camera == BoardCamera.Expanded ? " · 商店展开"
            : _recognized is not null ? " · 商店收起"
            : stale ? " · 暂时没看到棋盘" : "";
        MapSubline.Text = $"{level}{camera}" + (finals.Count > 0 ? " · " + string.Join(" + ", covs) : " · 还没有追踪阵容");
        int owned = Candidates().Count;
        PoolCountText.Text = owned.ToString();
        PoolSummary.Text = _plan is null ? "整备区" : $"整备区 · {_plan.Bench.Count} 人";
        PoolMeta.Text = _plan is null ? (owned == 0 ? "先在干员池里点亮手上的干员" : "") : $"上场 {_plan.Placed.Count}";
    }

    private static readonly Dictionary<string, BitmapImage?> Thumbs = [];

    private static BitmapImage? Thumbnail(string mapId)
    {
        if (Thumbs.TryGetValue(mapId, out var cached)) return cached;
        BitmapImage? img = null;
        try { img = new BitmapImage(new Uri($"pack://application:,,,/Resources/MapThumbs/{mapId}.jpg")); }
        catch (Exception) { /* no thumbnail for this map */ }
        Thumbs[mapId] = img;
        return img;
    }

    private Brush RoleBrush(PlacementRole role) => role switch
    {
        PlacementRole.Damage => (Brush)FindResource("DangerBrush"),
        PlacementRole.Tank => TankBrush,
        PlacementRole.Support => (Brush)FindResource("FinalBrush"),
        _ => StackerBrush,
    };

    private void DrawLegend() { }

    private void DrawBoard()
    {
        BoardCanvas.Children.Clear();
        if (CurrentMap is not { } map) return;
        int allCols = map.ColumnCount, rows = map.RowCount;
        // Rows / columns with nothing on them (every map's row E, columns 1, 11 and 12) are left out.
        while (rows > 1 && Enumerable.Range(0, allCols).All(c => map.Cells[rows - 1, c] == BoardCellType.Blocked)) rows--;
        bool EmptyCol(int c) => Enumerable.Range(0, rows).All(r => map.Cells[r, c] == BoardCellType.Blocked);
        int c0 = 0, c1 = allCols - 1;
        while (c0 < c1 && EmptyCol(c0)) c0++;
        while (c1 > c0 && EmptyCol(c1)) c1--;
        _firstCol = map.FirstColumn + c0;
        _lastCol = map.FirstColumn + c1;
        _rows = rows;
        int cols = c1 - c0 + 1;
        BoardCanvas.Width = L + cols * S + 2;
        BoardCanvas.Height = T + rows * S + 2;
        var text3 = (Brush)FindResource("TextSecondaryBrush");
        for (int i = 0; i < cols; i++)
            Place(Text((c0 + i + 1).ToString(), 8, "TextSecondaryBrush", center: true, width: S), L + i * S, 0);
        for (int r = 0; r < rows; r++)
            Place(Text(((char)('A' + r)).ToString(), 8, "TextSecondaryBrush", center: true, width: 12), 0, T + r * S + 4);

        var raised = (Brush)FindResource("SectionBackgroundBrush");
        var line = (Brush)FindResource("LineBrush");
        var accentSoft = (Brush)FindResource("AccentSoftBrush");
        var accentBorder = (Brush)FindResource("AccentBorderBrush");
        foreach (var (cell, type) in map.AllCells())
        {
            if (!Visible(cell)) continue;
            var rect = new Rectangle { Width = S - 2, Height = S - 2, RadiusX = 3, RadiusY = 3 };
            switch (type)
            {
                case BoardCellType.Ground: rect.Fill = raised; break;
                case BoardCellType.Special: rect.Fill = accentSoft; rect.Stroke = accentBorder; rect.StrokeDashArray = [2, 2]; break;
                case BoardCellType.High: rect.Fill = HighBrush; break;
                case BoardCellType.Defense: rect.Stroke = DefenseBrush; rect.StrokeThickness = 2; break;
                case BoardCellType.Spawn: rect.Stroke = (Brush)FindResource("DangerBrush"); rect.StrokeThickness = 2; break;
                default: rect.Fill = line; rect.Opacity = 0.25; break;
            }
            Place(rect, CellX(map, cell.Col) + 1, T + cell.Row * S + 1);
        }

        // The lanes the plan defends: this round's real walking paths (thicker = more enemies), else the fixed routes.
        var lanes = _plan is { Lanes.Count: > 0 } lp && lp.Map.Id == map.Id
            ? lp.Lanes.Select(l => (Cells: l.Cells, Thickness: 1.0 + 2.4 * l.Share)).ToList()
            : map.Routes.Select((r, k) => (Cells: r, Thickness: k == map.Routes.Count - 1 ? 2.6 : 1.4)).ToList();
        foreach (var (cells, thickness) in lanes)
        {
            var pl = new Polyline
            {
                Stroke = (Brush)FindResource("DangerBrush"),
                StrokeThickness = thickness,
                StrokeDashArray = [3, 2],
                Opacity = 0.75,
                IsHitTestVisible = false,
            };
            foreach (var c in cells) pl.Points.Add(new Point(CellX(map, c.Col) + S / 2, T + c.Row * S + S / 2));
            BoardCanvas.Children.Add(pl);
        }

        // Selected operator: where it can go, its range, and where the planner had it.
        if (_selected is { } sel && _working.TryGetValue(sel, out var sv) && _planner is not null)
        {
            var occupied = _working.Where(kv => kv.Key != sel).Select(kv => kv.Value.Cell).ToHashSet();
            bool melee = _planner.IsMelee(NameOf(sel));
            foreach (var (cell, type) in map.AllCells())
            {
                if (!Visible(cell)) continue;
                if (cell == sv.Cell || occupied.Contains(cell) || !GarrisonMap.CanDeploy(type, melee)) continue;
                Place(new Rectangle { Width = S - 8, Height = S - 8, Stroke = accentBorder, StrokeDashArray = [2, 2], RadiusX = 2, RadiusY = 2, IsHitTestVisible = false },
                    CellX(map, cell.Col) + 4, T + cell.Row * S + 4);
            }
            if (_operators.GetValueOrDefault(NameOf(sel))?.Profile?.RangeFor(_state.IsElite(NameOf(sel))) is { } range)
                foreach (var (dx, dy) in range.Offsets(sv.Facing))
                {
                    var c = new BoardCell(sv.Cell.Col + dx, sv.Cell.Row + dy);
                    if (!Visible(c)) continue;
                    Place(new Rectangle { Width = S, Height = S, Fill = accentSoft, Stroke = (Brush)FindResource("AccentBrush"), StrokeThickness = 1.2, RadiusX = 3, RadiusY = 3, IsHitTestVisible = false },
                        CellX(map, c.Col), T + c.Row * S);
                }
            if (_dirty && _recommended?.Find(sel) is { } rec && rec.Cell != sv.Cell)
            {
                Place(new Rectangle { Width = S - 2, Height = S - 2, Stroke = text3, StrokeThickness = 1.4, StrokeDashArray = [2, 2], RadiusX = 4, RadiusY = 4, IsHitTestVisible = false },
                    CellX(map, rec.Cell.Col) + 1, T + rec.Cell.Row * S + 1);
                var a = new Point(CellX(map, rec.Cell.Col) + S / 2, T + rec.Cell.Row * S + S / 2 + 6);
                var b = new Point(CellX(map, sv.Cell.Col) + S / 2, T + sv.Cell.Row * S + S / 2 + 6);
                var mid = new Point((a.X + b.X) / 2, Math.Max(a.Y, b.Y) + 10);
                var fig = new PathFigure { StartPoint = a, Segments = { new QuadraticBezierSegment(mid, b, true) } };
                BoardCanvas.Children.Add(new Path { Data = new PathGeometry([fig]), Stroke = (Brush)FindResource("AccentBrush"), StrokeThickness = 1.3, StrokeDashArray = [2, 2], IsHitTestVisible = false });
            }
        }

        if (_plan is null) return;
        int animIndex = 0;
        foreach (var p in _plan.Placed)
        {
            if (!_working.TryGetValue(p.Key, out var v)) continue;
            var brush = RoleBrush(p.Role);
            bool selected = p.Key == _selected;
            // Operator: its avatar framed in the role colour; facing = a short bar just outside that edge.
            var cellBox = new Grid { Width = S - 2, Height = S - 2, Cursor = Cursors.SizeAll, Tag = p.Key, Background = Brushes.Transparent };
            cellBox.Children.Add(new Border
            {
                Background = brush,
                BorderBrush = selected ? (Brush)FindResource("AccentBrush") : brush,
                BorderThickness = new Thickness(selected ? 2.5 : 2),
                CornerRadius = new CornerRadius(4),
                Child = new Avatar { Text = p.Name, Source = _operators.GetValueOrDefault(p.Name)?.IconPath, Small = true, CornerRadius = new CornerRadius(2) },
            });
            cellBox.Children.Add(FacingBar(v.Facing, brush));
            if (_pins.ContainsKey(p.Key))
                cellBox.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = PinBrush, Stroke = (Brush)FindResource("CardBackgroundBrush"), StrokeThickness = 1, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -3, -3, 0) });
            if (_selected is not null && !selected && !_pins.ContainsKey(p.Key)) cellBox.Opacity = 0.4;
            cellBox.ToolTip = $"{Display(p.Key)} · {PlacementRoleText.Of(p.Role)} · {p.CellCode} · {PlacementCells.FacingText(v.Facing)}";
            cellBox.MouseLeftButtonDown += OnOperatorMouseDown;
            var target = new Point(CellX(map, v.Cell.Col) + 1, T + v.Cell.Row * S + 1);
            Place(cellBox, target.X, target.Y);
            AnimateOperator(cellBox, p.Key, target, animIndex++);
        }
        _lastPos.Clear();
        foreach (var p in _plan.Placed)
            if (_working.TryGetValue(p.Key, out var v)) _lastPos[p.Key] = new Point(CellX(map, v.Cell.Col) + 1, T + v.Cell.Row * S + 1);
        _lastBoardMap = map.Id;
    }

    // ── Motion ─────────────────────────────────────────────────────────────

    private static readonly IEasingFunction Ease = new CubicEase { EasingMode = EasingMode.EaseOut };
    private static readonly IEasingFunction Pop = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.4 };
    private readonly Dictionary<string, Point> _lastPos = [];
    private string? _lastBoardMap;

    /// <summary>
    /// Operators glide from where they were to their new cell when the plan changes (or from the drop point after a
    /// drag); operators new to the board pop in one after another.
    /// </summary>
    private void AnimateOperator(FrameworkElement e, string key, Point target, int index)
    {
        if (!IsVisible) return;
        if (_lastBoardMap == CurrentMap?.Id && _lastPos.TryGetValue(key, out var from))
        {
            if ((from - target).Length < 0.5) return;
            e.BeginAnimation(Canvas.LeftProperty, new DoubleAnimation(from.X, target.X, TimeSpan.FromMilliseconds(260)) { EasingFunction = Ease });
            e.BeginAnimation(Canvas.TopProperty, new DoubleAnimation(from.Y, target.Y, TimeSpan.FromMilliseconds(260)) { EasingFunction = Ease });
            return;
        }
        var scale = new ScaleTransform(0.4, 0.4);
        e.RenderTransformOrigin = new Point(0.5, 0.5);
        e.RenderTransform = scale;
        var delay = TimeSpan.FromMilliseconds(35 * index);
        double opacity = e.Opacity;
        e.BeginAnimation(OpacityProperty, new DoubleAnimation(0, opacity, TimeSpan.FromMilliseconds(180)) { BeginTime = delay, EasingFunction = Ease });
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(260)) { BeginTime = delay, EasingFunction = Pop });
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.4, 1, TimeSpan.FromMilliseconds(260)) { BeginTime = delay, EasingFunction = Pop });
    }

    /// <summary>Cards fade and rise in, staggered (only when their content is new, not on every redraw).</summary>
    private static void RiseIn(FrameworkElement e, int index, double dy = 6)
    {
        var move = new TranslateTransform(0, dy);
        e.RenderTransform = move;
        var delay = TimeSpan.FromMilliseconds(Math.Min(index, 12) * 22);
        e.Opacity = 0;
        e.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(170)) { BeginTime = delay, EasingFunction = Ease });
        move.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(dy, 0, TimeSpan.FromMilliseconds(220)) { BeginTime = delay, EasingFunction = Ease });
    }

    /// <summary>A short press-and-release on a card the player just toggled.</summary>
    private static void Bounce(FrameworkElement e)
    {
        var scale = new ScaleTransform(1, 1);
        e.RenderTransformOrigin = new Point(0.5, 0.5);
        e.RenderTransform = scale;
        var a = new DoubleAnimation(0.94, 1, TimeSpan.FromMilliseconds(240)) { EasingFunction = Pop };
        scale.BeginAnimation(ScaleTransform.ScaleXProperty, a);
        scale.BeginAnimation(ScaleTransform.ScaleYProperty, a);
    }

    /// <summary>Dims the board while a new plan is being computed.</summary>
    private void SetComputing(bool computing) =>
        BoardCanvas.BeginAnimation(OpacityProperty, new DoubleAnimation(computing ? 0.55 : 1, TimeSpan.FromMilliseconds(computing ? 250 : 180)) { EasingFunction = Ease });

    private static UIElement FacingBar(Facing f, Brush brush) => f switch
    {
        Facing.Right => new Rectangle { Width = 3, Height = 9, Fill = brush, HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, -5, 0), RadiusX = 1, RadiusY = 1, IsHitTestVisible = false },
        Facing.Left => new Rectangle { Width = 3, Height = 9, Fill = brush, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(-5, 0, 0, 0), RadiusX = 1, RadiusY = 1, IsHitTestVisible = false },
        Facing.Down => new Rectangle { Width = 9, Height = 3, Fill = brush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom, Margin = new Thickness(0, 0, 0, -5), RadiusX = 1, RadiusY = 1, IsHitTestVisible = false },
        _ => new Rectangle { Width = 9, Height = 3, Fill = brush, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, -5, 0, 0), RadiusX = 1, RadiusY = 1, IsHitTestVisible = false },
    };

    private static UIElement FacingArrow(Facing f, Brush brush)
    {
        // A small triangle on the edge the operator faces.
        var tri = new Polygon { Fill = brush, Points = [new Point(0, 0), new Point(5, 3.5), new Point(0, 7)], IsHitTestVisible = false };
        var (h, vAlign, angle, margin) = f switch
        {
            Facing.Right => (HorizontalAlignment.Right, VerticalAlignment.Center, 0.0, new Thickness(0, 0, -4, 0)),
            Facing.Down => (HorizontalAlignment.Center, VerticalAlignment.Bottom, 90.0, new Thickness(0, 0, 0, -4)),
            Facing.Left => (HorizontalAlignment.Left, VerticalAlignment.Center, 180.0, new Thickness(-4, 0, 0, 0)),
            _ => (HorizontalAlignment.Center, VerticalAlignment.Top, 270.0, new Thickness(0, -4, 0, 0)),
        };
        tri.HorizontalAlignment = h;
        tri.VerticalAlignment = vAlign;
        tri.Margin = margin;
        tri.RenderTransformOrigin = new Point(0.5, 0.5);
        tri.RenderTransform = new RotateTransform(angle);
        tri.Width = 5;
        tri.Height = 7;
        return tri;
    }

    private int _firstCol, _lastCol, _rows;
    private bool Visible(BoardCell c) => c.Col >= _firstCol && c.Col <= _lastCol && c.Row >= 0 && c.Row < _rows;
    private double CellX(GarrisonMap map, int col) => L + (col - _firstCol) * S;

    private void Place(UIElement e, double x, double y)
    {
        Canvas.SetLeft(e, x);
        Canvas.SetTop(e, y);
        BoardCanvas.Children.Add(e);
    }

    private TextBlock Text(string text, double size, string brush, bool bold = false, bool center = false, double width = double.NaN,
        Thickness margin = default) => new()
    {
        Text = text,
        FontSize = size,
        FontWeight = bold ? FontWeights.Bold : FontWeights.Normal,
        Foreground = (Brush)FindResource(brush),
        TextAlignment = center ? TextAlignment.Center : TextAlignment.Left,
        Width = width,
        Margin = margin,
        VerticalAlignment = VerticalAlignment.Center,
        TextTrimming = TextTrimming.CharacterEllipsis,
    };

    private Border Pill(string text, string background, string border, string foreground) => new()
    {
        CornerRadius = (CornerRadius)FindResource("PillCornerRadius"),
        Background = (Brush)FindResource(background),
        BorderBrush = (Brush)FindResource(border),
        BorderThickness = new Thickness(1),
        Padding = new Thickness(6, 1, 6, 1),
        Margin = new Thickness(0, 0, 4, 4),
        Child = Text(text, 10, foreground),
    };

    private void RefreshBench()
    {
        RefreshTransforms();
        BenchRow.Children.Clear();
        var bench = _plan?.Bench ?? [];
        BenchRow.Visibility = bench.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        var stripe = (Brush)FindResource("TextSecondaryBrush");
        foreach (var b in bench)
        {
            string key = b.Name;
            bool pinned = _state.BenchPins.Contains(key);
            var card = OperatorCard(key, stripe, b.Note.Length > 0 ? ShortNote(b.Note) : "整备", stripe,
                (b.Note.Length > 0 ? b.Note + "\n" : "") + "拖到上面的在场干员上和他交换", pinned,
                pinned ? "已固定在整备区，点一下取消" : "固定在整备区（不会上场）",
                () => ToggleBenchPin(key), onBench: true);
            BenchRow.Children.Add(card);
        }
    }

    // ── Field / bench cards, pins and swapping ─────────────────────────────

    private IReadOnlyList<string> TransformsOf(string key) =>
        _state.Transforms.TryGetValue(key, out var t) ? t : [];

    /// <summary>
    /// Gives <paramref name="key"/> a (new) 转职 into <paramref name="covenant"/> and replans. One 转职 per operator: a new
    /// one replaces the old (暴躁铁皮 carries up to five). Any number of operators can hold the same 转职.
    /// </summary>
    private void AddTransform(string key, string covenant)
    {
        if (!_state.Transforms.TryGetValue(key, out var list)) _state.Transforms[key] = list = [];
        if (list.Contains(covenant)) return;
        int capacity = NameOf(key) == LineupEquipmentService.MechName ? 5 : 1;
        while (list.Count >= capacity) list.RemoveAt(0);
        list.Add(covenant);
        SaveState();
        _ = ReplanAsync();
    }

    /// <summary>Takes a 转职 off (the item sold with or taken from its holder) and replans.</summary>
    private void RemoveTransform(string key, string covenant)
    {
        if (!_state.Transforms.TryGetValue(key, out var list) || !list.Remove(covenant)) return;
        if (list.Count == 0) _state.Transforms.Remove(key);
        SaveState();
        _ = ReplanAsync();
    }

    /// <summary>
    /// The lineup's own 转职 (a tracked operator carrying 变形同构体 + a 转职 item) that an owned operator does not hold
    /// yet: (operator key, covenant).
    /// </summary>
    private List<(string Key, string Covenant)> LineupTransforms()
    {
        var list = new List<(string, string)>();
        foreach (var e in _tracking?.TrackedOperators ?? [])
        {
            if (!e.Equipments.Contains(LineupEquipmentService.Shapeshifter) || !_state.IsOwned(e.Name)) continue;
            foreach (var item in e.Equipments.Where(JobChangeCovenants.ContainsKey).Distinct())
            {
                string cov = JobChangeCovenants[item];
                if (!TransformsOf(e.Name).Contains(cov)) list.Add((e.Name, cov));
            }
        }
        return list;
    }

    /// <summary>A small 转职 tag (the cards in the 转职 row and the ones held on operator cards).</summary>
    private Border TransformTag(string text, string tip) => new()
    {
        CornerRadius = new CornerRadius(3),
        Background = (Brush)FindResource("EliteTintBrush"),
        BorderBrush = (Brush)FindResource("EliteBrush"), BorderThickness = new Thickness(1),
        Padding = new Thickness(4, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand, ToolTip = tip,
        Child = new TextBlock { Text = text, FontSize = 9, Foreground = (Brush)FindResource("EliteBrush") },
    };

    /// <summary>
    /// The 转职 row: small cards (炎转职, 奥术转职…) for the covenants worth a 转职 now: the plan's covenants one operator
    /// short, and the lineup's own 转职. Drag one onto an operator card to give it that 转职 (a new one, bought for it).
    /// </summary>
    private void RefreshTransforms()
    {
        TransformRow.Children.Clear();
        var why = new Dictionary<string, List<string>>();
        void Want(string cov, string reason)
        {
            if (!why.TryGetValue(cov, out var list)) why[cov] = list = [];
            if (!list.Contains(reason)) list.Add(reason);
        }
        foreach (var h in _plan?.TransformHints ?? [])
            Want(h.Covenant, $"现在 {h.Have}/{h.Need}，差 1 人激活（建议挂在 {Display(h.Holder)} 身上）");
        foreach (var (key, cov) in LineupTransforms())
            Want(cov, $"追踪的阵容里 {Display(key)} 挂了{cov}转职");
        foreach (var (cov, reasons) in why)
        {
            var tag = TransformTag(cov + "转职", string.Join("\n", reasons) + "\n拖到干员身上挂上（相当于给他新买一个；每人一个，会替换原来的，暴躁铁皮除外）");
            tag.Margin = new Thickness(0, 0, 5, 5);
            tag.Padding = new Thickness(8, 3, 8, 3);
            ((TextBlock)tag.Child).FontSize = 11;
            ((TextBlock)tag.Child).FontWeight = FontWeights.Bold;
            Point? start = null;
            tag.PreviewMouseLeftButtonDown += (_, e) => start = e.GetPosition(this);
            tag.PreviewMouseMove += (_, e) =>
            {
                if (e.LeftButton != MouseButtonState.Pressed || start is not { } s0) return;
                var now = e.GetPosition(this);
                if (Math.Abs(now.X - s0.X) < 5 && Math.Abs(now.Y - s0.Y) < 5) return;
                start = null;
                DragDrop.DoDragDrop(tag, new DataObject(DragFormat, "transform:" + cov), DragDropEffects.Copy);
            };
            TransformRow.Children.Add(tag);
        }
        TransformPanel.Visibility = TransformRow.Children.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private const string DragFormat = "aspa-deploy-key";
    private Point? _cardDragStart;

    /// <summary>
    /// One operator card (the on-field list and the 整备区 use the same one): avatar, name, covenants, a tag, and a pin
    /// that keeps the operator where it is listed. Cards can be dragged: a 整备区 card onto an on-field one swaps them.
    /// </summary>
    private Border OperatorCard(string key, Brush stripe, string tag, Brush tagBrush, string tooltip, bool pinned,
        string pinTip, Action togglePin, bool onBench, string? stats = null)
    {
        string name = NameOf(key);
        var card = new Border
        {
            Background = (Brush)FindResource("CardBackgroundBrush"),
            BorderBrush = (Brush)FindResource("LineBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = (CornerRadius)FindResource("RowCornerRadius"),
            Margin = new Thickness(0, 0, 4, 4),
            Height = 40,
            Cursor = Cursors.Hand,
            ClipToBounds = true,
            AllowDrop = true,
            Tag = key,
            Opacity = onBench ? 0.92 : 1,
        };
        // Hover: the operator's detail card, with this round's place, fight shares and reasons on top (built on open).
        var tip = new ToolTip
        {
            Placement = System.Windows.Controls.Primitives.PlacementMode.Left,
            Background = Brushes.Transparent, BorderThickness = new Thickness(0), Padding = new Thickness(0),
            HasDropShadow = false, MaxHeight = 700,
        };
        card.ToolTip = tip;
        ToolTipService.SetInitialShowDelay(card, 300);
        ToolTipService.SetBetweenShowDelay(card, 0);
        ToolTipService.SetShowDuration(card, 60000);
        HoverToolTipBehavior.Attach(card, tip);
        card.ToolTipOpening += (_, _) => tip.Content = DetailTip(key, tooltip, onBench);
        tip.Closed += (_, _) => tip.Content = null;
        var grid = new Grid();
        grid.Children.Add(new Rectangle { Width = 3, Fill = stripe, HorizontalAlignment = HorizontalAlignment.Left });
        var dock = new DockPanel { Margin = new Thickness(9, 0, 4, 0) };
        var opData = _operators.GetValueOrDefault(name);
        dock.Children.Add(new Avatar { Width = 24, Height = 24, Text = name, Source = opData?.IconPath, Small = true, VerticalAlignment = VerticalAlignment.Center });
        // The pin: filled when the operator is kept where it is listed.
        var pinDot = new Ellipse
        {
            Width = 9, Height = 9, StrokeThickness = 1.4,
            Fill = pinned ? PinBrush : Brushes.Transparent,
            Stroke = pinned ? PinBrush : (Brush)FindResource("TextSecondaryBrush"),
            Opacity = pinned ? 1 : 0.55,
        };
        var pinButton = new Border
        {
            Width = 20, Height = 28, Background = Brushes.Transparent, Cursor = Cursors.Hand, ToolTip = pinTip,
            VerticalAlignment = VerticalAlignment.Center, Child = pinDot,
        };
        pinButton.MouseLeftButtonDown += (_, e) => e.Handled = true;
        pinButton.MouseLeftButtonUp += (_, e) => { e.Handled = true; togglePin(); };
        DockPanel.SetDock(pinButton, Dock.Right);
        dock.Children.Add(pinButton);
        var tagBox = new Border
        {
            Background = Tint(tagBrush, 0.14), CornerRadius = new CornerRadius(3), Padding = new Thickness(4, 0, 4, 0),
            VerticalAlignment = VerticalAlignment.Center, MaxWidth = 64,
            Child = new TextBlock { Text = tag, FontSize = 9, Foreground = tagBrush, TextTrimming = TextTrimming.CharacterEllipsis },
        };
        DockPanel.SetDock(tagBox, Dock.Right);
        dock.Children.Add(tagBox);
        var body = new StackPanel { Margin = new Thickness(6, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
        var nameRow = new StackPanel { Orientation = Orientation.Horizontal };
        nameRow.Children.Add(Text(Display(key) + (_state.IsElite(name) ? " · 精" : ""), 11, "TextPrimaryBrush", bold: true));
        foreach (var cov in TransformsOf(key))
        {
            string c = cov;
            var held = TransformTag(c + "转职 ×", "点一下取下这个转职（卖掉或摘掉）");
            held.Margin = new Thickness(4, 0, 0, 0);
            held.MouseLeftButtonDown += (_, e) => e.Handled = true;
            held.MouseLeftButtonUp += (_, e) => { e.Handled = true; RemoveTransform(key, c); };
            nameRow.Children.Add(held);
        }
        body.Children.Add(nameRow);
        if (opData is not null)
            body.Children.Add(Text(string.Join(" · ", opData.CoreCovenants.Concat(opData.AdditionalCovenants)), 9.5, "TextSecondaryBrush"));
        dock.Children.Add(body);
        grid.Children.Add(dock);
        card.Child = grid;

        // Dragging: start once the mouse has moved a few pixels with the button down.
        card.PreviewMouseLeftButtonDown += (_, e) => _cardDragStart = e.GetPosition(this);
        card.PreviewMouseMove += (_, e) =>
        {
            if (e.LeftButton != MouseButtonState.Pressed || _cardDragStart is not { } start) return;
            var now = e.GetPosition(this);
            if (Math.Abs(now.X - start.X) < 6 && Math.Abs(now.Y - start.Y) < 6) return;
            _cardDragStart = null;
            card.Opacity = 0.5;
            DragDrop.DoDragDrop(card, new DataObject(DragFormat, (onBench ? "bench:" : "field:") + key), DragDropEffects.Move);
            card.Opacity = onBench ? 0.92 : 1;
        };
        card.MouseLeftButtonUp += (_, _) => _cardDragStart = null;
        // A 转职 card dropped on any operator card gives it that 转职; a 整备区 card dropped on an on-field one swaps them.
        bool Accepts(DragEventArgs e) => DraggedFrom(e) is ("transform", _) || !onBench && DraggedFrom(e) is ("bench", _);
        card.DragEnter += (_, e) => { if (Accepts(e)) card.BorderBrush = PinBrush; };
        card.DragLeave += (_, _) => card.BorderBrush = (Brush)FindResource("LineBrush");
        card.DragOver += (_, e) => { e.Effects = Accepts(e) ? DragDropEffects.Copy | DragDropEffects.Move : DragDropEffects.None; e.Handled = true; };
        card.Drop += (_, e) =>
        {
            card.BorderBrush = (Brush)FindResource("LineBrush");
            switch (DraggedFrom(e))
            {
                case ("transform", { } cov): e.Handled = true; AddTransform(key, cov); break;
                case ("bench", { } up) when !onBench: e.Handled = true; Swap(up, key); break;
            }
        };
        return card;
    }

    /// <summary>
    /// The hover card of an operator: this round (where it stands, its share of the fight's damage dealt / taken /
    /// healing as bars, its 转职, the reasons for its spot), then the operator's own detail card.
    /// </summary>
    private Border DetailTip(string key, string reasons, bool onBench)
    {
        var stack = new StackPanel();
        var round = new StackPanel();
        var placed = _plan?.Find(key);
        string where = placed is not null ? $"在场 · {placed.CellCode} 朝{PlacementCells.FacingText(placed.Facing)}" : "整备区";
        var title = Text($"{Display(key)} · {where}", 12, "TextPrimaryBrush", bold: true);
        title.Margin = new Thickness(0, 0, 0, 4);
        round.Children.Add(title);
        if (!onBench && _plan?.Shares.TryGetValue(key, out var share) == true && share is not null)
        {
            void Bar(string label, double value, string brush)
            {
                var row = new DockPanel { Margin = new Thickness(0, 1, 0, 1) };
                row.Children.Add(Text(label, 10.5, "TextSecondaryBrush", width: 34));
                var pct = Text($"{value:P0}", 10.5, "TextPrimaryBrush", bold: true, width: 40);
                pct.TextAlignment = TextAlignment.Right;
                DockPanel.SetDock(pct, Dock.Right);
                row.Children.Add(pct);
                var track = new Grid { Height = 6, VerticalAlignment = VerticalAlignment.Center, Width = 180, HorizontalAlignment = HorizontalAlignment.Left };
                track.Children.Add(new Border { Background = (Brush)FindResource("SectionBackgroundBrush"), CornerRadius = new CornerRadius(3) });
                track.Children.Add(new Border
                {
                    Background = (Brush)FindResource(brush), CornerRadius = new CornerRadius(3),
                    Width = Math.Max(0, Math.Min(1, value)) * 180, HorizontalAlignment = HorizontalAlignment.Left,
                });
                row.Children.Add(track);
                round.Children.Add(row);
            }
            Bar("输出", share.Damage, "DangerBrush");
            Bar("承伤", share.Taken, "AccentBrush");
            Bar("治疗", share.Healing, "SuccessTextBrush");
            var hint = Text("占全队的比例（模拟战斗，按本回合可能的敌人加权）", 9.5, "TextSecondaryBrush");
            hint.Margin = new Thickness(0, 2, 0, 0);
            round.Children.Add(hint);
        }
        if (TransformsOf(key) is { Count: > 0 } held)
        {
            var t = Text("转职：" + string.Join("、", held), 10.5, "EliteBrush", bold: true);
            t.Margin = new Thickness(0, 4, 0, 0);
            round.Children.Add(t);
        }
        foreach (var line in reasons.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var r = Text("· " + line, 10.5, "TextPrimaryBrush");
            r.TextWrapping = TextWrapping.Wrap;
            r.MaxWidth = 330;
            r.Margin = new Thickness(0, 2, 0, 0);
            round.Children.Add(r);
        }
        stack.Children.Add(new Border
        {
            Background = (Brush)FindResource("CardBackgroundBrush"), CornerRadius = (CornerRadius)FindResource("RowCornerRadius"),
            BorderBrush = (Brush)FindResource("LineBrush"), BorderThickness = new Thickness(1),
            Padding = new Thickness(9, 7, 9, 7), Margin = new Thickness(0, 0, 0, 6), Child = round,
        });
        if (_operators.GetValueOrDefault(NameOf(key)) is { } op)
            stack.Children.Add(new ContentControl { Content = op, ContentTemplate = (DataTemplate)FindResource("OperatorDetailsTemplate") });
        return new Border
        {
            Background = (Brush)FindResource("PanelBackgroundBrush"), CornerRadius = (CornerRadius)FindResource("CardCornerRadius"),
            BorderBrush = (Brush)FindResource("BorderBrush"), BorderThickness = new Thickness(1), Padding = new Thickness(6),
            Child = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled, Content = stack },
        };
    }

    /// <summary>"输出 23% · 承伤 40% · 治疗 60%" from the simulated fight (parts under 1 % left out; null: no fight).</summary>
    private string? ShareText(string key)
    {
        if (_plan?.Shares.TryGetValue(key, out var s) != true || s is null) return null;
        var parts = new List<string> { $"输出 {s.Damage:P0}" };
        if (s.Taken >= 0.01) parts.Add($"承伤 {s.Taken:P0}");
        if (s.Healing >= 0.01) parts.Add($"治疗 {s.Healing:P0}");
        return string.Join(" · ", parts);
    }

    private static (string From, string Key)? DraggedFrom(DragEventArgs e) =>
        e.Data.GetData(DragFormat) is string s && s.IndexOf(':') is int i and > 0 ? (s[..i], s[(i + 1)..]) : null;

    /// <summary>
    /// A 整备区 operator dragged onto an on-field one: it goes on (kept on the field) and the other comes off (kept in
    /// the 整备区), then the plan is recomputed around them.
    /// </summary>
    private void Swap(string up, string down)
    {
        _pins.Remove(down);
        _state.FieldPins.Remove(down);
        _state.BenchPins.Remove(up);
        if (!_state.FieldPins.Contains(up)) _state.FieldPins.Add(up);
        if (!_state.BenchPins.Contains(down)) _state.BenchPins.Add(down);
        SaveState();
        _ = ReplanAsync();
    }

    /// <summary>
    /// Keeps an on-field operator on the field, or lets it go again (its cell pin included); letting go replans, since
    /// the plan may now do better without it.
    /// </summary>
    private void ToggleFieldPin(string key)
    {
        bool released = _state.FieldPins.Remove(key) | _pins.Remove(key);
        if (!released)
        {
            _state.FieldPins.Add(key);
            _state.BenchPins.Remove(key);
        }
        SaveState();
        if (released) _ = ReplanAsync(); else RefreshAll();
    }

    /// <summary>Keeps a 整备区 operator off the field, or lets the plan use it again (which replans).</summary>
    private void ToggleBenchPin(string key)
    {
        bool released = _state.BenchPins.Remove(key);
        if (!released)
        {
            _state.BenchPins.Add(key);
            _state.FieldPins.Remove(key);
        }
        SaveState();
        if (released) _ = ReplanAsync(); else RefreshAll();
    }

    private void OnFieldAreaDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DraggedFrom(e) is ("bench", _) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>A 整备区 operator dropped on the on-field list (not on a card): it goes on, the plan picks who comes off.</summary>
    private void OnFieldAreaDrop(object sender, DragEventArgs e)
    {
        if (DraggedFrom(e) is not ("bench", { } up)) return;
        _state.BenchPins.Remove(up);
        if (!_state.FieldPins.Contains(up)) _state.FieldPins.Add(up);
        SaveState();
        _ = ReplanAsync();
    }

    private void OnBenchAreaDragOver(object sender, DragEventArgs e)
    {
        e.Effects = DraggedFrom(e) is ("field", _) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    /// <summary>An on-field operator dropped on the 整备区: it comes off (kept there), the plan picks who goes on.</summary>
    private void OnBenchAreaDrop(object sender, DragEventArgs e)
    {
        if (DraggedFrom(e) is not ("field", { } down)) return;
        _pins.Remove(down);
        _state.FieldPins.Remove(down);
        if (!_state.BenchPins.Contains(down)) _state.BenchPins.Add(down);
        SaveState();
        _ = ReplanAsync();
    }

    private static string ShortNote(string note) =>
        note.StartsWith("在整备区也生效") ? "整备区生效" : note.StartsWith("整备区也算") ? "凑盟约" : note.Length > 6 ? note[..6] : note;

    private static Brush Tint(Brush brush, double opacity)
    {
        var c = brush is SolidColorBrush sc ? sc.Color : Colors.Gray;
        var b = new SolidColorBrush(Color.FromArgb((byte)(255 * opacity), c.R, c.G, c.B));
        b.Freeze();
        return b;
    }

    private void RefreshPlanList()
    {
        ChecksRow.Children.Clear();
        OperatorGrid.Children.Clear();
        bool show = _plan is not null && _selected is null;
        PlanPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        // Under the board only the stacking forecast; the rest of the checks stay in the plan (cards, tooltips).
        var shownChecks = _plan?.Checks.Where(c => c.Detail?.StartsWith("预计每回合叠层") == true).ToList() ?? [];
        ChecksBorder.Visibility = shownChecks.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        BenchCard.Visibility = _selected is null ? Visibility.Visible : Visibility.Collapsed;
        if (_plan is null)
        {
            if (StatusText.Visibility != Visibility.Visible)
                SetStatus(Candidates().Count == 0 ? "干员池里还没有已拥有的干员：点下方“干员池 ›”，把手上的干员点亮。" : null);
            return;
        }
        foreach (var c in shownChecks)
        {
            // Hard requirements: ✓ / ✗. Hints (e.g. 蓝箭头): neutral, never red.
            (string sign, string fg, string? bg, string? border) = c.IsHint ? ("i", "TextSecondaryBrush", (string?)null, (string?)"BorderBrush")
                : c.Ok ? ("✓", "SuccessTextBrush", "SectionBackgroundBrush", null) : ("✗", "DangerBrush", "DangerTintBrush", null);
            var tb = new TextBlock { FontSize = 10, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource(c.Ok || c.IsHint ? "TextSecondaryBrush" : "DangerBrush") };
            tb.Inlines.Add(new System.Windows.Documents.Run(sign + " ") { FontWeight = FontWeights.Black, Foreground = (Brush)FindResource(fg) });
            tb.Inlines.Add(new System.Windows.Documents.Run(c.Text));
            ChecksRow.Children.Add(new Border
            {
                CornerRadius = new CornerRadius(3), Padding = new Thickness(6, 1, 6, 1), Margin = new Thickness(0, 0, 4, 3),
                Background = bg is null ? Brushes.Transparent : (Brush)FindResource(bg),
                BorderBrush = border is null ? Brushes.Transparent : (Brush)FindResource(border), BorderThickness = new Thickness(border is null ? 0 : 1),
                Child = tb,
                ToolTip = c.Detail ?? (c.IsHint ? "提示，不是硬要求" : null),
            });
        }
        if (!show) return;
        bool animateCards = IsVisible && !ReferenceEquals(_listedPlan, _plan);
        foreach (var p in _plan.Placed)
        {
            var brush = RoleBrush(p.Role);
            string key = p.Key;
            bool cellPinned = _pins.ContainsKey(key), pinned = cellPinned || _state.FieldPins.Contains(key);
            var card = OperatorCard(key, brush, PlacementRoleText.Of(p.Role), brush,
                string.Join("\n", p.Notes.DefaultIfEmpty("点开看站位理由")), pinned,
                cellPinned ? "已固定位置，点一下取消" : pinned ? "已固定在场，点一下取消" : "固定在场（位置仍由规划决定）",
                () => ToggleFieldPin(key), onBench: false, stats: ShareText(key));
            card.MouseLeftButtonUp += (_, _) => Select(key);
            OperatorGrid.Children.Add(card);
            if (animateCards) RiseIn(card, OperatorGrid.Children.Count);
        }
        _listedPlan = _plan;
    }

    private void Select(string? name)
    {
        _selected = name;
        RefreshAll();
    }

    private void RefreshDetail()
    {
        DetailBody.Children.Clear();
        if (_selected is not { } sel || _plan?.Find(sel) is not { } p || !_working.TryGetValue(sel, out var v))
        {
            _shownDetail = null;
            DetailCard.Visibility = Visibility.Collapsed;
            return;
        }
        DetailCard.Visibility = Visibility.Visible;
        if (_shownDetail != sel && IsVisible) PageTransition.SlideIn(DetailCard, fromY: 8);
        _shownDetail = sel;
        var op = _operators.GetValueOrDefault(NameOf(sel));
        var rec = _recommended?.Find(sel);
        bool moved = _dirty && rec is not null && (rec.Cell != v.Cell || rec.Facing != v.Facing);

        var head = new DockPanel();
        head.Children.Add(new Avatar { Width = 36, Height = 36, Text = p.Name, Source = op?.IconPath, Ring = RoleBrush(p.Role) });
        if (_dirty && _recommended is not null && _plan is not null)
        {
            double delta = Math.Round(_plan.Total - _recommended.Total, 1);
            var d = Text(delta >= 0 ? $"+{delta:0.#}" : $"{delta:0.#}", 13, delta >= 0 ? "SuccessTextBrush" : "DangerBrush", bold: true);
            d.ToolTip = "和推荐方案的总分差";
            DockPanel.SetDock(d, Dock.Right);
            head.Children.Add(d);
        }
        var titles = new StackPanel { Margin = new Thickness(9, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center };
        var line1 = new StackPanel { Orientation = Orientation.Horizontal };
        line1.Children.Add(Text(Display(sel), 13, "TextPrimaryBrush", bold: true));
        line1.Children.Add(new Border
        {
            BorderBrush = RoleBrush(p.Role), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(3),
            Padding = new Thickness(4, 0, 4, 0), Margin = new Thickness(6, 0, 0, 0), VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = PlacementRoleText.Of(p.Role), FontSize = 9, Foreground = RoleBrush(p.Role) },
        });
        if (op?.Profile is { } prof)
            line1.Children.Add(Text($"{prof.Profession} · {op.Tier} 阶" + (_state.IsElite(NameOf(sel)) ? " · 精锐" : ""), 10, "TextSecondaryBrush", margin: new Thickness(6, 0, 0, 0)));
        titles.Children.Add(line1);
        string where = $"{PlacementCells.Code(v.Cell)} · {PlacementCells.FacingText(v.Facing)}";
        titles.Children.Add(Text(moved ? $"拖到 {where} · 推荐位置是 {rec!.CellCode}" : where + (_pins.ContainsKey(sel) ? " · 已固定" : ""),
            11, "TextSecondaryBrush", margin: new Thickness(0, 3, 0, 0)));
        head.Children.Add(titles);
        DetailBody.Children.Add(head);

        DetailBody.Children.Add(new Border
        {
            Background = (Brush)FindResource("SectionBackgroundBrush"),
            CornerRadius = (CornerRadius)FindResource("RowCornerRadius"),
            Padding = new Thickness(8, 6, 8, 6),
            Margin = new Thickness(0, 8, 0, 0),
            Child = new TextBlock
            {
                Text = "在小地图上拖动干员换位置。点「固定在这里」后，其余没固定的干员会以此为前提重新排。",
                FontSize = 10.5, TextWrapping = TextWrapping.Wrap, Foreground = (Brush)FindResource("TextSecondaryBrush"),
            },
        });

        DetailBody.Children.Add(Text(moved ? $"放在 {PlacementCells.Code(v.Cell)} 会怎样" : "为什么站这里", 11, "TextPrimaryBrush", bold: true, margin: new Thickness(0, 10, 0, 3)));
        foreach (var note in p.Notes.DefaultIfEmpty("这个位置没有特别的加成或问题"))
            DetailBody.Children.Add(new TextBlock
            {
                Text = "· " + note, FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 1, 0, 1),
                Foreground = (Brush)FindResource("TextPrimaryBrush"),
            });

        if (_planner is not null && CurrentMap is { } map)
        {
            var alts = _planner.Alternatives(map, Candidates(), _working, sel, Options());
            if (alts.Count > 0)
            {
                DetailBody.Children.Add(Text("其他位置", 11, "TextPrimaryBrush", bold: true, margin: new Thickness(0, 10, 0, 2)));
                foreach (var a in alts)
                {
                    var row = new DockPanel { Margin = new Thickness(0, 2, 0, 2), Cursor = Cursors.Hand, Background = Brushes.Transparent, ToolTip = "移到这里" };
                    var code = new Border
                    {
                        Background = (Brush)FindResource("SectionBackgroundBrush"), CornerRadius = new CornerRadius(3),
                        Padding = new Thickness(5, 0, 5, 0), Margin = new Thickness(0, 0, 7, 0),
                        Child = Text(a.CellCode, 11, "TextPrimaryBrush", bold: true),
                    };
                    row.Children.Add(code);
                    var delta = Text(a.Delta >= 0 ? $"+{a.Delta:0.#}" : $"{a.Delta:0.#}", 11, a.Delta >= 0 ? "SuccessTextBrush" : "DangerBrush", bold: true);
                    DockPanel.SetDock(delta, Dock.Right);
                    row.Children.Add(delta);
                    row.Children.Add(Text($"{PlacementCells.FacingText(a.Facing)} · {a.Text}", 11, "TextPrimaryBrush"));
                    var target = a;
                    row.MouseLeftButtonUp += (_, _) => MoveSelected(target.Cell, target.Facing);
                    DetailBody.Children.Add(row);
                }
            }
        }

        var buttons = new WrapPanel { Margin = new Thickness(0, 10, 0, 0) };
        Button B(string text, RoutedEventHandler click, bool accent = false)
        {
            var b = new Button { Content = text, Margin = new Thickness(0, 0, 6, 4), Style = (Style)FindResource("SmallActionButton") };
            if (accent) b.Tag = "accent";
            b.Click += click;
            buttons.Children.Add(b);
            return b;
        }
        if (_pins.ContainsKey(sel) && !moved) B("取消固定", (_, _) => { _pins.Remove(sel); _ = ReplanAsync(); });
        else B("固定在这里", (_, _) => PinSelected(), accent: true);
        B("换朝向", (_, _) => RotateSelected());
        var transformButton = B("挂转职 ▾", (_, _) => { });
        transformButton.ToolTip = "给这名干员挂一个新的转职（变形同构体 + 转职装备），会替换他原来的转职（暴躁铁皮最多 5 个）；转职跟着他上下场";
        transformButton.Click += (_, _) =>
        {
            var have = _operators.GetValueOrDefault(NameOf(sel)) is { } od ? od.CoreCovenants.Concat(od.AdditionalCovenants).Concat(TransformsOf(sel)).ToHashSet() : [];
            var menu = new ContextMenu { PlacementTarget = transformButton };
            foreach (var cov in JobChangeCovenants.Values.Distinct().Where(c => !have.Contains(c)).OrderBy(c => c, StringComparer.Ordinal))
            {
                var item = new MenuItem { Header = cov };
                string c = cov;
                item.Click += (_, _) => AddTransform(sel, c);
                menu.Items.Add(item);
            }
            menu.IsOpen = menu.Items.Count > 0;
        };
        if (_dirty) B("撤销", (_, _) => Undo());
        B("返回", (_, _) => Select(null));
        DetailBody.Children.Add(buttons);

        if (_pins.Count > 0)
        {
            var pinned = new DockPanel { Margin = new Thickness(0, 6, 0, 0) };
            var clear = new Button { Content = "全部取消", Style = (Style)FindResource("LinkButton") };
            clear.Click += (_, _) => { _pins.Clear(); _ = ReplanAsync(); };
            DockPanel.SetDock(clear, Dock.Right);
            pinned.Children.Add(clear);
            pinned.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = PinBrush, Margin = new Thickness(0, 0, 6, 0) });
            pinned.Children.Add(Text($"已固定 {_pins.Count} 人：" + string.Join("、", _pins.Select(kv => $"{Display(kv.Key)} {PlacementCells.Code(kv.Value.Cell)}")), 11, "TextSecondaryBrush"));
            DetailBody.Children.Add(new Border { BorderBrush = (Brush)FindResource("LineBrush"), BorderThickness = new Thickness(0, 1, 0, 0), Margin = new Thickness(0, 4, 0, 0), Child = pinned });
        }
    }

    // ── Detail actions ─────────────────────────────────────────────────────

    private void MoveSelected(BoardCell cell, Facing facing)
    {
        if (_selected is not { } sel || !_working.ContainsKey(sel)) return;
        MoveOperator(sel, cell, facing);
    }

    private void MoveOperator(string name, BoardCell cell, Facing? facing = null)
    {
        if (CurrentMap is not { } map || _planner is null || !_working.TryGetValue(name, out var cur)) return;
        if (!GarrisonMap.CanDeploy(map.At(cell), _planner.IsMelee(NameOf(name)))) return;
        var other = _working.FirstOrDefault(kv => kv.Key != name && kv.Value.Cell == cell).Key;
        if (other is not null)
        {
            if (_pins.ContainsKey(other) || !GarrisonMap.CanDeploy(map.At(cur.Cell), _planner.IsMelee(NameOf(other)))) return;
            _working[other] = (cur.Cell, _working[other].Facing);
        }
        _working[name] = (cell, facing ?? cur.Facing);
        _selected = name;
        _dirty = true;
        Reevaluate();
    }

    private void RotateSelected()
    {
        if (_selected is not { } sel || !_working.TryGetValue(sel, out var v)) return;
        _working[sel] = (v.Cell, (Facing)(((int)v.Facing + 1) % 4));
        _dirty = true;
        if (_pins.ContainsKey(sel)) _pins[sel] = new PlacementPin(v.Cell, _working[sel].Facing);
        Reevaluate();
    }

    private void PinSelected()
    {
        if (_selected is not { } sel || !_working.TryGetValue(sel, out var v)) return;
        _pins[sel] = new PlacementPin(v.Cell, v.Facing);
        _pinsMap = CurrentMap?.Id;
        _ = ReplanAsync();
    }

    private void Undo()
    {
        if (_recommended is null) return;
        _working = _recommended.Placed.ToDictionary(p => p.Key, p => (p.Cell, p.Facing));
        _plan = _recommended;
        _dirty = false;
        RefreshAll();
        PublishPlan();
    }

    // ── Dragging on the mini board ─────────────────────────────────────────

    private void OnOperatorMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not string name) return;
        _dragName = name;
        _dragElement = fe;
        _dragStart = e.GetPosition(BoardCanvas);
        _dragMoved = false;
        BoardCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void OnBoardMouseMove(object sender, MouseEventArgs e)
    {
        if (_dragName is null || _dragElement is null || e.LeftButton != MouseButtonState.Pressed) return;
        var p = e.GetPosition(BoardCanvas);
        if (!_dragMoved && (p - _dragStart).Length < 3) return;
        if (!_dragMoved)
        {
            _dragElement.BeginAnimation(Canvas.LeftProperty, null);
            _dragElement.BeginAnimation(Canvas.TopProperty, null);
            var lift = new ScaleTransform(1, 1);
            _dragElement.RenderTransformOrigin = new Point(0.5, 0.5);
            _dragElement.RenderTransform = lift;
            var up = new DoubleAnimation(1.18, TimeSpan.FromMilliseconds(120)) { EasingFunction = Ease };
            lift.BeginAnimation(ScaleTransform.ScaleXProperty, up);
            lift.BeginAnimation(ScaleTransform.ScaleYProperty, up);
        }
        _dragMoved = true;
        Canvas.SetLeft(_dragElement, p.X - (S - 2) / 2);
        Canvas.SetTop(_dragElement, p.Y - (S - 2) / 2);
        Panel.SetZIndex(_dragElement, 10);
        _dragElement.Opacity = 0.85;
    }

    private void OnBoardMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_dragName is not { } name) return;
        BoardCanvas.ReleaseMouseCapture();
        var p = e.GetPosition(BoardCanvas);
        bool moved = _dragMoved;
        _dragName = null;
        _dragElement = null;
        if (!moved) { Select(_selected == name ? null : name); return; }
        if (CurrentMap is not { } map) return;
        var cell = new BoardCell((int)Math.Floor((p.X - L) / S) + _firstCol, (int)Math.Floor((p.Y - T) / S));
        _lastPos[name] = new Point(p.X - (S - 2) / 2, p.Y - (S - 2) / 2);   // animate from the drop point
        var old = _working.GetValueOrDefault(name);
        bool wasPinned = _pins.Remove(name);
        MoveOperator(name, cell);
        if (_working.GetValueOrDefault(name) == old)
        {
            // Illegal drop (blocked cell, high ground for melee, a fixed operator): snap back.
            if (wasPinned) _pins[name] = new PlacementPin(old.Cell, old.Facing);
            _selected = name;
            RefreshAll();
        }
    }

    // ── Header controls ────────────────────────────────────────────────────

    private bool _suppressSelection;

    private void OnModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection || !_ready || ModeList.SelectedIndex < 0) return;
        _state.Mode = (PlacementMode)ModeList.SelectedIndex;
        SaveState();
        ScheduleReplan();
    }

    private void OnFlyingChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection || !_ready || FlyingList.SelectedIndex < 0) return;
        _state.ExpectFlying = FlyingList.SelectedIndex == 0;
        _flyingOverride = _state.ExpectFlying;
        SaveState();
        ScheduleReplan();
    }

    private void OnFieldCountChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection || !_ready || FieldCountList.SelectedIndex < 0) return;
        _state.FieldCount = FieldCountList.SelectedIndex == 1 ? 9 : 8;
        SaveState();
        ScheduleReplan();
    }

    private void OnRecompute(object sender, RoutedEventArgs e)
    {
        _dirty = false;
        _ = ReplanAsync();
    }

    private void OnInGameToggled(object sender, RoutedEventArgs e)
    {
        if (!_ready) return;
        ShowInGameChanged?.Invoke(ShowInGame);
        PublishPlan();
    }

    private void OnOpenPool(object sender, RoutedEventArgs e)
    {
        _page = Page.Pool;
        RefreshAll();
        Scroller.ScrollToTop();
    }

    private void OnBackToMain(object sender, RoutedEventArgs e)
    {
        _page = Page.Main;
        _pickingMap = false;
        RefreshAll();
        Scroller.ScrollToTop();
    }

    private void OnPickMap(object sender, RoutedEventArgs e)
    {
        _pickingMap = true;
        RefreshAll();
    }

    private void OnFollowRecognition(object sender, RoutedEventArgs e)
    {
        _state.ManualMap = null;
        _pickingMap = false;
        SaveState();
        OnMapChanged();
    }

    // ── No-map page ────────────────────────────────────────────────────────

    private static readonly string[] MapOrder =
        ["desert_crates", "desert_camo", "desert_steel", "desert_laser", "desert_cactus", "palace", "palace_arrows", "snow", "snow_ice", "city"];

    private void BuildMapChoices()
    {
        MapChoices.Children.Clear();
        if (_board is null) return;
        foreach (var map in MapOrder.Select(id => _board.Find(id)).Where(m => m is not null).Concat(_board.Maps.Where(m => !MapOrder.Contains(m.Id))).Distinct())
        {
            var m = map!;
            var stack = new StackPanel();
            var img = new Image { Stretch = Stretch.UniformToFill, Height = 64 };
            try { img.Source = new BitmapImage(new Uri($"pack://application:,,,/Resources/MapThumbs/{m.Id}.jpg")); }
            catch (Exception) { /* missing thumbnail: name only */ }
            stack.Children.Add(new Border { CornerRadius = new CornerRadius(3), ClipToBounds = true, Child = img, Background = (Brush)FindResource("SectionBackgroundBrush") });
            stack.Children.Add(Text(m.Name, 11, "TextPrimaryBrush", margin: new Thickness(0, 4, 0, 0)));
            var button = new Button
            {
                Content = stack,
                Margin = new Thickness(0, 0, 6, 6),
                Padding = new Thickness(5),
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                Style = (Style)FindResource("SmallActionButton"),
                Height = double.NaN,
                Tag = m.Id,
                ToolTip = m.Name,
            };
            button.Click += (_, _) =>
            {
                _state.ManualMap = m.Id;
                _pickingMap = false;
                SaveState();
                OnMapChanged();
            };
            MapChoices.Children.Add(button);
        }
    }

    private void RefreshNoMap()
    {
        bool hasMap = CurrentMap is not null;
        NoMapTitle.Text = hasMap ? "换一张地图" : "还没认出当前地图";
        NoMapHint.Text = hasMap ? "选一张地图手动规划；识别到的地图会被手动选择覆盖，直到改回自动识别。"
            : "进入休整期、能看到完整棋盘时会自动识别。也可以先手动选：";
        NoMapBack.Visibility = hasMap ? Visibility.Visible : Visibility.Collapsed;
        FollowRecognitionButton.Visibility = _state.ManualMap is not null ? Visibility.Visible : Visibility.Collapsed;
    }

    // ── Pool page ──────────────────────────────────────────────────────────

    private void RefreshPool()
    {
        PoolGroups.Children.Clear();
        var groups = Groups();
        int owned = Candidates().Count;
        ClearPoolButton.IsEnabled = !_state.IsPoolEmpty;
        PoolHeaderMeta.Text = "";
        PoolSearchHint.Visibility = _poolQuery.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        var extItems = groups.Where(g => g.Kind == DeployPoolGroupKind.Extension).SelectMany(g => g.Items).ToList();
        if (_activeTier is null || !extItems.Any(i => i.Tier == _activeTier))
            // Start on a tier that already holds an owned operator, else the lowest tier with anyone in it.
            _activeTier = extItems.Where(i => i.Owned).Select(i => (int?)i.Tier).Min() ?? extItems.Select(i => (int?)i.Tier).Min();
        if (groups.Count == 0)
        {
            PoolGroups.Children.Add(new TextBlock
            {
                Text = "还没有追踪的干员。先在阵容或规划页点“追踪”，也可以直接用“＋ 添加”。",
                FontSize = 11, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(2, 4, 5, 8),
                Foreground = (Brush)FindResource("TextSecondaryBrush"),
            });
            return;
        }
        bool searching = _poolQuery.Length > 0;
        bool Match(DeployPoolItem i) =>
            (!_ownedOnly || i.Owned)
            && (!searching || i.Name.Contains(_poolQuery, StringComparison.OrdinalIgnoreCase) || i.Sub.Contains(_poolQuery, StringComparison.OrdinalIgnoreCase));
        int shown = 0;
        foreach (var g in groups)
        {
            var items = g.Items.Where(Match).ToList();
            if (items.Count == 0) continue;
            shown += items.Count;
            var header = new DockPanel { Margin = new Thickness(2, PoolGroups.Children.Count == 0 ? 2 : 10, 2, 5) };
            var count = Text($"{g.Items.Count(i => i.Owned)}/{g.Items.Count}", 11, "AccentBrush", bold: true);
            count.FontFamily = (FontFamily)FindResource("NumberFontFamily");
            DockPanel.SetDock(count, Dock.Right);
            header.Children.Add(count);
            header.Children.Add(Text(g.Title, 11.5, "TextPrimaryBrush", bold: true));
            header.Children.Add(Text(g.Sub, 10, "TextSecondaryBrush", margin: new Thickness(6, 0, 4, 0)));
            PoolGroups.Children.Add(header);
            if (g.Kind != DeployPoolGroupKind.Extension)
            {
                AddCards(items);
                continue;
            }
            // 扩展池: a rail of tier tiles (people per tier, a dot when one is owned); each tile unfolds its tier.
            if (!searching && !_ownedOnly)
            {
                var rail = new UniformGrid { Columns = 6, Margin = new Thickness(0, 0, 0, 4) };
                for (int tier = 1; tier <= 6; tier++)
                {
                    var inTier = g.Items.Where(i => i.Tier == tier).ToList();
                    rail.Children.Add(TierTile(tier, inTier.Count, inTier.Any(i => i.Owned)));
                }
                PoolGroups.Children.Add(rail);
            }
            foreach (var band in items.GroupBy(i => i.Tier).OrderBy(b => b.Key))
            {
                if (!searching && !_ownedOnly && band.Key != _activeTier) continue;
                var bandHead = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 4, 2, 4) };
                bandHead.Children.Add(TierBadge(band.Key));
                bandHead.Children.Add(Text($"{band.Count()} 人" + (searching ? "" : " · 共享盟约多的在前"), 10, "TextSecondaryBrush", margin: new Thickness(6, 0, 0, 0)));
                PoolGroups.Children.Add(bandHead);
                AddCards([.. band], animate: band.Key == _justOpenedTier);
            }
        }
        if (shown == 0)
            PoolGroups.Children.Add(Text(searching ? $"没有找到“{_poolQuery}”，可以用“＋ 添加”加入其他干员" : "还没有已拥有的干员", 11, "TextSecondaryBrush", margin: new Thickness(2, 6, 0, 0)));
    }

    private void AddCards(IReadOnlyList<DeployPoolItem> items, bool animate = false)
    {
        var grid = new UniformGrid { Columns = 2 };
        foreach (var item in items)
        {
            var card = (FrameworkElement)PoolCard(item);
            grid.Children.Add(card);
            if (animate) RiseIn(card, grid.Children.Count, dy: 4);
        }
        PoolGroups.Children.Add(grid);
    }

    private Border TierBadge(int tier)
    {
        string fg = tier >= 5 ? "EliteBrush" : tier == 4 ? "AccentBrush" : "TextSecondaryBrush";
        string bg = tier >= 5 ? "EliteTintBrush" : tier == 4 ? "AccentSoftBrush" : "SectionBackgroundBrush";
        var t = Text($"{tier} 阶", 11, fg, bold: true);
        t.FontFamily = (FontFamily)FindResource("NumberFontFamily");
        return new Border { CornerRadius = new CornerRadius(4), Padding = new Thickness(7, 1, 7, 1), Background = (Brush)FindResource(bg), Child = t };
    }

    private static readonly string[] Roman = ["", "Ⅰ", "Ⅱ", "Ⅲ", "Ⅳ", "Ⅴ", "Ⅵ"];

    private UIElement TierTile(int tier, int count, bool hasOwned)
    {
        bool open = tier == _activeTier;
        var stack = new StackPanel { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
        var num = Text(Roman[tier], 14, open ? "OnAccentBrush" : tier >= 5 ? "EliteBrush" : tier == 4 ? "AccentBrush" : "TextPrimaryBrush", bold: true, center: true);
        num.FontFamily = (FontFamily)FindResource("NumberFontFamily");
        stack.Children.Add(num);
        stack.Children.Add(Text($"{count} 人", 9, open ? "OnAccentBrush" : "TextSecondaryBrush", center: true));
        var grid = new Grid();
        grid.Children.Add(stack);
        if (hasOwned)
            grid.Children.Add(new Ellipse { Width = 5, Height = 5, Fill = (Brush)FindResource(open ? "OnAccentBrush" : "AccentBrush"), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 5, 0) });
        var tile = new Border
        {
            Height = 38, Margin = new Thickness(0, 0, 4, 0), Cursor = count > 0 ? Cursors.Hand : Cursors.Arrow,
            CornerRadius = (CornerRadius)FindResource("RowCornerRadius"), BorderThickness = new Thickness(1),
            Background = (Brush)FindResource(open ? "AccentSolidBrush" : "CardBackgroundBrush"),
            BorderBrush = (Brush)FindResource(open ? "AccentSolidBrush" : "BorderBrush"),
            Opacity = count == 0 ? 0.45 : 1,
            Child = grid,
            ToolTip = count == 0 ? "这一阶没有" : $"看 {tier} 阶",
        };
        if (count > 0)
            tile.MouseLeftButtonUp += (_, _) =>
            {
                if (_activeTier == tier) return;
                _activeTier = tier;
                _justOpenedTier = tier;
                RefreshPool();
                _justOpenedTier = null;
            };
        return tile;
    }

    private UIElement PoolCard(DeployPoolItem item)
    {
        var root = new Grid { Margin = new Thickness(0, 0, 4, 4), Cursor = Cursors.Hand, Height = 40, Background = Brushes.Transparent };
        root.Children.Add(new Border
        {
            CornerRadius = (CornerRadius)FindResource("RowCornerRadius"), BorderThickness = new Thickness(1),
            BorderBrush = (Brush)FindResource(item.Owned ? "AccentBorderBrush" : "LineBrush"),
            Background = item.Owned ? (Brush)FindResource("AccentSoftBrush") : Brushes.Transparent,
        });
        var dock = new DockPanel { Margin = new Thickness(7, 0, 6, 0) };
        dock.Children.Add(new Avatar { Width = 24, Height = 24, Text = item.Name, Source = item.Operator?.IconPath, Small = true, Opacity = item.Owned ? 1 : 0.35, VerticalAlignment = VerticalAlignment.Center });

        // Right side: ✓ for one copy, − n + when the operator is owned more than once, × for hand-added.
        if (item.Owned && item.Count > 1)
        {
            var step = new Border { CornerRadius = new CornerRadius(9), Height = 18, Background = (Brush)FindResource("AccentSolidBrush"), VerticalAlignment = VerticalAlignment.Center };
            var sp = new StackPanel { Orientation = Orientation.Horizontal };
            sp.Children.Add(StepButton("−", () => ChangeCount(item.Name, -1)));
            var n = Text(item.Count.ToString(), 11, "OnAccentBrush", bold: true);
            n.FontFamily = (FontFamily)FindResource("NumberFontFamily");
            sp.Children.Add(n);
            sp.Children.Add(StepButton("+", () => ChangeCount(item.Name, +1)));
            step.Child = sp;
            step.ToolTip = "同名干员可以上场多个";
            DockPanel.SetDock(step, Dock.Right);
            dock.Children.Add(step);
        }
        else if (item.Owned)
        {
            var check = new Border
            {
                Width = 14, Height = 14, CornerRadius = new CornerRadius(7), VerticalAlignment = VerticalAlignment.Center,
                Background = (Brush)FindResource("AccentSolidBrush"),
                Child = new TextBlock { Text = "✓", FontSize = 9, FontWeight = FontWeights.Black, Foreground = (Brush)FindResource("OnAccentBrush"), HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
            };
            DockPanel.SetDock(check, Dock.Right);
            dock.Children.Add(check);
        }
        if (item.Removable)
        {
            var remove = new Button { Width = 18, Height = 18, Padding = new Thickness(0), Margin = new Thickness(0, 0, 3, 0), Tag = FindResource("IconClose"), Style = (Style)FindResource("TitleBarButton"), ToolTip = "从干员池删除", VerticalAlignment = VerticalAlignment.Center };
            remove.Click += (_, e) =>
            {
                e.Handled = true;
                _state.Extra.Remove(item.Name);
                _state.SetOwned(item.Name, false);
                SaveState();
                RefreshPool();
                ScheduleReplan();
            };
            DockPanel.SetDock(remove, Dock.Right);
            dock.Children.Add(remove);
        }
        var body = new StackPanel { Margin = new Thickness(6, 0, 4, 0), VerticalAlignment = VerticalAlignment.Center, Opacity = item.Owned ? 1 : 0.5 };
        var line = new StackPanel { Orientation = Orientation.Horizontal };
        line.Children.Add(Text(item.Name, 11, "TextPrimaryBrush", bold: true));
        if (item.Owned)
        {
            var elite = new Border
            {
                CornerRadius = new CornerRadius(3), Padding = new Thickness(3, 0, 3, 0), Margin = new Thickness(4, 0, 0, 0),
                VerticalAlignment = VerticalAlignment.Center, Cursor = Cursors.Hand,
                Background = item.Elite ? (Brush)FindResource("EliteTintBrush") : Brushes.Transparent,
                BorderBrush = (Brush)FindResource(item.Elite ? "EliteTintBrush" : "BorderBrush"), BorderThickness = new Thickness(1),
                Child = new TextBlock { Text = "精", FontSize = 9, FontWeight = item.Elite ? FontWeights.Bold : FontWeights.Normal, Foreground = (Brush)FindResource(item.Elite ? "EliteBrush" : "TextSecondaryBrush") },
                ToolTip = item.Elite ? "精锐 · 点一下改回普通" : "普通 · 点一下改成精锐",
                Opacity = item.Elite ? 1 : 0.7,
            };
            elite.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                _state.SetElite(item.Name, !item.Elite);
                SaveState();
                RefreshPool();
                ScheduleReplan();
            };
            line.Children.Add(elite);
        }
        body.Children.Add(line);
        if (item.Sub.Length > 0) body.Children.Add(Text(item.Sub, 9.5, "TextSecondaryBrush"));
        dock.Children.Add(body);
        root.Children.Add(dock);
        root.ToolTip = item.Owned ? "已拥有 · 点一下变灰（卖掉了）；右键可以加减数量" : "点一下点亮（买到了）";
        var menu = new ContextMenu();
        var more = new MenuItem { Header = "再加一个（同名上场）" };
        more.Click += (_, _) => ChangeCount(item.Name, +1);
        menu.Items.Add(more);
        if (item.Count > 1)
        {
            var less = new MenuItem { Header = "减少一个" };
            less.Click += (_, _) => ChangeCount(item.Name, -1);
            menu.Items.Add(less);
        }
        root.ContextMenu = menu;
        if (_bouncePool == item.Name) { Bounce(root); _bouncePool = null; }
        root.MouseLeftButtonUp += (_, e) =>
        {
            if (e.Handled) return;
            _bouncePool = item.Name;
            _state.SetOwned(item.Name, !item.Owned);
            SaveState();
            RefreshPool();
            ScheduleReplan();
        };
        return root;
    }

    private UIElement StepButton(string text, Action click)
    {
        var t = new TextBlock
        {
            Text = text, Width = 15, TextAlignment = TextAlignment.Center, FontSize = 11, FontWeight = FontWeights.Black,
            Foreground = (Brush)FindResource("OnAccentBrush"), Opacity = 0.8, VerticalAlignment = VerticalAlignment.Center,
            Background = Brushes.Transparent, Cursor = Cursors.Hand,
        };
        t.MouseLeftButtonUp += (_, e) => { e.Handled = true; click(); };
        return t;
    }

    private void ChangeCount(string name, int delta)
    {
        _bouncePool = name;
        _state.SetCount(name, _state.CountOf(name) + delta);
        SaveState();
        RefreshPool();
        ScheduleReplan();
    }

    private void OnPoolSearchChanged(object sender, TextChangedEventArgs e)
    {
        _poolQuery = PoolSearch.Text.Trim();
        if (_ready) RefreshPool();
    }

    private void OnPoolFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        _ownedOnly = PoolFilterList.SelectedIndex == 1;
        if (_ready) RefreshPool();
    }

    private void OnOwnCurrentStage(object sender, RoutedEventArgs e)
    {
        int level = _tracking?.TerminalLevel ?? 0;
        foreach (var entry in _tracking?.TrackedOperators.Where(t => t.Type == TrackingType.Operator) ?? [])
            if (!entry.ExpiredAt(level) && !_state.IsOwned(entry.Name)) _state.SetOwned(entry.Name, true);
        SaveState();
        RefreshPool();
        ScheduleReplan();
    }

    private void OnClearPool(object sender, RoutedEventArgs e)
    {
        _state.ClearPool();
        _pins.Clear();
        _state.FieldPins.Clear();
        _state.BenchPins.Clear();
        _state.Transforms.Clear();
        SaveState();
        RefreshPool();
        ScheduleReplan();
    }

    private void OnAddOther(object sender, RoutedEventArgs e)
    {
        var strategies = _operators.Values.Select(o => o.RequiredStrategyId).Where(s => s.Length > 0).ToHashSet();
        var dlg = new OperatorPickerWindow(_operators.Values, "", strategies) { Owner = Window.GetWindow(this) };
        if (dlg.ShowDialog() != true || dlg.Result is not { Length: > 0 } name) return;
        // 手动添加的一律放进「其他」，扩展池里的也是：扩展池按阶折叠，加进去会被折起来，看着像没加上。
        bool tracked = _tracking?.TrackedOperators.Any(t => t.Name == name) == true;
        if (!tracked && !_state.Extra.Contains(name)) _state.Extra.Add(name);
        _state.SetOwned(name, true);
        _bouncePool = name;
        SaveState();
        RefreshPool();
        ScheduleReplan();
    }

    private static SolidColorBrush Frozen(Color c)
    {
        var b = new SolidColorBrush(c);
        b.Freeze();
        return b;
    }
}
