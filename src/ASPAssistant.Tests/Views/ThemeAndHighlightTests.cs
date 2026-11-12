// Modified for ASP Assistant Arknights on 2026-11-10; see NOTICE.
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ASPAssistant.App;
using ASPAssistant.App.Controls;
using ASPAssistant.App.Views;
using ASPAssistant.App.Windows;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.Views;

[Collection("Update environment")]
public class ThemeAndHighlightTests
{
    [Fact]
    public void UpdateDialog_ReportsDownloadProgress()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            // 同一 AppDomain 只能有一个 WPF Application；多个测试共用它，谁创建谁负责关闭。
            var ownsApplication = Application.Current is null;
            var application = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            try
            {
                foreach (var resource in new[] { "Themes/Terminal.Dark.xaml", "Styles.xaml", "Logo.xaml" })
                    application.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri($"/ASPAssistant.App;component/Resources/{resource}", UriKind.Relative)
                    });

                var info = new ASPAssistant.Core.Services.UpdateInfo(
                    "v9.9.9", "9.9.9", "- 测试更新说明", "https://example.test/a.zip", "a.zip", null);
                // 不 Show()：在测试宿主里显示窗口会拉起到前台并可能拖垮 testhost。
                var dialog = new UpdateDialog(info);
                dialog.ReportProgress(0.42);
                var bar = (System.Windows.Controls.ProgressBar)dialog.FindName("DialogProgress");
                var percent = (TextBlock)dialog.FindName("ProgressPercentText");
                var hint = (TextBlock)dialog.FindName("DownloadHintText");
                bar.Value.Should().Be(42);
                percent.Text.Should().Be("42%");
                hint.Text.Should().StartWith("正在下载安装包");

                dialog.ReportProgress(1.0);
                bar.Value.Should().Be(100);
                percent.Text.Should().Be("100%");
                hint.Text.Should().Contain("立即重启");
                dialog.ReportFailure("更新源返回 HTTP 503");
                hint.Text.Should().Contain("下载失败").And.Contain("503").And.Contain("重试");
                bar.Visibility.Should().Be(Visibility.Collapsed);
                percent.Visibility.Should().Be(Visibility.Collapsed);
                dialog.ReportProgress(0);
                bar.Visibility.Should().Be(Visibility.Visible);
                percent.Visibility.Should().Be(Visibility.Visible);
                bar.Value.Should().Be(0);
                percent.Text.Should().Be("0%");
                hint.Text.Should().StartWith("正在下载安装包");
                dialog.Close();
            }
            catch (Exception ex) { failure = ex; }
            finally { if (ownsApplication) application.Shutdown(); }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(60)).Should().BeTrue("WPF dialog test should finish");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    // Written against the pre-redesign UI (old dark/light palette colors, the 全部-chip filter lists, card layouts).
    // The redesign replaced all of those; to be rewritten once the new layout settles.
    [Fact(Skip = "界面重做后待重写：断言针对旧配色和旧筛选列表")]
    public void ThemeSwitch_UpdatesControlsAndPreservesHighlightAndSelection()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            // 同一 AppDomain 只能有一个 WPF Application；多个测试共用它，谁创建谁负责关闭。
            var ownsApplication = Application.Current is null;
            var application = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var host = new Window { ShowActivated = false, ShowInTaskbar = false };
            try
            {
                foreach (var resource in new[] { "Themes/Terminal.Dark.xaml", "Styles.xaml", "Logo.xaml" })
                    application.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri($"/ASPAssistant.App;component/Resources/{resource}", UriKind.Relative)
                    });

                var store = new JsonDataStore(Path.Combine(AppContext.BaseDirectory, "TestData", "Regulation"));
                var vm = new OperatorBrowseViewModel();
                var operators = store.LoadOperatorsAsync().GetAwaiter().GetResult();
                vm.LoadOperators(operators.Where(o => o.RequiredStrategyId.Length == 0).ToList());
                var view = new OperatorBrowseView { DataContext = vm };
                view.ClearRunRequested += () => vm.ClearCovenantHighlights();
                host.Content = view;
                var toggle = (System.Windows.Controls.Primitives.ToggleButton)view.FindName("FilterToggle");
                toggle.IsChecked = true;
                var coreList = (ListBox)view.FindName("CoreCovenantFilterList");
                var output = Path.Combine(AppContext.BaseDirectory, "TestResults", "theme-previews");
                Directory.CreateDirectory(output);

                foreach (var isLight in new[] { false, true })
                {
                    ThemeManager.Apply(ASPAssistant.Core.Models.GameShopTheme.Terminal, !isLight);
                    vm.SetAvailableCovenants(["乌萨斯", "奥术"]);
                    view.Measure(new Size(310, 900));
                    view.Arrange(new Rect(0, 0, 310, 900));
                    view.UpdateLayout();
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

                    var tierCandidates = vm.FilteredOperators.ToList();
                    AssertTierClicks((ListBox)view.FindName("TierFilterList"), expected =>
                    {
                        vm.SelectedTierFilters.Should().BeEquivalentTo(expected);
                        vm.FilteredOperators.Should().BeEquivalentTo(tierCandidates
                            .Where(op => expected.Length == 0 || expected.Contains(op.Tier)));
                    });

                    var availability = (ListBox)view.FindName("AvailabilityFilterList");
                    var tiers = (ListBox)view.FindName("TierFilterList");
                    var otherCovenants = (ListBox)view.FindName("OtherCovenantFilterList");
                    var traits = (ListBox)view.FindName("TraitFilterList");
                    var timing = (ListBox)view.FindName("TriggerTimingFilterList");
                    foreach (var single in new[] { availability, traits, timing })
                        single.SelectedIndex = 1;
                    foreach (var multiple in new[] { tiers, coreList, otherCovenants })
                    {
                        multiple.SelectedItems.Clear();
                        multiple.SelectedItems.Add(multiple.Items[1]);
                        multiple.SelectedItems.Add(multiple.Items[2]);
                    }
                    ((CheckBox)view.FindName("CoreCovenantMatchAllToggle")).IsChecked = true;
                    ((CheckBox)view.FindName("OtherCovenantMatchAllToggle")).IsChecked = true;
                    vm.SearchText = "塔露拉";
                    vm.ActiveFilterCount.Should().BeGreaterThan(0);
                    vm.SelectedTierFilters.Should().HaveCount(2);
                    vm.CoreCovenantMatchAll.Should().BeTrue();
                    vm.OtherCovenantMatchAll.Should().BeTrue();
                    view.UpdateLayout();
                    for (var reset = 0; reset < 2; reset++)
                    {
                        ((Button)view.FindName("ResetFiltersButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        view.UpdateLayout();
                        foreach (var list in new[] { availability, tiers, coreList, otherCovenants, traits, timing })
                        {
                            list.SelectedIndex.Should().Be(0);
                            list.SelectedItems.Count.Should().BeLessThanOrEqualTo(1);
                        }
                        vm.SearchText.Should().BeEmpty();
                        Descendants<TextBox>(view).First().Text.Should().BeEmpty();
                        vm.SelectedTierFilters.Should().BeEmpty();
                        vm.SelectedAvailabilityFilter.Should().Be(AvailabilityFilter.All);
                        vm.SelectedTraitTypeFilter.Should().BeNull();
                        vm.SelectedTriggerTimingFilter.Should().BeNull();
                        vm.CoreCovenantMatchAll.Should().BeFalse();
                        vm.OtherCovenantMatchAll.Should().BeFalse();
                        ((CheckBox)view.FindName("CoreCovenantMatchAllToggle")).IsChecked.Should().BeFalse();
                        ((CheckBox)view.FindName("OtherCovenantMatchAllToggle")).IsChecked.Should().BeFalse();
                        vm.ActiveFilterTags.Should().BeEmpty();
                        vm.ActiveFilterCount.Should().Be(0);
                        vm.FilteredOperators.Should().BeEquivalentTo(operators.Where(o => o.RequiredStrategyId.Length == 0));
                        vm.HighlightedCovenants.Should().BeEquivalentTo("乌萨斯", "奥术");
                    }

                    var item = (ListBoxItem)coreList.ItemContainerGenerator.ContainerFromItem("乌萨斯");
                    item.IsSelected = true;
                    item.ApplyTemplate();
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    var chip = (Border)item.Template.FindName("bg", item);
                    item.Tag.Should().Be("available");
                    ((SolidColorBrush)chip.BorderBrush).Color.Should().Be(
                        ((SolidColorBrush)application.FindResource("AvailableBrush")).Color);
                    // Selected chips use the theme's filter-selection palette: orange on the
                    // light theme, accent blue on the dark theme.
                    var selectedText = ((SolidColorBrush)application.FindResource("FilterSelectedBrush")).Color;
                    selectedText.Should().Be(isLight
                        ? Color.FromRgb(0xB4, 0x53, 0x09)
                        : Color.FromRgb(0x4F, 0xC3, 0xF7));
                    ((SolidColorBrush)chip.Background).Color.Should().Be(
                        ((SolidColorBrush)application.FindResource("FilterSelectedTintBrush")).Color);
                    ((SolidColorBrush)view.Background).Color.Should().Be(isLight
                        ? Color.FromRgb(247, 249, 252) : Color.FromRgb(26, 26, 46));

                    var scroll = Descendants<ScrollViewer>(view).Single(s => s.ScrollableHeight > 0);
                    scroll.ScrollToVerticalOffset(100);
                    view.UpdateLayout();
                    toggle.IsChecked.Should().BeTrue();
                    scroll.ScrollToTop();
                    view.UpdateLayout();

                    var firstCard = Descendants<OperatorCard>(view).First();
                    var tooltip = (ToolTip)firstCard.FindName("DetailsToolTip");
                    tooltip.PlacementTarget = (UIElement)firstCard.FindName("CardBorder");
                    tooltip.Measure(new Size(500, 700));
                    tooltip.Arrange(new Rect(tooltip.DesiredSize));
                    tooltip.UpdateLayout();
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    var labels = Descendants<TextBlock>(tooltip).Select(t => t.Text).ToList();
                    labels.Should().Contain("普通").And.Contain("精锐");
                    Descendants<ContentControl>(tooltip).Should().Contain(c => c.Tag as string == "精锐");
                    var detailCard = Descendants<Border>(tooltip).First(b => b.Width == 228);
                    ((SolidColorBrush)detailCard.Background).Color.Should().Be(isLight
                        ? Colors.White : Color.FromRgb(37, 37, 64));

                    Save(view, Path.Combine(output, isLight ? "light.png" : "dark.png"));
                    Save(tooltip, Path.Combine(output, isLight ? "light-details.png" : "dark-details.png"));

                    ((Button)view.FindName("ClearHighlightsButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    vm.HighlightedCovenants.Should().BeEmpty();
                    item.IsSelected.Should().BeTrue();
                    item.Tag.Should().Be("");
                    vm.ActiveFilterTags.Should().Contain("乌萨斯");
                    ((SolidColorBrush)chip.BorderBrush).Color.Should().Be(
                        ((SolidColorBrush)application.FindResource("FilterSelectedBrush")).Color);
                }
                ThemeManager.Apply(ASPAssistant.Core.Models.GameShopTheme.Terminal, true);
                ((SolidColorBrush)view.Background).Color.Should().Be(Color.FromRgb(26, 26, 46));

                var (equipment, manualJobChanges) = store.LoadEquipmentAsync().GetAwaiter().GetResult();
                var (covenants, mappings) = store.LoadCovenantsAsync().GetAwaiter().GetResult();
                var strategies = store.LoadStrategiesAsync().GetAwaiter().GetResult();
                var equipmentVm = new EquipmentBrowseViewModel();
                equipmentVm.LoadEquipment(equipment,
                    operators.SelectMany(o => o.CoreCovenants.Concat(o.AdditionalCovenants)), manualJobChanges, mappings);
                var lineupVm = new LineupViewModel(new LineupStore(Path.Combine(output, "lineups"), new LineupEquipmentService(mappings)),
                    new CovenantCalculator(operators, covenants, mappings));
                var trackingVm = new TrackingViewModel();
                trackingVm.AddTracking("烈夏", TrackingType.Operator);
                trackingVm.AddTracking("塔露拉", TrackingType.Operator);
                trackingVm.AddTracking("乌萨斯呼吸器", TrackingType.Equipment);
                foreach (var name in new[] { "坚守盾牌", "雪祀之衣", "萨尔贡浓茶", "盟约之币", "卫戍推荐信", "不屈弹射器", "博士投影", "不退者卫护系统" })
                    trackingVm.AddTracking(name, TrackingType.Equipment);
                trackingVm.AddTracking(equipment.MaxBy(e => e.Name.Length)!.Name, TrackingType.Equipment);
                var enemyVm = new EnemyViewModel();
                using var failedShareHttp = new HttpClient(new FailedShareHandler());
                var sidePanel = new SidePanelWindow(vm, equipmentVm, trackingVm,
                    new GameStateViewModel(), new BanViewModel(), enemyVm,
                    lineupVm, new LineupShareService(lineupVm.Calculator.EquipmentRules, Path.Combine(output, "share-links"), failedShareHttp), operators, equipment, strategies, new SettingsManager(output), GameShopTheme.Terminal,
                    new SidePanelPositionSettings(), new LineupPlanner(operators, covenants), covenants);
                try
                {
                    SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                    // 仅显示透明、不激活的测试窗口，核对真实 HWND 的物理边界。
                    sidePanel.Opacity = 0;
                    sidePanel.ShowActivated = false;
                    sidePanel.ShowInTaskbar = false;
                    sidePanel.Show();
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    var sideHwnd = new System.Windows.Interop.WindowInteropHelper(sidePanel).Handle;
                    var work = User32.GetMonitorGeometry(0, 0).WorkArea;
                    var gameBounds = new RECT
                    {
                        Left = work.Left + 20, Top = work.Top + 20,
                        Right = work.Right - 10, Bottom = Math.Min(work.Bottom - 20, work.Top + 640),
                    };
                    sidePanel.UpdatePosition(gameBounds, false);
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    sidePanel.UpdatePosition(gameBounds, false);
                    var sideBounds = User32.ReadWindowRect(sideHwnd);
                    var sideScale = User32.GetWindowDpiScale(sideHwnd);
                    sideBounds.Right.Should().Be(gameBounds.Right);
                    sideBounds.Top.Should().Be(gameBounds.Top);
                    sideBounds.Height.Should().Be(gameBounds.Height);
                    sideBounds.Width.Should().Be((int)Math.Round(310 * sideScale));
                    User32.GetForegroundWindow().Should().NotBe(sideHwnd);

                    User32.SendMessage(sideHwnd, User32.WM_ENTERSIZEMOVE, IntPtr.Zero, IntPtr.Zero);
                    var manualBounds = new RECT
                    {
                        Left = work.Left + 50, Top = work.Top + 50,
                        Right = work.Left + 50 + sideBounds.Width,
                        Bottom = work.Top + 50 + (int)Math.Round(300 * sideScale),
                    };
                    User32.SetWindowBounds(sideHwnd, manualBounds);
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    User32.SendMessage(sideHwnd, User32.WM_EXITSIZEMOVE, IntPtr.Zero, IntPtr.Zero);
                    var freeMovement = (CheckBox)sidePanel.FindName("FreeSidePanelMovementToggle");
                    var resetPosition = (Button)sidePanel.FindName("ResetSidePanelPositionButton");
                    WaitForPreferenceSave(freeMovement);
                    sidePanel.UpdatePosition(gameBounds, false);
                    User32.ReadWindowRect(sideHwnd).Should().BeEquivalentTo(manualBounds);

                    SidePanelPositionSettings LoadSavedPosition()
                    {
                        var task = new SettingsManager(output).LoadSidePanelPositionAsync();
                        WaitForUi(() => task.IsCompleted, "position settings should load");
                        return task.GetAwaiter().GetResult();
                    }
                    freeMovement.Content.Should().Be("侧边栏自由移动");
                    resetPosition.Content.Should().Be("侧边栏重置");
                    freeMovement.IsChecked = true;
                    WaitForPreferenceSave(freeMovement);
                    var movedGame = gameBounds;
                    movedGame.Left -= 30;
                    movedGame.Right -= 30;
                    movedGame.Top += 30;
                    movedGame.Bottom += 30;
                    sidePanel.UpdatePosition(movedGame, true);
                    User32.ReadWindowRect(sideHwnd).Should().BeEquivalentTo(manualBounds,
                        "free movement must ignore game movement and mode changes");
                    User32.SendMessage(sideHwnd, User32.WM_ENTERSIZEMOVE, IntPtr.Zero, IntPtr.Zero);
                    manualBounds.Left += 40;
                    manualBounds.Right += 40;
                    manualBounds.Top += 30;
                    manualBounds.Bottom += 30;
                    User32.SetWindowBounds(sideHwnd, manualBounds);
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    User32.SendMessage(sideHwnd, User32.WM_EXITSIZEMOVE, IntPtr.Zero, IntPtr.Zero);
                    WaitForPreferenceSave(freeMovement);
                    var savedFree = LoadSavedPosition();
                    savedFree.FreeMovement.Should().BeTrue();
                    var restoredFree = new SidePanelWindow(vm, equipmentVm, trackingVm,
                        new GameStateViewModel(), new BanViewModel(), enemyVm, lineupVm,
                        new LineupShareService(lineupVm.Calculator.EquipmentRules, Path.Combine(output, "share-links"), failedShareHttp), operators, equipment, strategies,
                        new SettingsManager(output), GameShopTheme.Terminal, savedFree)
                    { Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
                    try
                    {
                        restoredFree.Show();
                        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                        ((CheckBox)restoredFree.FindName("FreeSidePanelMovementToggle")).IsChecked.Should().BeTrue();
                        var restoredHwnd = new System.Windows.Interop.WindowInteropHelper(restoredFree).Handle;
                        User32.ReadWindowRect(restoredHwnd).Should().BeEquivalentTo(manualBounds);
                        restoredFree.UpdatePosition(gameBounds, false);
                        User32.ReadWindowRect(restoredHwnd).Should().BeEquivalentTo(manualBounds);
                    }
                    finally { restoredFree.Close(); }

                    freeMovement.IsChecked = false;
                    WaitForPreferenceSave(freeMovement);
                    User32.ReadWindowRect(sideHwnd).Should().BeEquivalentTo(manualBounds,
                        "disabling free movement should capture the current position without snapping");
                    var savedFollow = LoadSavedPosition();
                    savedFollow.FreeMovement.Should().BeFalse();
                    savedFollow.FollowOffsetDip.Should().NotBeNull();
                    movedGame.Left += 20;
                    movedGame.Right += 20;
                    movedGame.Top += 15;
                    movedGame.Bottom += 15;
                    sidePanel.UpdatePosition(movedGame, false);
                    var followedBounds = User32.ReadWindowRect(sideHwnd);
                    followedBounds.Left.Should().Be(manualBounds.Left + 20);
                    followedBounds.Top.Should().Be(manualBounds.Top + 15);
                    followedBounds.Height.Should().Be(manualBounds.Height);
                    var restoredFollow = new SidePanelWindow(vm, equipmentVm, trackingVm,
                        new GameStateViewModel(), new BanViewModel(), enemyVm, lineupVm,
                        new LineupShareService(lineupVm.Calculator.EquipmentRules, Path.Combine(output, "share-links"), failedShareHttp), operators, equipment, strategies,
                        new SettingsManager(output), GameShopTheme.Terminal, savedFollow)
                    { Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
                    try
                    {
                        restoredFollow.Show();
                        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                        restoredFollow.UpdatePosition(movedGame, false);
                        User32.ReadWindowRect(new System.Windows.Interop.WindowInteropHelper(restoredFollow).Handle)
                            .Should().BeEquivalentTo(followedBounds);
                    }
                    finally { restoredFollow.Close(); }
                    sidePanel.UpdatePosition(movedGame, true);
                    User32.ReadWindowRect(sideHwnd).Should().BeEquivalentTo(followedBounds);
                    freeMovement.IsChecked = true;
                    WaitForPreferenceSave(freeMovement);
                    resetPosition.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    WaitForPreferenceSave(freeMovement);
                    freeMovement.IsChecked.Should().BeFalse();
                    var resetBounds = User32.ReadWindowRect(sideHwnd);
                    resetBounds.Right.Should().Be(movedGame.Right);
                    resetBounds.Width.Should().Be(followedBounds.Width);
                    resetBounds.Height.Should().Be(followedBounds.Height);
                    var savedReset = LoadSavedPosition();
                    savedReset.FreeMovement.Should().BeFalse();
                    savedReset.FollowOffsetDip.Should().BeNull();
                    sidePanel.ClearGamePosition();
                    resetPosition.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    User32.ReadWindowRect(sideHwnd).Should().BeEquivalentTo(resetBounds);
                    ((TextBlock)sidePanel.FindName("ToastText")).Text.Should().Contain("未找到明日方舟");
                    LoadSavedPosition().Should().Be(savedReset);
                    sidePanel.UpdatePosition(gameBounds, false);

                    var overlay = new OverlayWindow { Opacity = 0, ShowActivated = false, ShowInTaskbar = false };
                    try
                    {
                        var clientBounds = new RECT
                        {
                            Left = work.Left + 40, Top = work.Top + 40,
                            Right = work.Left + 40 + Math.Min(800, work.Width - 80),
                            Bottom = work.Top + 40 + Math.Min(600, work.Height - 80),
                        };
                        overlay.UpdatePosition(clientBounds);
                        overlay.Show();
                        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                        var overlayHwnd = new System.Windows.Interop.WindowInteropHelper(overlay).Handle;
                        User32.ReadWindowRect(overlayHwnd).Should().BeEquivalentTo(clientBounds);
                        var overlayScale = User32.GetWindowDpiScale(overlayHwnd);
                        overlay.ActualWidth.Should().BeApproximately(clientBounds.Width / overlayScale, 1);
                        overlay.ActualHeight.Should().BeApproximately(clientBounds.Height / overlayScale, 1);
                        overlay.UpdateShopItems([new ShopItem
                        {
                            Id = "dpi-marker", Name = "塔露拉", IsTracked = true, OcrRegion = (0.4, 0.8, 0.1, 0.03),
                        }]);
                        var canvas = (Canvas)overlay.FindName("OverlayCanvas");
                        var marker = canvas.Children.OfType<Grid>().Single();
                        Canvas.GetLeft(marker).Should().BeApproximately(overlay.ActualWidth * 0.5 - 27, 0.001);
                        Canvas.GetTop(marker).Should().BeApproximately(overlay.ActualHeight * 0.8 - 29, 0.001);
                        // 即使变化小于原有 6 DIP 容差，视口改变后也必须重新投影已有标记。
                        clientBounds.Right += 4;
                        clientBounds.Bottom += 4;
                        overlay.UpdatePosition(clientBounds);
                        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                        User32.ReadWindowRect(overlayHwnd).Should().BeEquivalentTo(clientBounds);
                        canvas.Children.OfType<Grid>().Single().Should().BeSameAs(marker);
                        Canvas.GetLeft(marker).Should().BeApproximately(overlay.ActualWidth * 0.5 - 27, 0.001);
                        Canvas.GetTop(marker).Should().BeApproximately(overlay.ActualHeight * 0.8 - 29, 0.001);
                        (User32.GetWindowLong(overlayHwnd, User32.GWL_EXSTYLE) & User32.WS_EX_NOACTIVATE).Should().NotBe(0);
                        User32.GetForegroundWindow().Should().NotBe(sideHwnd).And.NotBe(overlayHwnd);
                        var cachedShop = new List<ShopItem>
                        {
                            new() { Id = "old-target", Name = "角峰", OcrRegion = (.2, .8, .1, .03) },
                            new() { Id = "new-target", Name = "隐现", OcrRegion = (.4, .8, .1, .03) },
                            new() { Id = "equipment-target", Name = "坚守盾牌", OcrRegion = (.6, .8, .1, .03) }
                        };
                        Core.GameState.GameStateUpdater.UpdateShopTracking(cachedShop,
                            [new() { Name = "角峰", Type = TrackingType.Operator }]);
                        overlay.UpdateShopItems(cachedShop);
                        WaitForUi(() => canvas.Children.OfType<Grid>().Count() == 1, "the old test marker must finish fading out");
                        var oldTarget = canvas.Children.OfType<Grid>().Single();
                        Core.GameState.GameStateUpdater.UpdateShopTracking(cachedShop,
                            [new() { Name = "隐现", Type = TrackingType.Operator }, new() { Name = "坚守盾牌", Type = TrackingType.Equipment }]);
                        overlay.UpdateShopItems(cachedShop);
                        WaitForUi(() => canvas.Children.OfType<Grid>().Count() == 2 && !canvas.Children.Contains(oldTarget),
                            "switching tracking must replace old highlights using the same shop results");
                        var markerPositions = canvas.Children.OfType<Grid>().Select(Canvas.GetLeft).Order().ToArray();
                        markerPositions[0].Should().BeApproximately(overlay.ActualWidth * .5 - 27, .001);
                        markerPositions[1].Should().BeApproximately(overlay.ActualWidth * .7 - 27, .001);
                        Core.GameState.GameStateUpdater.UpdateShopTracking(cachedShop, []);
                        overlay.UpdateShopItems(cachedShop);
                        WaitForUi(() => !canvas.Children.OfType<Grid>().Any(), "clearing tracking must remove every highlight");
                    }
                    finally { overlay.Close(); }

                    sidePanel.SetGameMinimized(true);
                    sidePanel.WindowState.Should().Be(WindowState.Minimized);
                    sidePanel.SetGameMinimized(false);
                    sidePanel.WindowState.Should().Be(WindowState.Normal);
                    sidePanel.WindowState = WindowState.Minimized;
                    sidePanel.SetGameMinimized(true);
                    sidePanel.SetGameMinimized(false);
                    sidePanel.WindowState.Should().Be(WindowState.Minimized,
                        "a manually minimized assistant must stay minimized when the game restores");
                    sidePanel.WindowState = WindowState.Normal;
                    var content = (Border)sidePanel.Content;
                    var tabs = Descendants<TabControl>(content).Single();
                    var settingsButton = (Button)sidePanel.FindName("SettingsButton");
                    settingsButton.Content.Should().BeOfType<System.Windows.Shapes.Path>();
                    sidePanel.FindName("ThemeToggleButton").Should().BeNull();
                    var settingsPanel = (Border)sidePanel.FindName("SettingsPanel");
                    settingsPanel.Effect.Should().BeNull();
                    var uiThemes = (ListBox)sidePanel.FindName("UiThemeSelector");
                    var gameThemes = (ListBox)sidePanel.FindName("GameThemeSelector");
                    var multiToggle = (CheckBox)sidePanel.FindName("MultiLineupTrackingToggle");
                    gameThemes.Items.Cast<ListBoxItem>().Select(i => i.Content).Should()
                        .Equal("卫戍协议终端", "乌萨斯齐心团", "“全能的”可露希尔");
                    gameThemes.SelectedIndex.Should().Be(0);
                    // 矮侧栏的设置区必须能滚动，新增位置控件不能挤掉已有更新入口。
                    settingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    var settingsScroll = (ScrollViewer)sidePanel.FindName("SettingsScroll");
                    settingsScroll.ScrollableHeight.Should().BeGreaterThan(0);
                    settingsScroll.ScrollToBottom();
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    settingsScroll.VerticalOffset.Should().BeGreaterThan(0);
                    settingsScroll.ScrollToTop();
                    settingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    var previewBounds = WindowGeometry.ClampBounds(new RECT
                    {
                        Left = work.Left + 20, Top = work.Top + 20,
                        Right = work.Left + 20 + (int)Math.Round(310 * sideScale),
                        Bottom = work.Top + 20 + (int)Math.Round(900 * sideScale),
                    }, work);
                    User32.SetWindowBounds(sideHwnd, previewBounds);
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    GameShopTheme changedTheme = GameShopTheme.Terminal;
                    sidePanel.GameThemeChanged += theme => changedTheme = theme;
                    foreach (var isLight in new[] { false, true })
                    {
                        ThemeManager.Apply(ASPAssistant.Core.Models.GameShopTheme.Terminal, !isLight);
                        settingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                        settingsPanel.Visibility.Should().Be(Visibility.Visible);
                        ((SolidColorBrush)settingsPanel.Background).Color.Should().Be(((SolidColorBrush)application.FindResource("PanelBackgroundBrush")).Color);
                        ((SolidColorBrush)settingsButton.Background).Color.Should().Be(((SolidColorBrush)application.FindResource("AccentTintBrush")).Color);
                        ((SolidColorBrush)((System.Windows.Shapes.Path)settingsButton.Content).Fill).Color.Should()
                            .Be(((SolidColorBrush)application.FindResource("AccentBrush")).Color);
                        uiThemes.SelectedIndex = isLight ? 1 : 0;
                        WaitForPreferenceSave(uiThemes);
                        ThemeManager.IsLight.Should().Be(isLight);
                        foreach (var theme in Enum.GetValues<GameShopTheme>())
                        {
                            gameThemes.SelectedIndex = (int)theme;
                            WaitForPreferenceSave(gameThemes);
                            sidePanel.GameTheme.Should().Be(theme);
                            changedTheme.Should().Be(theme);
                        }
                        gameThemes.SelectedIndex = 0;
                        WaitForPreferenceSave(gameThemes);
                        multiToggle.IsChecked = true;
                        WaitForPreferenceSave(multiToggle);
                        trackingVm.TrackMultipleLineups.Should().BeTrue();
                        File.ReadAllText(Path.Combine(output, "game-theme.json")).Should().Be("\"Terminal\"");
                        File.ReadAllText(Path.Combine(output, "multiple-lineups.json")).Should().Be("true");
                        content.Measure(new Size(310, 900));
                        content.Arrange(new Rect(0, 0, 310, 900));
                        content.UpdateLayout();
                        Save(content, Path.Combine(output, isLight ? "light-settings.png" : "dark-settings.png"));
                        Descendants<Button>(settingsPanel).Single(button => Equals(button.ToolTip, "收起设置"))
                            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                        settingsPanel.Visibility.Should().Be(Visibility.Collapsed);
                        ((SolidColorBrush)settingsButton.Background).Color.Should().Be(Colors.Transparent);
                        ((SolidColorBrush)((System.Windows.Shapes.Path)settingsButton.Content).Fill).Color.Should()
                            .Be(((SolidColorBrush)application.FindResource("TextPrimaryBrush")).Color);
                        for (var i = 0; i < tabs.Items.Count; i++)
                        {
                            tabs.SelectedIndex = i;
                            content.Measure(new Size(310, 900));
                            content.Arrange(new Rect(0, 0, 310, 900));
                            content.UpdateLayout();
                            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                            var page = (UserControl)((TabItem)tabs.Items[i]).Content;
                            if (page is OperatorBrowseView operatorPage)
                            {
                                var operatorFilters = (System.Windows.Controls.Primitives.ToggleButton)operatorPage.FindName("FilterToggle");
                                var filtersWereOpen = operatorFilters.IsChecked;
                                operatorFilters.IsChecked = true;
                                var previousAvailable = vm.HighlightedCovenants.ToArray();
                                var previousBans = sidePanel.BanVm.BannedCovenants.ToArray();
                                var previousOnly = vm.OnlyCurrentRun;
                                var previousPlannerOnly = sidePanel.PlannerVm.OnlyCurrentRun;
                                sidePanel.BanVm.SetCovenantBans(["炎"]);
                                vm.SetAvailableCovenants(["维多利亚", "精准"]);
                                var only = (CheckBox)operatorPage.FindName("OnlyCurrentRunToggle");
                                only.IsEnabled.Should().BeTrue();
                                only.IsChecked = true;
                                sidePanel.PlannerVm.OnlyCurrentRun = true;
                                content.UpdateLayout();
                                ((ListBox)operatorPage.FindName("CoreCovenantFilterList")).Items.Cast<string>()
                                    .Should().Contain("维多利亚").And.NotContain("炎");
                                sidePanel.Width = 280;
                                content.Measure(new Size(280, 900));
                                content.Arrange(new Rect(0, 0, 280, 900));
                                content.UpdateLayout();
                                only.ActualWidth.Should().BeGreaterThan(40);
                                (only.TranslatePoint(new Point(), operatorPage).X + only.ActualWidth).Should().BeLessThanOrEqualTo(operatorPage.ActualWidth);
                                Save(content, Path.Combine(output, isLight ? "light-operator-current-run.png" : "dark-operator-current-run.png"));
                                sidePanel.Width = 310;
                                content.Measure(new Size(310, 900));
                                content.Arrange(new Rect(0, 0, 310, 900));
                                content.UpdateLayout();
                                var clearRun = (Button)operatorPage.FindName("ClearHighlightsButton");
                                clearRun.Content.Should().Be("清除录入");
                                clearRun.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                                content.UpdateLayout();
                                only.IsEnabled.Should().BeFalse();
                                only.IsChecked.Should().BeFalse();
                                sidePanel.BanVm.BannedCovenants.Should().BeEmpty();
                                sidePanel.PlannerVm.HasRunInfo.Should().BeFalse();
                                sidePanel.PlannerVm.OnlyCurrentRun.Should().BeFalse();
                                sidePanel.BanVm.SetCovenantBans(previousBans);
                                vm.SetAvailableCovenants(previousAvailable);
                                vm.OnlyCurrentRun = previousOnly;
                                sidePanel.PlannerVm.OnlyCurrentRun = previousPlannerOnly;
                                operatorFilters.IsChecked = filtersWereOpen;
                            }
                            page.ActualWidth.Should().BeGreaterThan(290);
                            ((SolidColorBrush)page.Background).Color.Should().Be(isLight
                                ? Color.FromRgb(247, 249, 252) : Color.FromRgb(26, 26, 46));
                            if (page is LineupPlannerView plannerView)
                            {
                                plannerView.DataContext.Should().BeSameAs(sidePanel.PlannerVm);
                                sidePanel.PlannerVm.CoreCovenants.Should().HaveCount(10);
                                sidePanel.PlannerVm.ExtraCovenants.Should().HaveCount(16);
                                sidePanel.PlannerVm.AllCovenants.Should().Contain(c => c.HasEffects);
                                var levelButton = Descendants<Button>(plannerView).First(b => b.Name == "lv1");
                                levelButton.IsEnabled.Should().BeTrue();
                                var levelText = Descendants<TextBlock>(levelButton).Single(t => t.Text == "Ⅰ");
                                ((SolidColorBrush)levelText.Foreground).Color.Should().Be(
                                    ((SolidColorBrush)levelButton.Foreground).Color,
                                    "the planner level label must follow its button's theme foreground");
                                sidePanel.Width = 280;
                                content.Measure(new Size(280, 900));
                                content.Arrange(new Rect(0, 0, 280, 900));
                                content.UpdateLayout();
                                content.ActualWidth.Should().Be(280);
                                foreach (var button in Descendants<Button>(plannerView).Where(b => b.Name is "lv1" or "lv2"))
                                    button.TranslatePoint(new Point(button.ActualWidth, 0), plannerView).X
                                        .Should().BeLessThanOrEqualTo(plannerView.ActualWidth);
                                Save(content, Path.Combine(output, isLight ? "light-planner-narrow.png" : "dark-planner-narrow.png"));
                                sidePanel.Width = 310;
                                content.Measure(new Size(310, 900));
                                content.Arrange(new Rect(0, 0, 310, 900));
                                content.UpdateLayout();
                            }
                            if (page is EnemyView enemyView)
                            {
                                var button = (Button)enemyView.FindName("RecognizeEnemyButton");
                                enemyVm.BeginRecognition();
                                content.UpdateLayout();
                                button.IsEnabled.Should().BeFalse();
                                ((TextBlock)enemyView.FindName("RecognitionStatusText")).Text.Should().StartWith("正在");
                                enemyVm.Update(new("假想敌：鳍", ["特训敌人·元素", "特训敌人·致命"],
                                    ["假想敌：鳍", "特训敌人·元素", "特训敌人·致命"]),
                                    null, [new() { TypeName = "元素" }, null]);
                                content.UpdateLayout();
                                button.IsEnabled.Should().BeTrue();
                                Descendants<TextBlock>(enemyView).Should().Contain(t => t.Text.Contains("致命：资料未收录"));
                                int requested = 0;
                                Action onRequest = () => requested++;
                                sidePanel.EnemyRecognitionRequested += onRequest;
                                button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                                requested.Should().Be(1);
                                sidePanel.EnemyRecognitionRequested -= onRequest;
                            }
                            if (page is EquipmentBrowseView equipmentView)
                            {
                                ((ListBox)equipmentView.FindName("TypeFilterList")).Items.Cast<string>()
                                    .Should().Equal("", "普通", "转职");
                                ((ListBox)equipmentView.FindName("CategoryFilterList")).Items.Cast<string>()
                                    .Should().Equal("", "攻击", "防御", "生存", "技力", "经济", "盟约", "其他");
                                // “同时选中”复选框 + 空列表提示
                                var categoryMatchAll = (CheckBox)equipmentView.FindName("CategoryMatchAllToggle");
                                categoryMatchAll.Should().NotBeNull();
                                var emptyText = (TextBlock)equipmentView.FindName("EmptyFilterText");
                                emptyText.Visibility.Should().Be(Visibility.Collapsed);
                                equipmentVm.SelectedCategoryFilters = ["技力", "经济"];
                                content.UpdateLayout();
                                equipmentVm.FilteredEquipment.Should().HaveCount(9);
                                categoryMatchAll.IsChecked = true;
                                content.UpdateLayout();
                                equipmentVm.CategoryMatchAll.Should().BeTrue();
                                equipmentVm.FilteredEquipment.Should().BeEmpty();
                                emptyText.Visibility.Should().Be(Visibility.Visible);
                                emptyText.Text.Should().Be("没有同时满足所选条件的装备");
                                categoryMatchAll.IsChecked = false;
                                equipmentVm.SelectedCategoryFilters = [];
                                content.UpdateLayout();
                                emptyText.Visibility.Should().Be(Visibility.Collapsed);
                                ((System.Windows.Controls.Primitives.ToggleButton)equipmentView.FindName("FilterToggle")).IsChecked = true;
                                content.UpdateLayout();
                                var tiers = (ListBox)equipmentView.FindName("TierFilterList");
                                var tierCandidates = equipmentVm.FilteredEquipment.ToList();
                                Action<int[]> assertTiers = expected =>
                                {
                                    equipmentVm.SelectedTierFilters.Should().BeEquivalentTo(expected);
                                    equipmentVm.FilteredEquipment.Should().BeEquivalentTo(tierCandidates
                                        .Where(item => expected.Length == 0 || expected.Contains(item.Tier)));
                                };
                                AssertTierClicks(tiers, assertTiers);
                                equipmentVm.SelectedCategoryFilters = ["攻击"];
                                content.UpdateLayout();
                                tierCandidates = equipmentVm.FilteredEquipment.ToList();
                                AssertTierClicks(tiers, assertTiers);
                                equipmentVm.SelectedCategoryFilters = [];
                                content.UpdateLayout();
                                tiers.SelectedIndex = 1;
                                content.UpdateLayout();
                                equipmentVm.SelectedTierFilters.Should().ContainSingle();
                                equipmentVm.FilteredEquipment.Should().HaveCountLessThan(equipment.Count);
                                var allTier = (ListBoxItem)tiers.ItemContainerGenerator.ContainerFromIndex(0);
                                var click = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                                {
                                    RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent,
                                    Source = Descendants<TextBlock>(allTier).Single()
                                };
                                tiers.RaiseEvent(click);
                                content.UpdateLayout();
                                click.Handled.Should().BeTrue();
                                tiers.SelectedIndex.Should().Be(0);
                                equipmentVm.SelectedTierFilters.Should().BeEmpty();
                                equipmentVm.FilteredEquipment.Should().HaveCount(equipment.Count);

                                var cells = Descendants<EquipmentCell>(equipmentView).ToList();
                                cells.Should().NotBeEmpty();
                                // Full effect text lives in each cell's hover card.
                                var cards = equipment.Select(e => new EquipmentCard { DataContext = e }).ToList();
                                cards.Should().HaveCount(70);
                                var hammer = cards.Single(c => ((Equipment)c.DataContext).Name == "维式重锤");
                                var effect = (TextBlock)hammer.FindName("EffectText");
                                new TextRange(effect.ContentStart, effect.ContentEnd).Text.Should().Be("攻击力+15(25)%");
                                var highlighted = effect.Inlines.OfType<Run>().Single(r => r.Text == "15");
                                highlighted.FontWeight.Should().Be(FontWeights.Bold);
                                ((SolidColorBrush)highlighted.Foreground).Color.Should().Be(
                                    ((SolidColorBrush)application.FindResource("AccentBrush")).Color);
                                hammer.FindName("NormalToggle").Should().BeNull();
                                hammer.FindName("EliteToggle").Should().BeNull();
                                ((TextBlock)hammer.FindName("NoEliteText")).Visibility.Should().Be(Visibility.Collapsed);
                                var name = (TextBlock)hammer.FindName("NameText");
                                var icon = (Image)hammer.FindName("IconImage");
                                (name.TranslatePoint(new Point(), hammer).Y + name.ActualHeight)
                                    .Should().BeLessThanOrEqualTo(icon.TranslatePoint(new Point(), hammer).Y);

                                var ultimate = cards.Single(c => ((Equipment)c.DataContext).Name == "终极维式重锤");
                                var noElite = (TextBlock)ultimate.FindName("NoEliteText");
                                noElite.Visibility.Should().Be(Visibility.Visible);
                                var jobChange = (TextBlock)ultimate.FindName("JobChangeText");
                                jobChange.Text.Should().Be("转职：维多利亚");
                                jobChange.Visibility.Should().Be(Visibility.Visible);
                                jobChange.TranslatePoint(new Point(), ultimate).Y.Should()
                                    .BeGreaterThan(noElite.TranslatePoint(new Point(), ultimate).Y);
                                var deposit = cards.Single(c => ((Equipment)c.DataContext).Name == "定期存折");
                                ((TextBlock)deposit.FindName("NoEliteText")).Visibility.Should().Be(Visibility.Visible);
                                ((TextBlock)deposit.FindName("JobChangeText")).Visibility.Should().Be(Visibility.Collapsed);
                                var projector = cards.Single(c => ((Equipment)c.DataContext).Name == "博士投影");
                                var projectorEffect = (TextBlock)projector.FindName("EffectText");
                                new TextRange(projectorEffect.ContentStart, projectorEffect.ContentEnd).Text.Should()
                                    .Be("普通：装备后下个回合开始时销毁，并使该干员晋升为精锐干员\n精锐：装备时销毁，立即使该干员晋升为精锐干员");

                                foreach (var (equipmentName, expected) in new[]
                                {
                                    ("萨尔贡浓茶", "技力自然回复速度+0.15(0.25)/秒"),
                                    ("阿戈尔重刃", "攻击力+40(60)%，攻击速度-10"),
                                    ("叙拉古正装", "攻击速度+15(25)，携带者部署方向左右两侧的我方干员攻击速度+10(15)")
                                })
                                {
                                    var card = cards.Single(c => ((Equipment)c.DataContext).Name == equipmentName);
                                    var description = (TextBlock)card.FindName("EffectText");
                                    new TextRange(description.ContentStart, description.ContentEnd).Text.Should().Be(expected);
                                }

                                var samples = new StackPanel { Width = 294, Background = equipmentView.Background };
                                foreach (var equipmentName in new[] { "维式重锤", "终极维式重锤", "定期存折", "博士投影" })
                                    samples.Children.Add(new EquipmentCard { DataContext = equipment.Single(e => e.Name == equipmentName) });
                                samples.Measure(new Size(294, double.PositiveInfinity));
                                samples.Arrange(new Rect(samples.DesiredSize));
                                samples.UpdateLayout();
                                Save(samples, Path.Combine(output, isLight ? "light-equipment-samples.png" : "dark-equipment-samples.png"));
                            }
                            if (page is TrackingView trackingView)
                            {
                                trackingView.FindName("MultiLineupTrackingToggle").Should().BeNull();
                                trackingVm.TrackMultipleLineups.Should().BeTrue();
                                // Tracked operators are compact cells grouped by role (大成 / 过渡 / 已过).
                                var finalCells = ((IEnumerable<TrackedCell>)((ItemsControl)trackingView.FindName("FinalCells")).ItemsSource).ToList();
                                finalCells.Should().HaveCount(2);
                                var trackedCell = finalCells.Single(c => c.Name == "烈夏");
                                trackedCell.Operator.Should().NotBeNull();
                                trackedCell.Sub.Should().Contain("乌萨斯");
                                ((FrameworkElement)trackingView.FindName("TransitionSection")).Visibility.Should().Be(Visibility.Collapsed);
                                var detail = (ToolTip)null!;
                                var hoverCard = new OperatorCard
                                {
                                    DataContext = trackedCell.Operator,
                                    IsTrackingList = true,
                                    VerticalAlignment = VerticalAlignment.Top
                                };
                                host.Content = hoverCard;
                                host.Opacity = 0;
                                host.Left = -10000;
                                host.Top = -10000;
                                host.Width = 310;
                                host.Height = 100;
                                host.Show();
                                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                                detail = (ToolTip)hoverCard.FindName("DetailsToolTip");
                                detail.PlacementTarget = (UIElement)hoverCard.FindName("CardBorder");
                                detail.MaxHeight = 100;
                                var row = (Border)hoverCard.FindName("CardBorder");
                                row.ActualHeight.Should().Be(53);
                                detail.Opacity = 0; // Exercise the popup lifecycle without displaying test UI.
                                detail.IsOpen = true;
                                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                                detail.IsOpen.Should().BeTrue();
                                row.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
                                WaitForUi(() => !detail.IsOpen, "moving off the operator into equipment or blank space must close details");
                                detail.IsOpen = true;
                                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                                detail.IsOpen.Should().BeTrue();
                                row.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
                                detail.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
                                var grace = System.Diagnostics.Stopwatch.StartNew();
                                while (grace.Elapsed < TimeSpan.FromMilliseconds(250))
                                {
                                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                                    Thread.Sleep(1);
                                }
                                detail.IsOpen.Should().BeTrue("crossing the gap into details must cancel the pending close");
                                var detailScroll = Descendants<ScrollViewer>(detail).Single();
                                detailScroll.ScrollableHeight.Should().BeGreaterThan(0);
                                detailScroll.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120)
                                    { RoutedEvent = Mouse.PreviewMouseWheelEvent });
                                detail.IsOpen.Should().BeTrue("scrolling inside details must keep the popup open");
                                detail.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
                                WaitForUi(() => !detail.IsOpen, "leaving the details must also close the popup");
                                detail.IsOpen = true;
                                hoverCard.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, 120)
                                    { RoutedEvent = Mouse.PreviewMouseWheelEvent });
                                detail.IsOpen.Should().BeFalse("scrolling the operator list must dismiss its details");
                                detail.Opacity = 1;
                                Descendants<Button>(trackingView).First(b => b.DataContext is TrackedCell { Name: "烈夏" })
                                    .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                                trackingVm.IsTracked("烈夏").Should().BeFalse();
                                trackingVm.IsTracked("塔露拉").Should().BeTrue();
                                trackingVm.AddTracking("烈夏", TrackingType.Operator);
                                trackingVm.AddTracking("旧版已移除干员", TrackingType.Operator);
                                content.UpdateLayout();
                                Descendants<TextBlock>(trackingView).Should().Contain(t => t.Text == "旧版已移除干员");
                                var removeUnknown = Descendants<Button>(trackingView)
                                    .First(b => b.DataContext is TrackedCell { Name: "旧版已移除干员" });
                                removeUnknown.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                                trackingVm.IsTracked("旧版已移除干员").Should().BeFalse();
                                content.UpdateLayout();
                                var equipmentCards = Descendants<EquipmentCell>(trackingView).ToList();
                                equipmentCards.Should().HaveCount(trackingVm.TrackedEquipment.Count);
                                equipmentCards.SelectMany(c => ((Equipment)c.DataContext).Categories).Distinct().Should()
                                    .BeEquivalentTo(EquipmentBrowseViewModel.CategoryOrder);
                                foreach (var equipmentCard in equipmentCards)
                                {
                                    var item = (Equipment)equipmentCard.DataContext;
                                    var sub = ((TextBlock)equipmentCard.FindName("SubText")).Text;
                                    foreach (var category in item.Categories) sub.Should().Contain(category);
                                    if (item.Type == EquipmentType.JobChange) sub.Should().Contain("转职");
                                    if (!string.IsNullOrEmpty(item.JobChangeCovenant))
                                        sub.Should().Contain(item.JobChangeCovenant);
                                    var equipmentDetails = (ToolTip)equipmentCard.FindName("DetailsToolTip");
                                    equipmentDetails.PlacementTarget = (UIElement)equipmentCard.FindName("CardBorder");
                                    equipmentDetails.Measure(new Size(320, 700));
                                    equipmentDetails.Arrange(new Rect(equipmentDetails.DesiredSize));
                                    equipmentDetails.UpdateLayout();
                                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                                    var fullCard = (EquipmentCard)equipmentCard.FindName("DetailsCard");
                                    ((TextBlock)fullCard.FindName("NameText")).Text.Should().Be(item.Name);
                                    ((ItemsControl)fullCard.FindName("CategoryChips")).Items.Cast<string>().Should().Equal(item.Categories);
                                    ((Image)fullCard.FindName("IconImage")).Source.Should().NotBeNull();
                                    ((Button)fullCard.FindName("TrackButton")).Visibility.Should().Be(Visibility.Collapsed);
                                    ((TextBlock)fullCard.FindName("NoEliteText")).Visibility.Should().Be(
                                        string.IsNullOrEmpty(item.Elite.EffectDescription) ? Visibility.Visible : Visibility.Collapsed);
                                    var effect = (TextBlock)fullCard.FindName("EffectText");
                                    new TextRange(effect.ContentStart, effect.ContentEnd).Text.Should().NotBeNullOrWhiteSpace();
                                    if (item.Name == "博士投影")
                                        new TextRange(effect.ContentStart, effect.ContentEnd).Text.Should()
                                            .Be($"普通：{item.Normal.EffectDescription}\n精锐：{item.Elite.EffectDescription}");
                                    Save(equipmentDetails, Path.Combine(output,
                                        $"{(isLight ? "light" : "dark")}-tracked-equipment-{item.Name}.png"));
                                }

                                var equipmentHover = new EquipmentCell
                                {
                                    IsTrackingList = true,
                                    DataContext = equipment.Single(e => e.Name == "博士投影"),
                                    VerticalAlignment = VerticalAlignment.Top
                                };
                                host.Content = equipmentHover;
                                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                                var equipmentRow = (Border)equipmentHover.FindName("CardBorder");
                                var hoverDetails = (ToolTip)equipmentHover.FindName("DetailsToolTip");
                                hoverDetails.PlacementTarget = equipmentRow;
                                hoverDetails.MaxHeight = 100;
                                hoverDetails.Opacity = 0;
                                hoverDetails.IsOpen = true;
                                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                                hoverDetails.IsOpen.Should().BeTrue();
                                equipmentRow.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
                                hoverDetails.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseEnterEvent });
                                var equipmentGrace = System.Diagnostics.Stopwatch.StartNew();
                                while (equipmentGrace.Elapsed < TimeSpan.FromMilliseconds(250))
                                {
                                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                                    Thread.Sleep(1);
                                }
                                hoverDetails.IsOpen.Should().BeTrue("the equipment popup must allow crossing into its details");
                                Descendants<ScrollViewer>(hoverDetails).Single().RaiseEvent(
                                    new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, -120) { RoutedEvent = Mouse.PreviewMouseWheelEvent });
                                hoverDetails.IsOpen.Should().BeTrue("equipment details must remain open while scrolling inside");
                                hoverDetails.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice, 0) { RoutedEvent = Mouse.MouseLeaveEvent });
                                WaitForUi(() => !hoverDetails.IsOpen, "leaving equipment details must dismiss the popup");
                                hoverDetails.IsOpen = true;
                                equipmentHover.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice, 0, 120)
                                    { RoutedEvent = Mouse.PreviewMouseWheelEvent });
                                hoverDetails.IsOpen.Should().BeFalse("scrolling the tracking list must close equipment details");

                                var shield = equipmentCards.Single(c => ((Equipment)c.DataContext).Name == "坚守盾牌");
                                ((Button)shield.FindName("TrackButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                                trackingVm.IsTracked("坚守盾牌").Should().BeFalse();
                                trackingVm.IsTracked("乌萨斯呼吸器").Should().BeTrue();
                                trackingVm.AddTracking("坚守盾牌", TrackingType.Equipment);
                                trackingVm.AddTracking("旧版已移除装备", TrackingType.Equipment);
                                content.UpdateLayout();
                                var removeUnknownEquipment = Descendants<Button>(trackingView)
                                    .First(b => b.DataContext is TrackingEntry { Name: "旧版已移除装备" });
                                removeUnknownEquipment.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                                trackingVm.IsTracked("旧版已移除装备").Should().BeFalse();
                                content.UpdateLayout();
                                sidePanel.Width = 280;
                                content.Measure(new Size(280, 900));
                                content.Arrange(new Rect(0, 0, 280, 900));
                                content.UpdateLayout();
                                content.ActualWidth.Should().Be(280);
                                foreach (var equipmentCard in Descendants<EquipmentCell>(trackingView))
                                {
                                    var equipmentBorder = (Border)equipmentCard.FindName("CardBorder");
                                    foreach (var name in new[] { "NameText", "SubText", "TrackButton" })
                                    {
                                        var element = (FrameworkElement)equipmentCard.FindName(name);
                                        var left = element.TranslatePoint(new Point(), equipmentBorder).X;
                                        left.Should().BeGreaterThanOrEqualTo(0);
                                        (left + element.ActualWidth).Should().BeLessThanOrEqualTo(equipmentBorder.ActualWidth);
                                    }
                                }
                                Save(content, Path.Combine(output, isLight ? "light-tracking-narrow.png" : "dark-tracking-narrow.png"));
                                var previousEntries = trackingVm.AllEntries.ToList();
                                var multipleLineups = trackingVm.TrackMultipleLineups;
                                try
                                {
                                    trackingVm.TrackMultipleLineups = false;
                                    var target = covenants.First(c => c.BondType == "SEASON"
                                        && !operators.Single(o => o.Name == "烈夏").CoreCovenants.Contains(c.Name)).Name;
                                    var plannedLineup = new Lineup { Slots =
                                    [
                                        new() { OperatorName = "烈夏", Equipments = ["变形同构体", lineupVm.Calculator.EquipmentRules.EquipmentFor(target)] },
                                        new() { OperatorName = "魔王" }
                                    ] };
                                    trackingVm.TrackLineup(plannedLineup);
                                    content.UpdateLayout();
                                    var chips = (ItemsControl)trackingView.FindName("TrackedCovenantChips");
                                    chips.Items.Cast<TrackedCovenantChip>().ToDictionary(c => c.Name, c => c.Count)
                                        .Should().BeEquivalentTo(lineupVm.Calculator.Compute(plannedLineup)
                                            .Where(s => s.Count > 0).ToDictionary(s => s.Name, s => s.Count));
                                    var cancelTransform = Descendants<Button>(trackingView)
                                        .Single(b => b.Tag as string == "烈夏" && b.DataContext is EquipmentTransformation t && t.Covenant == target);
                                    cancelTransform.Visibility.Should().Be(Visibility.Visible);
                                    Descendants<TextBlock>(cancelTransform).Should().Contain(t => t.Text.Contains($"转职：{target}"));
                                    (cancelTransform.TranslatePoint(new Point(), trackingView).X + cancelTransform.ActualWidth)
                                        .Should().BeLessThanOrEqualTo(trackingView.ActualWidth);
                                    Save(content, Path.Combine(output, isLight ? "light-tracking-transform.png" : "dark-tracking-transform.png"));
                                    cancelTransform.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                                    content.UpdateLayout();
                                    trackingVm.TrackedOperators.Single(e => e.Name == "烈夏").Equipments.Should().BeEmpty();
                                    chips.Items.Cast<TrackedCovenantChip>().Should().NotContain(c => c.Name == target);
                                    plannedLineup.Slots[0].Equipments.Should().Contain(lineupVm.Calculator.EquipmentRules.EquipmentFor(target));
                                }
                                finally
                                {
                                    trackingVm.LoadEntries(previousEntries);
                                    trackingVm.TrackMultipleLineups = multipleLineups;
                                }
                                sidePanel.Width = 310;
                                content.Measure(new Size(310, 900));
                                content.Arrange(new Rect(0, 0, 310, 900));
                                content.UpdateLayout();
                                content.Measure(new Size(310, 900));
                                content.Arrange(new Rect(0, 0, 310, 900));
                                content.UpdateLayout();
                            }
                            Save(content, Path.Combine(output, $"{(isLight ? "light" : "dark")}-page-{i}.png"));
                        }
                    }

                    foreach (var isLight in new[] { false, true })
                    {
                        ThemeManager.Apply(ASPAssistant.Core.Models.GameShopTheme.Terminal, !isLight);
                        const string sharedUrl = "https://dpaste.com/TEST12345";
                        var attempts = 0;
                        var copiedText = "";
                        var shareWindow = new ShareLinkWindow("测试阵容", sharedUrl, text =>
                        {
                            attempts++;
                            if (attempts < 3) throw new COMException("Clipboard busy", unchecked((int)0x800401D0));
                            copiedText = text;
                        });
                        try
                        {
                            var copyTask = shareWindow.CopyLinkAsync();
                            var copyButton = (Button)shareWindow.FindName("CopyButton");
                            copyButton.IsEnabled.Should().BeFalse();
                            shareWindow.CopyLinkAsync().IsCompleted.Should().BeTrue("repeated clicks must not create another copy attempt");
                            WaitForUi(() => copyTask.IsCompleted, "a temporary clipboard lock should recover");
                            copyTask.GetAwaiter().GetResult();
                            attempts.Should().Be(3);
                            copiedText.Should().Be($"<测试阵容> {sharedUrl}");
                            ((TextBlock)shareWindow.FindName("CopyStatusText")).Text.Should().Contain("已复制");
                            copyButton.IsEnabled.Should().BeTrue();
                        }
                        finally { shareWindow.Close(); }

                        var clipboardBusy = true;
                        attempts = 0;
                        var fallbackWindow = new ShareLinkWindow("测试阵容", sharedUrl, _ =>
                        {
                            attempts++;
                            if (clipboardBusy) throw new COMException("Clipboard busy", unchecked((int)0x800401D0));
                        });
                        try
                        {
                            var copyTask = fallbackWindow.CopyLinkAsync();
                            WaitForUi(() => copyTask.IsCompleted, "a persistent clipboard lock must stop retrying");
                            copyTask.GetAwaiter().GetResult();
                            attempts.Should().Be(3);
                            var linkBox = (TextBox)fallbackWindow.FindName("LinkBox");
                            linkBox.Text.Should().Be($"<测试阵容> {sharedUrl}");
                            linkBox.IsReadOnly.Should().BeTrue();
                            linkBox.SelectionLength.Should().Be(linkBox.Text.Length);
                            ((TextBlock)fallbackWindow.FindName("CopyStatusText")).Text.Should().Contain("链接已生成").And.Contain("手动复制");
                            var dialogContent = (Border)fallbackWindow.Content;
                            dialogContent.Measure(new Size(460, 230));
                            dialogContent.Arrange(new Rect(0, 0, 460, 230));
                            dialogContent.UpdateLayout();
                            Save(dialogContent, Path.Combine(output, isLight ? "light-share-link.png" : "dark-share-link.png"));
                            clipboardBusy = false;
                            copyTask = fallbackWindow.CopyLinkAsync();
                            WaitForUi(() => copyTask.IsCompleted, "manual retry must reuse the existing link");
                            copyTask.GetAwaiter().GetResult();
                            attempts.Should().Be(4);
                            ((TextBlock)fallbackWindow.FindName("CopyStatusText")).Text.Should().Contain("已复制");
                        }
                        finally { fallbackWindow.Close(); }

                        var editorVm = new LineupEditorViewModel(null, operators, equipment, lineupVm.Calculator);
                        editorVm.SetStrategy(0, "band_amiya");
                        editorVm.SetStrategy(1, "band_upgrade");
                        var longestStrategy = strategies.MaxBy(s => s.Description.Length)!;
                        editorVm.SetStrategy(2, longestStrategy.Id);
                        editorVm.SetStrategy(3, "unknown-shared-strategy");
                        var editor = new LineupEditorWindow(editorVm, strategies);
                        try
                        {
                            var phaseTabs = (WrapPanel)editor.FindName("PhaseTabs");
                            phaseTabs.Children.Cast<Button>().Select(b => b.Content).Should().Equal("前期", "中期", "后期", "挂件", "策略");
                            ((Button)phaseTabs.Children[4]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            var editorContent = (Border)editor.Content;
                            editorContent.Measure(new Size(980, 660));
                            editorContent.Arrange(new Rect(0, 0, 980, 660));
                            editorContent.UpdateLayout();
                            var slots = (ItemsControl)editor.FindName("SlotsHost");
                            slots.Items.Count.Should().Be(9);
                            var firstCard = (Border)slots.Items[0];
                            Descendants<TextBlock>(firstCard).Should().NotContain(t => t.Text.Contains("解锁要求"));
                            Descendants<TextBlock>(firstCard).Should().Contain(t => t.Text == strategies.Single(s => s.Id == "band_amiya").Description);
                            Descendants<Button>(firstCard).Select(b => b.Content).Should().Equal("✕", "阵容核心", "优选", "次选");
                            Descendants<Image>(firstCard).Single().Source.Should().NotBeNull();
                            Descendants<TextBlock>((Border)slots.Items[1]).Should().Contain(t => t.Text == strategies.Single(s => s.Id == "band_upgrade").Description);
                            Descendants<TextBlock>((Border)slots.Items[2]).Should().Contain(t => t.Text == longestStrategy.Description);
                            foreach (var card in slots.Items.Cast<Border>().Take(3))
                                Descendants<TextBlock>(card).Should().NotContain(t => t.Text.Contains("解锁要求："));
                            Descendants<TextBlock>((Border)slots.Items[3]).Should().Contain(t => t.Text == "资料未收录");
                            Descendants<Button>((Border)slots.Items[3]).Single(b => (string)b.Content == "✕")
                                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            ((Border)editor.FindName("CovenantPanel")).Visibility.Should().Be(Visibility.Collapsed);
                            var core = Descendants<Button>(firstCard).Single(b => (string)b.Content == "阵容核心");
                            core.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            editorContent.UpdateLayout();
                            core = Descendants<Button>(firstCard).Single(b => (string)b.Content == "阵容核心");
                            core.Tag.Should().Be("core");
                            ((SolidColorBrush)core.Foreground).Color.Should().Be(
                                ((SolidColorBrush)application.FindResource("FilterSelectedBrush")).Color);
                            editorVm.StrategySlots[0].Tags.Should().Contain(StrategyTag.阵容核心);
                            Descendants<Button>(firstCard).Single(b => (string)b.Content == "优选")
                                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            editorVm.StrategySlots[0].Tags.Should().Contain(StrategyTag.优选);
                            editorContent.UpdateLayout();
                            Save(editorContent, Path.Combine(output, isLight ? "light-strategy-editor.png" : "dark-strategy-editor.png"));
                            editorContent.Measure(new Size(720, 500));
                            editorContent.Arrange(new Rect(0, 0, 720, 500));
                            editorContent.UpdateLayout();
                            Save(editorContent, Path.Combine(output, isLight ? "light-strategy-editor-narrow.png" : "dark-strategy-editor-narrow.png"));
                            foreach (var card in slots.Items.Cast<Border>().Take(3))
                            {
                                foreach (var text in Descendants<TextBlock>(card))
                                {
                                    (text.TranslatePoint(new Point(), card).X + text.ActualWidth)
                                        .Should().BeLessThanOrEqualTo(card.ActualWidth + 1);
                                    (text.TranslatePoint(new Point(), card).Y + text.ActualHeight)
                                        .Should().BeLessThanOrEqualTo(card.ActualHeight + 1);
                                    text.ActualHeight.Should().BeGreaterThanOrEqualTo(
                                        text.DesiredSize.Height - text.Margin.Top - text.Margin.Bottom - 1);
                                }
                            }
                            Descendants<Button>((Border)slots.Items[1]).Single(b => (string)b.Content == "✕")
                                .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            editorVm.StrategySlots[1].StrategyId.Should().BeEmpty();
                            ((Button)phaseTabs.Children[3]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            ((Border)editor.FindName("CovenantPanel")).Visibility.Should().Be(Visibility.Visible);
                            Descendants<Button>((Border)slots.Items[0]).Single().Content.Should().Be("+ 选择干员");
                            ((Button)phaseTabs.Children[4]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            Descendants<TextBlock>((Border)slots.Items[0]).Should().Contain(t => t.Text == "众志合一");
                            editorVm.SelectPhase(LineupPhase.后期);
                            editorVm.SetSlotOperator(0, "烈夏");
                            editorVm.SetSlotEquipment(0, 0, "变形同构体", false);
                            editorVm.SetSlotEquipment(0, 1, "炎国短刀", false);
                            editorVm.Recalculate();
                            ((Button)phaseTabs.Children[2]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            editorContent.UpdateLayout();
                            var transformedCard = (Border)slots.Items[0];
                            var cancelTransform = Descendants<Button>(transformedCard)
                                .Single(b => b.Tag as string == "炎国短刀");
                            (cancelTransform.TranslatePoint(new Point(), transformedCard).X + cancelTransform.ActualWidth)
                                .Should().BeLessThanOrEqualTo(transformedCard.ActualWidth);
                            Save(editorContent, Path.Combine(output, isLight ? "light-editor-transform.png" : "dark-editor-transform.png"));
                            cancelTransform.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            editorVm.ToLineup().Slots.Single().Equipments.Should().BeEmpty();
                            editorVm.ActiveStats.Should().NotContain(s => s.Name == "炎");
                            editorVm.SetStrategy(0, "band_mech");
                            editorVm.SetSlotOperator(0, LineupEquipmentService.MechName);
                            ((Button)phaseTabs.Children[2]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            editorContent.UpdateLayout();
                            var specialSlots = (ItemsControl)editor.FindName("SpecialSlotsHost");
                            specialSlots.Items.Count.Should().Be(1);
                            var mechCard = (Border)specialSlots.Items[0];
                            Descendants<Button>(mechCard).Count(b => System.Windows.Automation.AutomationProperties.GetName(b).StartsWith("装备槽"))
                                .Should().Be(6);
                            Descendants<Button>(mechCard).Count(b => System.Windows.Automation.AutomationProperties.GetName(b).StartsWith("转职槽"))
                                .Should().Be(5);
                            Descendants<Button>(mechCard).Count(b => !b.IsEnabled).Should().Be(4);
                            Save(editorContent, Path.Combine(output, isLight ? "light-mech-normal.png" : "dark-mech-normal.png"));
                            Descendants<CheckBox>(mechCard).Single().IsChecked = true;
                            editorContent.UpdateLayout();
                            Descendants<Button>(mechCard).Should().OnlyContain(b => b.IsEnabled);
                            foreach (var transfer in new[] { "炎国短刀", "维式重锤", "萨尔贡浓茶", "叙拉古正装", "乌萨斯呼吸器" }.Select((name, index) => (name, index)))
                                editorVm.SetSlotTransformation(0, transfer.index, transfer.name, []);
                            ((Button)phaseTabs.Children[2]).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            editorContent.UpdateLayout();
                            Save(editorContent, Path.Combine(output, isLight ? "light-mech-elite.png" : "dark-mech-elite.png"));
                            editorVm.ValidationError.Should().BeEmpty();
                        }
                        finally { editor.Close(); }

                        foreach (var strategySelected in new[] { false, true })
                        {
                            var operatorPicker = new OperatorPickerWindow(operators, "",
                                strategySelected ? new HashSet<string> { "band_mech" } : new HashSet<string>());
                            try
                            {
                                ((OperatorBrowseViewModel)operatorPicker.DataContext).SearchText = "暴躁铁皮";
                                var pickerContent = (Border)operatorPicker.Content;
                                pickerContent.SetResourceReference(Border.BackgroundProperty, "PanelBackgroundBrush");
                                pickerContent.Measure(new Size(760, 620));
                                pickerContent.Arrange(new Rect(0, 0, 760, 620));
                                pickerContent.UpdateLayout();
                                var list = (ListBox)operatorPicker.FindName("OperatorsList");
                                list.Items.Count.Should().Be(1);
                                var item = (ListBoxItem)list.ItemContainerGenerator.ContainerFromIndex(0);
                                item.IsEnabled.Should().Be(strategySelected);
                                if (!strategySelected)
                                {
                                    item.ToolTip.Should().Be("必须在策略中添加森蚺策略");
                                    ToolTipService.GetShowOnDisabled(item).Should().BeTrue();
                                }
                                Save(pickerContent, Path.Combine(output, $"{(isLight ? "light" : "dark")}-mech-picker-{strategySelected}.png"));
                            }
                            finally { operatorPicker.Close(); }
                        }

                        var strategyPicker = new SimplePickerDialog("选择策略", strategies.Select(s => new SimplePickerDialog.PickerItem(
                            s.Id, $"{s.Name} · {s.Initiator}", s.IconPath, null, s.Description)), null);
                        try
                        {
                            ((CheckBox)strategyPicker.FindName("CoreEquipmentToggle")).Visibility.Should().Be(Visibility.Collapsed);
                            var search = (TextBox)strategyPicker.FindName("SearchBox");
                            var options = (ListBox)strategyPicker.FindName("ItemsList");
                            search.Text = "阿米娅";
                            options.Items.Cast<SimplePickerDialog.PickerItem>().Single().Key.Should().Be("band_amiya");
                            search.Text = "至纯凝结";
                            options.Items.Cast<SimplePickerDialog.PickerItem>().Single().Key.Should().Be("band_upgrade");
                        }
                        finally { strategyPicker.Close(); }
                    }
                    var closeAttempts = 0;
                    var closingShareWindow = new ShareLinkWindow("测试阵容", "https://dpaste.com/TEST12345", _ =>
                    {
                        closeAttempts++;
                        throw new COMException("Clipboard busy", unchecked((int)0x800401D0));
                    });
                    var closingCopyTask = closingShareWindow.CopyLinkAsync();
                    closingShareWindow.Close();
                    WaitForUi(() => closingCopyTask.IsCompleted, "closing the result window must stop pending clipboard retries");
                    closingCopyTask.GetAwaiter().GetResult();
                    closeAttempts.Should().Be(1);

                    lineupVm.LoadLineups([new Lineup { Name = "分享失败回归阵容" }]);
                    tabs.SelectedIndex = 3;
                    content.Measure(new Size(310, 900));
                    content.Arrange(new Rect(0, 0, 310, 900));
                    content.UpdateLayout();
                    var lineupView = (LineupListView)((TabItem)tabs.Items[3]).Content;
                    Descendants<Button>(lineupView).Single(b => b.Content as string == "分享")
                        .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                    WaitForUi(() => ((TextBlock)sidePanel.FindName("ToastText")).Text.StartsWith("生成分享链接失败"),
                        "network failures must be distinguished from clipboard failures");

                    var checkUpdate = (Button)sidePanel.FindName("CheckUpdateButton");
                    var updateStatus = (TextBlock)sidePanel.FindName("UpdateStatusText");
                    var updateBanner = (Border)sidePanel.FindName("UpdateBanner");
                    var updateAction = (Button)sidePanel.FindName("UpdateActionButton");
                    ((TextBlock)sidePanel.FindName("CurrentVersionText")).Text.Should().Be(
                        $"当前版本：v{typeof(SidePanelWindow).Assembly.GetName().Version!.ToString(3)}");
                    var previousFeed = Environment.GetEnvironmentVariable(UpdateService.FeedOverrideVariable);
                    using var updateServer = new TcpListener(IPAddress.Loopback, 0);
                    updateServer.Start();
                    var feed = $"http://127.0.0.1:{((IPEndPoint)updateServer.LocalEndpoint).Port}/";
                    Environment.SetEnvironmentVariable(UpdateService.FeedOverrideVariable, feed);
                    try
                    {
                        const string futureTag = "v999.0.0";
                        var release = System.Text.Json.JsonSerializer.Serialize(new
                        {
                            tag_name = futureTag,
                            body = "Offline UI regression",
                            assets = new[] { new { name = "ASPAssistant-v999.0.0-win-x64.zip", browser_download_url = feed + "package.zip" } }
                        });
                        var saveSkipped = new SettingsManager(output).SaveSkippedVersionAsync(futureTag);
                        WaitForUi(() => saveSkipped.IsCompleted, "the skipped version should be saved");
                        saveSkipped.GetAwaiter().GetResult();
                        var automaticCheck = sidePanel.StartUpdateCheckAsync();
                        RespondToUpdateRequest(updateServer, 200, release);
                        WaitForUi(() => automaticCheck.IsCompleted, "the automatic update check should finish");
                        automaticCheck.GetAwaiter().GetResult();
                        updateStatus.Text.Should().Contain("已跳过").And.Contain(futureTag);
                        updateBanner.Visibility.Should().Be(Visibility.Collapsed);

                        checkUpdate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        checkUpdate.IsEnabled.Should().BeFalse();
                        updateStatus.Text.Should().Be("正在检查更新…");
                        checkUpdate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        RespondToUpdateRequest(updateServer, 200,
                            $"{{\"tag_name\":\"v{new UpdateService().CurrentVersion}\",\"assets\":[]}}");
                        WaitForUi(() => checkUpdate.IsEnabled, "manual update checks must restore the button");
                        updateStatus.Text.Should().Be("当前已是最新版本");
                        updateServer.Pending().Should().BeFalse("repeated clicks must not start another request");

                        checkUpdate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        RespondToUpdateRequest(updateServer, 403, "{}");
                        WaitForUi(() => checkUpdate.IsEnabled, "failed requests must allow another check");
                        updateStatus.Text.Should().Contain("检查失败").And.Contain("限流");
                        checkUpdate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        RespondToUpdateRequest(updateServer, 200, "invalid JSON");
                        WaitForUi(() => checkUpdate.IsEnabled, "malformed responses must allow another check");
                        updateStatus.Text.Should().Contain("检查失败").And.Contain("有效 JSON");

                        checkUpdate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        RespondToUpdateRequest(updateServer, 200, release);
                        WaitForUi(() => checkUpdate.IsEnabled, "manual checks must bypass a previously skipped version");
                        updateBanner.Visibility.Should().Be(Visibility.Visible);
                        updateStatus.Text.Should().Contain("正在下载").And.Contain(futureTag);
                        ((Button)sidePanel.FindName("SkipVersionButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        WaitForUi(() => updateBanner.Visibility == Visibility.Collapsed, "skipping should hide the banner");
                        checkUpdate.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        updateBanner.Visibility.Should().Be(Visibility.Visible);
                        RespondToUpdateRequest(updateServer, 503, "{}");
                        WaitForUi(() => updateStatus.Text.StartsWith("下载失败"), "download errors must be visible");
                        updateStatus.Text.Should().Contain("503");
                        updateServer.Pending().Should().BeFalse("reopening an active update must reuse the existing download");
                        ((TextBlock)sidePanel.FindName("UpdateActionText")).Text.Should().Be("重试");
                        var progressDialog = new UpdateDialog(new UpdateInfo(futureTag, "99.0.0", "", feed, "test.zip", null));
                        var dialogField = typeof(SidePanelWindow).GetField("_openUpdateDialog", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                        var lastProgress = typeof(SidePanelWindow).GetField("_lastUpdateProgress", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                        dialogField.SetValue(sidePanel, progressDialog);
                        lastProgress.SetValue(sidePanel, 0.83);
                        progressDialog.ReportProgress(0.83);
                        try
                        {
                            updateAction.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            updateStatus.Text.Should().Contain("正在下载");
                            lastProgress.GetValue(sidePanel).Should().Be(0.0);
                            ((ProgressBar)progressDialog.FindName("DialogProgress")).Value.Should().Be(0);
                            ((TextBlock)progressDialog.FindName("ProgressPercentText")).Text.Should().Be("0%");
                            RespondToUpdateRequest(updateServer, 503, "{}");
                            WaitForUi(() => updateStatus.Text.StartsWith("下载失败"), "retry must issue a fresh download request");
                            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                            ((TextBlock)progressDialog.FindName("DownloadHintText")).Text.Should().Contain("下载失败").And.Contain("503");
                            ((ProgressBar)progressDialog.FindName("DialogProgress")).Visibility.Should().Be(Visibility.Collapsed);
                        }
                        finally { dialogField.SetValue(sidePanel, null); progressDialog.Close(); }

                        var launchRoot = Directory.CreateTempSubdirectory("ASPAssistant-UpdateUI-").FullName;
                        var previousPath = Environment.GetEnvironmentVariable("PATH");
                        var updater = (UpdateService)typeof(SidePanelWindow).GetField("_updateService", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(sidePanel)!;
                        var readyField = typeof(SidePanelWindow).GetField("_isDownloadReady", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
                        try
                        {
                            foreach (var folder in new[] { AppContext.BaseDirectory, Environment.CurrentDirectory,
                                Environment.SystemDirectory, Environment.GetFolderPath(Environment.SpecialFolder.Windows) })
                                File.Exists(Path.Combine(folder, "powershell.exe")).Should().BeFalse("this isolated test must make the updater executable unresolvable");
                            var script = Path.Combine(launchRoot, "pending.ps1");
                            File.WriteAllText(script, "# Not executed by this test");
                            typeof(UpdateService).GetProperty(nameof(UpdateService.PendingScriptPath))!.SetValue(updater, script);
                            readyField.SetValue(sidePanel, true);
                            Environment.SetEnvironmentVariable("PATH", "");
                            updateAction.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                            updateStatus.Text.Should().Contain("无法启动更新程序").And.Contain("辅助未关闭");
                            readyField.GetValue(sidePanel).Should().Be(true);
                            updater.PendingScriptPath.Should().Be(script);
                        }
                        finally
                        {
                            Environment.SetEnvironmentVariable("PATH", previousPath);
                            readyField.SetValue(sidePanel, false);
                            typeof(UpdateService).GetProperty(nameof(UpdateService.PendingScriptPath))!.SetValue(updater, null);
                            Path.GetFullPath(launchRoot).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase).Should().BeTrue();
                            Directory.Delete(launchRoot, true);
                        }
                    }
                    finally
                    {
                        Environment.SetEnvironmentVariable(UpdateService.FeedOverrideVariable, previousFeed);
                    }
                }
                finally
                {
                    sidePanel.Close();
                }
                MinimizableDialogTests.VerifyNestedDialogs();
                AcquiredTrackingViewTests.Verify();
            }
            catch (Exception ex)
            {
                // Transfer assertion and WPF failures from the required STA thread to xUnit.
                failure = ex;
            }
            finally
            {
                host.Close();
                if (ownsApplication) application.Shutdown();
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        // 30 秒的旧上限在较慢的 CI runner（Windows Server 2022）上会被触碰：
        // 本地约 16～27 秒完成，云端同一步超过 30 秒而误判为阻塞。放宽到 180 秒，
        // 仍能在真正死锁时失败，只是判定更宽松。
        thread.Join(TimeSpan.FromSeconds(180)).Should().BeTrue("WPF layout should finish without blocking");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void AssertTierClicks(ListBox tiers, Action<int[]> assertFiltered)
    {
        string[] roman = ["", "Ⅰ", "Ⅱ", "Ⅲ", "Ⅳ", "Ⅴ", "Ⅵ"];
        for (var i = 0; i < tiers.Items.Count; i++)
        {
            var container = (ListBoxItem)tiers.ItemContainerGenerator.ContainerFromIndex(i);
            Descendants<TextBlock>(container).Single().Text.Should().Be(i == 0 ? "全部" : roman[(int)tiers.Items[i]]);
        }
        void Click(int index)
        {
            var item = (ListBoxItem)tiers.ItemContainerGenerator.ContainerFromIndex(index);
            var click = new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
            {
                RoutedEvent = UIElement.PreviewMouseLeftButtonDownEvent,
                Source = Descendants<TextBlock>(item).Single()
            };
            tiers.RaiseEvent(click);
            tiers.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            click.Handled.Should().BeTrue();
        }

        void AssertSelected(params int[] expected)
        {
            tiers.SelectedItems.OfType<int>().Should().BeEquivalentTo(expected);
            assertFiltered(expected);
            if (expected.Length == 0)
                tiers.SelectedIndex.Should().Be(0);
            else
                ((ListBoxItem)tiers.ItemContainerGenerator.ContainerFromIndex(0)).IsSelected.Should().BeFalse();
        }

        var first = (int)tiers.Items[1];
        var second = (int)tiers.Items[2];
        AssertSelected();
        Click(1);
        AssertSelected(first);
        Click(2);
        AssertSelected(first, second);
        Click(1);
        AssertSelected(second);
        Click(2);
        AssertSelected();
        Click(2);
        AssertSelected(second);
        Click(0);
        AssertSelected();
        Click(0);
        AssertSelected();
        Click(1);
        AssertSelected(first);
        Click(0);
        AssertSelected();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            if (child is T result) yield return result;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static void WaitForPreferenceSave(UIElement control)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!control.IsEnabled && timer.Elapsed < TimeSpan.FromSeconds(5))
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Thread.Sleep(1);
        }
        control.IsEnabled.Should().BeTrue("the preference write should complete");
    }

    private static void WaitForUi(Func<bool> completed, string reason)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        while (!completed() && timer.Elapsed < TimeSpan.FromSeconds(5))
        {
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            Thread.Sleep(1);
        }
        completed().Should().BeTrue(reason);
    }

    private static void RespondToUpdateRequest(TcpListener server, int statusCode, string body)
    {
        WaitForUi(server.Pending, "the updater should contact the local test server");
        using var client = server.AcceptTcpClient();
        client.ReceiveTimeout = 5000;
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);
        while (!string.IsNullOrEmpty(reader.ReadLine())) { }
        var content = Encoding.UTF8.GetBytes(body);
        stream.Write(Encoding.ASCII.GetBytes(
            $"HTTP/1.1 {statusCode} Test\r\nContent-Type: application/json\r\nContent-Length: {content.Length}\r\nConnection: close\r\n\r\n"));
        stream.Write(content);
    }

    private sealed class FailedShareHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }

    private static void Save(FrameworkElement element, string path)
    {
        var bitmap = new RenderTargetBitmap((int)Math.Ceiling(element.ActualWidth),
            (int)Math.Ceiling(element.ActualHeight), 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(element);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }
}
