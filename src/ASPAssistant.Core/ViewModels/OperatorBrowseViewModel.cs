// Modified for ASP Assistant Arknights on 2026-11-13; see NOTICE.
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Core.ViewModels;

public enum AvailabilityFilter
{
    All,
    Available,
    Unavailable
}

public partial class OperatorBrowseViewModel : ObservableObject
{
    private List<Operator> _allOperators = [];
    private Func<string, bool>? _isBannedCheck;
    private Func<string, bool>? _isCovenantBanned;

    // ── Filter card chips (nothing selected in a section = all) ────────────
    public ObservableCollection<FilterChip> TierChips { get; } = [];
    public ObservableCollection<FilterChip> CoreChips { get; } = [];
    public ObservableCollection<FilterChip> OtherChips { get; } = [];
    public ObservableCollection<FilterChip> TraitChips { get; } = [];
    public ObservableCollection<FilterChip> TimingChips { get; } = [];

    /// <summary>Selected chips, shown as removable tags while the filter card is collapsed.</summary>
    public IReadOnlyList<FilterChip> ActiveChips =>
        TierChips.Concat(CoreChips).Concat(OtherChips).Concat(TraitChips).Concat(TimingChips).Where(c => c.IsSelected).ToList();

    public bool HasActiveChips => ActiveChips.Count > 0;

    /// <summary>Filter badge count: chips only (the 全部 / 本局可用 / 不可用 switch and search are visible on their own).</summary>
    public int ChipFilterCount => ActiveChips.Count;

    public int MatchedCount => FilteredOperators.Count;

    [ObservableProperty]
    private ObservableCollection<Operator> _filteredOperators = [];

    [ObservableProperty]
    private string _searchText = "";

    /// <summary>Selected tiers (multi-select; empty = 全部).</summary>
    public List<int> SelectedTierFilters
    {
        get => _selectedTierFilters;
        set
        {
            _selectedTierFilters = (value ?? []).Distinct().OrderBy(t => t).ToList();
            ApplyFilters();
        }
    }

    private List<int> _selectedTierFilters = [];

    /// <summary>When true, selected core covenants must all be present (AND) instead of any (OR).</summary>
    [ObservableProperty]
    private bool _coreCovenantMatchAll;

    /// <summary>When true, selected additional covenants must all be present (AND).</summary>
    [ObservableProperty]
    private bool _otherCovenantMatchAll;

    [ObservableProperty]
    private AvailabilityFilter _selectedAvailabilityFilter = AvailabilityFilter.All;

    private readonly HashSet<string> _selectedCoreCovenantFilters = [];
    private readonly HashSet<string> _selectedOtherCovenantFilters = [];

    [ObservableProperty]
    private IReadOnlySet<string> _highlightedCovenants = new HashSet<string>();

    [ObservableProperty]
    private bool _onlyCurrentRun;

    public bool HasRunInfo => HighlightedCovenants.Count > 0;

    [ObservableProperty]
    private string? _selectedTraitTypeFilter;

    [ObservableProperty]
    private string? _selectedTriggerTimingFilter;

    public List<int?> AvailableTiers { get; private set; } = [];
    public ObservableCollection<string?> AvailableCoreCovenants { get; private set; } = [];
    public ObservableCollection<string?> AvailableOtherCovenants { get; private set; } = [];
    public List<string?> AvailableTraitTypes { get; private set; } = [];
    public List<string?> AvailableTriggerTimings { get; private set; } = [];

    /// <summary>
    /// Number of individual filter values currently applied — shown in the filter pill badge.
    /// "|" items are dimension separators produced for <see cref="ActiveFilterTags"/>.
    /// </summary>
    public int ActiveFilterCount => ActiveFilterTags.Count(t => t != "|");

    /// <summary>
    /// Individual tag strings for the active-filter summary bar.
    /// Regular tags are the filter values; "|" items are dimension separators.
    /// </summary>
    public List<string> ActiveFilterTags
    {
        get
        {
            var groups = new List<List<string>>();
            if (SelectedAvailabilityFilter != AvailabilityFilter.All)
                groups.Add([SelectedAvailabilityFilter == AvailabilityFilter.Available ? "本局可用" : "本局不可用"]);
            if (SelectedTierFilters.Count > 0)
                groups.Add([.. SelectedTierFilters.Select(TierToRoman)]);
            var allCovenants = _selectedCoreCovenantFilters.Concat(_selectedOtherCovenantFilters).Order().ToList();
            if (allCovenants.Count > 0)
                groups.Add(allCovenants);
            if (!string.IsNullOrEmpty(SelectedTraitTypeFilter))
                groups.Add([SelectedTraitTypeFilter]);
            if (!string.IsNullOrEmpty(SelectedTriggerTimingFilter))
                groups.Add([$"<{SelectedTriggerTimingFilter}>"]);

            var result = new List<string>();
            for (int i = 0; i < groups.Count; i++)
            {
                if (i > 0) result.Add("|");
                result.AddRange(groups[i]);
            }
            return result;
        }
    }

    private static readonly string[] _romanNumerals = ["", "Ⅰ", "Ⅱ", "Ⅲ", "Ⅳ", "Ⅴ", "Ⅵ"];
    private static string TierToRoman(int tier) =>
        tier >= 1 && tier <= 6 ? _romanNumerals[tier] : tier.ToString();

    public void LoadOperators(List<Operator> operators)
    {
        _allOperators = operators;

        AvailableTiers = [null, .. operators.Select(o => (int?)o.Tier).Distinct().OrderBy(t => t)];

        var coreSet = operators
            .SelectMany(o => o.CoreCovenants)
            .Where(c => !string.IsNullOrEmpty(c))
            .ToHashSet();
        AvailableCoreCovenants = ["", .. coreSet.Order()];
        AvailableOtherCovenants = ["", .. operators
            .SelectMany(o => o.AdditionalCovenants)
            .Where(c => !string.IsNullOrEmpty(c) && !coreSet.Contains(c))
            .Distinct().Order()];

        AvailableTraitTypes = [null, .. operators
            .SelectMany(o => o.Normal.Traits.Select(t => t.TraitType)
                .Concat(o.Elite.Traits.Select(t => t.TraitType)))
            .Where(t => !string.IsNullOrEmpty(t))
            .Distinct().Order()];

        var timingRegex = new Regex(@"<([^>]+)>");
        AvailableTriggerTimings = [null, .. operators
            .SelectMany(o => o.Normal.Traits.Select(t => t.TraitDescription)
                .Concat(o.Elite.Traits.Select(t => t.TraitDescription)))
            .SelectMany(desc => timingRegex.Matches(desc).Select(m => m.Groups[1].Value))
            .Distinct().Order()];

        BuildChips();

        OnPropertyChanged(nameof(AvailableTiers));
        OnPropertyChanged(nameof(AvailableCoreCovenants));
        OnPropertyChanged(nameof(AvailableOtherCovenants));
        OnPropertyChanged(nameof(AvailableTraitTypes));
        OnPropertyChanged(nameof(AvailableTriggerTimings));

        ApplyFilters();
    }

    public void SetBanChecker(Func<string, bool> checker)
    {
        _isBannedCheck = checker;
        RefreshChipCounts();
        ApplyFilters();
    }

    /// <summary>Which covenants this run's info screen banned (for the chips' ban state).</summary>
    public void SetCovenantBanChecker(Func<string, bool> checker)
    {
        _isCovenantBanned = checker;
        RefreshChipCounts();
    }

    /// <summary>
    /// Called when the ban set changes externally (e.g. new bans detected or bans cleared).
    /// Re-runs the filter so the operator list reflects the updated ban state.
    /// </summary>
    public void NotifyBansChanged()
    {
        RefreshChipCounts();
        ApplyFilters();
    }

    private static readonly string[] RomanLabels = ["", "Ⅰ", "Ⅱ", "Ⅲ", "Ⅳ", "Ⅴ", "Ⅵ"];

    private void BuildChips()
    {
        static void Fill(ObservableCollection<FilterChip> target, IEnumerable<FilterChip> chips)
        {
            target.Clear();
            foreach (var chip in chips) target.Add(chip);
        }
        Fill(TierChips, AvailableTiers.OfType<int>().Select(t =>
            new FilterChip(FilterChipKind.Tier, t.ToString(), t is >= 1 and <= 6 ? RomanLabels[t] : t.ToString(), OnChipToggled)));
        Fill(CoreChips, AvailableCoreCovenants.Where(c => !string.IsNullOrEmpty(c))
            .Select(c => new FilterChip(FilterChipKind.CoreCovenant, c!, c!, OnChipToggled)));
        Fill(OtherChips, AvailableOtherCovenants.Where(c => !string.IsNullOrEmpty(c))
            .Select(c => new FilterChip(FilterChipKind.OtherCovenant, c!, c!, OnChipToggled)));
        Fill(TraitChips, AvailableTraitTypes.Where(c => !string.IsNullOrEmpty(c))
            .Select(c => new FilterChip(FilterChipKind.TraitType, c!, c!, OnChipToggled)));
        Fill(TimingChips, AvailableTriggerTimings.Where(c => !string.IsNullOrEmpty(c))
            .Select(c => new FilterChip(FilterChipKind.TriggerTiming, c!, c!, OnChipToggled)));
        foreach (var chip in CoreChips.Concat(OtherChips)) chip.IsHighlighted = HighlightedCovenants.Contains(chip.Value);
        RefreshChipCounts();
    }

    /// <summary>Covenant chips count the holders usable this run; trait chips count all operators having them.</summary>
    private void RefreshChipCounts()
    {
        bool Usable(Operator o) => _isBannedCheck?.Invoke(o.Name) != true;
        foreach (var chip in CoreChips)
        {
            chip.Count = _allOperators.Count(o => o.CoreCovenants.Contains(chip.Value) && Usable(o));
            chip.Ban = _isCovenantBanned?.Invoke(chip.Value) == true ? FilterChipBan.Blocked : FilterChipBan.None;
        }
        foreach (var chip in OtherChips)
        {
            chip.Count = _allOperators.Count(o => o.AdditionalCovenants.Contains(chip.Value) && Usable(o));
            chip.Ban = _isCovenantBanned?.Invoke(chip.Value) == true ? FilterChipBan.Reduced : FilterChipBan.None;
        }
        foreach (var chip in TraitChips)
            chip.Count = _allOperators.Count(o => o.Normal.Traits.Concat(o.Elite.Traits).Any(t => t.TraitType == chip.Value));
        foreach (var chip in TimingChips)
        {
            var tag = $"<{chip.Value}>";
            chip.Count = _allOperators.Count(o => o.Normal.Traits.Concat(o.Elite.Traits).Any(t => t.TraitDescription.Contains(tag)));
        }
    }

    private void OnChipToggled(FilterChip chip)
    {
        switch (chip.Kind)
        {
            case FilterChipKind.Tier:
                _selectedTierFilters = TierChips.Where(c => c.IsSelected).Select(c => int.Parse(c.Value)).Order().ToList();
                break;
            case FilterChipKind.CoreCovenant:
                _selectedCoreCovenantFilters.Clear();
                foreach (var c in CoreChips.Where(c => c.IsSelected)) _selectedCoreCovenantFilters.Add(c.Value);
                break;
            case FilterChipKind.OtherCovenant:
                _selectedOtherCovenantFilters.Clear();
                foreach (var c in OtherChips.Where(c => c.IsSelected)) _selectedOtherCovenantFilters.Add(c.Value);
                break;
            case FilterChipKind.TraitType:
                // single choice: picking one releases the others
                SelectedTraitTypeFilter = chip.IsSelected ? chip.Value : null; // re-runs the filter
                return;
            case FilterChipKind.TriggerTiming:
                SelectedTriggerTimingFilter = chip.IsSelected ? chip.Value : null;
                return;
        }
        ApplyFilters();
    }

    /// <summary>Mirrors the filter state onto the chips (after external changes such as 重置 or the planner).</summary>
    private void SyncChips()
    {
        foreach (var c in TierChips) c.SetSelected(int.TryParse(c.Value, out var t) && _selectedTierFilters.Contains(t));
        foreach (var c in CoreChips) c.SetSelected(_selectedCoreCovenantFilters.Contains(c.Value));
        foreach (var c in OtherChips) c.SetSelected(_selectedOtherCovenantFilters.Contains(c.Value));
        foreach (var c in TraitChips) c.SetSelected(c.Value == SelectedTraitTypeFilter);
        foreach (var c in TimingChips) c.SetSelected(c.Value == SelectedTriggerTimingFilter);
    }

    /// <summary>Removes one condition (the ✕ on a collapsed tag).</summary>
    [CommunityToolkit.Mvvm.Input.RelayCommand]
    public void RemoveChip(FilterChip chip) => chip.IsSelected = false;

    public void SetAvailableCovenants(IEnumerable<string> names)
        => HighlightedCovenants = names.ToHashSet(StringComparer.Ordinal);

    public void ClearCovenantHighlights()
        => HighlightedCovenants = new HashSet<string>();

    partial void OnHighlightedCovenantsChanged(IReadOnlySet<string> value)
    {
        if (value.Count == 0) OnlyCurrentRun = false;
        OnPropertyChanged(nameof(HasRunInfo));
        RefreshCovenantChoices();
        foreach (var chip in CoreChips.Concat(OtherChips)) chip.IsHighlighted = value.Contains(chip.Value);
    }

    partial void OnOnlyCurrentRunChanged(bool value) => RefreshCovenantChoices();

    private void RefreshCovenantChoices()
    {
        var core = _allOperators.SelectMany(o => o.CoreCovenants).Where(c => c.Length > 0).ToHashSet();
        var other = _allOperators.SelectMany(o => o.AdditionalCovenants).Where(c => c.Length > 0 && !core.Contains(c));
        UpdateChoices(AvailableCoreCovenants, ["", .. core.Where(c => !OnlyCurrentRun || HighlightedCovenants.Contains(c)).Order()]);
        UpdateChoices(AvailableOtherCovenants, ["", .. other.Where(c => !OnlyCurrentRun || HighlightedCovenants.Contains(c)).Distinct().Order()]);
        _selectedCoreCovenantFilters.RemoveWhere(c => !AvailableCoreCovenants.Contains(c));
        _selectedOtherCovenantFilters.RemoveWhere(c => !AvailableOtherCovenants.Contains(c));
        // 只看本局: the covenant chips follow the choices, keeping existing chips (and their state) in place
        SyncCovenantChips(CoreChips, AvailableCoreCovenants, FilterChipKind.CoreCovenant);
        SyncCovenantChips(OtherChips, AvailableOtherCovenants, FilterChipKind.OtherCovenant);
        RefreshChipCounts();
        ApplyFilters();
    }

    private void SyncCovenantChips(ObservableCollection<FilterChip> chips, IEnumerable<string?> choices, FilterChipKind kind)
    {
        var wanted = choices.Where(c => !string.IsNullOrEmpty(c)).Select(c => c!).ToList();
        foreach (var chip in chips.Where(c => !wanted.Contains(c.Value)).ToList()) chips.Remove(chip);
        for (var i = 0; i < wanted.Count; i++)
        {
            var index = chips.ToList().FindIndex(c => c.Value == wanted[i]);
            if (index < 0)
                chips.Insert(i, new FilterChip(kind, wanted[i], wanted[i], OnChipToggled) { IsHighlighted = HighlightedCovenants.Contains(wanted[i]) });
            else if (index != i) chips.Move(index, i);
        }
    }

    private static void UpdateChoices(ObservableCollection<string?> choices, List<string?> wanted)
    {
        foreach (var item in choices.Where(c => !wanted.Contains(c)).ToList()) choices.Remove(item);
        for (var i = 0; i < wanted.Count; i++)
        {
            var oldIndex = choices.IndexOf(wanted[i]);
            if (oldIndex < 0) choices.Insert(i, wanted[i]);
            else if (oldIndex != i) choices.Move(oldIndex, i);
        }
    }

    partial void OnSearchTextChanged(string value) => ApplyFilters();
    partial void OnCoreCovenantMatchAllChanged(bool value) => ApplyFilters();
    partial void OnOtherCovenantMatchAllChanged(bool value) => ApplyFilters();
    partial void OnSelectedTraitTypeFilterChanged(string? value) => ApplyFilters();
    partial void OnSelectedTriggerTimingFilterChanged(string? value) => ApplyFilters();
    partial void OnSelectedAvailabilityFilterChanged(AvailabilityFilter value) => ApplyFilters();

    public void SetCoreCovenantFilters(IEnumerable<string> covenants)
    {
        _selectedCoreCovenantFilters.Clear();
        foreach (var c in covenants)
            _selectedCoreCovenantFilters.Add(c);
        ApplyFilters();
    }

    public void SetOtherCovenantFilters(IEnumerable<string> covenants)
    {
        _selectedOtherCovenantFilters.Clear();
        foreach (var c in covenants)
            _selectedOtherCovenantFilters.Add(c);
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        var filtered = _allOperators.AsEnumerable();

        if (SelectedAvailabilityFilter != AvailabilityFilter.All && _isBannedCheck != null)
        {
            filtered = SelectedAvailabilityFilter == AvailabilityFilter.Unavailable
                ? filtered.Where(o => _isBannedCheck(o.Name))
                : filtered.Where(o => !_isBannedCheck(o.Name));
        }

        if (SelectedTierFilters.Count > 0)
        {
            var tiers = SelectedTierFilters;
            filtered = filtered.Where(o => tiers.Contains(o.Tier));
        }

        // Core and other covenant dimensions are independent (AND between dimensions,
        // OR within each dimension).
        if (_selectedCoreCovenantFilters.Count > 0)
        {
            filtered = CoreCovenantMatchAll
                ? filtered.Where(o => _selectedCoreCovenantFilters.All(c => o.CoreCovenants.Contains(c)))
                : filtered.Where(o => o.CoreCovenants.Any(c => _selectedCoreCovenantFilters.Contains(c)));
        }

        if (_selectedOtherCovenantFilters.Count > 0)
        {
            filtered = OtherCovenantMatchAll
                ? filtered.Where(o => _selectedOtherCovenantFilters.All(c => o.AdditionalCovenants.Contains(c)))
                : filtered.Where(o => o.AdditionalCovenants.Any(c => _selectedOtherCovenantFilters.Contains(c)));
        }

        if (!string.IsNullOrEmpty(SelectedTraitTypeFilter))
            filtered = filtered.Where(o =>
                o.Normal.Traits.Any(t => t.TraitType == SelectedTraitTypeFilter) ||
                o.Elite.Traits.Any(t => t.TraitType == SelectedTraitTypeFilter));

        if (!string.IsNullOrEmpty(SelectedTriggerTimingFilter))
        {
            var tag = $"<{SelectedTriggerTimingFilter}>";
            filtered = filtered.Where(o =>
                o.Normal.Traits.Any(t => t.TraitDescription.Contains(tag)) ||
                o.Elite.Traits.Any(t => t.TraitDescription.Contains(tag)));
        }

        if (!string.IsNullOrEmpty(SearchText))
            filtered = filtered.Where(o => SearchMatch.Matches(SearchText, o.Name));

        SyncList(FilteredOperators, filtered.ToList());
        SyncChips();
        OnPropertyChanged(nameof(ActiveChips));
        OnPropertyChanged(nameof(HasActiveChips));
        OnPropertyChanged(nameof(ChipFilterCount));
        OnPropertyChanged(nameof(MatchedCount));
        OnPropertyChanged(nameof(ActiveFilterTags));
        OnPropertyChanged(nameof(ActiveFilterCount));
        OnPropertyChanged(nameof(EmptyListMessage));
    }

    /// <summary>
    /// Updates the shown list in place: only operators that leave or join are removed / inserted, so switching
    /// 全部 / 本局可用 / 不可用 keeps every unchanged row (and its rendered card) instead of rebuilding the list.
    /// </summary>
    internal static void SyncList<T>(ObservableCollection<T> target, IReadOnlyList<T> wanted) where T : class
    {
        var keep = new HashSet<T>(wanted, ReferenceEqualityComparer.Instance);
        for (var i = target.Count - 1; i >= 0; i--)
            if (!keep.Contains(target[i])) target.RemoveAt(i);
        for (var i = 0; i < wanted.Count; i++)
        {
            if (i < target.Count && ReferenceEquals(target[i], wanted[i])) continue;
            var at = -1;
            for (var j = i + 1; j < target.Count; j++)
                if (ReferenceEquals(target[j], wanted[i])) { at = j; break; }
            if (at >= 0) target.Move(at, i);
            else target.Insert(i, wanted[i]);
        }
        while (target.Count > wanted.Count) target.RemoveAt(target.Count - 1);
    }

    /// <summary>提示文案：勾选「同时选中」而结果为空时，说明是“同时满足”导致的。</summary>
    public string EmptyListMessage =>
        CoreCovenantMatchAll || OtherCovenantMatchAll
            ? "没有同时满足所选条件的干员"
            : "没有符合条件的干员";

    /// <summary>把全部筛选恢复为默认（各行回到“全部”、搜索清空、同时选中取消）。可用盟约高亮不属于筛选，保持不动。</summary>
    public void ResetFilters()
    {
        SearchText = "";
        SelectedAvailabilityFilter = AvailabilityFilter.All;
        _selectedTierFilters = [];
        _selectedCoreCovenantFilters.Clear();
        _selectedOtherCovenantFilters.Clear();
        SelectedTraitTypeFilter = null;
        SelectedTriggerTimingFilter = null;
        CoreCovenantMatchAll = false;
        OtherCovenantMatchAll = false;
        ApplyFilters();
    }
}
