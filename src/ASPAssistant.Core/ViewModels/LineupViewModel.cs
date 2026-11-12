// Modified for ASP Assistant Arknights on 2026-11-13; see NOTICE.
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Core.ViewModels;

/// <summary>
/// 已保存阵容列表 + 增删改 + 激活盟约统计访问。
/// </summary>
public partial class LineupViewModel : ObservableObject
{
    private readonly LineupStore _store;
    public CovenantCalculator Calculator { get; }

    [ObservableProperty]
    private ObservableCollection<Lineup> _lineups = [];

    public LineupViewModel(LineupStore store, CovenantCalculator calculator)
    {
        _store = store;
        Calculator = calculator;
        Lineups.CollectionChanged += (_, _) => Refresh();
    }

    partial void OnLineupsChanged(ObservableCollection<Lineup> value)
    {
        value.CollectionChanged += (_, _) => Refresh();
        Refresh();
    }

    public void LoadLineups(IEnumerable<Lineup> lineups)
    {
        Lineups = new ObservableCollection<Lineup>(
            lineups.OrderByDescending(l => l.UpdatedAt));
    }

    // ── 搜索与目标盟约筛选 ──────────────────────────────────────────────

    /// <summary>Lineups left by the search and the target-covenant filter, newest first.</summary>
    public ObservableCollection<Lineup> FilteredLineups { get; } = [];

    /// <summary>Target covenants picked in the 筛选 panel.</summary>
    public ObservableCollection<string> FilterCovenants { get; } = [];

    [ObservableProperty] private string _searchText = "";
    /// <summary>0 = any tier, 1 / 2 = only targets at that tier.</summary>
    [ObservableProperty] private int _filterTier;
    /// <summary>True: every picked covenant must be a target; false: any one.</summary>
    [ObservableProperty] private bool _filterMatchAll = true;

    partial void OnSearchTextChanged(string value) => Refresh();
    partial void OnFilterTierChanged(int value) => Refresh();
    partial void OnFilterMatchAllChanged(bool value) => Refresh();

    public int ActiveFilterCount => FilterCovenants.Count + (FilterTier > 0 ? 1 : 0);
    /// <summary>Same as <see cref="ActiveFilterCount"/>; the shared 筛选 pill style binds this name.</summary>
    public int ChipFilterCount => ActiveFilterCount;
    public bool HasActiveFilters => ActiveFilterCount > 0;

    /// <summary>筛选面板的核心盟约（固定顺序）和附加盟约（以它为目标的阵容多的在前）。</summary>
    public ObservableCollection<LineupFilterChip> CoreChips { get; } = [];
    public ObservableCollection<LineupFilterChip> ExtraChips { get; } = [];

    /// <summary>Removable tags shown under the search box while the panel is closed.</summary>
    public ObservableCollection<string> ActiveTags { get; } = [];

    private void RefreshChips()
    {
        var counts = Calculator.AllCovenants.ToDictionary(c => c.Name, c => 0, StringComparer.Ordinal);
        foreach (var l in Lineups)
            foreach (var t in LineupTargets.Of(l, Calculator))
                if (counts.ContainsKey(t.Name)) counts[t.Name]++;
        LineupFilterChip Chip(CovenantInfo c) => new(c.Name, FilterCovenants.Contains(c.Name), counts[c.Name] == 0, ToggleFilterCovenant);
        Replace(CoreChips, Calculator.AllCovenants.Where(c => c.BondType == "SEASON").Select(Chip));
        Replace(ExtraChips, Calculator.AllCovenants.Where(c => c.BondType != "SEASON")
            .OrderByDescending(c => counts[c.Name]).ThenBy(c => c.Name, StringComparer.Ordinal).Select(Chip));
        ActiveTags.Clear();
        foreach (var name in FilterCovenants) ActiveTags.Add(name);
        if (FilterTier > 0) ActiveTags.Add(FilterTier == 2 ? "仅 Ⅱ 档" : "仅 Ⅰ 档");
    }

    private static void Replace<T>(ObservableCollection<T> list, IEnumerable<T> items)
    {
        list.Clear();
        foreach (var item in items) list.Add(item);
    }

    /// <summary>Removes one tag from <see cref="ActiveTags"/>.</summary>
    public void RemoveTag(string tag)
    {
        if (tag.StartsWith("仅 ")) FilterTier = 0;
        else if (FilterCovenants.Remove(tag)) Refresh();
    }

    public void ToggleFilterCovenant(string name)
    {
        if (!FilterCovenants.Remove(name)) FilterCovenants.Add(name);
        Refresh();
    }

    public void ClearFilters()
    {
        FilterCovenants.Clear();
        FilterTier = 0;
        FilterMatchAll = true;
        Refresh();
    }

    /// <summary>How many saved lineups have <paramref name="covenant"/> as a target (0 = the chip is disabled).</summary>
    public int TargetCount(string covenant) => Lineups.Count(l => LineupTargets.Of(l, Calculator).Any(t => t.Name == covenant));

    public IReadOnlyList<TargetCovenant> TargetsOf(Lineup lineup) => LineupTargets.Of(lineup, Calculator);

    private bool MatchesSearch(Lineup lineup)
    {
        var q = SearchText.Trim();
        if (q.Length == 0) return true;
        return lineup.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
            || lineup.Slots.Concat(lineup.EarlySlots).Concat(lineup.MidSlots).Concat(lineup.SupportSlots)
                .Any(s => s.OperatorName.Contains(q, StringComparison.OrdinalIgnoreCase));
    }

    // ── 本局推荐 ──────────────────────────────────────────────────────────

    private Func<string, bool>? _unavailable;
    private IReadOnlySet<string> _bannedCovenants = new HashSet<string>();

    /// <summary>Saved lineups ranked against this run's bans; empty until the opening information is recorded.</summary>
    public ObservableCollection<LineupRecommendation> Recommendations { get; } = [];

    [ObservableProperty] private bool _hasRunInfo;

    /// <summary>This run's banned operators and incomplete core covenants (the 本局禁用 strip).</summary>
    public IReadOnlyList<string> RunBannedNames { get; private set; } = [];
    public IReadOnlyCollection<string> RunBannedCovenants => _bannedCovenants;

    /// <summary>
    /// This run's bans: <paramref name="unavailable"/> tells whether an operator is out (banned, or every covenant of
    /// theirs is banned). Null clears the run (no recommendations).
    /// </summary>
    public void SetRunInfo(Func<string, bool>? unavailable, IReadOnlySet<string>? bannedCovenants,
        IReadOnlyCollection<string>? bannedNames = null)
    {
        _unavailable = unavailable;
        _bannedCovenants = bannedCovenants ?? new HashSet<string>();
        RunBannedNames = unavailable is null ? [] : [.. bannedNames ?? []];
        HasRunInfo = unavailable is not null;
        Refresh();
    }

    public LineupRecommendation? RecommendationFor(Lineup lineup) => Recommendations.FirstOrDefault(r => r.Lineup.Id == lineup.Id);

    private void Refresh()
    {
        FilteredLineups.Clear();
        foreach (var l in Lineups.Where(MatchesSearch)
                     .Where(l => LineupTargets.Matches(l, Calculator, FilterCovenants, FilterTier, FilterMatchAll)))
            FilteredLineups.Add(l);
        Recommendations.Clear();
        if (_unavailable is { } unavailable)
            foreach (var r in LineupTargets.Rank(Lineups, Calculator, unavailable, _bannedCovenants))
                Recommendations.Add(r);
        RefreshChips();
        OnPropertyChanged(nameof(ActiveFilterCount));
        OnPropertyChanged(nameof(ChipFilterCount));
        OnPropertyChanged(nameof(HasActiveFilters));
    }

    /// <summary>新增或更新阵容，并落盘。</summary>
    public async Task AddOrUpdateAsync(Lineup lineup)
    {
        if (string.IsNullOrEmpty(lineup.Id))
            lineup.Id = Guid.NewGuid().ToString("N");
        lineup.UpdatedAt = DateTime.UtcNow;

        await _store.SaveAsync(lineup);

        var existing = Lineups.FirstOrDefault(l => l.Id == lineup.Id);
        if (existing is not null)
        {
            int idx = Lineups.IndexOf(existing);
            Lineups[idx] = lineup;
        }
        else
        {
            Lineups.Insert(0, lineup);
        }
    }

    public async Task DeleteAsync(Lineup lineup)
    {
        await _store.DeleteAsync(lineup.Id);
        Lineups.Remove(lineup);
    }

    public int CountActivated(Lineup lineup) => Calculator.CountActivated(lineup);

    public IReadOnlyList<CovenantStat> ComputeStats(Lineup lineup) => Calculator.Compute(lineup);
}

/// <summary>One covenant in the 阵容 筛选 panel (same shape as the operator page's chips).</summary>
public sealed class LineupFilterChip(string name, bool selected, bool blocked, Action<string> toggle) : ObservableObject
{
    public string Label { get; } = name;
    public string CountText => "";
    public bool IsHighlighted => false;
    public bool IsReduced => false;
    /// <summary>No saved lineup has this target: shown dashed and not clickable.</summary>
    public bool IsBlocked { get; } = blocked;

    public bool IsSelected
    {
        get => selected;
        set
        {
            if (value == selected) return;
            selected = value;
            toggle(Label);
        }
    }
}
