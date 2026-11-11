// Modified for ASP Assistant Arknights on 2026-11-12; see NOTICE.
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ASPAssistant.Core.ViewModels;

namespace ASPAssistant.App.Views;

/// <summary>规划 tab. All state lives in <see cref="LineupPlannerViewModel"/>.</summary>
public partial class LineupPlannerView : UserControl
{
    public LineupPlannerView()
    {
        InitializeComponent();
        IsVisibleChanged += (_, _) => SyncActive();
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is LineupPlannerViewModel old) old.RouteStageFocusRequested -= OnRouteStageFocus;
            if (e.NewValue is LineupPlannerViewModel vm) vm.RouteStageFocusRequested += OnRouteStageFocus;
            SyncActive();
        };
    }

    /// <summary>Stepper click: scroll the opened stage card to the top of the results.</summary>
    private void OnRouteStageFocus(RouteStageItem stage)
    {
        Dispatcher.BeginInvoke(() =>
        {
            if (RouteStagesList.ItemContainerGenerator.ContainerFromItem(stage) is FrameworkElement container)
                container.BringIntoView(new Rect(0, 0, container.ActualWidth, Math.Min(container.ActualHeight, 400)));
        }, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private LineupPlannerViewModel? Vm => DataContext as LineupPlannerViewModel;

    /// <summary>Only search while the tab is on screen; changes made elsewhere are applied on return.</summary>
    private void SyncActive()
    {
        if (Vm is { } vm) vm.IsActive = IsVisible;
    }

    private void OnLockSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (Vm is not { } vm) return;
        switch (e.Key)
        {
            case Key.Enter:
                vm.AddLocked(LockSearchBox.Text);
                e.Handled = true;
                break;
            case Key.Escape:
                vm.LockSearchText = "";
                e.Handled = true;
                break;
        }
    }
}
