// Modified for ASP Assistant Arknights on 2026-11-12; see NOTICE.
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
    public event Action? ClearRunRequested;
    public event Action? ManualScanRequested;

    public OperatorBrowseView()
    {
        InitializeComponent();
        DataContextChanged += OnViewDataContextChanged;
    }

    private void OnViewDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is OperatorBrowseViewModel vm)
            AvailabilityFilterList.SelectedIndex = vm.SelectedAvailabilityFilter switch
            {
                Core.ViewModels.AvailabilityFilter.Available => 1,
                Core.ViewModels.AvailabilityFilter.Unavailable => 2,
                _ => 0
            };
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
        ClearRunRequested?.Invoke();
    }

    private void OnManualScanClicked(object sender, RoutedEventArgs e)
    {
        ManualScanRequested?.Invoke();
    }

    /// <summary>Run banner: banned covenants as pills, how many operators that leaves unusable.</summary>
    public void SetBanSummary(IReadOnlyCollection<string> bannedCovenants, int unavailableOperators)
    {
        var any = bannedCovenants.Count > 0 || unavailableOperators > 0;
        BanBannerMeta.Text = !any ? "未录入 · 打开本局信息页后截屏录入"
            : unavailableOperators > 0 ? $"{unavailableOperators} 名干员不可用" : "无干员被禁用";
        BanCovenantPills.ItemsSource = bannedCovenants.ToList();
        BanCovenantPills.Visibility = bannedCovenants.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        ManualScanButton.Content = any ? "重新录入" : "截屏录入";
    }

    public void UpdateManualScanStatus(int pending)
    {
        ManualScanButton.IsEnabled = pending == 0;
        ManualScanStatusText.Text = $"{pending} 录入任务执行中";
        ManualScanStatusText.Visibility = pending > 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnRunMenuClicked(object sender, RoutedEventArgs e)
    {
        if (RunMenuButton.ContextMenu is not { } menu) return;
        menu.PlacementTarget = RunMenuButton;
        menu.IsOpen = true;
    }

    private void OnCollapseFiltersClicked(object sender, RoutedEventArgs e) => FilterToggle.IsChecked = false;

    /// <summary>重置: every chip, the availability switch, search and 任一/全部 back to the defaults.</summary>
    private void OnResetFiltersClicked(object sender, RoutedEventArgs e)
    {
        if (DataContext is not OperatorBrowseViewModel vm) return;
        AvailabilityFilterList.SelectedIndex = 0;
        vm.ResetFilters();
    }
}
