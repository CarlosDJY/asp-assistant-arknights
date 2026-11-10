using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.ViewModels;

namespace ASPAssistant.App.Windows;

public partial class OperatorPickerWindow : Window
{
    private readonly OperatorBrowseViewModel _filters = new();

    public string? Result { get; private set; }

    public OperatorPickerWindow(IEnumerable<Operator> operators, string current)
    {
        InitializeComponent();
        _filters.LoadOperators(operators.OrderBy(op => op.Tier).ThenBy(op => op.Name, StringComparer.CurrentCulture).ToList());
        DataContext = _filters;
        TierFilters.ItemsSource = _filters.AvailableTiers.OfType<int>().ToList();
        CoreFilters.ItemsSource = _filters.AvailableCoreCovenants.Where(name => !string.IsNullOrEmpty(name)).ToList();
        OtherFilters.ItemsSource = _filters.AvailableOtherCovenants.Where(name => !string.IsNullOrEmpty(name)).ToList();
        TraitFilter.ItemsSource = new[] { "全部" }.Concat(_filters.AvailableTraitTypes.OfType<string>()).ToList();
        TimingFilter.ItemsSource = new[] { "全部" }.Concat(_filters.AvailableTriggerTimings.OfType<string>()).ToList();
        TraitFilter.SelectedIndex = 0;
        TimingFilter.SelectedIndex = 0;
        OperatorsList.SelectedItem = _filters.FilteredOperators.FirstOrDefault(op => op.Name == current);
        Loaded += (_, _) => SearchBox.Focus();
    }

    private void OnFiltersChanged(object sender, SelectionChangedEventArgs e)
    {
        var list = (ListBox)sender;
        if (list == TierFilters)
            _filters.SelectedTierFilters = list.SelectedItems.Cast<int>().ToList();
        else if (list == CoreFilters)
            _filters.SetCoreCovenantFilters(list.SelectedItems.Cast<string>());
        else
            _filters.SetOtherCovenantFilters(list.SelectedItems.Cast<string>());
    }

    private void OnResetFiltersClick(object sender, RoutedEventArgs e)
    {
        TierFilters.SelectedItems.Clear();
        CoreFilters.SelectedItems.Clear();
        OtherFilters.SelectedItems.Clear();
        _filters.ResetFilters();
        TraitFilter.SelectedIndex = 0;
        TimingFilter.SelectedIndex = 0;
    }

    private void OnSingleFilterChanged(object sender, SelectionChangedEventArgs e)
    {
        var combo = (ComboBox)sender;
        var selected = combo.SelectedIndex == 0 ? null : (string?)combo.SelectedItem;
        if (combo == TraitFilter)
            _filters.SelectedTraitTypeFilter = selected;
        else
            _filters.SelectedTriggerTimingFilter = selected;
    }

    private void OnOperatorClick(object sender, MouseButtonEventArgs e)
    {
        if (ItemsControl.ContainerFromElement(OperatorsList, e.OriginalSource as DependencyObject) is not ListBoxItem item)
            return;
        Result = ((Operator)item.Content).Name;
        DialogResult = true;
    }

    private void OnOperatorKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || OperatorsList.SelectedItem is not Operator op)
            return;
        Result = op.Name;
        DialogResult = true;
        e.Handled = true;
    }

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        Result = "";
        DialogResult = true;
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;
}
