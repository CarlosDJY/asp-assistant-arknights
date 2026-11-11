// Modified for ASP Assistant Arknights on 2026-11-12; see NOTICE.
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

    private void OnViewDataContextChanged(object sender, DependencyPropertyChangedEventArgs e) { }

    private void OnCollapseFiltersClicked(object sender, RoutedEventArgs e) => FilterToggle.IsChecked = false;

    private void OnResetFiltersClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is EquipmentBrowseViewModel vm) vm.ResetFilters();
    }

    private void OnEquipmentTrackingToggled(object sender, RoutedEventArgs e)
    {
        if (e.OriginalSource is EquipmentCell card && card.DataContext is Equipment eq)
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
        foreach (var card in FindVisualChildren<EquipmentCell>(EquipmentListControl))
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
