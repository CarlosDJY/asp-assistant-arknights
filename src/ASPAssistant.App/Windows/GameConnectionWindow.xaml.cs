// Modified for ASP Assistant Arknights on 2026-11-11; see NOTICE.
using System.IO;
using System.Windows;
using System.Windows.Controls;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using MaaFramework.Binding;
using Microsoft.Win32;

namespace ASPAssistant.App.Windows;

public partial class GameConnectionWindow : Window
{
    private readonly ScreenCaptureService _capture;
    private readonly SettingsManager _settings;
    private bool _busy;

    private sealed record DeviceChoice(AdbDeviceInfo Device)
    {
        public string Label => $"{Device.Name} · {Device.AdbSerial}";
    }

    private sealed record WindowChoice(DesktopWindowInfo Window)
    {
        public string Label => $"{Window.Name} · 0x{Window.Handle.ToInt64():X}";
    }

    public GameConnectionWindow(ScreenCaptureService capture, SettingsManager settings)
    {
        _capture = capture;
        _settings = settings;
        InitializeComponent();
        var saved = capture.Connection.Settings;
        AdbPathBox.Text = saved.AdbPath;
        AdbSerialBox.Text = saved.AdbSerial;
        RefreshWindows();
        PcMode.IsChecked = !capture.Connection.IsAdb;
        AdbMode.IsChecked = capture.Connection.IsAdb;
        StatusText.Text = capture.Connection.Status;
        Closing += (_, e) => e.Cancel = _busy;
    }

    private void OnSourceChanged(object sender, RoutedEventArgs e)
    {
        if (AdbSettings is null) return;
        AdbSettings.IsEnabled = AdbMode.IsChecked == true;
        ApplyButton.Content = AdbMode.IsChecked == true ? "测试并应用" : "保存并应用";
    }

    private async void OnDiscover(object sender, RoutedEventArgs e)
    {
        DiscoverButton.IsEnabled = false;
        StatusText.Text = "正在查找 ADB 设备…";
        try
        {
            var path = AdbPathBox.Text.Trim();
            var devices = await Task.Run(() =>
            {
                using var found = MaaToolkit.Shared.AdbDevice.Find(File.Exists(path) ? path : "");
                return found.Select(d => new AdbDeviceInfo(d.Name, d.AdbPath, d.AdbSerial,
                    d.ScreencapMethods, d.InputMethods, d.Config, d.AgentPath)).ToList();
            });
            DeviceSelector.ItemsSource = devices.Select(d => new DeviceChoice(d)).ToList();
            StatusText.Text = devices.Count == 0 ? "未发现设备，可手动填写路径和连接地址。" : $"发现 {devices.Count} 个设备，请选择目标实例。";
            if (devices.Count == 1) DeviceSelector.SelectedIndex = 0;
            RefreshWindows();
        }
        catch (Exception ex) { StatusText.Text = ex.Message; }
        finally { DiscoverButton.IsEnabled = true; }
    }

    private void OnDeviceSelected(object sender, SelectionChangedEventArgs e)
    {
        if (DeviceSelector.SelectedItem is not DeviceChoice choice) return;
        AdbPathBox.Text = choice.Device.AdbPath;
        AdbSerialBox.Text = choice.Device.AdbSerial;
        AdbMode.IsChecked = true;
    }

    private void OnBrowseAdb(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog { Filter = "ADB 程序 (adb.exe)|adb.exe", CheckFileExists = true };
        if (dialog.ShowDialog(this) == true) AdbPathBox.Text = dialog.FileName;
    }

    private void OnRefreshWindows(object sender, RoutedEventArgs e) => RefreshWindows();

    private void RefreshWindows()
    {
        var previous = (WindowSelector.SelectedItem as WindowChoice)?.Window.Handle
            ?? _capture.Connection.FindGameWindow();
        var choices = GameConnectionService.FindMuMuWindows().Select(w => new WindowChoice(w)).ToList();
        WindowSelector.ItemsSource = choices;
        WindowSelector.SelectedItem = choices.FirstOrDefault(w => w.Window.Handle == previous);
        if (WindowSelector.SelectedItem is null && choices.Count == 1) WindowSelector.SelectedIndex = 0;
    }

    private async void OnApply(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        var adb = AdbMode.IsChecked == true;
        var window = (WindowSelector.SelectedItem as WindowChoice)?.Window;
        if (adb && window is null)
        {
            StatusText.Text = "请打开 MuMu 游戏窗口并刷新，选择对应实例。";
            return;
        }
        var candidate = new GameConnectionService(new GameConnectionSettings
        {
            Source = adb ? GameCaptureSource.Adb : GameCaptureSource.Pc,
            AdbPath = AdbPathBox.Text.Trim(),
            AdbSerial = AdbSerialBox.Text.Trim(),
            WindowTitle = window?.Name ?? _capture.Connection.Settings.WindowTitle,
        }, window?.Handle ?? IntPtr.Zero);
        _busy = true;
        ApplyButton.IsEnabled = false;
        AdbSettings.IsEnabled = false;
        PcMode.IsEnabled = AdbMode.IsEnabled = false;
        try
        {
            if (adb)
            {
                StatusText.Text = "正在连接并读取画面，首次连接需要数秒…";
                var png = await Task.Run(() => candidate.CaptureAdbScreen(_capture.MaxCaptureWidth));
                if (png is null) throw new IOException(candidate.Status);
                if (candidate.FindGameWindow() == IntPtr.Zero || candidate.GetViewport(window!.Handle) is null)
                    throw new IOException("MuMu 游戏显示区域不可用，请恢复游戏窗口后重试。");
            }
            await _settings.SaveGameConnectionAsync(candidate.Settings);
            _capture.SetConnection(candidate);
            _busy = false;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            candidate.Dispose();
            StatusText.Text = ex.Message;
        }
        finally
        {
            _busy = false;
            ApplyButton.IsEnabled = true;
            PcMode.IsEnabled = AdbMode.IsEnabled = true;
            AdbSettings.IsEnabled = AdbMode.IsChecked == true;
        }
    }
}
