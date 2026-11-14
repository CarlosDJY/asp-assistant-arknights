using System.Windows;
using System.Windows.Input;
using System.Windows.Navigation;
using System.Diagnostics;
using System.ComponentModel;

namespace ASPAssistant.App.Windows;

/// <summary>
/// Generic single-line input dialog. Returns user input via <see cref="InputText"/> when OK
/// is clicked (DialogResult = true).
/// </summary>
public partial class InputDialog : Window
{
    public string InputText => InputBox.Text;
    public bool ShowGuideLink
    {
        set
        {
            GuideLinkText.Visibility = value ? Visibility.Visible : Visibility.Collapsed;
            Height = value ? 340 : 240;
        }
    }

    public InputDialog(string title, string prompt, string initialText = "")
    {
        InitializeComponent();
        TitleText.Text = title;
        Title = title;
        PromptText.Text = prompt;
        InputBox.Text = initialText;
        Loaded += (_, _) =>
        {
            InputBox.Focus();
            InputBox.SelectAll();
        };
    }

    private void OnOkClick(object sender, RoutedEventArgs e) => DialogResult = true;
    private void OnCancelClick(object sender, RoutedEventArgs e) => DialogResult = false;

    private void OnUseExampleGuide(object sender, RoutedEventArgs e)
    {
        const string exampleUrl = "https://game.skland.com/arknights/stronghold-guide/detail?header=0&guideId=2120569391147659132";
        InputBox.Text = exampleUrl;
        InputBox.Focus();
        InputBox.SelectAll();
        try
        {
            Clipboard.SetText(exampleUrl);
            ExampleGuideStatus.Text = "已复制并填入示例网址，点击“确定”即可导入。";
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            ExampleGuideStatus.Text = "已填入示例网址；剪贴板被占用，复制失败，可手动复制。";
        }
    }

    private void OnGuideNavigate(object sender, RequestNavigateEventArgs e)
    {
        e.Handled = true;
        try
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        }
        catch (Win32Exception)
        {
            MessageBox.Show(this, $"无法打开默认浏览器，请复制网址访问：\n{e.Uri.AbsoluteUri}", "打开攻略站失败");
        }
    }

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }
}
