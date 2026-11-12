// Modified for ASP Assistant Arknights on 2026-11-12; see NOTICE.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.ViewModels;
using System.Globalization;
using System.Windows.Data;

namespace ASPAssistant.App.Windows;

public partial class OperatorPickerWindow : Window
{
    private readonly OperatorBrowseViewModel _filters = new();
    private readonly IReadOnlySet<string> _strategies;

    public string? Result { get; private set; }

    public OperatorPickerWindow(IEnumerable<Operator> operators, string current, IReadOnlySet<string> strategies)
    {
        _strategies = strategies;
        InitializeComponent();
        var style = new Style(typeof(ListBoxItem), OperatorsList.ItemContainerStyle);
        style.Setters.Add(new Setter(IsEnabledProperty, new Binding(nameof(Operator.RequiredStrategyId))
        { Converter = new StrategyAvailabilityConverter(), ConverterParameter = strategies }));
        var disabled = new Trigger { Property = IsEnabledProperty, Value = false };
        disabled.Setters.Add(new Setter(ToolTipProperty, "必须在策略中添加森蚺策略"));
        disabled.Setters.Add(new Setter(OpacityProperty, 0.45));
        disabled.Setters.Add(new Setter(ToolTipService.ShowOnDisabledProperty, true));
        disabled.Setters.Add(new Setter(ToolTipService.InitialShowDelayProperty, 0));
        style.Triggers.Add(disabled);
        OperatorsList.ItemContainerStyle = style;
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
        var op = (Operator)item.Content;
        if (op.RequiredStrategyId.Length > 0 && !_strategies.Contains(op.RequiredStrategyId)) return;
        Result = op.Name;
        Close();
    }

    private void OnOperatorKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || OperatorsList.SelectedItem is not Operator op)
            return;
        if (op.RequiredStrategyId.Length > 0 && !_strategies.Contains(op.RequiredStrategyId)) return;
        Result = op.Name;
        Close();
        e.Handled = true;
    }

    private void OnMinimize(object sender, RoutedEventArgs e)
        => WindowState = WindowState.Minimized;

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        Result = "";
        Close();
    }

    private void OnCancelClick(object sender, RoutedEventArgs e) => Close();

    private sealed class StrategyAvailabilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => ((string)value).Length == 0 || ((IReadOnlySet<string>)parameter).Contains((string)value);
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
