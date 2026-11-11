// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using ASPAssistant.App.Controls;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.ViewModels;

namespace ASPAssistant.App.Views;

public partial class OperatorBrowseView : UserControl
{
    public event Action<string, TrackingType>? TrackingRequested;
    public event Action<string>? UntrackingRequested;
    public event Action<string, bool>? BanToggleRequested;
    public Func<string, bool>? IsTrackedCheck { get; set; }

    public Func<string, bool>? IsBannedCheck { get; set; }
    public event Action? ClearBansRequested;
    public event Action? ManualScanRequested;

    public OperatorBrowseView()
    {
        InitializeComponent();
        DataContextChanged += OnViewDataContextChanged;
    }

    private void OnViewDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is OperatorBrowseViewModel)
        {
            // Pre-select "全部" (index 0) so the chip appears highlighted and
            // clicking it later always triggers SelectionChanged.
            AvailabilityFilterList.SelectedIndex = 0;
            TierFilterList.SelectedIndex = 0;
            CoreCovenantFilterList.SelectedIndex = 0;
            OtherCovenantFilterList.SelectedIndex = 0;
            TraitFilterList.SelectedIndex = 0;
            TriggerTimingFilterList.SelectedIndex = 0;
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T t) yield return t;
            foreach (var descendant in FindVisualChildren<T>(child))
                yield return descendant;
        }
    }

    private void OnOperatorBanToggled(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is OperatorCard card && card.DataContext is Operator op)
            BanToggleRequested?.Invoke(op.Name, card.IsBanned);
    }

    private void OnOperatorTrackingToggled(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is OperatorCard card && card.DataContext is Operator op)
        {
            if (card.IsTracked)
                TrackingRequested?.Invoke(op.Name, TrackingType.Operator);
            else
                UntrackingRequested?.Invoke(op.Name);
        }
    }

    public void RefreshTrackingStates()
    {
        if (IsTrackedCheck == null) return;
        foreach (var card in FindVisualChildren<OperatorCard>(OperatorListControl))
        {
            if (card.DataContext is Operator op)
            {
                card.RefreshTrackedState(IsTrackedCheck(op.Name));
            }
        }
    }

    public void RefreshBannedStates()
    {
        if (IsBannedCheck == null) return;
        foreach (var card in FindVisualChildren<OperatorCard>(OperatorListControl))
        {
            if (card.DataContext is Operator op)
                card.RefreshBannedState(IsBannedCheck(op.Name));
        }
    }

    private void OnAvailabilityFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not OperatorBrowseViewModel vm) return;
        vm.SelectedAvailabilityFilter = AvailabilityFilterList.SelectedIndex switch
        {
            1 => Core.ViewModels.AvailabilityFilter.Available,
            2 => Core.ViewModels.AvailabilityFilter.Unavailable,
            _ => Core.ViewModels.AvailabilityFilter.All
        };
    }

    private void OnClearBansClicked(object sender, RoutedEventArgs e)
    {
        ClearBansRequested?.Invoke();
    }

    private void OnClearHighlightsClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is OperatorBrowseViewModel vm)
            vm.ClearCovenantHighlights();
    }

    private void OnManualScanClicked(object sender, RoutedEventArgs e)
    {
        ManualScanRequested?.Invoke();
    }

    public void UpdateManualScanStatus(int pending)
    {
        ManualScanButton.IsEnabled = pending == 0;
        ManualScanStatusText.Text = $"{pending} 录入任务执行中";
        ManualScanStatusText.Visibility = pending > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    // Tier and Trait filters: single-select, shared null-item fix handler
    private void OnFilterListPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not ListBox lb) return;
        var container = ItemsControl.ContainerFromElement(lb, e.OriginalSource as DependencyObject) as ListBoxItem;
        if (container == null) return;
        if (lb.ItemContainerGenerator.IndexFromContainer(container) != 0) return;

        // WPF cannot select a null item via mouse — force selection programmatically
        lb.SelectedIndex = 0;
        e.Handled = true;
    }

    private bool _suppressTierSelectionChanged;

    private void OnTierFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressTierSelectionChanged) return;
        UpdateTierFiltersInVm();
    }

    private void UpdateTierFiltersInVm()
    {
        if (DataContext is not OperatorBrowseViewModel vm) return;
        vm.SelectedTierFilters = TierFilterList.SelectedItems.OfType<int>().ToList();
    }

    /// <summary>等阶多选：点「全部」清空本行，点具体阶位取消「全部」，全部取消时回落到「全部」。</summary>
    private void OnTierFilterPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var container = ItemsControl.ContainerFromElement(TierFilterList, e.OriginalSource as DependencyObject)
            as ListBoxItem;
        if (container == null) return;

        int index = TierFilterList.ItemContainerGenerator.IndexFromContainer(container);
        _suppressTierSelectionChanged = true;

        if (index == 0)
        {
            // 「全部」的列表项是 null，WPF 不允许直接选中 null 项，只能按索引选择
            TierFilterList.SelectedItems.Clear();
            TierFilterList.SelectedIndex = 0;
        }
        else
        {
            if (TierFilterList.ItemContainerGenerator.ContainerFromIndex(0) is ListBoxItem allItem)
                allItem.IsSelected = false;
            container.IsSelected = !container.IsSelected;

            bool anySelected = false;
            for (int i = 1; i < TierFilterList.Items.Count; i++)
            {
                if (TierFilterList.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem item && item.IsSelected)
                {
                    anySelected = true;
                    break;
                }
            }
            if (!anySelected)
            {
                TierFilterList.SelectedItems.Clear();
                TierFilterList.SelectedIndex = 0;
            }
        }

        _suppressTierSelectionChanged = false;
        UpdateTierFiltersInVm();
        e.Handled = true;
    }

    /// <summary>把所有筛选行恢复为「全部」，再清空 VM 里的筛选状态（含搜索与「同时选中」）。</summary>
    private void OnResetFiltersClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not OperatorBrowseViewModel vm) return;

        SelectOnlyFirst(AvailabilityFilterList);
        SelectOnlyFirst(TierFilterList);
        SelectOnlyFirst(CoreCovenantFilterList);
        SelectOnlyFirst(OtherCovenantFilterList);
        SelectOnlyFirst(TraitFilterList);
        SelectOnlyFirst(TriggerTimingFilterList);
        UpdateTierFiltersInVm();
        UpdateCovenantFiltersInVm();
        vm.ResetFilters();
    }

    private static void SelectOnlyFirst(ListBox list)
    {
        if (list.SelectionMode != SelectionMode.Single)
            list.SelectedItems.Clear();
        if (list.Items.Count > 0) list.SelectedIndex = 0;
    }

    private void OnTraitFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not OperatorBrowseViewModel vm) return;
        vm.SelectedTraitTypeFilter = TraitFilterList.SelectedItem as string;
    }

    private void OnTriggerTimingFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not OperatorBrowseViewModel vm) return;
        vm.SelectedTriggerTimingFilter = TriggerTimingFilterList.SelectedItem as string;
    }


    // ── Covenant multi-select ────────────────────────────────────────────────

    // Suppress flag prevents cascading SelectionChanged calls during programmatic
    // selection updates inside HandleCovenantListMouseDown.
    private bool _suppressCovenantSelectionChanged = false;

    private void OnCoreCovenantPreviewMouseDown(object sender, MouseButtonEventArgs e)
        => HandleCovenantListMouseDown(CoreCovenantFilterList, e);

    private void OnOtherCovenantPreviewMouseDown(object sender, MouseButtonEventArgs e)
        => HandleCovenantListMouseDown(OtherCovenantFilterList, e);

    private void HandleCovenantListMouseDown(ListBox lb, MouseButtonEventArgs e)
    {
        var container = ItemsControl.ContainerFromElement(lb, e.OriginalSource as DependencyObject) as ListBoxItem;
        if (container == null) return;

        int idx = lb.ItemContainerGenerator.IndexFromContainer(container);

        _suppressCovenantSelectionChanged = true;

        if (idx == 0) // "全部" clicked — clear all selections in this row
        {
            for (int i = 1; i < lb.Items.Count; i++)
            {
                if (lb.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem c)
                    c.IsSelected = false;
            }
            container.IsSelected = true;
        }
        else // specific covenant clicked — toggle it
        {
            container.IsSelected = !container.IsSelected;

            // Deselect 全部 in this row
            if (lb.ItemContainerGenerator.ContainerFromIndex(0) is ListBoxItem allContainer)
                allContainer.IsSelected = false;

            // If nothing remains selected in this row, fall back to 全部
            bool anySelected = false;
            for (int i = 1; i < lb.Items.Count; i++)
            {
                if (lb.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem c && c.IsSelected)
                {
                    anySelected = true;
                    break;
                }
            }
            if (!anySelected)
            {
                if (lb.ItemContainerGenerator.ContainerFromIndex(0) is ListBoxItem allContainer2)
                    allContainer2.IsSelected = true;
            }
        }

        _suppressCovenantSelectionChanged = false;
        UpdateCovenantFiltersInVm();
        e.Handled = true;
    }

    private void OnCovenantFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressCovenantSelectionChanged) return;
        // Handles initial "全部" selection restored by WPF after LoadOperators
        UpdateCovenantFiltersInVm();
    }

    private void UpdateCovenantFiltersInVm()
    {
        if (DataContext is not OperatorBrowseViewModel vm) return;

        var core = new List<string>();
        CollectSelectedCovenants(CoreCovenantFilterList, core);
        vm.SetCoreCovenantFilters(core);

        var other = new List<string>();
        CollectSelectedCovenants(OtherCovenantFilterList, other);
        vm.SetOtherCovenantFilters(other);
    }

    private static void CollectSelectedCovenants(ListBox lb, List<string> selected)
    {
        for (int i = 1; i < lb.Items.Count; i++)
        {
            if (lb.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem c
                && c.IsSelected
                && lb.Items[i] is string s)
                selected.Add(s);
        }
    }
}
