// Added for ASP Assistant Arknights on 2026-11-13; see NOTICE.
using System.Windows;
using System.Windows.Controls;
using ASPAssistant.Core.Models;

namespace ASPAssistant.App.Views;

/// <summary>This run's bans: banned covenants and operators, 截屏录入, and ⋯ (只看本局盟约 / 清除录入 / 清除 Ban 位).</summary>
public partial class RunBanView : UserControl
{
    public event Action? ClearBansRequested;
    public event Action? ClearRunRequested;
    public event Action? ManualScanRequested;

    public RunBanView() => InitializeComponent();

    private void OnClearBansClicked(object sender, RoutedEventArgs e) => ClearBansRequested?.Invoke();
    private void OnClearHighlightsClicked(object sender, RoutedEventArgs e) => ClearRunRequested?.Invoke();
    private void OnManualScanClicked(object sender, RoutedEventArgs e) => ManualScanRequested?.Invoke();

    /// <summary>Banned covenants as pills, the banned operators' portraits, and how many operators that leaves unusable.</summary>
    public void SetBanSummary(IReadOnlyCollection<string> bannedCovenants, int unavailableOperators, IReadOnlyList<Operator>? bannedOperators = null)
    {
        var any = bannedCovenants.Count > 0 || unavailableOperators > 0;
        BanBannerMeta.Text = !any ? "未录入 · 打开本局信息页后截屏录入"
            : unavailableOperators > 0 ? $"{unavailableOperators} 名干员不可用" : "无干员被禁用";
        BanCovenantPills.ItemsSource = bannedCovenants.ToList();
        BanCovenantPills.Visibility = bannedCovenants.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        BannedOperators.ItemsSource = bannedOperators;
        BannedOperators.Visibility = bannedOperators is { Count: > 0 } ? Visibility.Visible : Visibility.Collapsed;
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
}
