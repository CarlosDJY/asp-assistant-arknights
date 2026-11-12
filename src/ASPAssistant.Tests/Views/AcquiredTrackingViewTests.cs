using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ASPAssistant.App;
using ASPAssistant.App.Controls;
using ASPAssistant.App.Views;
using ASPAssistant.Core.GameState;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.Views;

internal static class AcquiredTrackingViewTests
{
    public static void Verify()
    {
        const string operatorName = "名字很长的测试干员甲";
        const string equipmentName = "名字很长的测试防御装备";
        foreach (var light in new[] { false, true })
        {
            ThemeManager.Apply(ASPAssistant.Core.Models.GameShopTheme.Terminal, !light);
            Operator[] operators = [new() { Name = operatorName, Tier = 5 }];
            Equipment[] equipment = [new() { Name = equipmentName, Categories = ["防御", "生命"] }];
            var vm = new TrackingViewModel();
            vm.AddTracking(operatorName, TrackingType.Operator);
            vm.AddTracking(equipmentName, TrackingType.Equipment);
            vm.AddTracking("旧版已移除干员", TrackingType.Operator);
            vm.AddTracking("旧版已移除装备", TrackingType.Equipment);
            var view = new TrackingView();
            view.SetResourceReference(Control.ForegroundProperty, "TextPrimaryBrush");
            view.SetOperators(operators);
            view.SetEquipment(equipment);
            view.SetCovenantCalculator(new CovenantCalculator(operators, [], new Dictionary<string, string>()));
            view.ToggleAcquiredRequested += vm.ToggleAcquired;
            view.RemoveTrackingRequested += vm.RemoveTracking;
            view.DataContext = vm;
            Layout(view);

            // Operators: the cell's right-click menu toggles 已获取 and back.
            ClickMenu(view, operatorName);
            Layout(view);
            vm.TrackedOperators.Should().HaveCount(2);
            vm.IsTracked(operatorName).Should().BeFalse();
            CellOf(view, operatorName).IsAcquired.Should().BeTrue();
            CellOf(view, operatorName).Sub.Should().StartWith("已获取");
            Save(view, light ? "light-acquired" : "dark-acquired");
            ClickMenu(view, operatorName);
            Layout(view);
            vm.IsTracked(operatorName).Should().BeTrue();
            CellOf(view, operatorName).IsAcquired.Should().BeFalse();

            // Equipment and names missing from the data use the same menu on their row.
            foreach (var name in new[] { equipmentName, "旧版已移除干员", "旧版已移除装备" })
            {
                ClickMenu(view, name);
                Layout(view);
                vm.IsTracked(name).Should().BeFalse();
                ClickMenu(view, name);
                Layout(view);
                vm.IsTracked(name).Should().BeTrue();
            }
            vm.AllEntries.Should().HaveCount(4).And.OnlyContain(e => !e.IsAcquired);

            foreach (var name in new[] { operatorName, equipmentName, "旧版已移除干员", "旧版已移除装备" })
            {
                ClickMenu(view, name);
                Layout(view);
            }
            vm.AllEntries.Should().HaveCount(4).And.OnlyContain(e => e.IsAcquired);
            ((FrameworkElement)view.FindName("EmptyState")).Visibility.Should().Be(Visibility.Collapsed);
            var shop = new List<ShopItem> { new() { Name = operatorName, IsTracked = true }, new() { Name = equipmentName, IsTracked = true } };
            GameStateUpdater.UpdateShopTracking(shop, vm.AllEntries.ToList());
            shop.Should().OnlyContain(i => !i.IsTracked);

            // Tracking again resumes the row.
            vm.AddTracking(operatorName, TrackingType.Operator);
            vm.AddTracking(equipmentName, TrackingType.Equipment);
            Layout(view);
            vm.AllEntries.Should().HaveCount(4);
            vm.IsTracked(operatorName).Should().BeTrue();
            vm.IsTracked(equipmentName).Should().BeTrue();
        }
        ThemeManager.Apply(ASPAssistant.Core.Models.GameShopTheme.Terminal, true);
    }

    private static TrackedCell CellOf(FrameworkElement view, string name)
        => Descendants<FrameworkElement>(view).Select(e => e.DataContext).OfType<TrackedCell>().First(c => c.Name == name);

    /// <summary>Clicks the first item of the right-click menu on the row showing <paramref name="name"/>.</summary>
    private static void ClickMenu(FrameworkElement view, string name)
    {
        var owner = Descendants<FrameworkElement>(view).First(e => e.ContextMenu is not null && e.DataContext switch
        {
            TrackedCell c => c.Name == name,
            TrackingEntry t => t.Name == name,
            _ => false
        });
        var menu = owner.ContextMenu!;
        menu.PlacementTarget = owner;
        menu.DataContext = owner.DataContext;
        var item = menu.Items.OfType<MenuItem>().First();
        item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent, item));
    }

    private static void Layout(FrameworkElement view)
    {
        view.Measure(new Size(280, 850));
        view.Arrange(new Rect(0, 0, 280, 850));
        view.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T item) yield return item;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static void Save(FrameworkElement view, string name)
    {
        var image = new RenderTargetBitmap(280, 500, 96, 96, PixelFormats.Pbgra32);
        image.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        var directory = Path.Combine(AppContext.BaseDirectory, "TestResults", "acquired-previews");
        Directory.CreateDirectory(directory);
        using var file = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(file);
    }
}
