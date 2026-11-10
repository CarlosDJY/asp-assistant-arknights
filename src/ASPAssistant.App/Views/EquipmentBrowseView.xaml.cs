// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ASPAssistant.App.Controls;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.ViewModels;

namespace ASPAssistant.App.Views;

public partial class EquipmentBrowseView : UserControl
{
    public event Action<string, TrackingType>? TrackingRequested;
    public event Action<string>? UntrackingRequested;
    public Func<string, bool>? IsTrackedCheck { get; set; }

    public EquipmentBrowseView()
    {
        InitializeComponent();
    }

    private void OnViewDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        TierFilterList.SelectedIndex = 0;
        CategoryFilterList.SelectedIndex = 0;
        TypeFilterList.SelectedIndex = 0;
    }

    private bool _suppressTierSelectionChanged;

    private void OnTierFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressTierSelectionChanged) return;
        UpdateTierFiltersInVm();
    }

    private void UpdateTierFiltersInVm()
    {
        if (DataContext is not EquipmentBrowseViewModel vm) return;
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

    private void OnTypeFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (DataContext is not EquipmentBrowseViewModel vm) return;
        vm.SelectedTypeFilter = TypeFilterList.SelectedItem as string ?? "";
    }

    private void OnCategoryFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressCategorySelectionChanged) return;
        UpdateCategoryFiltersFromList();
    }

    private void UpdateCategoryFiltersFromList()
    {
        if (DataContext is not EquipmentBrowseViewModel vm) return;
        vm.SelectedCategoryFilters = CategoryFilterList.SelectedItems
            .Cast<object>()
            .Select(o => o as string ?? "")
            .Where(s => s.Length > 0)
            .ToList();
    }

    private bool _suppressCategorySelectionChanged;

    /// <summary>
    /// Multi-select category chips keep 「全部」 mutually exclusive with the real values:
    /// clicking 全部 clears the row, clicking a value deselects 全部, and clearing the last
    /// value falls back to 全部.
    /// </summary>
    private void OnCategoryFilterPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var container = (ItemsControl.ContainerFromElement(CategoryFilterList, e.OriginalSource as DependencyObject)
            as ListBoxItem);
        if (container == null) return;

        int index = CategoryFilterList.ItemContainerGenerator.IndexFromContainer(container);
        _suppressCategorySelectionChanged = true;

        if (index == 0)
        {
            for (int i = 1; i < CategoryFilterList.Items.Count; i++)
                if (CategoryFilterList.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem item)
                    item.IsSelected = false;
            container.IsSelected = true;
        }
        else
        {
            container.IsSelected = !container.IsSelected;
            if (CategoryFilterList.ItemContainerGenerator.ContainerFromIndex(0) is ListBoxItem allItem)
                allItem.IsSelected = false;

            bool anySelected = false;
            for (int i = 1; i < CategoryFilterList.Items.Count; i++)
            {
                if (CategoryFilterList.ItemContainerGenerator.ContainerFromIndex(i) is ListBoxItem item && item.IsSelected)
                {
                    anySelected = true;
                    break;
                }
            }
            if (!anySelected
                && CategoryFilterList.ItemContainerGenerator.ContainerFromIndex(0) is ListBoxItem fallbackAll)
            {
                fallbackAll.IsSelected = true;
            }
        }

        _suppressCategorySelectionChanged = false;
        UpdateCategoryFiltersFromList();
        e.Handled = true;
    }

    private void OnEquipmentTrackingToggled(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is EquipmentCard card && card.DataContext is Equipment eq)
        {
            if (card.IsTracked)
                TrackingRequested?.Invoke(eq.Name, TrackingType.Equipment);
            else
                UntrackingRequested?.Invoke(eq.Name);
        }
    }

    public void RefreshTrackingStates()
    {
        if (IsTrackedCheck == null) return;
        foreach (var card in FindVisualChildren<EquipmentCard>(EquipmentListControl))
        {
            if (card.DataContext is Equipment eq)
                card.RefreshTrackedState(IsTrackedCheck(eq.Name));
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
}
