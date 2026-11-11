// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
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
            Slots = operators.Select(e => new LineupSlot
            {
                OperatorName = e.Name, TransformedCovenant = e.TransformedCovenant
            }).ToList()
        };
        var tally = (_calculator?.Compute(lineup) ?? []).Where(s => s.Count > 0)
            .OrderByDescending(s => s.BondType == "SEASON").ThenByDescending(s => s.Count)
            .Select(s => new TrackedCovenantChip(s.Name, s.Count, s.BondType == "SEASON")).ToList();
        TrackedCovenantChips.ItemsSource = tally;
        CovenantSection.Visibility = tally.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnClearPlannedTransformation(object sender, RoutedEventArgs e)
        => _vm!.ClearPlannedTransformation(((TrackingEntry)((Button)sender).DataContext).Name);

    public void SetEquipment(IReadOnlyList<Equipment> equipment)
        => SetValue(EquipmentDatabaseProperty, equipment.ToDictionary(e => e.Name));

    private void OnTrackedEquipmentToggled(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is TrackedEquipmentCard { DataContext: Equipment equipment })
            RemoveTrackingRequested?.Invoke(equipment.Name);
    }

    private void OnTrackedOperatorToggled(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is OperatorCard { DataContext: Operator op })
            RemoveTrackingRequested?.Invoke(op.Name);
    }

    private void OnRemoveTracking(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string name)
        {
            RemoveTrackingRequested?.Invoke(name);
        }
    }

    private void OnClearAllTracking(object sender, RoutedEventArgs e)
    {
        ClearAllTrackingRequested?.Invoke();
    }
}

public sealed record TrackedCovenantChip(string Name, int Count, bool IsCore);
