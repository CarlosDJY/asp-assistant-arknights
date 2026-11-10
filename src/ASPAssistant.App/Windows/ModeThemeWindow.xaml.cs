using System.Windows;
using System.Windows.Input;
using ASPAssistant.Core.Models;

namespace ASPAssistant.App.Windows;

public partial class ModeThemeWindow : Window
{
    public GameShopTheme SelectedTheme => (GameShopTheme)ThemeSelector.SelectedIndex;

    public ModeThemeWindow() => InitializeComponent();

    private void OnConfirm(object sender, RoutedEventArgs e)
    {
        if (ThemeSelector.SelectedIndex < 0) return;
        DialogResult = true;
    }

    private void OnCancel(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
}
