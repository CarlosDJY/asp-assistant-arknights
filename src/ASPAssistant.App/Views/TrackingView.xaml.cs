// Modified for ASP Assistant Arknights on 2026-11-13; see NOTICE.
using System.Collections.Specialized;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ASPAssistant.App.Controls;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;

namespace ASPAssistant.App.Views;

public partial class TrackingView : UserControl
{
    public event Action<string>? RemoveTrackingRequested;
    public event Action<string>? ToggleAcquiredRequested;
    public event Action? ClearAllTrackingRequested;
    public static readonly DependencyProperty OperatorDatabaseProperty = DependencyProperty.Register(
        nameof(OperatorDatabase), typeof(IReadOnlyDictionary<string, Operator>), typeof(TrackingView),
        new PropertyMetadata(new Dictionary<string, Operator>()));

    public IReadOnlyDictionary<string, Operator> OperatorDatabase
        => (IReadOnlyDictionary<string, Operator>)GetValue(OperatorDatabaseProperty);

    public static readonly DependencyProperty EquipmentDatabaseProperty = DependencyProperty.Register(
        nameof(EquipmentDatabase), typeof(IReadOnlyDictionary<string, Equipment>), typeof(TrackingView),
        new PropertyMetadata(new Dictionary<string, Equipment>()));

    public IReadOnlyDictionary<string, Equipment> EquipmentDatabase
        => (IReadOnlyDictionary<string, Equipment>)GetValue(EquipmentDatabaseProperty);
    private TrackingViewModel? _vm;
    private INotifyCollectionChanged? _operators;
    private INotifyCollectionChanged? _equipment;
    private CovenantCalculator? _calculator;
    public static readonly DependencyProperty EquipmentRulesProperty = DependencyProperty.Register(
        nameof(EquipmentRules), typeof(LineupEquipmentService), typeof(TrackingView));
    public LineupEquipmentService EquipmentRules => (LineupEquipmentService)GetValue(EquipmentRulesProperty);

    public TrackingView()
    {
        InitializeComponent();
        DataContextChanged += (_, _) => Attach(DataContext as TrackingViewModel);
    }

    public void SetOperators(IReadOnlyList<Operator> operators)
    {
        SetValue(OperatorDatabaseProperty, operators.GroupBy(o => o.Name).ToDictionary(g => g.Key, g => g.First()));
        RefreshSummary();
    }

    public void SetCovenantCalculator(CovenantCalculator calculator)
    {
        _calculator = calculator;
        SetValue(EquipmentRulesProperty, calculator.EquipmentRules);
        RefreshSummary();
    }

    private void Attach(TrackingViewModel? vm)
    {
        if (_vm is not null) _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = vm;
        if (_vm is not null) _vm.PropertyChanged += OnVmPropertyChanged;
        HookCollections();
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(TrackingViewModel.TrackedOperators) or nameof(TrackingViewModel.TrackedEquipment))
            HookCollections();
        else if (e.PropertyName is nameof(TrackingViewModel.TerminalLevel) or nameof(TrackingViewModel.TerminalLevelText)
                 or nameof(TrackingViewModel.Economy))
            RefreshSummary();
    }

    private void HookCollections()
    {
        if (_operators is not null) _operators.CollectionChanged -= OnCollectionChanged;
        if (_equipment is not null) _equipment.CollectionChanged -= OnCollectionChanged;
        _operators = _vm?.TrackedOperators;
        _equipment = _vm?.TrackedEquipment;
        if (_operators is not null) _operators.CollectionChanged += OnCollectionChanged;
        if (_equipment is not null) _equipment.CollectionChanged += OnCollectionChanged;
        RefreshSummary();
    }

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RefreshSummary();

    /// <summary>Updates the empty state and the covenant tally of tracked operators.</summary>
    private void RefreshSummary()
    {
        var operators = _vm?.TrackedOperators.ToList() ?? [];
        var equipmentCount = _vm?.TrackedEquipment.Count ?? 0;
        var empty = operators.Count == 0 && equipmentCount == 0;
        EmptyState.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        ClearAllButton.Visibility = empty ? Visibility.Collapsed : Visibility.Visible;

        var lineup = new Lineup
        {
            Slots = operators.Where(e => e.Role == TrackingRole.Final && !e.IsAcquired).Select(e => new LineupSlot
            {
                OperatorName = e.Name, Equipments = e.Equipments, IsElite = e.IsElite
            }).ToList()
        };
        var tally = (_calculator?.Compute(lineup) ?? []).Where(s => s.Count > 0)
            .OrderByDescending(s => s.BondType == "SEASON").ThenByDescending(s => s.Count)
            .Select(s => new TrackedCovenantChip(s.Name, s.Count, s.BondType == "SEASON")).ToList();
        TrackedCovenantChips.ItemsSource = tally;
        TrackedCovenantChips.Visibility = tally.Count > 0 ? Visibility.Visible : Visibility.Collapsed;

        var db = OperatorDatabase;
        var rules = EquipmentRules;
        var cells = operators.Select(e => TrackedCell.From(e, db.GetValueOrDefault(e.Name), rules)).ToList();
        Fill(FinalSection, FinalCells, FinalCount, cells.Where(c => c.Kind == TrackedCellKind.Final).ToList());
        Fill(TransitionSection, TransitionCells, TransitionCount, cells.Where(c => c.Kind == TrackedCellKind.Transition).ToList());
        var past = cells.Where(c => c.Kind == TrackedCellKind.Past).ToList();
        Fill(PastSection, PastCells, PastCount, past);
        EquipmentSection.Visibility = equipmentCount > 0 ? Visibility.Visible : Visibility.Collapsed;

        // Terminal level card
        var level = _vm?.TerminalLevel ?? 0;
        LevelNumber.Text = level > 0 ? level.ToString() : "–";
        LevelTitle.Text = level > 0 ? $"终端 {level} 级" : "终端等级未识别";
        var source = _vm is null || level == 0 ? "打开商店后按升级卡自动识别"
            : _vm.ManualTerminalLevel is not null ? "手动指定 · 自动识别追上后恢复自动"
            : _vm.IsTerminalLevelRestored ? "上次记录 · 打开商店后确认"
            : "自动识别";
        var pastStages = operators.Where(e => e.IsExpired).SelectMany(e => e.Stages.Split(['、', '→', '·', ' '],
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).Distinct().ToList();
        LevelDetail.Text = pastStages.Count > 0 && level > 0 ? $"{source} · {string.Join("、", pastStages)}已过" : source;
        LevelDetail.ToolTip = _vm?.TerminalLevelText;
        PastHint.Text = level > 0 ? $"终端 {level} 级起不再标出" : "不再标出";

        // Shop HUD
        var economy = _vm?.Economy;
        EconomyRow.Visibility = economy is { HasAny: true } || economy?.Mode is not null ? Visibility.Visible : Visibility.Collapsed;
        EconomyRound.Text = economy?.Round?.ToString() ?? "–";
        EconomyMoney.Text = economy?.Money?.ToString() ?? "–";
        EconomyCost.Text = economy is { UpgradeMaxed: true } ? "满级" : economy?.UpgradeCost?.ToString() ?? "–";
        EconomyMode.Text = economy?.Mode ?? "";
    }

    private static void Fill(FrameworkElement section, ItemsControl list, TextBlock count, IReadOnlyList<TrackedCell> cells)
    {
        list.ItemsSource = cells;
        count.Text = $"{cells.Count} 名";
        section.Visibility = cells.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnCellRemove(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TrackedCell cell) RemoveTrackingRequested?.Invoke(cell.Name);
    }

    /// <summary>Removes the planned transformation(s): 变形同构体 + job-change gear, or a v1.5 covenant without gear.</summary>
    private void OnCellClearTransform(object sender, RoutedEventArgs e)
    {
        if (_vm is null || (sender as FrameworkElement)?.DataContext is not TrackedCell cell) return;
        foreach (var t in cell.Transformations) _vm.ClearPlannedTransformation(cell.Name, t.EquipmentName, EquipmentRules);
        if (cell.LegacyCovenant.Length > 0) _vm.ClearPlannedTransformation(cell.Name, "", EquipmentRules);
    }

    /// <summary>购买清单 / 站位 segment.</summary>
    private void OnTrackingPageChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DeployPage is null || ShoppingPage is null) return;
        bool deploy = TrackingPageList.SelectedIndex == 1;
        DeployPage.Visibility = deploy ? Visibility.Visible : Visibility.Collapsed;
        ShoppingPage.Visibility = deploy ? Visibility.Collapsed : Visibility.Visible;
        if (IsLoaded) ASPAssistant.App.Windows.PageTransition.SlideIn(deploy ? DeployPage : ShoppingPage, fromX: deploy ? 14 : -14);
    }

    public void SetEquipment(IReadOnlyList<Equipment> equipment)
        => SetValue(EquipmentDatabaseProperty, equipment.ToDictionary(e => e.Name));

    private void OnTrackedEquipmentToggled(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is EquipmentCell { DataContext: Equipment equipment })
            RemoveTrackingRequested?.Invoke(equipment.Name);
    }

    /// <summary>Right-click 标记已获取 / 继续追踪 on a tracked operator cell.</summary>
    private void OnCellToggleAcquired(object sender, RoutedEventArgs e)
    {
        if (MenuContext(sender) is TrackedCell cell) ToggleAcquiredRequested?.Invoke(cell.Name);
    }

    /// <summary>Same toggle for tracked equipment and names missing from the data.</summary>
    private void OnToggleAcquired(object sender, RoutedEventArgs e)
    {
        if (MenuContext(sender) is TrackingEntry entry) ToggleAcquiredRequested?.Invoke(entry.Name);
    }

    /// <summary>A menu item's data, falling back to the element the menu belongs to.</summary>
    private static object? MenuContext(object sender) => sender is FrameworkElement { DataContext: { } data } ? data
        : (sender as FrameworkElement)?.Parent is ContextMenu { PlacementTarget: FrameworkElement target } ? target.DataContext : null;

    private void OnRemoveTracking(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is TrackingEntry entry) RemoveTrackingRequested?.Invoke(entry.Name);
    }

    private void OnClearAllTracking(object sender, RoutedEventArgs e)
    {
        ClearAllTrackingRequested?.Invoke();
    }
}

public sealed record TrackedCovenantChip(string Name, int Count, bool IsCore);

public enum TrackedCellKind { Final, Transition, Past }

/// <summary>One tracked operator on the tracking page: grouped by role, ringed in the shop marker color.</summary>
public sealed record TrackedCell(string Name, TrackedCellKind Kind, string Sub, Operator? Operator,
    IReadOnlyList<EquipmentTransformation> Transformations, string LegacyCovenant)
{
    /// <summary>Marked 已获取: kept on the list, no longer starred in the shop.</summary>
    public bool IsAcquired { get; init; }
    public string Initial => Name.Length > 0 ? Name[..1] : "";
    public string IconPath => Operator?.IconPath ?? "";
    public bool HasDetails => Operator is not null;
    public bool HasTransform => Transformations.Count > 0 || LegacyCovenant.Length > 0;
    /// <summary>Transformation target(s), e.g. “不屈” or “不屈、精准” (暴躁铁皮 can hold several).</summary>
    public string TransformedCovenant => string.Join("、",
        Transformations.Select(t => t.Covenant).Append(LegacyCovenant).Where(c => c.Length > 0).Distinct());

    public static TrackedCell From(TrackingEntry entry, Operator? op, LineupEquipmentService? rules)
    {
        var kind = entry.IsExpired ? TrackedCellKind.Past
            : entry.IsTransition ? TrackedCellKind.Transition : TrackedCellKind.Final;
        IReadOnlyList<EquipmentTransformation> transformations = rules?.Transformations(new LineupSlot
        {
            OperatorName = entry.Name, Equipments = entry.Equipments, IsElite = entry.IsElite
        }) ?? [];
        var cell = new TrackedCell(entry.Name, kind, "", op, transformations, entry.TransformedCovenant ?? "");
        string sub;
        if (kind != TrackedCellKind.Final && !string.IsNullOrEmpty(entry.Stages)) sub = entry.Stages;
        else
        {
            var covenants = op is null ? new List<string>() : [.. op.CoreCovenants, .. op.AdditionalCovenants];
            var text = string.Join(" · ", covenants.Distinct());
            sub = cell.HasTransform ? $"转职 {cell.TransformedCovenant}" + (text.Length > 0 ? $" · {text}" : "")
                : text.Length > 0 ? text : op is null ? "无数据" : "";
        }
        if (entry.IsAcquired) sub = sub.Length > 0 ? $"已获取 · {sub}" : "已获取";
        return cell with { Sub = sub, IsAcquired = entry.IsAcquired };
    }
}
