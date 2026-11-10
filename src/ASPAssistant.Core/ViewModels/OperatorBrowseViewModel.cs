// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
using System.Collections.ObjectModel;
using System.Text.RegularExpressions;
using CommunityToolkit.Mvvm.ComponentModel;
using ASPAssistant.Core.Models;

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
    private string? _selectedTraitTypeFilter;

    [ObservableProperty]
    private string? _selectedTriggerTimingFilter;

    public List<int?> AvailableTiers { get; private set; } = [];
    public List<string?> AvailableCoreCovenants { get; private set; } = [];
    public List<string?> AvailableOtherCovenants { get; private set; } = [];
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
        ApplyFilters();
    }

    /// <summary>
    /// Called when the ban set changes externally (e.g. new bans detected or bans cleared).
    /// Re-runs the filter so the operator list reflects the updated ban state.
    /// </summary>
    public void NotifyBansChanged() => ApplyFilters();

    public void SetAvailableCovenants(IEnumerable<string> names)
        => HighlightedCovenants = names.ToHashSet(StringComparer.Ordinal);

    public void ClearCovenantHighlights()
        => HighlightedCovenants = new HashSet<string>();

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
            filtered = filtered.Where(o =>
                o.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        FilteredOperators = new ObservableCollection<Operator>(filtered);
        OnPropertyChanged(nameof(ActiveFilterTags));
        OnPropertyChanged(nameof(ActiveFilterCount));
        OnPropertyChanged(nameof(EmptyListMessage));
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
