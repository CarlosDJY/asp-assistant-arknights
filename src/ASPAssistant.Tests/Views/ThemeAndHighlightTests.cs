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
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.Views;

public class ThemeAndHighlightTests
{
    [Fact]
    public void ThemeSwitch_UpdatesControlsAndPreservesHighlightAndSelection()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var application = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var host = new Window { ShowActivated = false, ShowInTaskbar = false };
            try
            {
                foreach (var resource in new[] { "Themes/Dark.xaml", "Styles.xaml", "Logo.xaml" })
                    application.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri($"/ASPAssistant.App;component/Resources/{resource}", UriKind.Relative)
                    });

                var store = new JsonDataStore(Path.Combine(AppContext.BaseDirectory, "TestData", "Regulation"));
                var vm = new OperatorBrowseViewModel();
                var operators = store.LoadOperatorsAsync().GetAwaiter().GetResult();
                vm.LoadOperators(operators);
                var view = new OperatorBrowseView { DataContext = vm };
                host.Content = view;
                var toggle = (System.Windows.Controls.Primitives.ToggleButton)view.FindName("FilterToggle");
                toggle.IsChecked = true;
                var coreList = (ListBox)view.FindName("CoreCovenantFilterList");
                var output = Path.Combine(AppContext.BaseDirectory, "TestResults", "theme-previews");
                Directory.CreateDirectory(output);

                foreach (var isLight in new[] { false, true })
                {
                    ThemeManager.Apply(isLight);
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
                ThemeManager.Apply(false);
                ((SolidColorBrush)view.Background).Color.Should().Be(Color.FromRgb(26, 26, 46));

                var (equipment, manualJobChanges) = store.LoadEquipmentAsync().GetAwaiter().GetResult();
                var (covenants, mappings) = store.LoadCovenantsAsync().GetAwaiter().GetResult();
                var equipmentVm = new EquipmentBrowseViewModel();
                equipmentVm.LoadEquipment(equipment,
                    operators.SelectMany(o => o.CoreCovenants.Concat(o.AdditionalCovenants)), manualJobChanges, mappings);
                var lineupVm = new LineupViewModel(new LineupStore(Path.Combine(output, "lineups")),
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
                    lineupVm, new LineupShareService(failedShareHttp), operators, equipment, new SettingsManager(output), GameShopTheme.Terminal);
                try
                {
                    SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
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
                    settingsButton.Content.Should().Be("⚙");
                    sidePanel.FindName("ThemeToggleButton").Should().BeNull();
                    var settingsPanel = (Border)sidePanel.FindName("SettingsPanel");
                    var uiThemes = (ListBox)sidePanel.FindName("UiThemeSelector");
                    var gameThemes = (ListBox)sidePanel.FindName("GameThemeSelector");
                    var multiToggle = (CheckBox)sidePanel.FindName("MultiLineupTrackingToggle");
                    gameThemes.Items.Cast<ListBoxItem>().Select(i => i.Content).Should()
                        .Equal("卫戍协议终端", "乌萨斯齐心团", "“全能的”可露希尔");
                    gameThemes.SelectedIndex.Should().Be(0);
                    GameShopTheme changedTheme = GameShopTheme.Terminal;
                    sidePanel.GameThemeChanged += theme => changedTheme = theme;
                    foreach (var isLight in new[] { false, true })
                    {
                        ThemeManager.Apply(isLight);
                        settingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        settingsPanel.Visibility.Should().Be(Visibility.Visible);
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
                        settingsButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        settingsPanel.Visibility.Should().Be(Visibility.Collapsed);
                        for (var i = 0; i < tabs.Items.Count; i++)
                        {
                            tabs.SelectedIndex = i;
                            content.Measure(new Size(310, 900));
                            content.Arrange(new Rect(0, 0, 310, 900));
                            content.UpdateLayout();
                            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                            var page = (UserControl)((TabItem)tabs.Items[i]).Content;
                            page.ActualWidth.Should().BeGreaterThan(290);
                            ((SolidColorBrush)page.Background).Color.Should().Be(isLight
                                ? Color.FromRgb(247, 249, 252) : Color.FromRgb(26, 26, 46));
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

                                var cards = Descendants<EquipmentCard>(equipmentView).ToList();
                                cards.Should().HaveCount(70);
                                var hammer = cards.Single(c => ((Equipment)c.DataContext).Name == "维式重锤");
                                var effect = (TextBlock)hammer.FindName("EffectText");
                                new TextRange(effect.ContentStart, effect.ContentEnd).Text.Should().Be("攻击力+15(25%)%");
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
                                    ("阿戈尔重刃", "攻击力+40(60%)%，攻击速度-10"),
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
                                var trackedCards = Descendants<OperatorCard>(trackingView).ToList();
                                trackedCards.Should().HaveCount(2);
                                trackedCards.Should().OnlyContain(c => c.IsTrackingList && c.IsTracked);
                                var trackedCard = trackedCards.Single(c => ((Operator)c.DataContext).Name == "烈夏");
                                var portrait = (System.Windows.Shapes.Ellipse)trackedCard.FindName("TrackingPortrait");
                                portrait.Visibility.Should().Be(Visibility.Visible);
                                ((ImageBrush)portrait.Fill).ImageSource.Should().NotBeNull();
                                ((IEnumerable<CovenantChipItem>)((ItemsControl)trackedCard.FindName("CovenantChips")).ItemsSource)
                                    .Should().Contain(c => c.Name == "乌萨斯" && c.IsCore);
                                ((Button)trackedCard.FindName("BanButton")).Visibility.Should().Be(Visibility.Collapsed);
                                var detail = (ToolTip)trackedCard.FindName("DetailsToolTip");
                                detail.PlacementTarget = (UIElement)trackedCard.FindName("CardBorder");
                                detail.Measure(new Size(500, 700));
                                detail.Arrange(new Rect(detail.DesiredSize));
                                detail.UpdateLayout();
                                Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                                Descendants<TextBlock>(detail).Select(t => t.Text)
                                    .Should().Contain("普通").And.Contain("精锐");
                                var hoverCard = new OperatorCard
                                {
                                    DataContext = trackedCard.DataContext,
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
                                row.ActualHeight.Should().Be(40);
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
                                ((Button)trackedCard.FindName("TrackButton")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                                trackingVm.IsTracked("烈夏").Should().BeFalse();
                                trackingVm.IsTracked("塔露拉").Should().BeTrue();
                                trackingVm.AddTracking("烈夏", TrackingType.Operator);
                                trackingVm.AddTracking("旧版已移除干员", TrackingType.Operator);
                                content.UpdateLayout();
                                Descendants<TextBlock>(trackingView).Should().Contain(t => t.Text == "旧版已移除干员");
                                var removeUnknown = Descendants<Button>(trackingView)
                                    .Single(b => b.Tag as string == "旧版已移除干员");
                                removeUnknown.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                                trackingVm.IsTracked("旧版已移除干员").Should().BeFalse();
                                content.UpdateLayout();
                                var equipmentCards = Descendants<TrackedEquipmentCard>(trackingView).ToList();
                                equipmentCards.Should().HaveCount(trackingVm.TrackedEquipment.Count);
                                equipmentCards.SelectMany(c => ((Equipment)c.DataContext).Categories).Distinct().Should()
                                    .BeEquivalentTo(EquipmentBrowseViewModel.CategoryOrder);
                                foreach (var equipmentCard in equipmentCards)
                                {
                                    var item = (Equipment)equipmentCard.DataContext;
                                    var chips = (ItemsControl)equipmentCard.FindName("CategoryChips");
                                    chips.Items.Cast<string>().Should().Equal(item.Categories);
                                    var type = ((TextBlock)equipmentCard.FindName("TypeText")).Text;
                                    type.Should().StartWith(item.Type == EquipmentType.JobChange ? "转职" : "普通");
                                    if (!string.IsNullOrEmpty(item.JobChangeCovenant))
                                        type.Should().Contain(item.JobChangeCovenant);
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

                                var equipmentHover = new TrackedEquipmentCard
                                {
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
                                    .Single(b => b.Tag as string == "旧版已移除装备");
                                removeUnknownEquipment.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                                trackingVm.IsTracked("旧版已移除装备").Should().BeFalse();
                                content.UpdateLayout();
                                content.Measure(new Size(280, 900));
                                content.Arrange(new Rect(0, 0, 280, 900));
                                content.UpdateLayout();
                                foreach (var equipmentCard in Descendants<TrackedEquipmentCard>(trackingView))
                                {
                                    var equipmentBorder = (Border)equipmentCard.FindName("CardBorder");
                                    foreach (var name in new[] { "NameText", "CategoryChips", "TypeText", "TrackButton" })
                                    {
                                        var element = (FrameworkElement)equipmentCard.FindName(name);
                                        var left = element.TranslatePoint(new Point(), equipmentBorder).X;
                                        left.Should().BeGreaterThanOrEqualTo(0);
                                        (left + element.ActualWidth).Should().BeLessThanOrEqualTo(equipmentBorder.ActualWidth);
                                    }
                                }
                                Save(content, Path.Combine(output, isLight ? "light-tracking-narrow.png" : "dark-tracking-narrow.png"));
                                content.Measure(new Size(310, 900));
                                content.Arrange(new Rect(0, 0, 310, 900));
                                content.UpdateLayout();
                            }
                            Save(content, Path.Combine(output, $"{(isLight ? "light" : "dark")}-page-{i}.png"));
                        }
                    }

                    foreach (var isLight in new[] { false, true })
                    {
                        ThemeManager.Apply(isLight);
                        const string sharedUrl = "https://dpaste.com/TEST12345";
                        var attempts = 0;
                        var copiedText = "";
                        var shareWindow = new ShareLinkWindow(sharedUrl, text =>
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
                            copiedText.Should().Be(sharedUrl);
                            ((TextBlock)shareWindow.FindName("CopyStatusText")).Text.Should().Contain("已复制");
                            copyButton.IsEnabled.Should().BeTrue();
                        }
                        finally { shareWindow.Close(); }

                        var clipboardBusy = true;
                        attempts = 0;
                        var fallbackWindow = new ShareLinkWindow(sharedUrl, _ =>
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
                            linkBox.Text.Should().Be(sharedUrl);
                            linkBox.IsReadOnly.Should().BeTrue();
                            linkBox.SelectionLength.Should().Be(sharedUrl.Length);
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
                    }
                    var closeAttempts = 0;
                    var closingShareWindow = new ShareLinkWindow("https://dpaste.com/TEST12345", _ =>
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
                        updateAction.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
                        updateStatus.Text.Should().Contain("正在下载");
                        RespondToUpdateRequest(updateServer, 503, "{}");
                        WaitForUi(() => updateStatus.Text.StartsWith("下载失败"), "retry must issue a fresh download request");
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
            }
            catch (Exception ex)
            {
                // Transfer assertion and WPF failures from the required STA thread to xUnit.
                failure = ex;
            }
            finally
            {
                host.Close();
                application.Shutdown();
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
