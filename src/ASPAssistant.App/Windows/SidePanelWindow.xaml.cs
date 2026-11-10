// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using ASPAssistant.App.Views;
using ASPAssistant.Core;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.Interop;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;

namespace ASPAssistant.App.Windows;

public partial class SidePanelWindow : Window
{
    public OperatorBrowseViewModel OperatorBrowseVm { get; }
    public EquipmentBrowseViewModel EquipmentBrowseVm { get; }
    public TrackingViewModel TrackingVm { get; }
    public GameStateViewModel GameStateVm { get; }
    public BanViewModel BanVm { get; }
    public EnemyViewModel EnemyVm { get; }
    public LineupViewModel LineupVm { get; }
    public LineupPlannerViewModel PlannerVm { get; private set; } = null!;

    public event Action? ManualScanRequested;
    public event Action? EnemyRecognitionRequested;
    public event Action<string, bool>? BanToggleRequested;

    private readonly LineupShareService _lineupShareService;
    private readonly IReadOnlyList<Operator> _allOperators;
    private readonly IReadOnlyList<Equipment> _allEquipment;
    private readonly LineupPlanner _lineupPlanner;
    private readonly IReadOnlyList<CovenantInfo> _covenants;
    private readonly DispatcherTimer _toastHideTimer;

    private readonly UpdateService _updateService = new();
    private bool _isCheckingForUpdates;
    private bool _hasCheckedForUpdates;
    private readonly SettingsManager _settingsManager;
    public GameShopTheme GameTheme { get; private set; }
    public event Action<GameShopTheme>? GameThemeChanged;
    private UpdateInfo? _pendingUpdate;
    private Task? _downloadTask;
    private bool _isDownloadReady;

    // 用户意图追踪
    private bool _isUserPositioned;       // 用户手动移动过窗口
    private bool _isUserSized;            // 用户手动调整过窗口高度
    private bool _isProgrammaticChange;   // 程序正在设置位置/尺寸，抑制事件响应
    private bool _isRestoring;            // 正在从最小化恢复，抑制事件响应
    private WindowState _previousWindowState;
    private WindowState? _stateBeforeGameMinimized;
    // WPF 初始 Show() 会触发 SizeChanged/LocationChanged，在首次 UpdatePosition 前清除误设的标志
    private bool _layoutInitialized;

    // Global hotkey (Ctrl+Shift+/) for manual screenshot scan
    private const int HotkeyIdManualScan = 0x4153; // 'AS' — arbitrary unique app-scoped ID
    private HwndSource? _hwndSource;

    // 全屏状态追踪
    private bool _wasFullscreen;
    private bool _fullscreenBannerDismissed;

    // 全屏时侧边栏的起始/结束位置（占游戏窗口高度的比例）
    private const double FullscreenTopRatio    = 0.22;  // 从顶部留出 22%
    private const double FullscreenBottomRatio = 0.68;  // 底部截止在 68% 处（留出底部 32%）

    public SidePanelWindow(
        OperatorBrowseViewModel operatorVm,
        EquipmentBrowseViewModel equipmentVm,
        TrackingViewModel trackingVm,
        GameStateViewModel gameStateVm,
        BanViewModel banVm,
        EnemyViewModel enemyVm,
        LineupViewModel lineupVm,
        LineupShareService lineupShareService,
        IReadOnlyList<Operator> allOperators,
        IReadOnlyList<Equipment> allEquipment,
        SettingsManager settingsManager,
        GameShopTheme gameTheme,
        LineupPlanner? lineupPlanner = null,
        IReadOnlyList<CovenantInfo>? covenants = null)
    {
        OperatorBrowseVm = operatorVm;
        EquipmentBrowseVm = equipmentVm;
        TrackingVm = trackingVm;
        GameStateVm = gameStateVm;
        BanVm = banVm;
        EnemyVm = enemyVm;
        LineupVm = lineupVm;
        _lineupShareService = lineupShareService;
        _allOperators = allOperators;
        _allEquipment = allEquipment;
        _lineupPlanner = lineupPlanner ?? new LineupPlanner(allOperators, covenants ?? []);
        _covenants = covenants ?? [];
        _settingsManager = settingsManager;
        GameTheme = gameTheme;

        InitializeComponent();
        CurrentVersionText.Text = $"当前版本：v{typeof(SidePanelWindow).Assembly.GetName().Version!.ToString(3)}";
        UiThemeSelector.SelectedIndex = ThemeManager.IsLight ? 1 : 0;
        GameThemeSelector.SelectedIndex = (int)gameTheme;
        MultiLineupTrackingToggle.IsChecked = trackingVm.TrackMultipleLineups;
        UiThemeSelector.SelectionChanged += OnUiThemeChanged;
        GameThemeSelector.SelectionChanged += OnGameThemeChanged;
        MultiLineupTrackingToggle.Checked += OnMultipleLineupsChanged;
        MultiLineupTrackingToggle.Unchecked += OnMultipleLineupsChanged;

        _toastHideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2.5),
        };
        _toastHideTimer.Tick += (_, _) => HideToast();

        LocationChanged += OnLocationChanged;
        SizeChanged     += OnSizeChanged;
        StateChanged    += OnStateChanged;

        trackingVm.GameState = gameStateVm;

        OperatorView.DataContext = operatorVm;
        EquipmentView.DataContext = equipmentVm;
        TrackingView.SetOperators(allOperators);
        TrackingView.SetEquipment(allEquipment);
        TrackingView.DataContext = trackingVm;
        EnemyView.DataContext = enemyVm;
        EnemyView.RecognitionRequested += () => EnemyRecognitionRequested?.Invoke();
        LineupView.DataContext = lineupVm;
        WireLineupView();

        OperatorView.IsTrackedCheck = trackingVm.IsTracked;
        EquipmentView.IsTrackedCheck = trackingVm.IsTracked;

        OperatorView.IsBannedCheck = banVm.IsBanned;

        OperatorView.TrackingRequested += (name, type) => trackingVm.AddTracking(name, type);
        OperatorView.UntrackingRequested += name => trackingVm.RemoveTracking(name);
        EquipmentView.TrackingRequested += (name, type) => trackingVm.AddTracking(name, type);
        EquipmentView.UntrackingRequested += name => trackingVm.RemoveTracking(name);
        TrackingView.RemoveTrackingRequested += name => trackingVm.RemoveTracking(name);
        TrackingView.ClearAllTrackingRequested += () => trackingVm.ClearAllTracking();

        OperatorView.ClearBansRequested += () => banVm.ClearBans();
        OperatorView.ManualScanRequested += () => ManualScanRequested?.Invoke();
        OperatorView.BanToggleRequested += (name, banned) => BanToggleRequested?.Invoke(name, banned);

        banVm.BansChanged += () =>
        {
            Dispatcher.Invoke(() =>
            {
                OperatorView.RefreshBannedStates();
                operatorVm.NotifyBansChanged();
            });
        };

        trackingVm.TrackedOperators.CollectionChanged += (_, _) =>
        {
            OperatorView.RefreshTrackingStates();
            EquipmentView.RefreshTrackingStates();
        };
        trackingVm.TrackedEquipment.CollectionChanged += (_, _) =>
        {
            OperatorView.RefreshTrackingStates();
            EquipmentView.RefreshTrackingStates();
        };
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        _hwndSource = HwndSource.FromHwnd(hwnd);
        _hwndSource.AddHook(WndProc);
        User32.RegisterHotKey(hwnd, HotkeyIdManualScan,
            User32.MOD_CONTROL | User32.MOD_SHIFT, User32.VK_OEM_2);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == User32.WM_HOTKEY && wParam.ToInt32() == HotkeyIdManualScan)
        {
            ManualScanRequested?.Invoke();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void UpdateManualScanStatus(int pending)
        => OperatorView.UpdateManualScanStatus(pending);

    public void SetBanProcessingMaskVisible(bool visible, string? message = null)
    {
        BanProcessingMask.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        if (!string.IsNullOrWhiteSpace(message))
            BanProcessingMaskText.Text = message;
    }

    public async Task StartUpdateCheckAsync()
    {
        await Task.Delay(3000);
        await CheckForUpdatesAsync(isManual: false);
    }

    private async void OnCheckUpdate(object sender, RoutedEventArgs e)
    {
        await CheckForUpdatesAsync(isManual: true);
    }

    private async Task CheckForUpdatesAsync(bool isManual)
    {
        if (_isCheckingForUpdates || (!isManual && _hasCheckedForUpdates)) return;
        _hasCheckedForUpdates = true;

        if (_pendingUpdate is not null && (_downloadTask is { IsCompleted: false } || _isDownloadReady))
        {
            UpdateBanner.Visibility = Visibility.Visible;
            UpdateStatusText.Text = _isDownloadReady
                ? $"新版本 {_pendingUpdate.TagName} 已就绪，点击“立即重启”完成更新"
                : $"正在下载新版本 {_pendingUpdate.TagName}…";
            return;
        }

        if (_updateService.IsDevBuild)
        {
            UpdateStatusText.Text = "当前为开发测试版（0.0.0），不检查自动更新";
            return;
        }

        _isCheckingForUpdates = true;
        CheckUpdateButton.IsEnabled = false;
        CheckUpdateButton.Content = "正在检查…";
        UpdateStatusText.Text = "正在检查更新…";
        try
        {
            var info = await _updateService.CheckForUpdateAsync();
            if (info is null)
            {
                UpdateStatusText.Text = _updateService.LastError is null
                    ? "当前已是最新版本"
                    : $"检查失败：{_updateService.LastError}，请稍后重试";
                if (_updateService.LastError is not null)
                    AppLogger.Warn("Update", $"Update check failed: {_updateService.LastError}");
                return;
            }

            if (!isManual && await _settingsManager.LoadSkippedVersionAsync() == info.TagName)
            {
                UpdateStatusText.Text = $"已跳过 {info.TagName}，可点击“检查更新”重新查看";
                return;
            }

            _pendingUpdate = info;
            _isDownloadReady = false;
            UpdateBannerText.Text = $"发现新版本 {info.TagName}";
            UpdateStatusText.Text = $"正在下载新版本 {info.TagName}…";
            UpdateActionIcon.Text = "↑ ";
            UpdateActionText.Text = "查看更新";
            UpdateBannerProgress.Value = 0;
            UpdateBannerProgress.Visibility = Visibility.Visible;
            UpdateBanner.Visibility = Visibility.Visible;
            _downloadTask = RunBackgroundDownloadAsync(info);
        }
        finally
        {
            _isCheckingForUpdates = false;
            CheckUpdateButton.IsEnabled = true;
            CheckUpdateButton.Content = "检查更新";
        }
    }

    private async Task RunBackgroundDownloadAsync(UpdateInfo info)
    {
        var progress = new Progress<double>(value =>
        {
            Dispatcher.Invoke(() =>
            {
                UpdateBannerProgress.Value = value * 100;
            });
        });

        try
        {
            await _updateService.DownloadAndApplyUpdateAsync(info, progress);

            _isDownloadReady = true;
            Dispatcher.Invoke(() =>
            {
                UpdateBannerProgress.Visibility = Visibility.Collapsed;
                UpdateActionIcon.Text = "↻ ";
                UpdateActionText.Text = "立即重启";
                UpdateStatusText.Text = $"新版本 {info.TagName} 已就绪，点击“立即重启”完成更新";
            });
        }
        catch (Exception ex)
        {
            var reason = UpdateService.DescribeError(ex);
            AppLogger.Warn("Update", $"Download or apply failed: {reason}");
            Dispatcher.Invoke(() =>
            {
                UpdateBannerProgress.Visibility = Visibility.Collapsed;
                UpdateBannerText.Text = $"下载失败（{reason}），点击重试";
                UpdateActionIcon.Text = "↺ ";
                UpdateActionText.Text = "重试";
                UpdateStatusText.Text = $"下载失败：{reason}，可在更新横幅中重试";
            });
            _isDownloadReady = false;
        }
    }

    private void OnBannerAction(object sender, RoutedEventArgs e)
    {
        if (_isDownloadReady)
        {
            Application.Current.Shutdown();
            return;
        }

        if (_pendingUpdate is null) return;

        // Download failures are handled inside RunBackgroundDownloadAsync, so its
        // task completes normally even when the banner needs a retry.
        if (_downloadTask is { IsCompleted: true })
        {
            UpdateBannerText.Text = $"发现新版本 {_pendingUpdate.TagName}";
            UpdateStatusText.Text = $"正在下载新版本 {_pendingUpdate.TagName}…";
            UpdateActionIcon.Text = "↑ ";
            UpdateActionText.Text = "查看更新";
            UpdateBannerProgress.Value = 0;
            UpdateBannerProgress.Visibility = Visibility.Visible;
            _downloadTask = RunBackgroundDownloadAsync(_pendingUpdate);
            return;
        }

        // Download in progress — open notes dialog
        var dialog = new UpdateDialog(_pendingUpdate)
        {
            Owner = this
        };
        dialog.ShowDialog();
    }

    private async void OnSkipVersion(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate is null) return;
        if (_settingsManager is not null)
            await _settingsManager.SaveSkippedVersionAsync(_pendingUpdate.TagName);
        UpdateBanner.Visibility = Visibility.Collapsed;
    }

    public void SetGameMinimized(bool minimized)
    {
        WindowState target;
        if (minimized)
        {
            if (WindowState == WindowState.Minimized)
                return;
            _stateBeforeGameMinimized = WindowState;
            target = WindowState.Minimized;
        }
        else
        {
            if (_stateBeforeGameMinimized is not { } previous)
                return;
            _stateBeforeGameMinimized = null;
            if (WindowState != WindowState.Minimized)
                return;
            target = previous;
        }
        _isProgrammaticChange = true;
        try { WindowState = target; }
        finally { _isProgrammaticChange = false; }
    }

    public void UpdatePosition(RECT gameRect, bool attachInside, bool isFullscreen, bool gameActuallyMoved)
    {
        _isProgrammaticChange = true;
        try
        {
            // 首次调用：清除 WPF 初始布局期间可能误设的标志
            if (!_layoutInitialized)
            {
                _layoutInitialized = true;
                _isUserSized = false;
                _isUserPositioned = false;
            }

            if (gameActuallyMoved)
                _isUserPositioned = false;

            // 全屏/窗口模式切换时重置用户手动标志，让位置和高度自动适配新模式
            if (isFullscreen != _wasFullscreen)
            {
                _isUserSized      = false;
                _isUserPositioned = false;
                _wasFullscreen    = isFullscreen;
                UpdateFullscreenBanner(isFullscreen);
            }

            var monitorRight = Core.Interop.User32.GetMonitorRect(gameRect.Right - 1, gameRect.Top).Right;
            var targetLeft = attachInside
                ? monitorRight - Width
                : (double)gameRect.Right;
            var targetTop = isFullscreen
                ? gameRect.Top + gameRect.Height * FullscreenTopRatio
                : (double)gameRect.Top;
            var targetHeight = isFullscreen
                ? gameRect.Height * (FullscreenBottomRatio - FullscreenTopRatio)
                : (double)gameRect.Height;

            if (!_isUserPositioned)
            {
                Left = targetLeft;
                Top  = targetTop;
            }

            if (!_isUserSized)
                Height = targetHeight;
        }
        finally
        {
            _isProgrammaticChange = false;
        }
    }

    private void UpdateFullscreenBanner(bool isFullscreen)
    {
        if (isFullscreen && !_fullscreenBannerDismissed)
            FullscreenBanner.Visibility = Visibility.Visible;
        else
            FullscreenBanner.Visibility = Visibility.Collapsed;
    }

    private void OnDismissFullscreenBanner(object sender, RoutedEventArgs e)
    {
        _fullscreenBannerDismissed = true;
        FullscreenBanner.Visibility = Visibility.Collapsed;
    }

    private void OnLocationChanged(object? sender, EventArgs e)
    {
        if (!_isProgrammaticChange && !_isRestoring)
            _isUserPositioned = true;
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_isProgrammaticChange && !_isRestoring && e.HeightChanged)
            _isUserSized = true;
    }

    private void OnStateChanged(object? sender, EventArgs e)
    {
        if (WindowState != WindowState.Minimized)
            _stateBeforeGameMinimized = null;
        if (_previousWindowState != WindowState.Normal && WindowState == WindowState.Normal)
        {
            _isRestoring = true;
            LayoutUpdated += ClearRestoringFlag;
        }
        _previousWindowState = WindowState;
    }

    private void ClearRestoringFlag(object? sender, EventArgs e)
    {
        _isRestoring = false;
        LayoutUpdated -= ClearRestoringFlag;
    }

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnMinimize(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void OnToggleSettings(object sender, RoutedEventArgs e)
    {
        SettingsPanel.Visibility = SettingsPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    }

    private async void OnUiThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UiThemeSelector.SelectedIndex < 0) return;
        ThemeManager.Apply(UiThemeSelector.SelectedIndex == 1);
        await SavePreferenceAsync(() => _settingsManager.SaveLightThemeAsync(ThemeManager.IsLight), UiThemeSelector);
    }

    private async void OnGameThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GameThemeSelector.SelectedIndex < 0) return;
        GameTheme = (GameShopTheme)GameThemeSelector.SelectedIndex;
        GameThemeChanged?.Invoke(GameTheme);
        await SavePreferenceAsync(() => _settingsManager.SaveGameShopThemeAsync(GameTheme), GameThemeSelector);
    }

    private async void OnMultipleLineupsChanged(object sender, RoutedEventArgs e)
    {
        TrackingVm.TrackMultipleLineups = MultiLineupTrackingToggle.IsChecked == true;
        await SavePreferenceAsync(() => _settingsManager.SaveTrackMultipleLineupsAsync(TrackingVm.TrackMultipleLineups), MultiLineupTrackingToggle);
    }

    private async Task SavePreferenceAsync(Func<Task> save, UIElement control)
    {
        control.IsEnabled = false;
        try
        {
            await save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLogger.Warn("Settings", $"Could not save preference: {ex.Message}");
            ShowToast("设置已生效，但保存失败");
        }
        finally
        {
            control.IsEnabled = true;
        }
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        var confirm = MessageBox.Show(this,
            "确定要关闭 ASP Assistant · Arknights 吗？\n关闭后识别、悬浮提示与快捷键都会停止。",
            "退出确认", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No);
        if (confirm != MessageBoxResult.Yes) return;

        User32.UnregisterHotKey(new WindowInteropHelper(this).Handle, HotkeyIdManualScan);
        Application.Current.Shutdown();
    }

    // ── Lineup feature wiring ──────────────────────────────────────────────

    private void WireLineupView()
    {
        LineupView.NewLineupRequested += () => OpenEditor(null);
        LineupView.EditLineupRequested += lineup => OpenEditor(lineup);
        LineupView.DeleteLineupRequested += async lineup =>
        {
            var ok = MessageBox.Show(
                this,
                $"确定删除阵容“{lineup.Name}”？此操作不可撤销。",
                "删除阵容",
                MessageBoxButton.OKCancel,
                MessageBoxImage.Warning);
            if (ok != MessageBoxResult.OK) return;
            try
            {
                await LineupVm.DeleteAsync(lineup);
                LineupView.RefreshAllBadges();
                ShowToast($"已删除阵容“{lineup.Name}”");
            }
            catch (Exception ex)
            {
                ShowToast($"删除失败：{ex.Message}");
            }
        };

        LineupView.ShareLineupRequested += async lineup =>
        {
            string url;
            try
            {
                ShowToast("正在生成分享链接…");
                url = await _lineupShareService.ExportAsync(lineup);
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or InvalidOperationException)
            {
                AppLogger.Warn("Lineup", $"Share failed: {ex.Message}");
                ShowToast($"生成分享链接失败：{ex.Message}");
                return;
            }
            HideToast();
            new ShareLinkWindow(url, Clipboard.SetText) { Owner = this }.ShowDialog();
        };

        LineupView.ImportLineupRequested += async () =>
        {
            string clipboard = "";
            try { clipboard = Clipboard.GetText() ?? ""; } catch { /* ignore clipboard race */ }

            var dlg = new InputDialog(
                "导入阵容",
                "粘贴 dpaste.com 短链接 或 阵容 JSON：",
                clipboard.Trim())
            {
                Owner = this,
            };
            if (dlg.ShowDialog() != true) return;
            var input = dlg.InputText.Trim();
            if (string.IsNullOrEmpty(input)) return;

            try
            {
                ShowToast("正在导入…");
                var lineup = await _lineupShareService.ImportAsync(input);
                // Avoid id collision with existing lineups by re-id-ing on import.
                lineup.Id = Guid.NewGuid().ToString("N");
                lineup.UpdatedAt = DateTime.UtcNow;
                if (string.IsNullOrWhiteSpace(lineup.Name))
                    lineup.Name = "导入的阵容";
                await LineupVm.AddOrUpdateAsync(lineup);
                LineupView.RefreshAllBadges();
                ShowToast($"已导入阵容“{lineup.Name}”");
            }
            catch (Exception ex)
            {
                AppLogger.Warn("Lineup", $"Import failed: {ex.Message}");
                ShowToast($"导入失败：{ex.Message}");
            }
        };

        LineupView.TrackLineupRequested += lineup =>
        {
            try
            {
                var added = TrackingVm.TrackLineup(lineup);
                ShowToast($"已追踪阵容“{lineup.Name}”：新增 {added.Operators} 干员 / {added.Equipment} 装备");
            }
            catch (Exception ex)
            {
                ShowToast($"追踪失败：{ex.Message}");
            }
        };
        WirePlannerView();
    }

    private void WirePlannerView()
    {
        PlannerVm = new LineupPlannerViewModel(_lineupPlanner, _allOperators, BanVm)
        {
            TrackedOperatorsProvider = () => TrackingVm.TrackedOperators.Select(e => e.Name).ToList(),
            RunOnUiThread = action =>
            {
                if (Dispatcher.CheckAccess()) action();
                else Dispatcher.BeginInvoke(action);
            }
        };
        PlannerVm.SaveRequested += async lineup =>
        {
            try
            {
                await LineupVm.AddOrUpdateAsync(lineup);
                LineupView.RefreshAllBadges();
                ShowToast($"已保存阵容“{lineup.Name}”");
            }
            catch (Exception ex)
            {
                ShowToast($"保存失败：{ex.Message}");
            }
        };
        PlannerVm.TrackRequested += lineup =>
        {
            try
            {
                var added = TrackingVm.TrackLineup(lineup);
                ShowToast($"已追踪规划阵容：新增 {added.Operators} 名干员");
            }
            catch (Exception ex)
            {
                ShowToast($"追踪失败：{ex.Message}");
            }
        };
        PlannerVm.SetAvailableCovenants(OperatorBrowseVm.HighlightedCovenants);
        OperatorBrowseVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(OperatorBrowseViewModel.HighlightedCovenants))
                Dispatcher.Invoke(() => PlannerVm.SetAvailableCovenants(OperatorBrowseVm.HighlightedCovenants));
        };
        PlannerView.DataContext = PlannerVm;
    }

    private void OpenEditor(Lineup? source)
    {
        var calc = LineupVm.Calculator;
        var editorVm = new LineupEditorViewModel(source, _allOperators, _allEquipment, calc);
        var window = new LineupEditorWindow(editorVm) { Owner = this };
        if (window.ShowDialog() != true || window.Result is null) return;

        _ = LineupVm.AddOrUpdateAsync(window.Result).ContinueWith(_ =>
        {
            Dispatcher.Invoke(() =>
            {
                LineupView.RefreshAllBadges();
                ShowToast(source is null ? "已创建阵容" : "已保存阵容");
            });
        });
    }

    // ── Transient toast ────────────────────────────────────────────────────

    public void ShowToast(string message)
    {
        ToastText.Text = message;
        ToastBorder.Visibility = Visibility.Visible;

        var fadeIn = new DoubleAnimation
        {
            From = ToastBorder.Opacity,
            To = 1.0,
            Duration = TimeSpan.FromMilliseconds(140),
        };
        ToastBorder.BeginAnimation(UIElement.OpacityProperty, fadeIn);

        _toastHideTimer.Stop();
        _toastHideTimer.Start();
    }

    private void HideToast()
    {
        _toastHideTimer.Stop();

        var fadeOut = new DoubleAnimation
        {
            From = ToastBorder.Opacity,
            To = 0.0,
            Duration = TimeSpan.FromMilliseconds(220),
        };
        fadeOut.Completed += (_, _) =>
        {
            ToastBorder.Visibility = Visibility.Collapsed;
        };
        ToastBorder.BeginAnimation(UIElement.OpacityProperty, fadeOut);
    }
}
