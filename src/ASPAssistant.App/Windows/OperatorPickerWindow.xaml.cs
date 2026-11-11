// Modified for ASP Assistant Arknights on 2026-11-12; see NOTICE.
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
        OperatorsList.SelectedItem = _filters.FilteredOperators.FirstOrDefault(op => op.Name == current);
        Loaded += (_, _) => SearchBox.Focus();
    }

    private void OnResetFiltersClick(object sender, RoutedEventArgs e) => _filters.ResetFilters();

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
