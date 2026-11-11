// Modified for ASP Assistant Arknights on 2026-11-12; see NOTICE.
using CommunityToolkit.Mvvm.ComponentModel;

namespace ASPAssistant.Core.ViewModels;

/// <summary>Which filter section a chip belongs to (also its label prefix in the active-filter tags).</summary>
public enum FilterChipKind { Tier, CoreCovenant, OtherCovenant, TraitType, TriggerTiming, Category }

/// <summary>Ban state of a covenant chip this run.</summary>
public enum FilterChipBan
{
    None,
    /// <summary>Core covenant banned: cannot be used at all (dashed, struck through).</summary>
    Blocked,
    /// <summary>Additional covenant banned: only its fully banned holders are gone; <see cref="FilterChip.CountText"/> shows what is left.</summary>
    Reduced
}

/// <summary>
/// One toggle in the redesigned filter card. Nothing selected in a section means "all" — there is no "全部" chip.
/// Toggling <see cref="IsSelected"/> calls back into the owning view model, which re-runs the filter.
/// </summary>
public sealed partial class FilterChip : ObservableObject
{
    private readonly Action<FilterChip> _onToggled;

    public FilterChip(FilterChipKind kind, string value, string label, Action<FilterChip> onToggled)
    {
        Kind = kind;
        Value = value;
        Label = label;
        _onToggled = onToggled;
    }

    public FilterChipKind Kind { get; }

    /// <summary>Filter value (covenant name, tier number as text, trait type, …).</summary>
    public string Value { get; }

    public string Label { get; }

    /// <summary>Tag text when the filter card is collapsed, e.g. "Ⅴ 阶", "核心 · 莱塔尼亚".</summary>
    public string TagText => Kind switch
    {
        FilterChipKind.Tier => $"{Label} 阶",
        FilterChipKind.CoreCovenant => $"核心 · {Label}",
        FilterChipKind.OtherCovenant => $"附加 · {Label}",
        FilterChipKind.TriggerTiming => $"<{Label}>",
        _ => Label
    };

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText), nameof(HasCount))]
    private int? _count;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText), nameof(IsBlocked), nameof(IsReduced))]
    private FilterChipBan _ban;

    /// <summary>"本局可用" covenant read from the run's info screen (shown as a small dot).</summary>
    [ObservableProperty]
    private bool _isHighlighted;

    [ObservableProperty]
    private bool _isSelected;

    /// <summary>Set while the view model syncs selection itself, so the change does not re-enter the filter.</summary>
    internal bool Syncing { get; set; }

    public bool HasCount => Count is not null && Ban != FilterChipBan.Blocked;
    public bool IsBlocked => Ban == FilterChipBan.Blocked;
    public bool IsReduced => Ban == FilterChipBan.Reduced;
    public string CountText => Count is not { } n || Ban == FilterChipBan.Blocked ? "" : Ban == FilterChipBan.Reduced ? $"剩 {n}" : n.ToString();

    partial void OnIsSelectedChanged(bool value)
    {
        if (!Syncing) _onToggled(this);
    }

    internal void SetSelected(bool value)
    {
        Syncing = true;
        IsSelected = value;
        Syncing = false;
    }
}
