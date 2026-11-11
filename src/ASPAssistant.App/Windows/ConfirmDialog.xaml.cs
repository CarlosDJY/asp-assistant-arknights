// Modified for ASP Assistant Arknights on 2026-11-12; see NOTICE.
using System.Windows;
using System.Windows.Input;

namespace ASPAssistant.App.Windows;

/// <summary>Themed yes / no (or OK only) dialog in place of the system message box.</summary>
public partial class ConfirmDialog : Window
{
    public ConfirmDialog(string title, string message, string okText = "确定", bool danger = false, bool okOnly = false)
    {
        InitializeComponent();
        Title = title;
        TitleText.Text = title;
        MessageText.Text = message;
        OkButton.Content = okText;
        if (danger) OkButton.Style = (Style)FindResource("DialogDangerButton");
        if (okOnly) CancelButton.Visibility = Visibility.Collapsed;
    }

    /// <summary>Shows the dialog over <paramref name="owner"/>; true when confirmed.</summary>
    public static bool Ask(Window owner, string title, string message, string okText = "确定", bool danger = false)
        => new ConfirmDialog(title, message, okText, danger) { Owner = owner }.ShowDialog() == true;

    public static void Tell(Window owner, string title, string message)
        => new ConfirmDialog(title, message, "知道了", okOnly: true) { Owner = owner }.ShowDialog();

    private void OnOkClick(object sender, RoutedEventArgs e) => DialogResult = true;
    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
}
