// Modified for ASP Assistant Arknights on 2026-11-12; see NOTICE.
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using MaaFramework.Binding;
using Microsoft.Win32;

namespace ASPAssistant.App.Windows;

/// <summary>
/// Chooses where screenshots come from. ADB is tested first (status strip shows the frame it read);
/// a passing test turns the button into 应用, and editing any field asks for a new test.
/// </summary>
public partial class GameConnectionWindow : Window
{
    private enum Tone { Neutral, Busy, Ok, Error }

    private readonly ScreenCaptureService _capture;
    private readonly SettingsManager _settings;
    private bool _busy;
    private bool _loading = true;
    /// <summary>An ADB connection that passed the test for the current fields.</summary>
    private GameConnectionService? _tested;

    private sealed record DeviceChoice(AdbDeviceInfo Device)
    {
        public string Name => Device.Name;
        public string Serial => Device.AdbSerial;
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
        // A saved manual address is only visible in the manual section, so open it.
        ManualToggle.IsChecked = !string.IsNullOrWhiteSpace(saved.AdbSerial);
        RefreshWindows();
        PcMode.IsChecked = !capture.Connection.IsAdb;
        AdbMode.IsChecked = capture.Connection.IsAdb;
        _loading = false;
        UpdateMode();
        if (capture.Connection.IsAdb) SetStatus(Tone.Neutral, "当前连接", capture.Connection.Status);
        Closing += (_, e) => e.Cancel = _busy;
        Closed += (_, _) => { if (DialogResult != true) _tested?.Dispose(); };
    }

    private bool IsAdb => AdbMode.IsChecked == true;

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        if (!_busy) Close();
    }

    private void OnSourceChanged(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        UpdateMode();
    }

    private void UpdateMode()
    {
        AdbSettings.Visibility = IsAdb ? Visibility.Visible : Visibility.Collapsed;
        PcSettings.Visibility = IsAdb ? Visibility.Collapsed : Visibility.Visible;
        NoDevicesText.Visibility = DeviceSelector.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (IsAdb)
        {
            ApplyButton.Content = _tested is null ? "测试并应用" : "应用";
            if (_tested is null) SetStatus(Tone.Neutral, "还没有测试", "选好实例和游戏窗口后点「测试并应用」，会先读一张画面确认。");
        }
        else
        {
            ApplyButton.Content = "保存并应用";
            var pc = new GameConnectionService(new GameConnectionSettings { Source = GameCaptureSource.Pc });
            var window = pc.FindGameWindow();
            var rect = pc.GetViewport(window);
            PcWindowText.Text = rect is { } r ? $"当前：明日方舟 · {r.Right - r.Left}×{r.Bottom - r.Top}"
                : window != IntPtr.Zero ? "找到了明日方舟窗口，但它现在最小化了" : "没有找到明日方舟窗口，打开游戏后会自动连接";
            if (rect is not null) SetStatus(Tone.Ok, "已找到游戏窗口", "商店、Ban 位和地图识别都从这个窗口截图");
            else SetStatus(Tone.Neutral, "等待游戏窗口", "保存后，打开或恢复明日方舟窗口即可识别");
            pc.Dispose();
        }
    }

    private void SetStatus(Tone tone, string title, string text, BitmapSource? preview = null)
    {
        var (fill, border, icon, glyph, titleBrush) = tone switch
        {
            Tone.Ok => ("SuccessTintBrush", "SuccessBorderBrush", "SuccessBrush", "✓", "SuccessTextBrush"),
            Tone.Error => ("DangerTintBrush", "DangerBorderBrush", "DangerBrush", "!", "DangerBrush"),
            Tone.Busy => ("AccentTintBrush", "AccentBorderBrush", "AccentBrush", "…", "AccentBrush"),
            _ => ("SectionBackgroundBrush", "BorderBrush", "TextSecondaryBrush", "i", "TextPrimaryBrush"),
        };
        StatusStrip.SetResourceReference(Border.BackgroundProperty, fill);
        StatusStrip.SetResourceReference(Border.BorderBrushProperty, border);
        StatusIcon.SetResourceReference(Border.BackgroundProperty, icon);
        StatusTitle.SetResourceReference(TextBlock.ForegroundProperty, titleBrush);
        StatusGlyph.Text = glyph;
        StatusTitle.Text = title;
        StatusText.Text = text;
        StatusText.Visibility = string.IsNullOrEmpty(text) ? Visibility.Collapsed : Visibility.Visible;
        PreviewImage.Source = preview;
        PreviewFrame.Visibility = preview is null ? Visibility.Collapsed : Visibility.Visible;
    }

    /// <summary>Any edit after a passing test asks for a new one.</summary>
    private void InvalidateTest()
    {
        if (_loading || _busy || _tested is null) return;
        _tested.Dispose();
        _tested = null;
        UpdateMode();
    }

    private void OnInputChanged(object sender, RoutedEventArgs e) => InvalidateTest();

    private void OnManualToggled(object sender, RoutedEventArgs e)
        => ManualPanel.Visibility = ManualToggle.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

    private async void OnDiscover(object sender, RoutedEventArgs e)
    {
        DiscoverButton.IsEnabled = false;
        SetStatus(Tone.Busy, "正在查找 ADB 设备…", "");
        try
        {
            var path = AdbPathBox.Text.Trim();
            var devices = await Task.Run(() =>
            {
                using var found = MaaToolkit.Shared.AdbDevice.Find(File.Exists(path) ? path : "");
                return found.Select(d => new AdbDeviceInfo(d.Name, d.AdbPath, d.AdbSerial,
                    d.ScreencapMethods, d.InputMethods, d.Config, d.AgentPath)).ToList();
            });
            var choices = devices.Select(d => new DeviceChoice(d)).ToList();
            DeviceSelector.ItemsSource = choices;
            NoDevicesText.Visibility = choices.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            NoDevicesText.Text = "没有发现设备。确认模拟器已启动，或在下方手动填写路径和地址。";
            DeviceSelector.SelectedItem = choices.FirstOrDefault(c => c.Serial == AdbSerialBox.Text.Trim())
                ?? (choices.Count == 1 ? choices[0] : null);
            if (choices.Count == 0)
            {
                ManualToggle.IsChecked = true;
                SetStatus(Tone.Error, "没有发现设备", "确认 MuMu 已启动，再点「发现设备」；也可以手动填写 ADB 路径和地址。");
            }
            else SetStatus(Tone.Neutral, $"发现 {choices.Count} 个实例", "选中正在运行游戏的那个，再选对应的游戏窗口。");
            RefreshWindows();
        }
        catch (Exception ex) { SetStatus(Tone.Error, "查找设备失败", ex.Message); }
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

    private GameConnectionService NewCandidate(bool adb, DesktopWindowInfo? window) => new(new GameConnectionSettings
    {
        Source = adb ? GameCaptureSource.Adb : GameCaptureSource.Pc,
        AdbPath = AdbPathBox.Text.Trim(),
        AdbSerial = AdbSerialBox.Text.Trim(),
        WindowTitle = window?.Name ?? _capture.Connection.Settings.WindowTitle,
    }, window?.Handle ?? IntPtr.Zero);

    private async void OnApply(object sender, RoutedEventArgs e)
    {
        if (_busy) return;
        if (!IsAdb)
        {
            await ApplyAsync(NewCandidate(false, null));
            return;
        }
        if (_tested is not null)
        {
            await ApplyAsync(_tested);
            return;
        }
        var window = (WindowSelector.SelectedItem as WindowChoice)?.Window;
        if (window is null)
        {
            SetStatus(Tone.Error, "还没选游戏窗口", "打开 MuMu 里的明日方舟，点刷新，选对应的实例窗口。");
            return;
        }
        var candidate = NewCandidate(true, window);
        SetBusy(true);
        SetStatus(Tone.Busy, "正在连接并读取画面", "首次连接需要几秒");
        try
        {
            var png = await Task.Run(() => candidate.CaptureAdbScreen(_capture.MaxCaptureWidth));
            if (png is null) throw new IOException(candidate.Status);
            if (candidate.FindGameWindow() == IntPtr.Zero || candidate.GetViewport(window.Handle) is null)
                throw new IOException("MuMu 游戏显示区域不可用，请恢复游戏窗口后重试。");
            var (preview, width, height) = Decode(png);
            _tested = candidate;
            SetStatus(Tone.Ok, "已读到画面",
                preview is null ? candidate.Status : $"{width}×{height} · 点「应用」开始使用", preview);
            ApplyButton.Content = "应用";
        }
        catch (Exception ex)
        {
            candidate.Dispose();
            SetStatus(Tone.Error, "读不到画面", $"{ex.Message} 确认实例已启动、地址正确，再点「发现设备」。");
        }
        finally { SetBusy(false); }
    }

    private async Task ApplyAsync(GameConnectionService candidate)
    {
        SetBusy(true);
        try
        {
            await _settings.SaveGameConnectionAsync(candidate.Settings);
            _capture.SetConnection(candidate);
            _tested = null;
            _busy = false;
            DialogResult = true;
        }
        catch (Exception ex)
        {
            SetStatus(Tone.Error, "保存失败", ex.Message);
            SetBusy(false);
        }
    }

    private void SetBusy(bool busy)
    {
        _busy = busy;
        ApplyButton.IsEnabled = CancelButton.IsEnabled = !busy;
        AdbSettings.IsEnabled = PcMode.IsEnabled = AdbMode.IsEnabled = !busy;
    }

    /// <summary>A small preview plus the real frame size.</summary>
    private static (BitmapSource? Image, int Width, int Height) Decode(byte[] png)
    {
        try
        {
            var frame = BitmapFrame.Create(new MemoryStream(png), BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
            var scale = Math.Min(1.0, 256.0 / frame.PixelWidth);
            BitmapSource preview = new TransformedBitmap(frame, new System.Windows.Media.ScaleTransform(scale, scale));
            preview.Freeze();
            return (preview, frame.PixelWidth, frame.PixelHeight);
        }
        catch { return (null, 0, 0); }
    }
}
