using System.Reflection;
using System.Windows;
using System.Windows.Input;
using ASPAssistant.Core.Services;

namespace ASPAssistant.App.Windows;

public partial class UpdateDialog : Window
{
    public UpdateDialog(UpdateInfo updateInfo)
    {
        InitializeComponent();

        TitleText.Text = $"发现新版本 {updateInfo.TagName}";

        var currentVersion = Assembly.GetEntryAssembly()?.GetName().Version;
        CurrentVersionText.Text = currentVersion == new Version(0, 0, 0) || currentVersion is null
            ? "0.0.0 (dev)"
            : currentVersion.ToString(3);

        LatestVersionText.Text = updateInfo.Version;
        ReleaseNotesText.Text = string.IsNullOrWhiteSpace(updateInfo.ReleaseNotes)
            ? "（暂无更新说明）"
            : updateInfo.ReleaseNotes;
    }

    /// <summary>把下载进度（0～1）与阶段文案同步到对话框。</summary>
    public void ReportProgress(double fraction)
    {
        DialogProgress.Visibility = Visibility.Visible;
        ProgressPercentText.Visibility = Visibility.Visible;
        var percent = (int)Math.Round(Math.Clamp(fraction, 0, 1) * 100);
        DialogProgress.Value = percent;
        ProgressPercentText.Text = percent + "%";
        DownloadHintText.Text = fraction switch
        {
            < 0.85 => "正在下载安装包…",
            < 0.95 => "正在校验安装包…",
            < 1.0 => "正在解压安装包…",
            _ => "下载完成，点击主界面横幅上的“立即重启”完成更新。"
        };
    }

    public void ReportFailure(string reason)
    {
        DownloadHintText.Text = $"下载失败：{reason}。请关闭此窗口，在主界面点击“重试”。";
        DialogProgress.Visibility = Visibility.Collapsed;
        ProgressPercentText.Visibility = Visibility.Collapsed;
    }

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnClose(object sender, RoutedEventArgs e) => Close();
}
