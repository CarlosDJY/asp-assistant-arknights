using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ASPAssistant.App;
using ASPAssistant.App.Windows;
using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;
using FluentAssertions;

namespace ASPAssistant.Tests.Views;

internal static class MinimizableDialogTests
{
    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowEnabled(IntPtr window);

    public static void VerifyNestedDialogs()
    {
        var previousContext = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        try
        {
            Operator[] operators = [new() { Name = "角峰", Tier = 4 }, new() { Name = "阿米娅", Tier = 5 }];
            var calculator = new CovenantCalculator(operators, [], new Dictionary<string, string>());
            foreach (var light in new[] { false, true })
            {
                ThemeManager.Apply(ASPAssistant.Core.Models.GameShopTheme.Terminal, !light);
                var parentBody = new TextBox { Text = "parent content" };
                var parent = new Window
                {
                    Content = parentBody, Left = -20000, Top = -20000, Width = 600, Height = 600,
                    ShowActivated = false, ShowInTaskbar = false, Opacity = 0
                };
                try
                {
                    parent.Show();
                    var source = new Lineup { Name = "原始阵容" };
                    var vm = new LineupEditorViewModel(source, operators, [], calculator);
                    var editor = new LineupEditorWindow(vm, [])
                    {
                        Owner = parent, ShowActivated = false, ShowInTaskbar = false, Opacity = 0,
                        WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000
                    };
                    var session = MinimizableDialog.ShowAsync(editor, parentBody);
                    Pump();
                    parent.IsEnabled.Should().BeFalse();
                    IsWindowEnabled(new WindowInteropHelper(parent).Handle).Should().BeFalse();
                    ((TextBox)editor.FindName("LineupNameBox")).Text = "未保存草稿";
                    SaveTitle(editor, light ? "light-editor" : "dark-editor");

                    for (var cycle = 0; cycle < 2; cycle++)
                    {
                        Click(editor, "─");
                        Pump();
                        editor.WindowState.Should().Be(WindowState.Minimized);
                        parent.IsEnabled.Should().BeTrue();
                        IsWindowEnabled(new WindowInteropHelper(parent).Handle).Should().BeTrue();
                        parentBody.IsEnabled.Should().BeFalse("only the parent's chrome is unlocked");
                        var bounds = User32.ReadWindowRect(new WindowInteropHelper(parent).Handle);
                        bounds.Left += 20;
                        bounds.Right += 20;
                        User32.SetWindowBounds(new WindowInteropHelper(parent).Handle, bounds);
                        User32.ReadWindowRect(new WindowInteropHelper(parent).Handle).Should().BeEquivalentTo(bounds);
                        editor.WindowState = WindowState.Normal;
                        Pump();
                        parent.IsEnabled.Should().BeFalse();
                        vm.Name.Should().Be("未保存草稿");
                    }

                    Click(editor, "+ 选择干员");
                    var picker = editor.OwnedWindows.OfType<OperatorPickerWindow>().Single();
                    picker.ShowInTaskbar = false;
                    picker.Opacity = 0;
                    var filters = (OperatorBrowseViewModel)picker.DataContext;
                    filters.SearchText = "阿米娅";
                    ((ListBox)picker.FindName("TierFilters")).SelectedItems.Add(5);
                    Pump();
                    SaveTitle(picker, light ? "light-picker" : "dark-picker");
                    Click(picker, "─");
                    Pump();
                    editor.IsEnabled.Should().BeTrue();
                    IsWindowEnabled(new WindowInteropHelper(editor).Handle).Should().BeTrue();
                    ((UIElement)editor.FindName("EditorContent")).IsEnabled.Should().BeFalse();
                    Descendants<Button>(editor).Single(b => Equals(b.Content, "─")).IsEnabled.Should().BeTrue();
                    Descendants<Button>(editor).Single(b => Equals(b.Content, "保存")).IsEnabled.Should().BeFalse();
                    parent.IsEnabled.Should().BeFalse();

                    Click(editor, "─");
                    Pump();
                    parent.IsEnabled.Should().BeTrue();
                    editor.WindowState = WindowState.Normal;
                    Pump();
                    parent.IsEnabled.Should().BeFalse();
                    picker.WindowState = WindowState.Normal;
                    Pump();
                    editor.IsEnabled.Should().BeFalse();
                    filters.SearchText.Should().Be("阿米娅");
                    filters.SelectedTierFilters.Should().Equal(5);
                    var list = (ListBox)picker.FindName("OperatorsList");
                    list.SelectedItem = list.Items.Cast<Operator>().Single();
                    list.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(picker),
                        Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                    Pump();
                    picker.Result.Should().Be("阿米娅", "the routed Enter event must choose the operator");
                    picker.IsVisible.Should().BeFalse();
                    editor.IsEnabled.Should().BeTrue();
                    vm.Slots.Select(s => s.OperatorName).Should().Contain("阿米娅");
                    vm.Slots[0].OperatorName.Should().Be("阿米娅");
                    source.Slots.Should().BeEmpty("the draft must not update the source before Save");
                    editor.IsEnabled.Should().BeTrue();
                    ((UIElement)editor.FindName("EditorContent")).IsEnabled.Should().BeTrue();

                    var firstCard = (Border)((ItemsControl)editor.FindName("SlotsHost")).Items[0];
                    var portrait = Descendants<Border>(firstCard).Single(b => b.Child is Image && b.Width == 44);
                    portrait.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice, Environment.TickCount, MouseButton.Left)
                        { RoutedEvent = UIElement.MouseLeftButtonUpEvent });
                    var clearPicker = editor.OwnedWindows.OfType<OperatorPickerWindow>().Single();
                    Click(clearPicker, "清除选择");
                    Pump();
                    clearPicker.Result.Should().BeEmpty();
                    vm.Slots[0].OperatorName.Should().BeEmpty();
                    Click(editor, "+ 选择干员");
                    var chosenAgain = editor.OwnedWindows.OfType<OperatorPickerWindow>().Single();
                    var chooseList = (ListBox)chosenAgain.FindName("OperatorsList");
                    chooseList.SelectedItem = chooseList.Items.Cast<Operator>().Single(o => o.Name == "阿米娅");
                    chooseList.RaiseEvent(new KeyEventArgs(Keyboard.PrimaryDevice, PresentationSource.FromVisual(chosenAgain),
                        Environment.TickCount, Key.Enter) { RoutedEvent = Keyboard.PreviewKeyDownEvent });
                    Pump();

                    Click(editor, "保存");
                    Pump();
                    session.IsCompletedSuccessfully.Should().BeTrue();
                    editor.Result!.Name.Should().Be("未保存草稿");
                    editor.Result.Slots.Should().Contain(s => s.OperatorName == "阿米娅");
                    parent.IsEnabled.Should().BeTrue();
                    parentBody.IsEnabled.Should().BeTrue();

                    var cancelVm = new LineupEditorViewModel(source, operators, [], calculator);
                    var cancelled = new LineupEditorWindow(cancelVm, [])
                    {
                        Owner = parent, ShowActivated = false, ShowInTaskbar = false, Opacity = 0,
                        WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000
                    };
                    var cancelSession = MinimizableDialog.ShowAsync(cancelled, parentBody);
                    Click(cancelled, "+ 选择干员");
                    var cancelledPicker = cancelled.OwnedWindows.OfType<OperatorPickerWindow>().Single();
                    Click(cancelledPicker, "─");
                    Pump();
                    Click(cancelled, "✕");
                    Pump();
                    cancelSession.IsCompletedSuccessfully.Should().BeTrue();
                    cancelledPicker.IsVisible.Should().BeFalse();
                    cancelled.Result.Should().BeNull();
                    parent.IsEnabled.Should().BeTrue();
                    parentBody.IsEnabled.Should().BeTrue();
                    var abandoned = new LineupEditorWindow(new LineupEditorViewModel(source, operators, [], calculator), [])
                    {
                        Owner = parent, ShowActivated = false, ShowInTaskbar = false, Opacity = 0,
                        WindowStartupLocation = WindowStartupLocation.Manual, Left = -20000, Top = -20000
                    };
                    var abandonedSession = MinimizableDialog.ShowAsync(abandoned, parentBody);
                    Click(abandoned, "+ 选择干员");
                    var abandonedPicker = abandoned.OwnedWindows.OfType<OperatorPickerWindow>().Single();
                    Click(abandonedPicker, "─");
                    Click(abandoned, "─");
                    Pump();
                    parent.IsEnabled.Should().BeTrue();
                    parent.Close();
                    Pump();
                    abandonedSession.IsCompletedSuccessfully.Should().BeTrue();
                    abandoned.IsVisible.Should().BeFalse();
                    abandonedPicker.IsVisible.Should().BeFalse();
                    abandoned.Result.Should().BeNull();
                    source.Name.Should().Be("原始阵容");
                    source.Slots.Should().BeEmpty();
                }
                finally { parent.Close(); Pump(); }
            }
        }
        finally
        {
            ThemeManager.Apply(ASPAssistant.Core.Models.GameShopTheme.Terminal, true);
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static void Click(Window window, string content)
        => Descendants<Button>(window).First(b => Equals(b.Content, content)).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T item) yield return item;
            foreach (var nested in Descendants<T>(child)) yield return nested;
        }
    }

    private static void SaveTitle(Window window, string name)
    {
        var content = (FrameworkElement)window.Content;
        var image = new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth), 50, 96, 96, PixelFormats.Pbgra32);
        image.Render(content);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(image));
        var directory = Path.Combine(AppContext.BaseDirectory, "TestResults", "minimize-previews");
        Directory.CreateDirectory(directory);
        using var file = File.Create(Path.Combine(directory, name + ".png"));
        encoder.Save(file);
    }
}
