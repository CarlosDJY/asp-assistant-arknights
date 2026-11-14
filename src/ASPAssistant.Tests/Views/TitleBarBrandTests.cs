// Added for ASP Assistant Regulation on 2026-11-14; see NOTICE.
using System.IO;
using System.Net;
using System.Net.Http;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using ASPAssistant.App.Windows;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.Views;

public class TitleBarBrandTests
{
    [Fact]
    public void BrandNameGivesWayOnlyWhenTheTitleBarRunsOutOfRoom()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var ownsApplication = Application.Current is null;
            var application = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
            var output = Path.Combine(Path.GetTempPath(), $"title_bar_{Guid.NewGuid():N}");
            try
            {
                foreach (var resource in new[] { "Themes/Terminal.Dark.xaml", "Styles.xaml", "Logo.xaml" })
                    application.Resources.MergedDictionaries.Add(new ResourceDictionary
                    {
                        Source = new Uri($"/ASPAssistant.App;component/Resources/{resource}", UriKind.Relative)
                    });

                var store = new JsonDataStore(Path.Combine(AppContext.BaseDirectory, "TestData", "Regulation"));
                var operators = store.LoadOperatorsAsync().GetAwaiter().GetResult();
                var (equipment, _) = store.LoadEquipmentAsync().GetAwaiter().GetResult();
                var (covenants, mappings) = store.LoadCovenantsAsync().GetAwaiter().GetResult();
                var strategies = store.LoadStrategiesAsync().GetAwaiter().GetResult();
                var calculator = new CovenantCalculator(operators, covenants, mappings);
                var lineupVm = new LineupViewModel(new LineupStore(Path.Combine(output, "lineups"), calculator.EquipmentRules), calculator);
                using var http = new HttpClient(new UnavailableHandler());
                var sidePanel = new SidePanelWindow(new OperatorBrowseViewModel(), new EquipmentBrowseViewModel(), new TrackingViewModel(),
                    new GameStateViewModel(), new BanViewModel(), new EnemyViewModel(), lineupVm,
                    new LineupShareService(calculator.EquipmentRules, Path.Combine(output, "share-links"), http),
                    operators, equipment, strategies, new SettingsManager(output), GameShopTheme.Terminal,
                    new SidePanelPositionSettings(), new LineupPlanner(operators, covenants), covenants);
                // Not shown: laying out the content is enough and keeps the test host in the background.
                var root = (FrameworkElement)sidePanel.Content;
                var brand = (TextBlock)sidePanel.FindName("BrandTitleText");
                var updateChip = (Button)sidePanel.FindName("UpdateChip");
                ((TextBlock)sidePanel.FindName("UpdateChipText")).Text = "重启更新";

                bool BrandShownAt(double width, bool updateShowing)
                {
                    updateChip.Visibility = updateShowing ? Visibility.Visible : Visibility.Collapsed;
                    for (var pass = 0; pass < 2; pass++)
                    {
                        root.Measure(new Size(width, 700));
                        root.Arrange(new Rect(0, 0, width, 700));
                        root.UpdateLayout();
                    }
                    return brand.Visibility == Visibility.Visible;
                }

                BrandShownAt(310, updateShowing: false).Should().BeTrue("the default width has room for the name");
                BrandShownAt(280, updateShowing: false).Should().BeTrue("the minimum width still fits the name without the update chip");
                BrandShownAt(280, updateShowing: true).Should().BeFalse("the update chip needs the room at the minimum width");
                BrandShownAt(400, updateShowing: true).Should().BeTrue("the widest panel fits both");
                BrandShownAt(280, updateShowing: false).Should().BeTrue("the name comes back once the chip is gone");
                // Edge auto-hide turns the resize border off while tucked; that needs a live (unfrozen) WindowChrome.
                System.Windows.Shell.WindowChrome.GetWindowChrome(sidePanel).Should().NotBeNull()
                    .And.Match<System.Windows.Shell.WindowChrome>(c => !c.IsFrozen);
                ((CheckBox)sidePanel.FindName("EdgeAutoHideToggle")).IsChecked.Should().BeTrue("edge auto-hide is on by default");
                sidePanel.Close();
            }
            catch (Exception ex) { failure = ex; }
            finally
            {
                if (ownsApplication) application.Shutdown();
                try { Directory.Delete(output, true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }) { IsBackground = true };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join(TimeSpan.FromSeconds(60)).Should().BeTrue("WPF title bar test should finish");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private sealed class UnavailableHandler : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));
    }
}
