using System.Collections.ObjectModel;
using System.Diagnostics;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Core.ViewModels;

/// <summary>One covenant tile on the planner's target page.</summary>
public partial class PlannerCovenantItem : ObservableObject
{
    private readonly Action<PlannerCovenantItem, int> _onToggle;

    public PlannerCovenantItem(CovenantInfo info, Action<PlannerCovenantItem, int> onToggle, bool canTransformInto)
    {
        Info = info;
        _onToggle = onToggle;
        CanTransformInto = canTransformInto;
        HasSecond = LineupPlanner.HasSecondLevel(info);
        SecondManual = LineupPlanner.IsSecondLevelManual(info);
    }

    public CovenantInfo Info { get; }
    public string Name => Info.Name;
    public bool IsCore => Info.BondType == "SEASON";
    public bool HasSecond { get; }
    public bool SecondManual { get; }
    public string SecondLabel => CanPlanSecond ? "Ⅱ" : "—";
    public string TypeText => IsCore ? "核心盟约" : "附加盟约";
    public string FirstRequirement => $"{LineupPlanner.CreateGoal(Info, 1).Count} 人";
    public string SecondRequirement => !HasSecond ? "没有 Ⅱ 级"
        : SecondManual ? "规划不提供 Ⅱ 级效果"
        : $"{LineupPlanner.CreateGoal(Info, 2).Count} 人";
    public bool CanPlanSecond => HasSecond && !SecondManual && !SecondUnreachable;
    /// <summary>
    /// A banned covenant cannot be a transformation target, so only its native holders that are still available
    /// count. Ⅱ is out of reach when fewer of them remain than Ⅱ needs.
    /// </summary>
    public bool SecondUnreachable => IsBanned && !IsCore && HasSecond && !SecondManual
        && AvailableMembers < LineupPlanner.CreateGoal(Info, 2).Count;
    /// <summary>Banned additional covenant with fewer available holders than Ⅰ needs.</summary>
    public bool FirstUnreachable => IsBanned && !IsCore && AvailableMembers < LineupPlanner.CreateGoal(Info, 1).Count;
    public int AvailableMembers => Members.Count(m => !m.IsUnavailable);
    public bool CanTransformInto { get; }
    /// <summary>In-game effect texts (data/covenant_effects.json).</summary>
    public IReadOnlyList<CovenantEffect> Effects => Info.Effects;
    public bool HasEffects => Info.Effects.Count > 0;

    /// <summary>Explains the ban state in the hover details; empty when not banned.</summary>
    public string BanNote => !IsBanned ? ""
        : IsCore ? "本局已被 Ban，不能作为目标。"
        : FirstUnreachable ? $"本局已被 Ban，剩余可用持有者只有 {AvailableMembers} 人，凑不齐 Ⅰ，不能作为目标。"
        : SecondUnreachable ? $"本局已被 Ban：剩余可用持有者 {AvailableMembers} 人，只能规划 Ⅰ。"
        : $"本局已被 Ban：自身盟约全部被 Ban 的干员被移出，其余 {AvailableMembers} 名持有者仍可凑人数（不能转职进这个盟约）。";
    public bool HasBanNote => IsBanned;

    /// <summary>Second line of a goal tile: how many holders are left this run.</summary>
    public string TileSubText => IsBanned && (IsCore || FirstUnreachable) ? "本局 Ban"
        : IsBanned ? $"Ban · 剩 {AvailableMembers} 人"
        : $"可用 {AvailableMembers} 人";

    /// <summary>Operators who hold this covenant natively (tier order); unavailable ones are dimmed.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(MemberSummary), nameof(AvailableMembers), nameof(FirstUnreachable), nameof(SecondUnreachable),
        nameof(CanPlanSecond), nameof(SecondLabel), nameof(IsBlocked), nameof(IsEnabled), nameof(BanNote), nameof(ToolTip), nameof(TileSubText))]
    private IReadOnlyList<PlannerCovenantMember> _members = [];

    public string MemberSummary
    {
        get
        {
            var available = Members.Count(m => !m.IsUnavailable);
            return available == Members.Count ? $"持有该盟约的干员 · {Members.Count} 人"
                : $"持有该盟约的干员 · 本局可用 {available} / {Members.Count}";
        }
    }

    public string ToolTip
    {
        get
        {
            if (IsBlocked) return IsCore ? $"{Name}：核心盟约本局已被 Ban" : $"{Name}：{BanNote}";
            var first = LineupPlanner.CreateGoal(Info, 1);
            var text = (IsBanned ? $"{Name} {BanNote}\n" : "") + $"{Name} Ⅰ：{first.Count} 人";
            if (!HasSecond) return text + "（没有 Ⅱ 级）";
            var second = LineupPlanner.CreateGoal(Info, 2);
            return text + (SecondManual
                ? "\n规划不提供Ⅱ级效果"
                : $"\nⅡ：{second.Count} 人");
        }
    }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLevel1), nameof(IsLevel2), nameof(IsSelected))]
    private int _level;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToolTip), nameof(IsEnabled), nameof(IsBlocked), nameof(BanNote), nameof(HasBanNote),
        nameof(FirstUnreachable), nameof(SecondUnreachable), nameof(CanPlanSecond), nameof(SecondLabel), nameof(TileSubText))]
    private bool _isBanned;

    [ObservableProperty]
    private bool _isAvailable;

    [ObservableProperty]
    private bool _isVisible = true;

    public bool IsLevel1 => Level == 1;
    public bool IsLevel2 => Level == 2;
    public bool IsSelected => Level > 0;
    /// <summary>
    /// A banned core covenant cannot be targeted. A banned additional covenant only removes the operators whose
    /// covenants are all banned, so it can still be reached with the remaining operators and stays selectable.
    /// </summary>
    public bool IsBlocked => IsBanned && (IsCore || FirstUnreachable);
    public bool IsEnabled => !IsBlocked;

    [RelayCommand]
    private void ToggleLevel1() => _onToggle(this, 1);

    [RelayCommand]
    private void ToggleLevel2() { if (CanPlanSecond) _onToggle(this, 2); }
}

public sealed partial class PlannerLockedOperator : ObservableObject
{
    public PlannerLockedOperator(Operator op, Action<PlannerLockedOperator> onRemove)
    {
        Operator = op; Name = op.Name; IconPath = op.IconPath;
        RemoveCommand = new RelayCommand(() => onRemove(this));
    }

    public string Name { get; }
    public string IconPath { get; }
    public Operator Operator { get; }
    public string Initial => Name.Length > 0 ? Name[..1] : "?";
    public IRelayCommand RemoveCommand { get; }
}

public sealed class PlannerOperatorItem
{
    public string Name { get; init; } = "";
    public string IconPath { get; init; } = "";
    public string Initial => IsFree ? "+" : Name.Length > 0 ? Name[..1] : "?";
    public string Sub { get; init; } = "";
    public string ToolTip { get; init; } = "";
    public bool IsLocked { get; init; }
    public bool IsTransformed { get; init; }
    public bool IsHarmony { get; init; }
    public bool IsFree { get; init; }
    /// <summary>Full operator data for the hover details (null for free slots).</summary>
    public Operator? Operator { get; init; }
    public bool HasDetails => Operator is not null;
    /// <summary>Planner-specific note shown above the details, e.g. “转职 → 不屈 · 必带”.</summary>
    public string ExtraInfo { get; init; } = "";
    public bool HasExtraInfo => ExtraInfo.Length > 0;
}

/// <summary>The run strategy's own piece (暴躁铁皮 / Pith), shown above the lineup; it takes one of the places.</summary>
public sealed record PlannerStrategyPiece(string Name, string IconPath, string Sub, string Badge)
{
    public string Initial => Name.Trim('“', '”').Length > 0 ? Name.Trim('“', '”')[..1] : "?";
    public bool HasBadge => Badge.Length > 0;
}

/// <summary>One choice of the 本局策略 selector.</summary>
public sealed record PlannerStrategyChoice(PlannerStrategy Strategy, string Name, string Icon, string Hint);

/// <summary>An operator who holds a covenant, listed in the covenant hover details.</summary>
public sealed record PlannerCovenantMember(string Name, string IconPath, int Tier, bool IsUnavailable, Operator Operator)
{
    public string Initial => Name.Length > 0 ? Name[..1] : "?";
}

/// <summary>Activated covenant on a result card; <see cref="IsGoal"/> marks the covenants the user planned for.</summary>
public sealed record PlannerGoalPill(string Text, bool IsManual, bool IsGoal = false);

public sealed partial class PlannerLineupItem : ObservableObject
{
    public required PlannedLineup Source { get; init; }
    /// <summary>1-based position in the sorted list (shown as the rank tile).</summary>
    public int Rank { get; init; }
    public bool IsFirst => Rank == 1;
    /// <summary>Expanded cards list members in two columns with the actions; collapsed ones show one row of avatars.</summary>
    [ObservableProperty] private bool _isExpanded;
    [RelayCommand] private void ToggleExpanded() => IsExpanded = !IsExpanded;
    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public string? Note { get; init; }
    public bool HasNote => !string.IsNullOrEmpty(Note);
    public IReadOnlyList<PlannerOperatorItem> Operators { get; init; } = [];
    public IReadOnlyList<PlannerGoalPill> Goals { get; init; } = [];
    public string LineupName { get; init; } = "";
    /// <summary>Repair mode: who replaces whom, e.g. 「塔露拉、冻河 → 伊桑、霜叶」.</summary>
    public string RepairText { get; init; } = "";
    public bool HasRepairText => RepairText.Length > 0;
    public PlannerStrategyPiece? StrategyPiece { get; init; }
    public bool HasStrategyPiece => StrategyPiece is not null;

    public Lineup ToLineup(LineupEquipmentService equipmentRules) => new()
    {
        Name = LineupName,
        Slots = PlannerLineupItem.Slots(Source.Operators, equipmentRules).Concat(MechSlots(Source, equipmentRules)).ToList(),
        Strategies = StrategySlots(Source.Strategy)
    };

    internal static IEnumerable<LineupSlot> Slots(IEnumerable<PlannedOperator> operators, LineupEquipmentService equipmentRules)
        => operators.Select(o => new LineupSlot
        {
            OperatorName = o.Name,
            Equipments = o.TransformedCovenant is null || !equipmentRules.CanTransformInto(o.TransformedCovenant) ? [] :
                [LineupEquipmentService.Shapeshifter, equipmentRules.EquipmentFor(o.TransformedCovenant)]
        });

    /// <summary>暴躁铁皮 with one 变形同构体 and the job-change gear of each covenant it carries.</summary>
    internal static IEnumerable<LineupSlot> MechSlots(PlannedLineup lineup, LineupEquipmentService equipmentRules)
    {
        if (lineup.Strategy != PlannerStrategy.Mech) yield break;
        var gear = lineup.MechCovenants.Where(equipmentRules.CanTransformInto).Select(equipmentRules.EquipmentFor).ToList();
        yield return new LineupSlot
        {
            OperatorName = LineupEquipmentService.MechName, IsElite = lineup.MechElite,
            Equipments = gear.Count == 0 ? [] : [LineupEquipmentService.Shapeshifter, .. gear]
        };
    }

    internal static List<LineupStrategySlot> StrategySlots(PlannerStrategy strategy)
        => PlannerStrategies.StrategyId(strategy) is { } id ? [new LineupStrategySlot { StrategyId = id, Tags = [StrategyTag.阵容核心] }] : [];
}

public sealed partial class PlannerVariantCell : ObservableObject
{
    public int People { get; init; }
    public int Transforms { get; init; }
    public int Count { get; init; }
    public int BestScore { get; init; }
    public bool IsLimited { get; init; }
    public bool HasResults => Count > 0;
    public string Text => Count > 0 ? Count.ToString() : "—";
    public string SubText => Count > 0 ? "套" : IsLimited ? "受限" : "无";
    public string ToolTip => Count > 0
        ? $"{People} 人 · {Transforms} 转：{Count} 套" + (IsLimited ? "（达到搜索上限，可能还有其他组合）" : "")
        : IsLimited ? $"{People} 人 · {Transforms} 转：达到搜索上限仍未找到" : $"{People} 人 · {Transforms} 转：未找到候选";

    [ObservableProperty]
    private bool _isSelected;
}

/// <summary>One operator in a route stage (道中路线).</summary>
public sealed class RouteOperatorItem
{
    public string Name { get; init; } = "";
    public string IconPath { get; init; } = "";
    public string Initial => Name.Length > 0 ? Name[..1] : "?";
    public string TierText { get; init; } = "";
    public bool IsFinal { get; init; }
    public string RoleText => IsFinal ? "最终" : "过渡";
    public bool ViaTriple { get; init; }
    public bool IsNew { get; init; }
    public string Sub { get; init; } = "";
    public Operator? Operator { get; init; }
    public bool HasDetails => Operator is not null;
    public string ExtraInfo { get; init; } = "";
    public bool HasExtraInfo => ExtraInfo.Length > 0;
    /// <summary>“盾 / 奶 / 叠” marks from operator_profiles.json.</summary>
    public string RoleMarks { get; init; } = "";
    public bool HasRoleMarks => RoleMarks.Length > 0;
}

public sealed record RouteCovenantPill(string Text, bool IsGoal, bool IsActive);

/// <summary>A small chip on a route stage: a condition (≤ Ⅲ 阶 · 8 人) or a role check (盾 1, red when missing).</summary>
public sealed record RouteChip(string Text, bool IsRole, bool IsMissing, string ToolTip = "");

/// <summary>One node of the 道中 stepper above the stages.</summary>
public sealed partial class RouteStepItem : ObservableObject
{
    public string Name { get; init; } = "";
    public int Number { get; init; }
    /// <summary>Connector to the next node.</summary>
    public bool HasLine { get; init; }
    /// <summary>Connector from the previous node.</summary>
    public bool HasLineIn => Number > 1;
    [ObservableProperty] private bool _isPast;
    [ObservableProperty] private bool _isCurrent;
    public RouteStageItem? Stage { get; init; }
}

public sealed partial class RouteStageItem : ObservableObject
{
    /// <summary>Terminal level from which this stage is behind the player (0 = never).</summary>
    public int ExpiresAt { get; init; }

    /// <summary>The current terminal level is already past this stage (its 过渡 cards are no longer tracked).</summary>
    [ObservableProperty]
    private bool _isPast;

    /// <summary>The stage the player is in now (first one not behind the terminal level).</summary>
    [ObservableProperty] private bool _isCurrent;
    /// <summary>Only the current stage is open by default; the others show one row of small avatars.</summary>
    [ObservableProperty] private bool _isExpanded;
    [RelayCommand] private void ToggleExpanded() => IsExpanded = !IsExpanded;

    public string Title { get; init; } = "";
    /// <summary>Short name for the stepper: 开局 / 前期 / 四本 / 六本 / 大成.</summary>
    public string ShortTitle { get; init; } = "";
    public string Subtitle { get; init; } = "";
    /// <summary>“终端 3”, or empty for 大成.</summary>
    public string LevelText { get; init; } = "";
    public bool IsFinal { get; init; }
    public bool IsStacking { get; init; }
    public IReadOnlyList<RouteOperatorItem> Operators { get; init; } = [];
    public IReadOnlyList<RouteCovenantPill> Covenants { get; init; } = [];
    public IReadOnlyList<RouteChip> Conditions { get; init; } = [];
    public IReadOnlyList<RouteChip> Roles { get; init; } = [];
    /// <summary>“换下 莎草、蓝毒”: operators of the previous stage no longer used here.</summary>
    public string ChangeText { get; init; } = "";
    public bool HasChange => ChangeText.Length > 0;
    public string WarningText { get; init; } = "";
    public bool HasWarning => WarningText.Length > 0;
    public PlannerStrategyPiece? StrategyPiece { get; init; }
    public bool HasStrategyPiece => StrategyPiece is not null;
}

/// <summary>
/// State of the 规划 tab: covenant targets, locked operators, debounced/cancellable search and result browsing.
/// </summary>
public partial class LineupPlannerViewModel : ObservableObject
{
    private static readonly int[] PeopleOptions = [8, 9];
    private IReadOnlyList<int> TransformOptions => PlannerStrategies.TransformOptions(Strategy);

    /// <summary>The run's strategy (one per run); only 暴躁铁皮 / Pith / 变形同构 change planning.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsMechStrategy), nameof(StrategyHint), nameof(TransformHeaders))]
    private PlannerStrategy _strategy = PlannerStrategy.Other;
    /// <summary>暴躁铁皮 is elite: it carries 5 transformations instead of 3.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StrategyHint))]
    private bool _mechElite;
    public bool IsMechStrategy => Strategy == PlannerStrategy.Mech;
    public IReadOnlyList<PlannerStrategyChoice> StrategyChoices { get; } =
    [
        new(PlannerStrategy.Other, "其他", "—", "其他策略不影响规划"),
        new(PlannerStrategy.Mech, "暴躁铁皮", "铁", "森蚺：送一个占 1 人口、可挂多件转职装备的棋子"),
        new(PlannerStrategy.Pith, "优等生", "P", "Pith：开局送一个调和棋子，占 1 人口，一直保留"),
        new(PlannerStrategy.Shapeshifter, "变形同构", "变", "每 5 回合送一个变形同构体，转职装备仍需购买"),
    ];
    public string StrategyHint => Strategy switch
    {
        PlannerStrategy.Mech => $"暴躁铁皮占 1 人口，最多转 {PlannerStrategies.MechTransformCap(MechElite)} 个盟约" + (MechElite ? "（精锐）" : "（精锐 5 个）"),
        PlannerStrategy.Pith => "Pith 占 1 人口：本局已激活的核心盟约各 +1 人",
        PlannerStrategy.Shapeshifter => "最多送 3 个变形同构体（第 5、10、15 回合），可规划 3 次转职",
        _ => "带的是其他策略时规划不受影响"
    };
    /// <summary>Column headers of the people × transforms matrix (“0 转” …).</summary>
    public IReadOnlyList<string> TransformHeaders => TransformOptions.Select(t => $"{t} 转").ToList();

    [RelayCommand]
    private void SelectStrategy(PlannerStrategyChoice? choice)
    {
        if (choice is not null) Strategy = choice.Strategy;
    }

    partial void OnStrategyChanged(PlannerStrategy value)
    {
        if (value != PlannerStrategy.Mech) MechElite = false;
        RequestRecompute();
    }

    partial void OnMechEliteChanged(bool value)
    {
        if (Strategy == PlannerStrategy.Mech) RequestRecompute();
    }

    private readonly LineupPlanner _planner;
    private readonly LineupRoutePlanner _routePlanner;
    private CancellationTokenSource? _routeCts;
    private readonly BanViewModel _banVm;
    private readonly IReadOnlyList<Operator> _operators;
    private readonly Dictionary<string, Operator> _operatorByName;
    private readonly Dictionary<string, string> _allOperatorIcons;
    private IReadOnlySet<string> _availableCovenants = new HashSet<string>();
    private PlannerResult? _lastResult;
    private CancellationTokenSource? _cts;
    private bool _dirty;
    private bool _isActive;

    private readonly LineupEquipmentService _equipmentRules;

    public LineupPlannerViewModel(LineupPlanner planner, IReadOnlyList<Operator> operators, BanViewModel banVm, LineupEquipmentService equipmentRules)
    {
        _equipmentRules = equipmentRules;
        _planner = planner;
        _banVm = banVm;
        // Strategy-only pieces (“暴躁铁皮”) never appear in the shop pool, so neither planner uses them.
        _operators = operators.Where(o => o.RequiredStrategyId.Length == 0).ToList();
        _routePlanner = new LineupRoutePlanner(_operators, planner.Covenants);
        _operatorByName = _operators.GroupBy(o => o.Name).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        _allOperatorIcons = operators.GroupBy(o => o.Name).ToDictionary(g => g.Key, g => g.First().IconPath, StringComparer.Ordinal);
        foreach (var c in planner.Covenants)
            (c.BondType == "SEASON" ? CoreCovenants : ExtraCovenants).Add(new PlannerCovenantItem(c, OnToggleLevel,
                _equipmentRules.CanTransformInto(c.Name) && !LineupPlanner.NoTransform.Contains(c.Name)));
        foreach (var p in PeopleOptions)
            foreach (var t in TransformOptions)
                VariantCells.Add(new PlannerVariantCell { People = p, Transforms = t });
        banVm.BansChanged += OnBansChanged;
        ApplyBans();
        UpdateStatus();
    }

    /// <summary>Delay before a search starts after the last change. Tests set 0.</summary>
    public int DebounceMilliseconds { get; set; } = 180;

    /// <summary>Marshals external events (ban changes) onto the UI thread. Null runs them inline.</summary>
    public Action<Action>? RunOnUiThread { get; set; }

    /// <summary>The search currently running (or debouncing); exposed for tests.</summary>
    public Task? PendingSearch { get; private set; }

    public ObservableCollection<PlannerCovenantItem> CoreCovenants { get; } = [];
    public ObservableCollection<PlannerCovenantItem> ExtraCovenants { get; } = [];
    public ObservableCollection<PlannerLockedOperator> LockedOperators { get; } = [];
    public ObservableCollection<Operator> LockSuggestions { get; } = [];
    public ObservableCollection<PlannerVariantCell> VariantCells { get; } = [];
    public ObservableCollection<PlannerLineupItem> VisibleLineups { get; } = [];
    public ObservableCollection<string> GoalChips { get; } = [];

    public IEnumerable<PlannerCovenantItem> AllCovenants => CoreCovenants.Concat(ExtraCovenants);

    /// <summary>Raised when the player saves a candidate as a lineup.</summary>
    public event Action<Lineup>? SaveRequested;
    /// <summary>Raised when the player tracks a candidate.</summary>
    public event Action<Lineup>? TrackRequested;
    /// <summary>Raised when the player tracks a whole 道中路线 (大成 + 过渡 operators).</summary>
    public event Action<PlannedRoute>? TrackRouteRequested;

    /// <summary>Repair mode: replace the source lineup's late phase with this result (other phases are kept).</summary>
    public event Action<Lineup>? OverwriteRequested;

    // ── 修补：从「我的阵容」里被禁的阵容出发 ─────────────────────────────

    /// <summary>The saved lineup being repaired; null for ordinary planning.</summary>
    [ObservableProperty] private Lineup? _repairSource;
    [ObservableProperty] private string _repairSummary = "";
    private HashSet<string> _repairBanned = [];

    public bool IsRepairing => RepairSource is not null;
    partial void OnRepairSourceChanged(Lineup? value) => OnPropertyChanged(nameof(IsRepairing));

    /// <summary>
    /// Plans replacements for <paramref name="banned"/>: the lineup's targets become the goals and the late-game
    /// operators still available are locked.
    /// </summary>
    public void BeginRepair(Lineup lineup, IReadOnlyList<TargetCovenant> targets, IReadOnlyCollection<string> banned)
    {
        foreach (var c in AllCovenants) c.Level = 0;
        foreach (var t in targets)
            if (AllCovenants.FirstOrDefault(c => c.Name == t.Name) is { IsBlocked: false } item)
                item.Level = t.Tier >= 2 && item.CanPlanSecond ? 2 : 1;
        LockedOperators.Clear();
        var kept = lineup.Slots.Select(s => s.OperatorName)
            .Where(n => n.Length > 0 && !banned.Contains(n) && _operatorByName.ContainsKey(n)).Distinct().ToList();
        foreach (var name in kept.Take(LineupPlanner.MaxLockedOperators))
            LockedOperators.Add(new PlannerLockedOperator(_operatorByName[name], RemoveLocked));
        OnPropertyChanged(nameof(HasLocked));
        _repairBanned = [.. banned];
        RepairSource = lineup;
        var goals = string.Join(" · ", AllCovenants.Where(c => c.IsSelected).Select(c => $"{c.Name} {(c.Level == 2 ? "Ⅱ" : "Ⅰ")}"));
        RepairSummary = $"固定没被禁的 {kept.Count} 人，替补 {banned.Count} 人" + (goals.Length > 0 ? $"；目标盟约保持 {goals}" : "");
        ShowResults = true;
        GoalsChanged();
    }

    [RelayCommand]
    private void EndRepair()
    {
        RepairSource = null;
        RepairSummary = "";
        _repairBanned = [];
        RebuildVisibleLineups();
    }

    [RelayCommand]
    private void OverwriteLineup(PlannerLineupItem? item)
    {
        if (item is not null && Repaired(item) is { } lineup) OverwriteRequested?.Invoke(lineup);
    }

    /// <summary>
    /// The source lineup with its late phase replaced by <paramref name="item"/>: operators who stay keep their
    /// equipment, candidates and tags; the other phases, strategies and targets are kept.
    /// </summary>
    private Lineup? Repaired(PlannerLineupItem item, bool asCopy = false)
    {
        if (RepairSource is not { } source) return null;
        var result = item.ToLineup(_equipmentRules);
        var slots = result.Slots.Select(r => source.Slots.FirstOrDefault(s => s.OperatorName == r.OperatorName) ?? r).ToList();
        var copy = System.Text.Json.JsonSerializer.Deserialize<Lineup>(System.Text.Json.JsonSerializer.Serialize(source))!;
        copy.Slots = System.Text.Json.JsonSerializer.Deserialize<List<LineupSlot>>(System.Text.Json.JsonSerializer.Serialize(slots))!;
        if (asCopy)
        {
            copy.Id = Guid.NewGuid().ToString("N");
            copy.Name = item.LineupName;
            copy.CreatedAt = DateTime.UtcNow;
        }
        return copy;
    }

    private string RepairTextFor(PlannedLineup lineup)
    {
        if (RepairSource is not { } source) return "";
        var before = source.Slots.Select(s => s.OperatorName).Where(n => n.Length > 0).ToHashSet(StringComparer.Ordinal);
        var after = lineup.Operators.Select(o => o.Name).ToHashSet(StringComparer.Ordinal);
        var removed = before.Where(n => !after.Contains(n)).ToList();
        var added = lineup.Operators.Select(o => o.Name).Where(n => !before.Contains(n)).ToList();
        if (removed.Count == 0 && added.Count == 0) return "";
        return $"{(removed.Count > 0 ? string.Join("、", removed) : "空位")} → {(added.Count > 0 ? string.Join("、", added) : "空位")}";
    }
    private PlannedRoute? _currentRoute;
    /// <summary>Supplies the currently tracked operator names for “从追踪导入”.</summary>
    public Func<IEnumerable<string>>? TrackedOperatorsProvider { get; set; }

    [ObservableProperty] private bool _showResults;
    /// <summary>Results page shows the 道中路线 of one candidate instead of the candidate list.</summary>
    [ObservableProperty] private bool _showRoute;
    [ObservableProperty] private bool _isRouteBusy;
    [ObservableProperty] private string _routeTitle = "";
    /// <summary>Current terminal level (from the tracking tab; 0 = unknown). Stages behind it are shown as 已过.</summary>
    [ObservableProperty] private int _currentTerminalLevel;

    partial void OnCurrentTerminalLevelChanged(int value) => RefreshRouteProgress(expandCurrent: false);

    /// <summary>Marks stages behind the terminal level as 已过 and the first one ahead as current.</summary>
    private void RefreshRouteProgress(bool expandCurrent)
    {
        var level = CurrentTerminalLevel;
        RouteStageItem? current = null;
        foreach (var stage in RouteStages)
        {
            stage.IsPast = stage.ExpiresAt > 0 && level >= stage.ExpiresAt;
            stage.IsCurrent = false;
            if (current is null && !stage.IsPast) current = stage;
        }
        if (current is not null)
        {
            current.IsCurrent = true;
            if (expandCurrent) current.IsExpanded = true;
        }
        foreach (var step in RouteSteps)
        {
            step.IsPast = step.Stage?.IsPast == true;
            step.IsCurrent = ReferenceEquals(step.Stage, current);
        }
    }

    public ObservableCollection<RouteStageItem> RouteStages { get; } = [];
    public ObservableCollection<RouteStepItem> RouteSteps { get; } = [];
    /// <summary>Raised when a stepper node is clicked: the view scrolls the stage into view.</summary>
    public event Action<RouteStageItem>? RouteStageFocusRequested;

    /// <summary>Stepper click: open that stage (only it) and bring it into view.</summary>
    [RelayCommand]
    private void SelectRouteStep(RouteStepItem? step)
    {
        if (step?.Stage is not { } stage) return;
        foreach (var s in RouteStages) s.IsExpanded = ReferenceEquals(s, stage);
        RouteStageFocusRequested?.Invoke(stage);
    }
    /// <summary>“伊比利亚 6 · 9 人 · 1 转” under the route title.</summary>
    [ObservableProperty] private string _routeSubtitle = "";
    private PlannerLineupItem? _routeItem;
    [ObservableProperty] private bool _onlyCurrentRun;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private bool _hasError;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _statusDetail = "";
    [ObservableProperty] private string _runInfoText = "";
    [ObservableProperty] private bool _hasRunInfo;
    [ObservableProperty] private int _selectedGoalCount;
    [ObservableProperty] private int _totalResults;
    [ObservableProperty] private string _lockSearchText = "";
    [ObservableProperty] private string _resultHint = "";
    [ObservableProperty] private PlannerVariantCell? _selectedVariant;

    public bool HasGoals => SelectedGoalCount > 0;
    public bool HasLocked => LockedOperators.Count > 0;
    public bool HasVisibleLineups => VisibleLineups.Count > 0;
    public string EmptyResultText => SelectedGoalCount == 0 ? "先在“目标”里选择盟约。"
        : IsBusy ? "正在搜索…"
        : HasError ? StatusText
        : "这一组没有可行组合，可以换一格看看，或减少目标、增加转职。";
    public string GoalTabText => SelectedGoalCount > 0 ? $"目标 · 已选 {SelectedGoalCount}" : "目标";
    public string ResultTabText => TotalResults > 0 ? $"结果 {TotalResults}" : "结果";
    public string ViewResultsText => TotalResults > 0 ? $"查看 {TotalResults} 套 →" : "查看结果 →";

    /// <summary>Whether the 规划 tab is on screen. Changes while hidden only mark the results stale.</summary>
    public bool IsActive
    {
        get => _isActive;
        set
        {
            if (_isActive == value) return;
            _isActive = value;
            if (value && _dirty) RequestRecompute();
        }
    }

    partial void OnSelectedGoalCountChanged(int value)
    {
        OnPropertyChanged(nameof(HasGoals));
        OnPropertyChanged(nameof(GoalTabText));
        OnPropertyChanged(nameof(EmptyResultText));
    }

    partial void OnIsBusyChanged(bool value) => OnPropertyChanged(nameof(EmptyResultText));
    partial void OnStatusTextChanged(string value) => OnPropertyChanged(nameof(EmptyResultText));

    partial void OnTotalResultsChanged(int value)
    {
        OnPropertyChanged(nameof(ResultTabText));
        OnPropertyChanged(nameof(ViewResultsText));
    }

    partial void OnOnlyCurrentRunChanged(bool value) => ApplyVisibility();

    partial void OnLockSearchTextChanged(string value)
    {
        LockSuggestions.Clear();
        var text = value.Trim();
        if (text.Length == 0) return;
        foreach (var op in _operators
                     .Where(o => o.Name.Contains(text, StringComparison.OrdinalIgnoreCase) && LockedOperators.All(l => l.Name != o.Name))
                     .OrderBy(o => o.Name.StartsWith(text, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                     .ThenBy(o => o.Name.Length)
                     .Take(8))
            LockSuggestions.Add(op);
    }

    partial void OnSelectedVariantChanged(PlannerVariantCell? value)
    {
        foreach (var cell in VariantCells) cell.IsSelected = ReferenceEquals(cell, value);
        RebuildVisibleLineups();
    }

    // ── Commands ────────────────────────────────────────────────────────────

    [RelayCommand]
    private void ClearGoals()
    {
        foreach (var c in AllCovenants) c.Level = 0;
        GoalsChanged();
    }

    [RelayCommand] private void ShowGoalPage() => ShowResults = false;
    [RelayCommand] private void ShowResultPage() => ShowResults = true;

    [RelayCommand]
    private void SelectVariant(PlannerVariantCell? cell)
    {
        if (cell is not null) SelectedVariant = cell;
    }

    /// <summary>Locks the exact match, or the first suggestion, of <paramref name="name"/>.</summary>
    [RelayCommand]
    private void LockOperator(string? name) => AddLocked(name);

    public bool AddLocked(string? name)
    {
        name = name?.Trim();
        if (string.IsNullOrEmpty(name)) return false;
        if (!_operatorByName.TryGetValue(name, out var op))
        {
            op = LockSuggestions.FirstOrDefault();
            if (op is null) return false;
        }
        if (LockedOperators.Any(l => l.Name == op.Name)) { LockSearchText = ""; return false; }
        if (LockedOperators.Count >= LineupPlanner.MaxLockedOperators) return false;
        LockedOperators.Add(new PlannerLockedOperator(op, RemoveLocked));
        LockSearchText = "";
        OnPropertyChanged(nameof(HasLocked));
        RequestRecompute();
        return true;
    }

    [RelayCommand]
    private void ImportTracked()
    {
        var names = TrackedOperatorsProvider?.Invoke()?.ToList() ?? [];
        var changed = false;
        foreach (var name in names)
        {
            if (LockedOperators.Count >= LineupPlanner.MaxLockedOperators) break;
            if (!_operatorByName.TryGetValue(name, out var op) || LockedOperators.Any(l => l.Name == name)) continue;
            LockedOperators.Add(new PlannerLockedOperator(op, RemoveLocked));
            changed = true;
        }
        if (!changed) return;
        OnPropertyChanged(nameof(HasLocked));
        RequestRecompute();
    }

    [RelayCommand]
    private void ClearLocked()
    {
        if (LockedOperators.Count == 0) return;
        LockedOperators.Clear();
        OnPropertyChanged(nameof(HasLocked));
        RequestRecompute();
    }

    [RelayCommand]
    private void SaveLineup(PlannerLineupItem? item)
    {
        if (item is not null) SaveRequested?.Invoke(Repaired(item, asCopy: true) ?? item.ToLineup(_equipmentRules));
    }

    [RelayCommand]
    private void TrackLineup(PlannerLineupItem? item)
    {
        if (item is not null) TrackRequested?.Invoke(item.ToLineup(_equipmentRules));
    }

    private void RemoveLocked(PlannerLockedOperator item)
    {
        if (!LockedOperators.Remove(item)) return;
        OnPropertyChanged(nameof(HasLocked));
        RequestRecompute();
    }

    // ── External inputs ─────────────────────────────────────────────────────

    /// <summary>Covenants recognised as available in the current run (from the ban screen).</summary>
    public void SetAvailableCovenants(IEnumerable<string> names)
    {
        _availableCovenants = names.ToHashSet(StringComparer.Ordinal);
        if (_availableCovenants.Count == 0) OnlyCurrentRun = false;
        foreach (var c in AllCovenants) c.IsAvailable = _availableCovenants.Contains(c.Name);
        ApplyVisibility();
        UpdateRunInfo();
    }

    private void OnBansChanged()
    {
        if (RunOnUiThread is { } dispatch)
        {
            dispatch(() => { ApplyBans(); RequestRecompute(); });
            return;
        }
        ApplyBans();
        RequestRecompute();
    }

    private void ApplyBans()
    {
        var goalDropped = false;
        foreach (var c in AllCovenants)
            c.IsBanned = _banVm.IsCovenantBanned(c.Name);
        // Availability of the holders decides whether a banned additional covenant can still be reached.
        RefreshMembers();
        foreach (var c in AllCovenants)
        {
            if (c.IsBlocked && c.Level > 0) { c.Level = 0; goalDropped = true; }
            else if (c.Level == 2 && !c.CanPlanSecond) { c.Level = 1; goalDropped = true; }
        }
        if (goalDropped) RefreshGoalChips();
        UpdateRunInfo();
    }

    private void RefreshMembers()
    {
        var bannedCovenants = _banVm.BannedCovenants;
        foreach (var c in AllCovenants)
        {
            c.Members = _operators
                .Where(o => o.CoreCovenants.Contains(c.Name) || o.AdditionalCovenants.Contains(c.Name))
                .OrderBy(o => o.Tier).ThenBy(o => o.Name, StringComparer.Ordinal)
                .Select(o => new PlannerCovenantMember(o.Name, o.IconPath, o.Tier,
                    _banVm.IsBanned(o.Name) || _planner.IsExcludedByBans(o, bannedCovenants), o))
                .ToList();
        }
    }

    private void ApplyVisibility()
    {
        var filter = OnlyCurrentRun && _availableCovenants.Count > 0;
        foreach (var c in AllCovenants)
            c.IsVisible = !filter || c.IsAvailable || c.IsSelected || (c.IsBanned && !c.IsBlocked);
    }

    private void UpdateRunInfo()
    {
        var banned = AllCovenants.Count(c => c.IsBanned);
        HasRunInfo = _availableCovenants.Count > 0 || banned > 0;
        RunInfoText = HasRunInfo
            ? $"已读取本局盟约 · {banned} 个被 Ban"
            : "未录入本局信息 · 开局信息页可截屏录入";
    }

    private void OnToggleLevel(PlannerCovenantItem item, int level)
    {
        if (item.IsBlocked) return;
        item.Level = item.Level == level ? 0 : level;
        GoalsChanged();
    }

    private void GoalsChanged()
    {
        RefreshGoalChips();
        ApplyVisibility();
        RequestRecompute();
    }

    private void RefreshGoalChips()
    {
        GoalChips.Clear();
        foreach (var c in AllCovenants.Where(c => c.IsSelected))
            GoalChips.Add($"{c.Name} {LineupPlanner.CreateGoal(c.Info, c.Level).Count}"); // as people, e.g. "乌萨斯 6"
        SelectedGoalCount = GoalChips.Count;
    }

    // ── Search pipeline ─────────────────────────────────────────────────────

    // ── 道中路线 ────────────────────────────────────────────────────────────

    [RelayCommand]
    private void CloseRoute()
    {
        _routeCts?.Cancel();
        _routeCts = null;
        ShowRoute = false;
        IsRouteBusy = false;
        RouteStages.Clear();
        RouteSteps.Clear();
        _currentRoute = null;
        TrackRouteCommand.NotifyCanExecuteChanged();
        SaveRouteCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Tracks every operator of the shown route: 大成 green, 过渡 orange.</summary>
    [RelayCommand(CanExecute = nameof(CanTrackRoute))]
    private void TrackRoute()
    {
        if (_currentRoute is not null) TrackRouteRequested?.Invoke(_currentRoute);
    }

    private bool CanTrackRoute() => _currentRoute is not null;

    /// <summary>
    /// Saves the route as one lineup: 前期 → 前期, 中期 → 中期, 大成 → 后期, and the 后期 (叠层) stackers that
    /// 大成 does not keep → 挂件. 开局 is not saved.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanTrackRoute))]
    private void SaveRoute()
    {
        if (_currentRoute is null || _routeItem is null) return;
        var lineup = _routeItem.ToLineup(_equipmentRules);
        lineup.Name = $"{_routeItem.LineupName} 路线";
        var finalNames = _routeItem.Source.Operators.Select(o => o.Name).ToHashSet(StringComparer.Ordinal);
        List<LineupSlot> StageSlots(Func<RouteStage, bool> pick, bool onlyTransition = false)
            => _currentRoute.Stages.FirstOrDefault(pick) is { } stage
                ? stage.Operators.Where(o => !onlyTransition || !finalNames.Contains(o.Name))
                    .Select(o => new LineupSlot { OperatorName = o.Name }).ToList()
                : [];
        lineup.EarlySlots = StageSlots(s => s.Definition.Name == "前期");
        lineup.MidSlots = StageSlots(s => s.Definition.Name == "中期");
        lineup.SupportSlots = StageSlots(s => s.Definition.Stacking, onlyTransition: true);
        SaveRequested?.Invoke(lineup);
    }

    /// <summary>Plans 开局 → 大成 transition lineups for one candidate.</summary>
    [RelayCommand]
    private Task ShowRouteFor(PlannerLineupItem? item) => item is null ? Task.CompletedTask : PlanRouteAsync(item);

    public async Task PlanRouteAsync(PlannerLineupItem item)
    {
        _routeCts?.Cancel();
        var cts = new CancellationTokenSource();
        _routeCts = cts;
        RouteStages.Clear();
        RouteSteps.Clear();
        _currentRoute = null;
        _routeItem = item;
        TrackRouteCommand.NotifyCanExecuteChanged();
        SaveRouteCommand.NotifyCanExecuteChanged();
        RouteTitle = $"{item.Title} · 道中路线";
        RouteSubtitle = string.Join(" · ", item.Source.Goals.Select(g => $"{g.Name} {g.Count}")
            .Append($"{item.Source.People} 人").Append($"{item.Source.Transforms} 转"));
        ShowRoute = true;
        IsRouteBusy = true;
        var bannedCovenants = new HashSet<string>(_banVm.BannedCovenants, StringComparer.Ordinal);
        var request = new RouteRequest
        {
            Final = item.Source,
            Goals = item.Source.Goals,
            BannedCovenants = bannedCovenants,
            BannedOperators = new HashSet<string>(_banVm.BannedNames, StringComparer.Ordinal)
        };
        try
        {
            var route = await Task.Run(() => _routePlanner.Plan(request, cts.Token), cts.Token);
            if (!ReferenceEquals(_routeCts, cts)) return;
            for (var i = 0; i < route.Stages.Count; i++)
            {
                var stageItem = BuildStageItem(route.Stages[i], route.ExpiresAt(i), item.Source);
                RouteStages.Add(stageItem);
                RouteSteps.Add(new RouteStepItem
                {
                    Name = stageItem.ShortTitle, Number = i + 1, HasLine = i < route.Stages.Count - 1, Stage = stageItem
                });
            }
            RefreshRouteProgress(expandCurrent: true);
            _currentRoute = route;
            TrackRouteCommand.NotifyCanExecuteChanged();
            SaveRouteCommand.NotifyCanExecuteChanged();
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (ReferenceEquals(_routeCts, cts)) IsRouteBusy = false;
        }
    }

    private static readonly string[] Roman = ["", "Ⅰ", "Ⅱ", "Ⅲ", "Ⅳ", "Ⅴ", "Ⅵ"];
    private static string RomanOf(int tier) => tier >= 0 && tier < Roman.Length ? Roman[tier] : tier.ToString();

    private RouteStageItem BuildStageItem(RouteStage stage, int expiresAt, PlannedLineup final)
    {
        var def = stage.Definition;
        var subtitle = stage.IsFinal
            ? $"{def.People} 人 · 打 Boss 用的阵容"
            : def.Stacking
            ? $"终端 {def.TerminalLevel} · {def.People} 人 · 叠层用：保持大成盟约激活、尽量多上叠层手，打 Boss 前换成大成阵容"
            : $"终端 {def.TerminalLevel} · {RomanOf(def.MaxTier)} 阶及以下 · {def.People} 人"
              + (def.TripleSlots > 0 ? $" · 至多 {def.TripleSlots} 张三连 {RomanOf(def.MaxTier + 1)} 阶" : "");
        var goalNames = stage.Covenants.Where(c => c.IsGoal).Select(c => c.Name).ToHashSet(StringComparer.Ordinal);
        var ops = stage.Operators.Select(o =>
        {
            var relevant = o.Covenants.Where(goalNames.Contains).ToList();
            var roleMarks = new[] { o.IsTank ? "盾" : null, o.IsHealer ? "奶" : null, o.IsStacker ? "叠" : null }.OfType<string>().ToList();
            // Role marks first (盾 / 奶 / 叠), else the covenant that matters for the targets
            var sub = o.TransformedCovenant is not null ? $"转职 {o.TransformedCovenant}"
                : roleMarks.Count > 0 ? string.Join(" · ", roleMarks)
                : string.Join(" ", relevant.Count > 0 ? relevant.Take(1) : o.Covenants.Take(1));
            var extra = new List<string> { o.Role == RouteOperatorRole.Final ? "最终卡：一路保留到大成" : "过渡卡：大成前会被替换" };
            if (o.ViaTriple) extra.Add($"需要三连获得（终端 {def.TerminalLevel} 只出 {RomanOf(def.MaxTier)} 阶及以下）");
            if (o.IsNew) extra.Add("本阶段新加入");
            if (stage.Summary.OperatorLayers.TryGetValue(o.Name, out var opLayers))
                extra.Add($"每回合约为大成盟约叠 {opLayers:0.#} 层（按普通版本估算）");
            var marks = new List<string>();
            if (o.IsTank) marks.Add("盾");
            if (o.IsHealer) marks.Add("奶");
            if (o.IsStacker) marks.Add("叠");
            if (o.TransformedCovenant is not null) extra.Add($"转职 → {o.TransformedCovenant}");
            return new RouteOperatorItem
            {
                Name = o.Name, IconPath = o.IconPath, TierText = RomanOf(o.Tier), Sub = sub,
                IsFinal = o.Role == RouteOperatorRole.Final, ViaTriple = o.ViaTriple, IsNew = o.IsNew,
                Operator = _operatorByName.GetValueOrDefault(o.Name),
                ExtraInfo = string.Join(" · ", extra),
                RoleMarks = string.Join(" ", marks)
            };
        }).ToList();
        var sum = stage.Summary;
        // Covenants as people (“伊比利亚 4”); target covenants are outlined
        var pills = stage.Covenants.Where(c => c.IsGoal || c.Level > 0)
            .OrderByDescending(c => c.IsGoal).ThenByDescending(c => c.Level)
            .Select(c => new RouteCovenantPill($"{c.Name} {c.Count}", c.IsGoal, c.Level > 0)).ToList();
        var conditions = new List<RouteChip>();
        if (!stage.IsFinal && !def.Stacking) conditions.Add(new RouteChip($"≤ {RomanOf(def.MaxTier)} 阶", false, false, $"终端 {def.TerminalLevel} 只出 {RomanOf(def.MaxTier)} 阶及以下"));
        conditions.Add(new RouteChip($"{def.People} 人", false, false));
        if (def.TripleSlots > 0) conditions.Add(new RouteChip($"三连 ≤ {def.TripleSlots}", false, false, $"至多 {def.TripleSlots} 张三连 {RomanOf(def.MaxTier + 1)} 阶"));
        if (def.Stacking) conditions.Add(new RouteChip("叠层", false, false, "保持大成盟约激活、尽量多上叠层手，打 Boss 前换成大成阵容"));
        var needHealer = def.TerminalLevel >= LineupRoutePlanner.HealerFromTerminal;
        var hasHealer = sum.Healers > 0 || sum.HealingCovenant;
        var roles = new List<RouteChip>
        {
            new($"盾 {sum.Tanks}", true, sum.Tanks == 0, "盾位：重装或能稳定阻挡的干员"),
            new(sum.HealingCovenant && sum.Healers == 0 ? "奶 聚合" : $"奶 {sum.Healers}", true, needHealer && !hasHealer,
                needHealer ? "奶位：医疗干员或激活的聚合" : "三本以后需要奶位"),
            new($"叠 {sum.Stackers}", true, def.Stacking && sum.Stackers == 0,
                sum.FinalLayers > 0 ? $"叠层手；为大成盟约每回合约叠 {sum.FinalLayers:0.#} 层" : "叠层手")
        };
        var shortTitle = stage.IsFinal ? "大成" : def.Name.Length > 2 ? def.Name[..2] : def.Name;
        return new RouteStageItem
        {
            Title = stage.IsFinal ? "大成" : def.Name, ShortTitle = shortTitle, Subtitle = subtitle,
            LevelText = stage.IsFinal ? "打 Boss" : $"终端 {def.TerminalLevel}",
            IsFinal = stage.IsFinal, IsStacking = def.Stacking, ExpiresAt = expiresAt,
            Operators = ops, Covenants = pills, Conditions = conditions, Roles = roles,
            ChangeText = stage.Removed.Count == 0 ? "" : $"换下 {string.Join("、", stage.Removed)}",
            WarningText = string.Join("；", sum.Warnings),
            // Pith is there all run; 暴躁铁皮's transformations are done by 后期 (叠层)
            StrategyPiece = final.Strategy == PlannerStrategy.Pith || final.Strategy == PlannerStrategy.Mech
                ? StrategyPieceFor(final, transformsDone: def.Stacking || stage.IsFinal) : null
        };
    }

    public PlannerRequest BuildRequest()
    {
        var bannedCovenants = new HashSet<string>(_banVm.BannedCovenants, StringComparer.Ordinal);
        return new PlannerRequest
        {
            Goals = AllCovenants.Where(c => c.IsSelected).Select(c => LineupPlanner.CreateGoal(c.Info, c.Level)).ToList(),
            BannedCovenants = bannedCovenants,
            BannedOperators = new HashSet<string>(_banVm.BannedNames, StringComparer.Ordinal),
            LockedOperators = LockedOperators.Select(l => l.Name).ToHashSet(StringComparer.Ordinal),
            TransformAllowed = _planner.DefaultTransformAllowed(bannedCovenants).Where(_equipmentRules.CanTransformInto).ToHashSet(),
            PeopleOptions = PeopleOptions,
            TransformOptions = TransformOptions,
            Strategy = Strategy,
            MechElite = MechElite
        };
    }

    /// <summary>Cancels any running search and schedules a new one (debounced). Deferred while the tab is hidden.</summary>
    public void RequestRecompute()
    {
        _cts?.Cancel();
        _cts = null;
        if (!IsActive) { _dirty = true; return; }
        _dirty = false;

        var request = BuildRequest();
        if (request.Goals.Count == 0)
        {
            _lastResult = null;
            IsBusy = false;
            ApplyResult(null, TimeSpan.Zero);
            PendingSearch = Task.CompletedTask;
            return;
        }

        var cts = new CancellationTokenSource();
        _cts = cts;
        PendingSearch = RunSearchAsync(request, cts);
    }

    private async Task RunSearchAsync(PlannerRequest request, CancellationTokenSource cts)
    {
        var token = cts.Token;
        try
        {
            if (DebounceMilliseconds > 0) await Task.Delay(DebounceMilliseconds, token);
            IsBusy = true;
            StatusText = "正在搜索…";
            StatusDetail = "";
            var sw = Stopwatch.StartNew();
            var result = await Task.Run(() => _planner.Plan(request, token), token);
            if (token.IsCancellationRequested || !ReferenceEquals(_cts, cts)) return;
            _lastResult = result;
            ApplyResult(result, sw.Elapsed);
        }
        catch (OperationCanceledException)
        {
            // Superseded by a newer search.
        }
        catch (Exception ex)
        {
            if (!ReferenceEquals(_cts, cts)) return;
            HasError = true;
            StatusText = "规划出错";
            StatusDetail = ex.Message;
        }
        finally
        {
            if (ReferenceEquals(_cts, cts)) IsBusy = false;
        }
    }

    private void ApplyResult(PlannerResult? result, TimeSpan elapsed)
    {
        CloseRoute();
        var previous = SelectedVariant;
        VariantCells.Clear();
        foreach (var p in PeopleOptions)
            foreach (var t in TransformOptions)
            {
                var v = result?.Variants.FirstOrDefault(x => x.People == p && x.Transforms == t);
                VariantCells.Add(new PlannerVariantCell
                {
                    People = p, Transforms = t, Count = v?.Lineups.Count ?? 0,
                    BestScore = v?.Lineups.FirstOrDefault()?.AdditionalScore ?? 0,
                    IsLimited = v?.SearchLimited ?? false
                });
            }
        TotalResults = VariantCells.Sum(c => c.Count);

        SelectedVariant =
            VariantCells.FirstOrDefault(c => previous is not null && c.People == previous.People && c.Transforms == previous.Transforms && c.HasResults)
            ?? VariantCells.Where(c => c.HasResults)
                .OrderByDescending(c => c.BestScore)
                .ThenByDescending(c => c.People)
                .ThenBy(c => c.Transforms)
                .FirstOrDefault()
            ?? VariantCells.FirstOrDefault();
        UpdateStatus(result, elapsed);
    }

    private void UpdateStatus(PlannerResult? result = null, TimeSpan elapsed = default)
    {
        HasError = result?.Error is not null;
        if (SelectedGoalCount == 0)
        {
            StatusText = "请选择目标盟约";
            StatusDetail = "点击 Ⅰ / Ⅱ 选择档位，再点一次取消";
            ResultHint = "";
            return;
        }
        if (result?.Error is { } error)
        {
            StatusText = error;
            StatusDetail = "";
            ResultHint = error;
            return;
        }
        if (result is null) return;

        var solved = VariantCells.Count(c => c.HasResults);
        StatusText = solved == 0 ? "没有找到可行组合" : $"✓ {VariantCells.Count} 组中 {solved} 组有解";
        var detail = $"{elapsed.TotalSeconds:0.0} 秒 · 改动后自动重算";
        if (VariantCells.Any(c => c.IsLimited)) detail += " · 部分组达到搜索上限";
        StatusDetail = detail;

        // The matrix cells already say which combinations have no result; the hint is kept for errors only.
        ResultHint = "";
    }

    private void RebuildVisibleLineups()
    {
        VisibleLineups.Clear();
        var cell = SelectedVariant;
        var variant = _lastResult?.Variants.FirstOrDefault(v => cell is not null && v.People == cell.People && v.Transforms == cell.Transforms);
        if (variant is not null)
        {
            var index = 0;
            foreach (var lineup in variant.Lineups) VisibleLineups.Add(BuildItem(lineup, index++));
        }
        OnPropertyChanged(nameof(HasVisibleLineups));
    }

    private PlannerLineupItem BuildItem(PlannedLineup lineup, int index)
    {
        var goalNames = lineup.Goals.Select(g => g.Name).ToHashSet(StringComparer.Ordinal);
        var ops = lineup.Operators.Select(o =>
        {
            string sub;
            if (o.TransformedCovenant is not null) sub = $"转职 → {o.TransformedCovenant}";
            else
            {
                var relevant = o.NativeCovenants.Where(goalNames.Contains).ToList();
                if (o.IsHarmony && relevant.Count == 0) relevant.Add(CovenantCalculator.HarmonyCovenant);
                sub = string.Join(" · ", relevant.Count > 0 ? relevant : o.NativeCovenants.Take(2));
            }
            var tip = $"{o.Name}\n原生：{string.Join("、", o.NativeCovenants)}";
            if (o.TransformedCovenant is not null) tip += $"\n转入：{o.TransformedCovenant}";
            if (o.IsLocked) tip += "\n必带干员";
            var extra = new List<string>();
            if (o.TransformedCovenant is not null) extra.Add($"转职 → {o.TransformedCovenant}");
            if (o.IsHarmony) extra.Add("调和：阵容内已有的核心盟约各 +1");
            if (o.IsLocked) extra.Add("必带干员");
            return new PlannerOperatorItem
            {
                Name = o.Name, IconPath = o.IconPath, Sub = sub, ToolTip = tip,
                IsLocked = o.IsLocked, IsTransformed = o.TransformedCovenant is not null, IsHarmony = o.IsHarmony,
                Operator = _operatorByName.GetValueOrDefault(o.Name),
                ExtraInfo = string.Join(" · ", extra)
            };
        }).ToList();
        for (var i = 0; i < lineup.FreeSlots; i++)
            ops.Add(new PlannerOperatorItem { Name = "空位", Sub = "任选", IsFree = true, ToolTip = "目标已满足，这个位置可以放任意干员" });

        var goals = BuildActivatedCovenantPills(lineup);

        var subtitle = $"{lineup.People} 人 · {lineup.Transforms} 转 · 附加盟约 {lineup.AdditionalScore} 分";
        if (lineup.FreeSlots > 0) subtitle += $" · 空 {lineup.FreeSlots}";
        var goalText = string.Join(" ", lineup.Goals.Select(g => g.Name + (g.Level == 2 ? "Ⅱ" : "Ⅰ")));
        return new PlannerLineupItem
        {
            Source = lineup,
            Rank = index + 1,
            IsExpanded = index == 0,
            Title = $"方案 {index + 1}",
            Subtitle = subtitle,
            Note = string.Join("；", new[]
            {
                lineup.MissingHealer ? "没有奶位：这些目标下凑不出带医疗 / 治疗干员的阵容" : null,
                lineup.ForcedTransforms > 0 ? $"含 {lineup.ForcedTransforms} 次非必要转职（只为凑满转职次数）" : null
            }.Where(n => n is not null)) is { Length: > 0 } note ? note : null,
            Operators = ops,
            Goals = goals,
            LineupName = RepairSource is { } src ? $"{src.Name}（修补）" : $"规划 {goalText} {lineup.People}人{lineup.Transforms}转",
            RepairText = RepairTextFor(lineup),
            StrategyPiece = StrategyPieceFor(lineup, transformsDone: true)
        };
    }

    /// <summary>暴躁铁皮 or Pith for a result card / route stage (null for other strategies).</summary>
    private PlannerStrategyPiece? StrategyPieceFor(PlannedLineup lineup, bool transformsDone)
    {
        switch (lineup.Strategy)
        {
            case PlannerStrategy.Mech:
                var icon = _allOperatorIcons.GetValueOrDefault(LineupEquipmentService.MechName, "");
                var covenants = transformsDone ? lineup.MechCovenants : [];
                return new PlannerStrategyPiece(LineupEquipmentService.MechName, icon,
                    covenants.Count > 0 ? $"转职 → {string.Join("、", covenants)}" : "协防干员 · 转职在后期叠层前完成",
                    covenants.Count > 0 ? $"转×{covenants.Count}" : "");
            case PlannerStrategy.Pith:
                return new PlannerStrategyPiece(PlannerStrategies.PithName, "", "调和 · 已激活核心盟约各 +1", "");
            default:
                return null;
        }
    }

    private IReadOnlyList<PlannerGoalPill> BuildActivatedCovenantPills(PlannedLineup lineup)
    {
        var pills = new List<PlannerGoalPill>();
        var goalNames = lineup.Goals.Select(g => g.Name).ToHashSet(StringComparer.Ordinal);
        foreach (var covenant in _planner.Covenants)
        {
            var count = lineup.Counts.GetValueOrDefault(covenant.Name);
            var first = LineupPlanner.CreateGoal(covenant, 1);
            if (count < first.Count) continue;

            // Shown as people, e.g. "乌萨斯 6"
            var text = $"{covenant.Name} {count}";
            pills.Add(new PlannerGoalPill(text, false, goalNames.Contains(covenant.Name)));
        }
        // Planned-for covenants first, then the rest in their usual order
        return pills.OrderByDescending(p => p.IsGoal).ToList();
    }
}
