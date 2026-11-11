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

    public PlannerCovenantItem(CovenantInfo info, Action<PlannerCovenantItem, int> onToggle)
    {
        Info = info;
        _onToggle = onToggle;
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
    public bool CanTransformInto => !LineupPlanner.NoTransform.Contains(Name);
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

    public Lineup ToLineup() => new()
    {
        Name = LineupName,
        Slots = Source.Operators.Select(o => new LineupSlot
        {
            OperatorName = o.Name,
            TransformedCovenant = o.TransformedCovenant ?? ""
        }).ToList()
    };
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

public sealed partial class RouteStageItem : ObservableObject
{
    /// <summary>Terminal level from which this stage is behind the player (0 = never).</summary>
    public int ExpiresAt { get; init; }

    /// <summary>The current terminal level is already past this stage (its 过渡 cards are no longer tracked).</summary>
    [ObservableProperty]
    private bool _isPast;

    public string Title { get; init; } = "";
    public string Subtitle { get; init; } = "";
    public bool IsFinal { get; init; }
    public IReadOnlyList<RouteOperatorItem> Operators { get; init; } = [];
    public IReadOnlyList<RouteCovenantPill> Covenants { get; init; } = [];
    public string ChangeText { get; init; } = "";
    public bool HasChange => ChangeText.Length > 0;
    /// <summary>“强度 10 分 · 盾 1 · 奶 1 · 叠层手 5”.</summary>
    public string SummaryText { get; init; } = "";
    public string WarningText { get; init; } = "";
    public bool HasWarning => WarningText.Length > 0;
}

/// <summary>
/// State of the 规划 tab: covenant targets, locked operators, debounced/cancellable search and result browsing.
/// </summary>
public partial class LineupPlannerViewModel : ObservableObject
{
    private static readonly int[] PeopleOptions = [8, 9];
    private static readonly int[] TransformOptions = [0, 1, 2];

    private readonly LineupPlanner _planner;
    private readonly LineupRoutePlanner _routePlanner;
    private CancellationTokenSource? _routeCts;
    private readonly BanViewModel _banVm;
    private readonly IReadOnlyList<Operator> _operators;
    private readonly Dictionary<string, Operator> _operatorByName;
    private IReadOnlySet<string> _availableCovenants = new HashSet<string>();
    private PlannerResult? _lastResult;
    private CancellationTokenSource? _cts;
    private bool _dirty;
    private bool _isActive;

    public LineupPlannerViewModel(LineupPlanner planner, IReadOnlyList<Operator> operators, BanViewModel banVm)
    {
        _planner = planner;
        _routePlanner = new LineupRoutePlanner(operators, planner.Covenants);
        _banVm = banVm;
        _operators = operators;
        _operatorByName = operators.GroupBy(o => o.Name).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        foreach (var c in planner.Covenants)
            (c.BondType == "SEASON" ? CoreCovenants : ExtraCovenants).Add(new PlannerCovenantItem(c, OnToggleLevel));
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

    partial void OnCurrentTerminalLevelChanged(int value)
    {
        foreach (var stage in RouteStages)
            stage.IsPast = stage.ExpiresAt > 0 && value >= stage.ExpiresAt;
    }
    public ObservableCollection<RouteStageItem> RouteStages { get; } = [];
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
        if (item is not null) SaveRequested?.Invoke(item.ToLineup());
    }

    [RelayCommand]
    private void TrackLineup(PlannerLineupItem? item)
    {
        if (item is not null) TrackRequested?.Invoke(item.ToLineup());
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
        _currentRoute = null;
        TrackRouteCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Tracks every operator of the shown route: 大成 green, 过渡 orange.</summary>
    [RelayCommand(CanExecute = nameof(CanTrackRoute))]
    private void TrackRoute()
    {
        if (_currentRoute is not null) TrackRouteRequested?.Invoke(_currentRoute);
    }

    private bool CanTrackRoute() => _currentRoute is not null;

    /// <summary>Plans 开局 → 大成 transition lineups for one candidate.</summary>
    [RelayCommand]
    private Task ShowRouteFor(PlannerLineupItem? item) => item is null ? Task.CompletedTask : PlanRouteAsync(item);

    public async Task PlanRouteAsync(PlannerLineupItem item)
    {
        _routeCts?.Cancel();
        var cts = new CancellationTokenSource();
        _routeCts = cts;
        RouteStages.Clear();
        _currentRoute = null;
        TrackRouteCommand.NotifyCanExecuteChanged();
        RouteTitle = $"{item.Title} 的道中路线";
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
                var stageItem = BuildStageItem(route.Stages[i], route.ExpiresAt(i));
                stageItem.IsPast = route.IsPast(i, CurrentTerminalLevel);
                RouteStages.Add(stageItem);
            }
            _currentRoute = route;
            TrackRouteCommand.NotifyCanExecuteChanged();
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

    private RouteStageItem BuildStageItem(RouteStage stage, int expiresAt)
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
            var sub = o.TransformedCovenant is not null ? $"转→{o.TransformedCovenant}"
                : string.Join(" ", relevant.Count > 0 ? relevant : o.Covenants.Take(1));
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
        var summaryText = $"强度 {sum.Points} 分 · 盾 {sum.Tanks} · 奶 {sum.Healers}" + (sum.HealingCovenant ? "（+聚合）" : "")
                          + $" · 叠层手 {sum.Stackers} · 大成盟约约 {sum.FinalLayers:0.#} 层/回合";
        var pills = stage.Covenants.Select(c =>
        {
            var level = c.Level switch { 2 => " Ⅱ", 1 => " Ⅰ", _ => "" };
            var text = c.IsGoal ? $"{c.Name} {c.Count}/{c.Target}{level}" : $"{c.Name} {c.Count}{level}";
            return new RouteCovenantPill(text, c.IsGoal, c.Level > 0);
        }).ToList();
        var added = stage.Operators.Where(o => o.IsNew).Select(o => o.Name).ToList();
        var change = stage.Removed.Count == 0 ? ""
            : $"换下 {string.Join("、", stage.Removed)}" + (added.Count > 0 ? $"，换上 {string.Join("、", added)}" : "");
        return new RouteStageItem
        {
            Title = def.Name, Subtitle = subtitle, IsFinal = stage.IsFinal, ExpiresAt = expiresAt,
            Operators = ops, Covenants = pills, ChangeText = change,
            SummaryText = summaryText,
            WarningText = string.Join("；", sum.Warnings)
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
            TransformAllowed = _planner.DefaultTransformAllowed(bannedCovenants),
            PeopleOptions = PeopleOptions,
            TransformOptions = TransformOptions
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
            LineupName = $"规划 {goalText} {lineup.People}人{lineup.Transforms}转"
        };
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
