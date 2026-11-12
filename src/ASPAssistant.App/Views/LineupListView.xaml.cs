// Modified for ASP Assistant Arknights on 2026-11-13; see NOTICE.
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;

namespace ASPAssistant.App.Views;

/// <summary>
/// 我的阵容: search and 筛选 by target covenant, 本局推荐 once the opening bans are known, and the saved lineups.
/// </summary>
public partial class LineupListView : UserControl
{
    private const int RecommendShown = 3;

    public event Action? NewLineupRequested;
    public event Action? ImportLineupRequested;
    public event Action<Lineup>? EditLineupRequested;
    public event Action<Lineup>? ShareLineupRequested;
    public event Action<Lineup>? TrackLineupRequested;
    public event Action<Lineup>? DeleteLineupRequested;
    /// <summary>修补: open the planner with this lineup's surviving operators locked.</summary>
    public event Action<LineupRecommendation>? RepairLineupRequested;

    private LineupViewModel? _vm;
    private bool _rebuildQueued;
    private bool _showAllRecommendations;
    private bool _syncingSegments;

    public LineupListView()
    {
        InitializeComponent();
        DataContextChanged += OnViewDataContextChanged;
        FilterToggle.Checked += (_, _) => UpdateTagsRow();
        FilterToggle.Unchecked += (_, _) => UpdateTagsRow();
    }

    private void OnViewDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm is not null)
        {
            _vm.FilteredLineups.CollectionChanged -= OnListChanged;
            _vm.Recommendations.CollectionChanged -= OnListChanged;
            _vm.PropertyChanged -= OnVmPropertyChanged;
        }
        _vm = e.NewValue as LineupViewModel;
        if (_vm is not null)
        {
            _vm.FilteredLineups.CollectionChanged += OnListChanged;
            _vm.Recommendations.CollectionChanged += OnListChanged;
            _vm.PropertyChanged += OnVmPropertyChanged;
        }
        SyncSegments();
        Rebuild();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(LineupViewModel.FilterTier) or nameof(LineupViewModel.FilterMatchAll)) SyncSegments();
        if (e.PropertyName is nameof(LineupViewModel.HasActiveFilters)) UpdateTagsRow();
    }

    /// <summary>The view model refills its lists item by item; rebuild the cards once afterwards.</summary>
    private void OnListChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (_rebuildQueued) return;
        _rebuildQueued = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () => { _rebuildQueued = false; Rebuild(); });
    }

    public void RefreshAllBadges() => Rebuild();

    /// <summary>Scrolls back to the top so 本局推荐 is in view (after the opening info is recorded).</summary>
    public void ShowRecommendations()
    {
        _showAllRecommendations = false;
        Rebuild();
        if (FindScroll(this) is { } sv) sv.ScrollToTop();
    }

    private static ScrollViewer? FindScroll(DependencyObject root)
    {
        for (int i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(root, i);
            if (child is ScrollViewer sv) return sv;
            if (FindScroll(child) is { } found) return found;
        }
        return null;
    }

    private void Rebuild()
    {
        if (_vm is null) return;
        var recs = _vm.Recommendations.ToDictionary(r => r.Lineup.Id);

        // 本局推荐
        bool hasRun = _vm.HasRunInfo && _vm.Lineups.Count > 0;
        RecommendSection.Visibility = hasRun ? Visibility.Visible : Visibility.Collapsed;
        if (hasRun)
        {
            var shown = _showAllRecommendations ? _vm.Recommendations.ToList() : _vm.Recommendations.Take(RecommendShown).ToList();
            RecommendItems.ItemsSource = shown.Select((r, i) => LineupCardItem.From(r.Lineup, r, _vm, isRecommendation: true,
                isBest: i == 0 && r.Kind <= RecommendationKind.BannedButFine)).ToList();
            int usable = _vm.Recommendations.Count(r => r.Kind <= RecommendationKind.BannedButFine);
            RecommendMeta.Text = usable > 0 ? $"{usable} 套能直接用" : "没有能直接用的，可以修补";
            RecommendMore.Content = _showAllRecommendations ? "收起" : $"全部 {_vm.Recommendations.Count} 套";
            RecommendMore.Visibility = _vm.Recommendations.Count > RecommendShown ? Visibility.Visible : Visibility.Collapsed;
            BannedAvatars.ItemsSource = _vm.RunBannedNames.Select(n => new LineupAvatarItem(n, false)).ToList();
            BannedCovenantsList.ItemsSource = _vm.RunBannedCovenants.ToList();
            BannedStrip.Visibility = _vm.RunBannedNames.Count + _vm.RunBannedCovenants.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        // 全部
        LineupItems.ItemsSource = _vm.FilteredLineups
            .Select(l => LineupCardItem.From(l, recs.GetValueOrDefault(l.Id), _vm, isRecommendation: false, isBest: false)).ToList();
        int total = _vm.Lineups.Count, matched = _vm.FilteredLineups.Count;
        bool filtered = matched != total;
        CountText.Text = filtered ? $"符合 {matched} 套" : $"{total} 套阵容";
        TotalText.Text = filtered ? $"· 共 {total} 套" : "";
        EmptyHint.Visibility = total == 0 ? Visibility.Visible : Visibility.Collapsed;
        NoMatchHint.Visibility = total > 0 && matched == 0 ? Visibility.Visible : Visibility.Collapsed;
        UpdateTagsRow();
    }

    private void UpdateTagsRow()
        => TagsRow.Visibility = FilterToggle.IsChecked != true && _vm?.HasActiveFilters == true ? Visibility.Visible : Visibility.Collapsed;

    private void SyncSegments()
    {
        if (_vm is null) return;
        _syncingSegments = true;
        TierList.SelectedIndex = Math.Clamp(_vm.FilterTier, 0, 2);
        MatchModeList.SelectedIndex = _vm.FilterMatchAll ? 1 : 0;
        _syncingSegments = false;
    }

    private void OnTierChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSegments || _vm is null || TierList.SelectedIndex < 0) return;
        _vm.FilterTier = TierList.SelectedIndex;
    }

    private void OnMatchModeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingSegments || _vm is null || MatchModeList.SelectedIndex < 0) return;
        _vm.FilterMatchAll = MatchModeList.SelectedIndex == 1;
    }

    private void OnResetFilter(object sender, RoutedEventArgs e) => _vm?.ClearFilters();
    private void OnCloseFilter(object sender, RoutedEventArgs e) => FilterToggle.IsChecked = false;

    private void OnRemoveTag(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: string tag }) _vm?.RemoveTag(tag);
    }

    private void OnToggleAllRecommendations(object sender, RoutedEventArgs e)
    {
        _showAllRecommendations = !_showAllRecommendations;
        Rebuild();
    }

    private void OnNewLineup(object sender, RoutedEventArgs e) => NewLineupRequested?.Invoke();
    private void OnImportLineup(object sender, RoutedEventArgs e) => ImportLineupRequested?.Invoke();

    private static LineupCardItem? Card(object sender) => (sender as FrameworkElement)?.DataContext as LineupCardItem;

    private void OnEditLineup(object sender, RoutedEventArgs e) { if (Card(sender) is { } c) EditLineupRequested?.Invoke(c.Lineup); }
    private void OnShareLineup(object sender, RoutedEventArgs e) { if (Card(sender) is { } c) ShareLineupRequested?.Invoke(c.Lineup); }
    private void OnTrackLineup(object sender, RoutedEventArgs e) { if (Card(sender) is { } c) TrackLineupRequested?.Invoke(c.Lineup); }
    private void OnDeleteLineup(object sender, RoutedEventArgs e) { if (Card(sender) is { } c) DeleteLineupRequested?.Invoke(c.Lineup); }
    private void OnRepairLineup(object sender, RoutedEventArgs e)
    {
        if (Card(sender) is { Recommendation: { } r }) RepairLineupRequested?.Invoke(r);
    }
}

public sealed record LineupAvatarItem(string Name, bool IsBanned)
{
    public string IconPath => $"icons/operators/{Name}.png";
    public string ToolTip => IsBanned ? $"{Name}（本局被禁）" : Name;
}

/// <summary>A target covenant pill. Tone: ok / warn / bad / accent (core, no run) / neutral.</summary>
public sealed record LineupTargetPill(string Text, string Tone, string? ToolTip);

public sealed class LineupCardItem
{
    public required Lineup Lineup { get; init; }
    public LineupRecommendation? Recommendation { get; init; }
    public string Name => Lineup.Name.Length > 0 ? Lineup.Name : "未命名阵容";
    public string SizeText => $"{Lineup.Slots.Count(s => s.OperatorName.Length > 0)} 人";
    public required IReadOnlyList<LineupTargetPill> Targets { get; init; }
    public required IReadOnlyList<LineupAvatarItem> Avatars { get; init; }
    public bool HasStatus => Recommendation is not null;
    public string StatusText => Recommendation?.StatusText ?? "";
    public string Tone => Recommendation?.Kind switch
    {
        RecommendationKind.Usable => "ok",
        RecommendationKind.BannedButFine => "accent",
        RecommendationKind.NeedsRepair => "warn",
        RecommendationKind.Broken => "bad",
        _ => "neutral",
    };
    public string Note => Recommendation?.Note ?? "";
    public bool HasNote => Note.Length > 0;
    public bool CanRepair => Recommendation is { Kind: RecommendationKind.NeedsRepair };
    public bool IsBest { get; init; }
    /// <summary>Share / delete live on the full list; 本局推荐 keeps to 修补 / 追踪 / 编辑.</summary>
    public bool ShowManage { get; init; }

    public static LineupCardItem From(Lineup lineup, LineupRecommendation? rec, LineupViewModel vm, bool isRecommendation, bool isBest)
    {
        List<LineupTargetPill> targets;
        if (rec is not null)
            targets = [.. rec.Targets.Select(t => t.CovenantBanned
                ? new LineupTargetPill(t.Label, "bad", $"本局{t.Name}不完整")
                : t.Reached
                    ? new LineupTargetPill(t.Label, "ok", $"{t.Count}/{t.Needed} 人")
                    : new LineupTargetPill($"{t.Label} 差 {t.Missing}", "warn", $"{t.Count}/{t.Needed} 人"))];
        else
        {
            var core = vm.Calculator.AllCovenants.Where(c => c.BondType == "SEASON").Select(c => c.Name).ToHashSet();
            targets = [.. vm.TargetsOf(lineup).Select(t => new LineupTargetPill(
                $"{t.Name} {(t.Tier >= 2 ? "Ⅱ" : "Ⅰ")}", core.Contains(t.Name) ? "accent" : "neutral", null))];
        }
        var banned = rec?.Banned.ToHashSet() ?? [];
        return new LineupCardItem
        {
            Lineup = lineup,
            Recommendation = rec,
            Targets = targets,
            Avatars = [.. lineup.Slots.Where(s => s.OperatorName.Length > 0)
                .Select(s => new LineupAvatarItem(s.OperatorName, banned.Contains(s.OperatorName)))],
            IsBest = isBest,
            ShowManage = !isRecommendation,
        };
    }
}
