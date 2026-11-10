using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using ASPAssistant.Core;

namespace ASPAssistant.App.Windows;

public partial class ShareLinkWindow : Window
{
    private readonly Action<string> _copyText;
    private bool _closed;

    public ShareLinkWindow(string url, Action<string> copyText)
    {
        InitializeComponent();
        LinkBox.Text = url;
        _copyText = copyText;
        Closed += (_, _) => _closed = true;
        Loaded += async (_, _) => await CopyLinkAsync();
    }

    public async Task CopyLinkAsync()
    {
        if (_closed || !CopyButton.IsEnabled) return;
        CopyButton.IsEnabled = false;
        CopyButton.Content = "正在复制…";
        try
        {
            for (var attempt = 0; attempt < 3; attempt++)
            {
                if (_closed) return;
                try
                {
                    _copyText(LinkBox.Text);
                    CopyStatusText.Text = "链接已复制，可以粘贴分享。";
                    return;
                }
                catch (ExternalException ex)
                {
                    if (ex.ErrorCode == unchecked((int)0x800401D0) && attempt < 2)
                    {
                        CopyStatusText.Text = "剪贴板暂时被占用，正在重试…";
                        await Task.Delay(150);
                        continue;
                    }
                    AppLogger.Warn("Lineup", $"Link generated; clipboard copy failed: 0x{ex.ErrorCode:X8}");
                    CopyStatusText.Text = "链接已生成，但暂时无法复制。可再次点击复制，或选中链接手动复制。";
                    LinkBox.Focus();
                    LinkBox.SelectAll();
                    return;
                }
            }
        }
        finally
        {
            CopyButton.IsEnabled = true;
            CopyButton.Content = "复制链接";
        }
    }

    private async void OnCopy(object sender, RoutedEventArgs e) => await CopyLinkAsync();
    private void OnClose(object sender, RoutedEventArgs e) => Close();
    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed) DragMove();
    }
}
