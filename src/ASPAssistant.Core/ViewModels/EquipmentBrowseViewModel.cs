// Modified for ASP Assistant Arknights on 2026-11-12; see NOTICE.
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.ViewModels;

public partial class EquipmentBrowseViewModel : ObservableObject
{
    private List<Equipment> _allEquipment = [];

    // ── Filter card chips (nothing selected = all) ─────────────────────────
    public ObservableCollection<FilterChip> TierChips { get; } = [];
    public ObservableCollection<FilterChip> CategoryChips { get; } = [];

    public IReadOnlyList<FilterChip> ActiveChips => TierChips.Concat(CategoryChips).Where(c => c.IsSelected).ToList();
    public bool HasActiveChips => ActiveChips.Count > 0;
    public int ChipFilterCount => ActiveChips.Count;
    public int MatchedCount => FilteredEquipment.Count;

    /// <summary>0 = 全部, 1 = 普通, 2 = 转职 (the switch outside the filter card).</summary>
    public int TypeIndex
    {
        get => SelectedTypeFilter switch { "普通" => 1, "转职" => 2, _ => 0 };
        set => SelectedTypeFilter = value switch { 1 => "普通", 2 => "转职", _ => "" };
    }

    [CommunityToolkit.Mvvm.Input.RelayCommand]
    public void RemoveChip(FilterChip chip) => chip.IsSelected = false;

    private static readonly string[] RomanLabels = ["", "Ⅰ", "Ⅱ", "Ⅲ", "Ⅳ", "Ⅴ", "Ⅵ"];

    private void BuildChips()
    {
        TierChips.Clear();
        foreach (var t in AvailableTiers.OfType<int>())
            TierChips.Add(new FilterChip(FilterChipKind.Tier, t.ToString(), t is >= 1 and <= 6 ? RomanLabels[t] : t.ToString(), OnChipToggled));
        CategoryChips.Clear();
        foreach (var c in AvailableCategories.Where(c => c.Length > 0))
            CategoryChips.Add(new FilterChip(FilterChipKind.Category, c, c, OnChipToggled)
                { Count = _allEquipment.Count(e => e.Categories.Contains(c)) });
    }

    private void OnChipToggled(FilterChip chip)
    {
        if (chip.Kind == FilterChipKind.Tier)
            _selectedTierFilters = TierChips.Where(c => c.IsSelected).Select(c => int.Parse(c.Value)).Order().ToList();
        else
            _selectedCategoryFilters = CategoryChips.Where(c => c.IsSelected).Select(c => c.Value).ToList();
        ApplyFilters();
    }

    private void SyncChips()
    {
        foreach (var c in TierChips) c.SetSelected(int.TryParse(c.Value, out var t) && _selectedTierFilters.Contains(t));
        foreach (var c in CategoryChips) c.SetSelected(_selectedCategoryFilters.Contains(c.Value));
    }

    [ObservableProperty]
    private ObservableCollection<Equipment> _filteredEquipment = [];

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

    /// <summary>When true, items must belong to every selected category (AND) instead of any (OR).</summary>
    [ObservableProperty]
    private bool _categoryMatchAll;

    [ObservableProperty]
    private string _selectedTypeFilter = "";

    /// <summary>
    /// Selected equipment function categories (multi-select; empty = 全部). An item belongs to
    /// one or more categories, so it can appear under several selected values.
    /// </summary>
    public List<string> SelectedCategoryFilters
    {
        get => _selectedCategoryFilters;
        set
        {
            _selectedCategoryFilters = (value ?? []).Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList();
            ApplyFilters();
        }
    }

    private List<string> _selectedCategoryFilters = [];

    /// <summary>Fixed display order of the function categories.</summary>
    public static readonly string[] CategoryOrder = ["攻击", "防御", "生存", "技力", "经济", "盟约", "其他"];

    public List<string> AvailableCategories { get; private set; } = [];

    public List<int?> AvailableTiers { get; private set; } = [];
    public static List<string> AvailableTypes { get; } = ["", "普通", "转职"];

    /// <summary>Number of filter values currently applied — shown in the filter pill badge.</summary>
    public int ActiveFilterCount =>
        (SelectedTierFilters.Count > 0 ? 1 : 0)
        + (string.IsNullOrEmpty(SelectedTypeFilter) ? 0 : 1)
        + SelectedCategoryFilters.Count;

    public void LoadEquipment(
        List<Equipment> equipment,
        IEnumerable<string> covenantNames,
        IEnumerable<string> manualJobChangeNames,
        IReadOnlyDictionary<string, string> jobChangeEquipmentToCovenant)
    {
        var covenants = covenantNames
            .Where(c => !string.IsNullOrEmpty(c))
            .ToList();
        var manualSet = new HashSet<string>(manualJobChangeNames, StringComparer.Ordinal);

        foreach (var e in equipment)
        {
            var nameCovenant = covenants.FirstOrDefault(c =>
                e.Name.Contains(c, StringComparison.Ordinal));

            e.Type = (nameCovenant != null || manualSet.Contains(e.Name)) ? EquipmentType.JobChange
                   : EquipmentType.Normal;
            e.JobChangeCovenant = e.Type == EquipmentType.JobChange
                ? (jobChangeEquipmentToCovenant.TryGetValue(e.Name, out var covenant) ? covenant : nameCovenant ?? "")
                : "";
        }

        _allEquipment = equipment;
        AvailableTiers = [null, .. equipment.Select(e => (int?)e.Tier).Distinct().OrderBy(t => t)];
        OnPropertyChanged(nameof(AvailableTiers));

        AvailableCategories = ["", .. CategoryOrder.Where(c => equipment.Any(e => e.Categories.Contains(c)))];
        OnPropertyChanged(nameof(AvailableCategories));
        var stillAvailable = SelectedCategoryFilters.Where(AvailableCategories.Contains).ToList();
        if (stillAvailable.Count != SelectedCategoryFilters.Count)
            _selectedCategoryFilters = stillAvailable;
        _allEquipment = equipment;
        BuildChips();

        ApplyFilters();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilters();
    partial void OnCategoryMatchAllChanged(bool value) => ApplyFilters();
    partial void OnSelectedTypeFilterChanged(string value)
    {
        OnPropertyChanged(nameof(TypeIndex));
        ApplyFilters();
    }

    private void ApplyFilters()
    {
        var filtered = _allEquipment.AsEnumerable();

        if (SelectedTierFilters.Count > 0)
        {
            var tiers = SelectedTierFilters;
            filtered = filtered.Where(e => tiers.Contains(e.Tier));
        }

        if (!string.IsNullOrEmpty(SelectedTypeFilter))
        {
            var t = SelectedTypeFilter switch
            {
                "普通" => EquipmentType.Normal,
                "转职" => EquipmentType.JobChange,
                _ => (EquipmentType?)null
            };
            if (t.HasValue)
                filtered = filtered.Where(e => e.Type == t.Value);
        }

        // Function category: an item may belong to several categories, so it can show up
        // under more than one filter value.
        if (SelectedCategoryFilters.Count > 0)
        {
            var selected = SelectedCategoryFilters;
            filtered = CategoryMatchAll
                ? filtered.Where(e => selected.All(e.Categories.Contains))
                : filtered.Where(e => e.Categories.Any(selected.Contains));
        }

        // Name or effect text ("搜索装备或效果").
        if (!string.IsNullOrEmpty(SearchText))
            filtered = filtered.Where(e =>
                e.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                || e.Normal.EffectDescription.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                || e.Elite.EffectDescription.Contains(SearchText, StringComparison.OrdinalIgnoreCase));

        OperatorBrowseViewModel.SyncList(FilteredEquipment, filtered.ToList());
        SyncChips();
        OnPropertyChanged(nameof(ActiveChips));
        OnPropertyChanged(nameof(HasActiveChips));
        OnPropertyChanged(nameof(ChipFilterCount));
        OnPropertyChanged(nameof(MatchedCount));
        OnPropertyChanged(nameof(ActiveFilterCount));
        OnPropertyChanged(nameof(EmptyListMessage));
    }

    /// <summary>提示文案：勾选「同时选中」而结果为空时，说明是“同时满足”导致的。</summary>
    public string EmptyListMessage =>
        CategoryMatchAll ? "没有同时满足所选条件的装备" : "没有符合条件的装备";

    /// <summary>把全部筛选恢复为默认（等阶/类型/装备类别回到“全部”，同时选中取消）。</summary>
    public void ResetFilters()
    {
        SearchText = "";
        _selectedTierFilters = [];
        SelectedCategoryFilters = [];
        SelectedTypeFilter = "";
        CategoryMatchAll = false;
        ApplyFilters();
    }
}
