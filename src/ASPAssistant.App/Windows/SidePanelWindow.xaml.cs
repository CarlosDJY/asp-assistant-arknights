// Modified for ASP Assistant Regulation on 2026-11-13; see NOTICE.
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
    public event Action? GameConnectionRequested;
    public event Action? EnemyRecognitionRequested;
    public event Action<string, bool>? BanToggleRequested;

    private readonly LineupShareService _lineupShareService;
    private readonly IReadOnlyList<Operator> _allOperators;
    /// <summary>The pool plus the 自选编队 picks: what 我的阵容 can hold.</summary>
    private readonly IReadOnlyList<Operator> _lineupOperators;
    private readonly IReadOnlyList<Equipment> _allEquipment;
    private readonly IReadOnlyList<Strategy> _allStrategies;
    private readonly LineupPlanner _lineupPlanner;
    private readonly IReadOnlyList<CovenantInfo> _covenants;
    private readonly DispatcherTimer _toastHideTimer;

    private readonly UpdateService _updateService = new();
    private bool _isCheckingForUpdates;
    private bool _hasCheckedForUpdates;
    private readonly SettingsManager _settingsManager;
    private SidePanelPositionSettings _positionSettings;
    private RECT? _lastGameRect;
    public GameShopTheme GameTheme { get; private set; }
    public event Action<GameShopTheme>? GameThemeChanged;
    public event Action<bool>? AutomaticGameThemeChanged;
    public bool AutomaticGameTheme => AutomaticThemeToggle.IsChecked == true;
    private UpdateInfo? _pendingUpdate;
    private Task? _downloadTask;
    private bool _isDownloadReady;
    private UpdateDialog? _openUpdateDialog;
    private double _lastUpdateProgress;
    private string? _downloadError;

    // 用户意图追踪
    private double? _userHeightDip;       // 用户手动设置的逻辑高度，跨 DPI 时不反复取整
    private double _preferredWidthDip;
    private bool _isProgrammaticChange;   // 程序正在设置位置/尺寸，抑制事件响应
    private bool _isRestoring;            // 正在从最小化恢复，抑制事件响应
    private WindowState _previousWindowState;
    private WindowState? _stateBeforeGameMinimized;
    private bool _isUserMovingOrSizing;

    // Global hotkey (Ctrl+Shift+/) for manual screenshot scan
    private const int HotkeyIdManualScan = 0x4153; // 'AS' — arbitrary unique app-scoped ID
    private const int HotkeyIdTogglePanel = 0x4154;
    private HotkeySettings _hotkeys;
    /// <summary>Which hotkey row is waiting for a new combination (null = none).</summary>
    private Button? _capturingHotkey;
    private HwndSource? _hwndSource;

    // 全屏状态追踪
    private bool _wasFullscreen;
    private bool _fullscreenBannerDismissed;
    /// <summary>「不再提示」: the fullscreen hint never shows again (saved).</summary>
    private bool _fullscreenHintOff;

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
        IReadOnlyList<Strategy> allStrategies,
        SettingsManager settingsManager,
        GameShopTheme gameTheme,
        SidePanelPositionSettings positionSettings,
        LineupPlanner? lineupPlanner = null,
        IReadOnlyList<CovenantInfo>? covenants = null,
        SidebarAppearance? appearance = null,
        HotkeySettings? hotkeys = null,
        IReadOnlyList<Operator>? lineupOperators = null)
    {
        _lineupOperators = lineupOperators ?? allOperators;
        _hotkeys = hotkeys ?? new HotkeySettings();
        _appearance = appearance ?? new SidebarAppearance();
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
        _allStrategies = allStrategies;
        _lineupPlanner = lineupPlanner ?? new LineupPlanner(allOperators, covenants ?? []);
        _covenants = covenants ?? [];
        _settingsManager = settingsManager;
        _positionSettings = positionSettings;
        GameTheme = gameTheme;

        InitializeComponent();
        _preferredWidthDip = Width;
        if (positionSettings.Placement is { } placement)
        {
            _preferredWidthDip = Math.Clamp(placement.WidthDip, MinWidth, MaxWidth);
            Width = _preferredWidthDip;
            _userHeightDip = placement.HeightDip;
        }
        CurrentVersionText.Text = $"当前版本  v{typeof(SidePanelWindow).Assembly.GetName().Version!.ToString(3)}";
        GameThemeSelector.SelectedIndex = (int)gameTheme;
        FollowModeThemeToggle.IsChecked = _appearance.FollowModeTheme;
        UiThemeSelector.SelectedIndex = (int)_appearance.Theme;
        UiThemeSelector.Visibility = _appearance.FollowModeTheme ? Visibility.Collapsed : Visibility.Visible;
        SyncDayNightSelector();
        DayNightSelector.SelectionChanged += OnDayNightChanged;
        FollowModeThemeToggle.Checked += OnFollowModeThemeChanged;
        FollowModeThemeToggle.Unchecked += OnFollowModeThemeChanged;
        trackingVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName is nameof(TrackingViewModel.TerminalLevel) or nameof(TrackingViewModel.IsTerminalLevelRestored))
                UpdateStatusChip();
        };
        UpdateStatusChip();
        MultiLineupTrackingToggle.IsChecked = trackingVm.TrackMultipleLineups;
        FreeSidePanelMovementToggle.IsChecked = positionSettings.FreeMovement;
        UiThemeSelector.SelectionChanged += OnUiThemeChanged;
        GameThemeSelector.SelectionChanged += OnGameThemeChanged;
        MultiLineupTrackingToggle.Checked += OnMultipleLineupsChanged;
        MultiLineupTrackingToggle.Unchecked += OnMultipleLineupsChanged;
        FreeSidePanelMovementToggle.Checked += OnFreeSidePanelMovementChanged;
        FreeSidePanelMovementToggle.Unchecked += OnFreeSidePanelMovementChanged;
        InitEdgeAutoHide();

        _toastHideTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(2.5),
        };
        _toastHideTimer.Tick += (_, _) => HideToast();

        SizeChanged     += OnSizeChanged;
        StateChanged    += OnStateChanged;
        PreviewMouseLeftButtonDown += ClosePopoversOnOutsideClick;
        SizeChanged += (_, _) =>
        {
            if (UpdatePopover.IsVisible) AimArrow(UpdatePopArrow, UpdateChip);
            if (FullscreenPopover.IsVisible) AimArrow(FullscreenPopArrow, FullscreenHintButton);
        };
        if ((object?)settingsManager is not null)
            _ = Task.Run(settingsManager.LoadFullscreenHintOffAsync).ContinueWith(t =>
            {
                if (t.IsCompletedSuccessfully && t.Result) Dispatcher.Invoke(() => { _fullscreenHintOff = true; UpdateFullscreenBanner(false); });
            });
        Loaded += (_, _) =>
        {
            if (!_positionSettings.FreeMovement || _positionSettings.Placement is not { } saved) return;
            _isProgrammaticChange = true;
            try
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                var work = User32.GetMonitorGeometry((int)saved.Left, (int)saved.Top).WorkArea;
                saved = saved with { WidthDip = _preferredWidthDip };
                // 第一次移动使 HWND 切换到记忆位置所在屏幕，再使用该屏的 DPI 恢复尺寸。
                User32.SetWindowBounds(hwnd, WindowGeometry.RestoreBounds(saved, work, User32.GetWindowDpiScale(hwnd)));
                User32.SetWindowBounds(hwnd, WindowGeometry.RestoreBounds(saved, work, User32.GetWindowDpiScale(hwnd)));
            }
            finally { _isProgrammaticChange = false; }
        };

        trackingVm.GameState = gameStateVm;

        OperatorView.DataContext = operatorVm;
        EquipmentView.DataContext = equipmentVm;
        TrackingView.SetOperators(allOperators);
        TrackingView.SetEquipment(allEquipment);
        TrackingView.SetCovenantCalculator(lineupVm.Calculator);
        TrackingView.DataContext = trackingVm;
        EnemyView.DataContext = enemyVm;
        RunBanView.DataContext = operatorVm;
        EnemyView.RecognitionRequested += () => EnemyRecognitionRequested?.Invoke();
        LineupView.DataContext = lineupVm;
        Views.LineupCardItem.AllStrategies = allStrategies;
        WireLineupView();

        OperatorView.IsTrackedCheck = trackingVm.IsTracked;
        EquipmentView.IsTrackedCheck = trackingVm.IsTracked;

        OperatorView.IsBannedCheck = banVm.IsBanned;

        OperatorView.TrackingRequested += (name, type) => trackingVm.AddTracking(name, type);
        OperatorView.UntrackingRequested += name => trackingVm.RemoveTracking(name);
        EquipmentView.TrackingRequested += (name, type) => trackingVm.AddTracking(name, type);
        EquipmentView.UntrackingRequested += name => trackingVm.RemoveTracking(name);
        TrackingView.RemoveTrackingRequested += name => trackingVm.RemoveTracking(name);
        TrackingView.ToggleAcquiredRequested += name => trackingVm.ToggleAcquired(name);
        TrackingView.ClearAllTrackingRequested += () => trackingVm.ClearAllTracking();

        RunBanView.ClearBansRequested += () => banVm.ClearBans();
        RunBanView.ClearRunRequested += () =>
        {
            banVm.SetCovenantBans([]);
            operatorVm.ClearCovenantHighlights();
            PlannerVm.OnlyCurrentRun = false;
        };
        RunBanView.ManualScanRequested += () => ManualScanRequested?.Invoke();
        OperatorView.BanToggleRequested += (name, banned) => BanToggleRequested?.Invoke(name, banned);

        banVm.BansChanged += () =>
        {
            Dispatcher.Invoke(() =>
            {
                OperatorView.RefreshBannedStates();
                operatorVm.NotifyBansChanged();
                UpdateBanSummary();
                UpdateLineupRunInfo();
            });
        };
        UpdateBanSummary();

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
        SystemBorder.Hide(hwnd);
        RegisterHotkeys();
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == User32.WM_WINDOWPOSCHANGED) OnEdgeWindowPosChanged();
        if (msg == User32.WM_ENTERSIZEMOVE) _isUserMovingOrSizing = true;
        if (msg == User32.WM_EXITSIZEMOVE)
        {
            _isUserMovingOrSizing = false;
            var placement = CaptureSidePanelPlacement();
            _userHeightDip = placement.HeightDip;
            _positionSettings = _positionSettings with
            {
                Placement = placement,
                FollowOffsetDip = !_positionSettings.FreeMovement && _lastGameRect is { } game
                    ? WindowGeometry.CaptureFollowOffset(game, placement, User32.GetWindowDpiScale(hwnd))
                    : _positionSettings.FollowOffsetDip,
            };
            _ = SavePreferenceAsync(() => _settingsManager.SaveSidePanelPositionAsync(_positionSettings), FreeSidePanelMovementToggle);
        }
        if (msg == User32.WM_HOTKEY && wParam.ToInt32() == HotkeyIdManualScan)
        {
            ManualScanRequested?.Invoke();
            handled = true;
        }
        if (msg == User32.WM_HOTKEY && wParam.ToInt32() == HotkeyIdTogglePanel)
        {
            ToggleCollapsed();
            handled = true;
        }
        return IntPtr.Zero;
    }

    /// <summary>对局信息 header: banned covenants, banned operators' portraits and the unusable count.</summary>
    private void UpdateBanSummary()
    {
        var byName = _allOperators.GroupBy(o => o.Name).ToDictionary(g => g.Key, g => g.First());
        var banned = BanVm.BannedNames.OrderBy(n => n, StringComparer.Ordinal)
            .Select(n => byName.TryGetValue(n, out var op) ? op : new Operator { Name = n }).ToList();
        RunBanView.SetBanSummary([.. BanVm.BannedCovenants], BanVm.BannedNames.Count, banned);
    }

    public void UpdateManualScanStatus(int pending)
        => RunBanView.UpdateManualScanStatus(pending);

    /// <summary>Settings card: source in the title, live status below, icon tinted by state (PC grey, ADB green / red).</summary>
    public void UpdateGameConnectionStatus(bool isAdb, string status)
    {
        var failed = isAdb && (status.Contains("重试") || status.Contains("失败") || status.Contains("横屏"));
        var connected = isAdb && status.StartsWith("ADB 已连接");
        GameConnectionTitle.Text = isAdb ? "截图来源 · MuMu / ADB" : "截图来源 · PC 客户端";
        GameConnectionStatus.Text = status;
        GameConnectionStatus.ToolTip = status;
        GameConnectionGlyph.Text = !isAdb ? "PC" : failed ? "!" : connected ? "✓" : "…";
        var (fill, text) = !isAdb ? ("SectionBackgroundBrush", "TextSecondaryBrush")
            : failed ? ("DangerTintBrush", "DangerBrush")
            : connected ? ("SuccessTintBrush", "SuccessTextBrush") : ("AccentTintBrush", "AccentBrush");
        GameConnectionIcon.SetResourceReference(Border.BackgroundProperty, fill);
        GameConnectionGlyph.SetResourceReference(TextBlock.ForegroundProperty, text);
        GameConnectionStatus.SetResourceReference(TextBlock.ForegroundProperty, failed ? "DangerBrush" : connected ? "SuccessTextBrush" : "TextSecondaryBrush");
        GameConnectionButton.Content = failed ? "重新设置…" : "更改…";
    }

    private void OnGameConnection(object sender, RoutedEventArgs e) => GameConnectionRequested?.Invoke();

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
            RefreshUpdateUi();
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
            UpdateStatusText.Text = $"正在下载新版本 {info.TagName}…";
            _downloadTask = RunBackgroundDownloadAsync(info);
            // First sight of a new version: open the bubble once so it is noticed.
            ShowUpdatePopover();
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
        _lastUpdateProgress = 0;
        _downloadError = null;
        RefreshUpdateUi();
        _openUpdateDialog?.ReportProgress(0);
        var progress = new Progress<double>(value =>
        {
            Dispatcher.Invoke(() =>
            {
                if (_downloadError is not null) return;
                _lastUpdateProgress = value;
                RefreshUpdateUi();
                _openUpdateDialog?.ReportProgress(value);
            });
        });

        try
        {
            await _updateService.DownloadAndApplyUpdateAsync(info, progress);

            _isDownloadReady = true;
            Dispatcher.Invoke(() =>
            {
                RefreshUpdateUi();
                UpdateStatusText.Text = $"新版本 {info.TagName} 已就绪，点标题栏的“重启更新”完成更新（会自动关闭并重新打开）";
                _openUpdateDialog?.ReportProgress(1.0);
            });
        }
        catch (Exception ex)
        {
            var reason = UpdateService.DescribeError(ex);
            _downloadError = reason;
            AppLogger.Warn("Update", $"Download or apply failed: {reason}");
            Dispatcher.Invoke(() =>
            {
                RefreshUpdateUi();
                UpdateStatusText.Text = $"下载失败：{reason}，点标题栏的更新图标重试";
                _openUpdateDialog?.ReportFailure(reason);
            });
            _isDownloadReady = false;
        }
    }

    private void OnBannerAction(object sender, RoutedEventArgs e)
    {
        if (_isDownloadReady)
        {
            // 先让更新脚本接管，再退出本进程；脚本会等本进程结束后替换目录并启动新版。
            try
            {
                if (!_updateService.LaunchPendingUpdateScript())
                {
                    UpdateStatusText.Text = "更新文件已失效，请点击“检查更新”重新下载";
                    _isDownloadReady = false;
                    return;
                }
            }
            catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or IOException or UnauthorizedAccessException)
            {
                AppLogger.Warn("Update", $"无法启动更新程序：{ex.Message}");
                UpdateStatusText.Text = $"无法启动更新程序：{ex.Message}。辅助未关闭，可点击“立即重启”重试。";
                return;
            }

            UpdateStatusText.Text = "正在退出以完成更新…";
            System.Windows.Application.Current.Shutdown();
            return;
        }

        if (_pendingUpdate is null) return;

        // Download failures are handled inside RunBackgroundDownloadAsync, so its
        // task completes normally even when the banner needs a retry.
        if (_downloadTask is { IsCompleted: true })
        {
            UpdateStatusText.Text = $"正在下载新版本 {_pendingUpdate.TagName}…";
            _downloadTask = RunBackgroundDownloadAsync(_pendingUpdate);
            return;
        }

        // Download in progress — open notes dialog
        UpdatePopover.Visibility = Visibility.Collapsed;
        var dialog = new UpdateDialog(_pendingUpdate)
        {
            Owner = this
        };
        dialog.ReportProgress(_lastUpdateProgress);
        _openUpdateDialog = dialog;
        try
        {
            dialog.ShowDialog();
        }
        finally
        {
            _openUpdateDialog = null;
        }
    }

    private async void OnSkipVersion(object sender, RoutedEventArgs e)
    {
        if (_pendingUpdate is null) return;
        if (_settingsManager is not null)
            await _settingsManager.SaveSkippedVersionAsync(_pendingUpdate.TagName);
        _updateSkipped = true;
        RefreshUpdateUi();
    }

    // ---- Update chip + bubble (title bar) ----

    private bool _updateSkipped;

    /// <summary>Title-bar chip and bubble from the update state: downloading (ring + %), ready (重启更新), failed (重试).</summary>
    private void RefreshUpdateUi()
    {
        var info = _pendingUpdate;
        if (info is null || _updateSkipped)
        {
            UpdateChip.Visibility = Visibility.Collapsed;
            UpdatePopover.Visibility = Visibility.Collapsed;
            return;
        }
        UpdateChip.Visibility = Visibility.Visible;
        var current = _updateService.CurrentVersion;
        UpdatePopTitle.Text = $"发现新版本 {info.TagName}";
        var percent = (int)Math.Round(_lastUpdateProgress * 100);
        if (_isDownloadReady)
        {
            SetChip(solid: true, ring: false, "重启更新");
            UpdateChip.ToolTip = $"新版本 {info.TagName} 已下载，点一下重启完成更新";
            UpdatePopMeta.Text = $"当前 v{current.ToString(3)} · 已下载，重启后完成更新";
            UpdatePopPrimary.Content = "重启更新";
        }
        else if (_downloadError is { } error)
        {
            SetChip(solid: false, ring: false, "更新失败");
            UpdateChip.ToolTip = $"下载失败：{error}";
            UpdatePopMeta.Text = $"下载失败：{error}";
            UpdatePopPrimary.Content = "重试";
        }
        else
        {
            SetChip(solid: false, ring: true, $"{percent}%");
            UpdateChip.ToolTip = $"正在下载新版本 {info.TagName}";
            UpdatePopMeta.Text = $"当前 v{current.ToString(3)} · 正在后台下载 {percent}%";
            UpdatePopPrimary.Content = "更新说明";
            // Ring: dash length in stroke-thickness units (circumference of the 14px ring, 2px stroke).
            var units = Math.PI * 12 / 2;
            UpdateRingArc.StrokeDashArray = [Math.Max(0.01, units * _lastUpdateProgress), 100];
        }
    }

    private void SetChip(bool solid, bool ring, string text)
    {
        UpdateChipText.Text = text;
        UpdateRing.Visibility = ring ? Visibility.Visible : Visibility.Collapsed;
        UpdateArrow.Visibility = ring ? Visibility.Collapsed : Visibility.Visible;
        UpdateChip.SetResourceReference(BackgroundProperty, solid ? "AccentSolidBrush" : "TitleChipBrush");
        UpdateChip.SetResourceReference(BorderBrushProperty, solid ? "AccentSolidBrush" : "TitleChipBorderBrush");
        UpdateChip.SetResourceReference(ForegroundProperty, solid ? "OnAccentBrush" : _downloadError is null ? "TitleTextBrush" : "WarningBrush");
        UpdateChipText.FontFamily = ring ? (System.Windows.Media.FontFamily)FindResource("NumberFontFamily") : FontFamily;
    }

    private void OnUpdateChipClick(object sender, RoutedEventArgs e)
    {
        if (_isDownloadReady) { OnBannerAction(sender, e); return; }
        if (UpdatePopover.Visibility == Visibility.Visible) UpdatePopover.Visibility = Visibility.Collapsed;
        else ShowUpdatePopover();
    }

    private void ShowUpdatePopover()
    {
        RefreshUpdateUi();
        if (UpdateChip.Visibility != Visibility.Visible) return;
        FullscreenPopover.Visibility = Visibility.Collapsed;
        UpdatePopover.Visibility = Visibility.Visible;
        AimArrow(UpdatePopArrow, UpdateChip);
    }

    /// <summary>Points a bubble's arrow at the title-bar button above it (the bubble sits 8px from the right edge).</summary>
    private void AimArrow(FrameworkElement arrow, FrameworkElement target)
    {
        void Aim()
        {
            if (!target.IsVisible || ContentRoot.ActualWidth <= 0) return;
            var center = target.TranslatePoint(new Point(target.ActualWidth / 2, 0), ContentRoot).X;
            var right = ContentRoot.ActualWidth - center - 8 - arrow.Width / 2;
            arrow.Margin = new Thickness(0, -5, Math.Max(6, right), 0);
        }
        Aim();
        Dispatcher.BeginInvoke(Aim, System.Windows.Threading.DispatcherPriority.Loaded);
    }

    /// <summary>A click anywhere outside a bubble (and its button) closes it.</summary>
    private void ClosePopoversOnOutsideClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is not DependencyObject source) return;
        foreach (var (pop, button) in new (FrameworkElement, FrameworkElement)[] { (UpdatePopover, UpdateChip), (FullscreenPopover, FullscreenHintButton) })
            if (pop.IsVisible && !IsInside(source, pop) && !IsInside(source, button))
                pop.Visibility = Visibility.Collapsed;
    }

    private static bool IsInside(DependencyObject source, DependencyObject container)
    {
        for (var d = source; d is not null; d = d is System.Windows.Media.Visual or System.Windows.Media.Media3D.Visual3D ? System.Windows.Media.VisualTreeHelper.GetParent(d) : LogicalTreeHelper.GetParent(d))
            if (ReferenceEquals(d, container)) return true;
        return false;
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

    public void ClearGamePosition() => _lastGameRect = null;

    public void UpdatePosition(RECT gameRect, bool isFullscreen)
    {
        _lastGameRect = gameRect;
        if (WindowState != WindowState.Normal || _isUserMovingOrSizing) return;
        _isProgrammaticChange = true;
        try
        {
            if (isFullscreen != _wasFullscreen)
            {
                if (!_positionSettings.FreeMovement && _positionSettings.Placement is null)
                    _userHeightDip = null;
                _wasFullscreen    = isFullscreen;
                UpdateFullscreenBanner(isFullscreen);
            }

            var hwnd = new WindowInteropHelper(this).EnsureHandle();
            var current = User32.ReadWindowRect(hwnd);
            if (_positionSettings.FreeMovement && _positionSettings.Placement is not null)
            {
                var work = User32.GetMonitorGeometry(current.Left + current.Width / 2, current.Top + current.Height / 2).WorkArea;
                User32.SetWindowBounds(hwnd, WindowGeometry.ClampBounds(current, work));
                return;
            }
            // Free movement without a saved place yet (first start): dock beside the game once, then remember it.
            var firstFreePlacement = _positionSettings.FreeMovement;
            var offset = _positionSettings.FollowOffsetDip;
            var scale = User32.GetWindowDpiScale(hwnd);
            var monitor = offset is null
                ? User32.GetMonitorGeometry(gameRect.Right - 1, gameRect.Top + gameRect.Height / 2)
                : User32.GetMonitorGeometry(
                    (int)Math.Clamp(gameRect.Right + offset.X * scale + current.Width / 2, int.MinValue, int.MaxValue),
                    (int)Math.Clamp(gameRect.Top + offset.Y * scale + current.Height / 2, int.MinValue, int.MaxValue));

            // 先将原生窗口移到目标屏，WPF 处理 DPI 变化后再读取该窗口的真实缩放。
            // 不对虚拟桌面的绝对坐标除以缩放率，避免不同 DPI 和负坐标屏幕错位。
            var centerX = current.Left + current.Width / 2;
            var centerY = current.Top + current.Height / 2;
            if (centerX < monitor.Bounds.Left || centerX >= monitor.Bounds.Right
                || centerY < monitor.Bounds.Top || centerY >= monitor.Bounds.Bottom)
            {
                var left = monitor.Bounds.Left + (monitor.Bounds.Width - current.Width) / 2;
                var top = monitor.Bounds.Top + (monitor.Bounds.Height - current.Height) / 2;
                User32.SetWindowBounds(hwnd, new RECT
                {
                    Left = left, Top = top, Right = left + current.Width, Bottom = top + current.Height,
                });
            }

            scale = User32.GetWindowDpiScale(hwnd);
            var target = offset is null
                ? WindowGeometry.SidePanelBounds(gameRect, monitor.WorkArea, scale, _preferredWidthDip, _userHeightDip, isFullscreen)
                : WindowGeometry.FollowBounds(gameRect, monitor.WorkArea, scale, _preferredWidthDip, _userHeightDip, isFullscreen, offset);
            User32.SetWindowBounds(hwnd, target);
            if (firstFreePlacement)
            {
                _positionSettings = _positionSettings with { Placement = CaptureSidePanelPlacement() };
                _ = SavePositionQuietlyAsync();
            }
        }
        finally
        {
            _isProgrammaticChange = false;
        }
    }

    private async Task SavePositionQuietlyAsync()
    {
        try { await _settingsManager.SaveSidePanelPositionAsync(_positionSettings); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            AppLogger.Warn("Settings", $"Could not save side panel position: {ex.Message}");
        }
    }

    /// <summary>Fullscreen game: an amber hint in the title bar; its bubble opens by itself once per session.</summary>
    private void UpdateFullscreenBanner(bool isFullscreen)
    {
        var show = isFullscreen && !_fullscreenHintOff;
        FullscreenHintButton.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
        if (!show) { FullscreenPopover.Visibility = Visibility.Collapsed; return; }
        if (!_fullscreenBannerDismissed && UpdatePopover.Visibility != Visibility.Visible)
        {
            FullscreenPopover.Visibility = Visibility.Visible;
            AimArrow(FullscreenPopArrow, FullscreenHintButton);
        }
    }

    private void OnFullscreenHintClick(object sender, RoutedEventArgs e)
    {
        if (FullscreenPopover.Visibility == Visibility.Visible) { FullscreenPopover.Visibility = Visibility.Collapsed; return; }
        UpdatePopover.Visibility = Visibility.Collapsed;
        FullscreenPopover.Visibility = Visibility.Visible;
        AimArrow(FullscreenPopArrow, FullscreenHintButton);
    }

    private void OnFullscreenHintOk(object sender, RoutedEventArgs e)
    {
        _fullscreenBannerDismissed = true;
        FullscreenPopover.Visibility = Visibility.Collapsed;
    }

    private async void OnFullscreenHintOff(object sender, RoutedEventArgs e)
    {
        _fullscreenBannerDismissed = _fullscreenHintOff = true;
        FullscreenPopover.Visibility = Visibility.Collapsed;
        FullscreenHintButton.Visibility = Visibility.Collapsed;
        try { await _settingsManager.SaveFullscreenHintOffAsync(true); }
        catch (Exception ex) { AppLogger.Warn("Settings", $"Saving the fullscreen hint setting failed: {ex.Message}"); }
    }

    private void OnSizeChanged(object sender, SizeChangedEventArgs e)
    {
        if (!_isUserMovingOrSizing || _isProgrammaticChange || _isRestoring) return;
        if (e.HeightChanged) _userHeightDip = e.NewSize.Height;
        if (e.WidthChanged) _preferredWidthDip = e.NewSize.Width;
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

    private void OnTitleBarSizeChanged(object sender, SizeChangedEventArgs e) => FitBrandTitle();

    /// <summary>
    /// 「鸿雪·卫戍助手」 is much wider than the old "ASP": with the update chip or the fullscreen hint showing,
    /// a narrow panel cannot fit it next to the terminal chip, so the name is hidden (logo and chip stay).
    /// </summary>
    private void FitBrandTitle()
    {
        if (TitleBarDock.ActualWidth <= 0) return;
        BrandTitleText.Visibility = Visibility.Visible;
        TitleBarBrand.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        TitleBarActions.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        bool fits = TitleBarBrand.DesiredSize.Width + TitleBarActions.DesiredSize.Width <= TitleBarDock.ActualWidth;
        BrandTitleText.Visibility = fits ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnTitleBarDrag(object sender, MouseButtonEventArgs e)
    {
        if (_positionSettings.FreeMovement && e.LeftButton == MouseButtonState.Pressed)
            DragMove();
    }

    private void OnMinimize(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    /// <summary>收起 / 展开 hotkey: minimize, or bring the panel back without taking focus from the game.</summary>
    public void ToggleCollapsed()
    {
        if (WindowState == WindowState.Minimized)
            User32.ShowWindow(new WindowInteropHelper(this).Handle, User32.SW_SHOWNOACTIVATE);
        else
            WindowState = WindowState.Minimized;
    }

    // ---- Hotkeys (设置 · 快捷键) ----

    /// <summary>(Re)registers both hotkeys; a combination another program holds is reported on its row.</summary>
    private void RegisterHotkeys()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        User32.UnregisterHotKey(hwnd, HotkeyIdManualScan);
        User32.UnregisterHotKey(hwnd, HotkeyIdTogglePanel);
        var scanOk = Register(hwnd, HotkeyIdManualScan, _hotkeys.ManualScan);
        var toggleOk = Register(hwnd, HotkeyIdTogglePanel, _hotkeys.TogglePanel);
        ShowHotkey(TogglePanelHotkeyButton, TogglePanelHotkeyText, _hotkeys.TogglePanel, toggleOk, "最小化侧栏，再按一次恢复（不会抢走游戏焦点）");
        ShowHotkey(ManualScanHotkeyButton, ManualScanHotkeyText, _hotkeys.ManualScan, scanOk, "打开游戏内的本局信息页后按下，录入 Ban 位、盟约和敌人");
        RunBanView.SetScanHotkey(_hotkeys.ManualScan.IsNone ? null : HotkeyText.Describe(_hotkeys.ManualScan));
    }

    private static bool Register(IntPtr hwnd, int id, Hotkey key)
        => key.IsNone || User32.RegisterHotKey(hwnd, id, key.Modifiers | User32.MOD_NOREPEAT, key.Key);

    private static void ShowHotkey(Button button, TextBlock status, Hotkey key, bool ok, string hint)
    {
        button.Content = HotkeyText.Describe(key);
        status.Text = ok ? hint : $"{HotkeyText.Describe(key)} 已被其他程序占用，点右侧换一个组合";
        status.SetResourceReference(TextBlock.ForegroundProperty, ok ? "TextSecondaryBrush" : "DangerBrush");
    }

    private void OnHotkeyButtonClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button) return;
        if (_capturingHotkey is not null) EndHotkeyCapture();
        _capturingHotkey = button;
        // Let the current combinations reach the box instead of firing.
        var hwnd = new WindowInteropHelper(this).Handle;
        User32.UnregisterHotKey(hwnd, HotkeyIdManualScan);
        User32.UnregisterHotKey(hwnd, HotkeyIdTogglePanel);
        button.Content = "按下组合键…";
        button.Tag = "accent";
        // The side panel normally sits beside the game without focus; it needs the keyboard while recording.
        Activate();
        button.Focus();
    }

    private void OnHotkeyButtonKeyDown(object sender, KeyEventArgs e)
    {
        if (_capturingHotkey is null || !ReferenceEquals(sender, _capturingHotkey)) return;
        e.Handled = true;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        var modifiers = HotkeyText.Modifiers(Keyboard.Modifiers);
        if (key == Key.Escape && modifiers == 0) { EndHotkeyCapture(); return; }
        if (key is Key.Back or Key.Delete && modifiers == 0) { ApplyHotkey(Hotkey.None); return; }
        if (HotkeyText.IsModifierKey(key))
        {
            _capturingHotkey.Content = modifiers == 0 ? "按下组合键…" : HotkeyText.DescribeModifiers(modifiers) + " + …";
            return;
        }
        if (modifiers == 0 && !HotkeyText.IsFunctionKey(key))
        {
            ShowToast("需要搭配 Ctrl / Alt / Shift / Win，或者用 F1–F12");
            return;
        }
        ApplyHotkey(new Hotkey(modifiers, (uint)KeyInterop.VirtualKeyFromKey(key)));
    }

    private void OnHotkeyButtonLostFocus(object sender, RoutedEventArgs e)
    {
        if (ReferenceEquals(sender, _capturingHotkey)) EndHotkeyCapture();
    }

    private void ApplyHotkey(Hotkey key)
    {
        var toggle = ReferenceEquals(_capturingHotkey, TogglePanelHotkeyButton);
        var other = toggle ? _hotkeys.ManualScan : _hotkeys.TogglePanel;
        if (!key.IsNone && key == other)
        {
            ShowToast($"{HotkeyText.Describe(key)} 已用于{(toggle ? "截屏录入" : "收起 / 展开侧栏")}");
            return;
        }
        _hotkeys = toggle ? _hotkeys with { TogglePanel = key } : _hotkeys with { ManualScan = key };
        EndHotkeyCapture();
        _ = SaveHotkeysAsync();
    }

    private void EndHotkeyCapture()
    {
        if (_capturingHotkey is { } button) button.Tag = null;
        _capturingHotkey = null;
        RegisterHotkeys();
    }

    private void OnResetHotkeys(object sender, RoutedEventArgs e)
    {
        _capturingHotkey = null;
        _hotkeys = new HotkeySettings();
        RegisterHotkeys();
        _ = SaveHotkeysAsync();
    }

    private async Task SaveHotkeysAsync()
    {
        try { await _settingsManager.SaveHotkeysAsync(_hotkeys); }
        catch (Exception ex)
        {
            AppLogger.Warn("Settings", $"Saving hotkeys failed: {ex.Message}");
            ShowToast("快捷键保存失败");
        }
    }

    private void OnToggleSettings(object sender, RoutedEventArgs e)
    {
        var open = SettingsPanel.Visibility != Visibility.Visible || SettingsPanel.Tag as string == "closing";
        HighlightSettingsButton(open);
        if (open)
        {
            SettingsPanel.Tag = null;
            SettingsPanel.Visibility = Visibility.Visible;
            PageTransition.SlideIn(SettingsPanel, fromX: 18);
        }
        else
        {
            SettingsPanel.Tag = "closing";
            PageTransition.SlideOut(SettingsPanel, toX: 18, () =>
            {
                if (SettingsPanel.Tag as string != "closing") return;
                SettingsPanel.Visibility = Visibility.Collapsed;
                SettingsPanel.Tag = null;
            });
        }
    }

    /// <summary>Switching tabs fades the new page in with a short upward slide.</summary>
    private void OnMainTabChanged(object sender, SelectionChangedEventArgs e)
    {
        if (!ReferenceEquals(e.OriginalSource, MainTabs) || !IsLoaded) return;
        if (MainTabs.SelectedItem is TabItem { Content: FrameworkElement page }) PageTransition.SlideIn(page, fromY: 8);
    }

    /// <summary>The gear stays highlighted while the settings page is open.</summary>
    private void HighlightSettingsButton(bool open)
    {
        if (open)
        {
            SettingsButton.SetResourceReference(BackgroundProperty, "TitleChipBrush");
            SettingsButton.SetResourceReference(ForegroundProperty, "TitleDecorBrush");
        }
        else
        {
            SettingsButton.ClearValue(BackgroundProperty);
            SettingsButton.ClearValue(ForegroundProperty);
        }
    }

    private void UpdateStatusChip()
    {
        var level = TrackingVm.TerminalLevel;
        // "4?" = level saved by the last session, not yet seen on the shop's upgrade card.
        StatusTerminalText.Text = level <= 0 ? "–"
            : TrackingVm.IsTerminalLevelRestored && TrackingVm.ManualTerminalLevel is null ? $"{level}?" : level.ToString();
        StatusTerminalText.ToolTip = TrackingVm.TerminalLevelText;
    }

    // ── Appearance: 3 mode themes × day / night ────────────────────────────

    private SidebarAppearance _appearance;
    private bool _syncingAppearance;

    private GameShopTheme CurrentUiTheme => _appearance.ThemeFor(GameTheme);

    private void SyncDayNightSelector()
    {
        _syncingAppearance = true;
        DayNightSelector.SelectedIndex = _appearance.IsNight(CurrentUiTheme) ? 1 : 0;
        _syncingAppearance = false;
    }

    private void ApplyAppearance()
    {
        ThemeManager.Apply(CurrentUiTheme, _appearance.IsNight(CurrentUiTheme));
        SyncDayNightSelector();
    }

    private Task SaveAppearanceAsync(UIElement control)
        => SavePreferenceAsync(() => _settingsManager.SaveSidebarAppearanceAsync(_appearance), control);

    private async void OnDayNightChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_syncingAppearance || DayNightSelector.SelectedIndex < 0) return;
        _appearance.Night[CurrentUiTheme] = DayNightSelector.SelectedIndex == 1;
        ApplyAppearance();
        await SaveAppearanceAsync(DayNightSelector);
    }

    private async void OnFollowModeThemeChanged(object sender, RoutedEventArgs e)
    {
        _appearance.FollowModeTheme = FollowModeThemeToggle.IsChecked == true;
        UiThemeSelector.Visibility = _appearance.FollowModeTheme ? Visibility.Collapsed : Visibility.Visible;
        ApplyAppearance();
        await SaveAppearanceAsync(FollowModeThemeToggle);
    }

    private SidePanelPlacement CaptureSidePanelPlacement()
    {
        var hwnd = new WindowInteropHelper(this).EnsureHandle();
        var rect = User32.ReadWindowRect(hwnd);
        return new SidePanelPlacement(rect.Left, rect.Top, _preferredWidthDip, rect.Height / User32.GetWindowDpiScale(hwnd));
    }

    private async void OnFreeSidePanelMovementChanged(object sender, RoutedEventArgs e)
    {
        var enabled = FreeSidePanelMovementToggle.IsChecked == true;
        if (enabled == _positionSettings.FreeMovement) return; // 重置按钮同步复选框时不重新记录偏移。
        var placement = CaptureSidePanelPlacement();
        _userHeightDip = placement.HeightDip;
        _positionSettings = _positionSettings with
        {
            FreeMovement = enabled,
            Placement = placement,
            FollowOffsetDip = !enabled && _lastGameRect is { } game
                ? WindowGeometry.CaptureFollowOffset(game, placement, User32.GetWindowDpiScale(new WindowInteropHelper(this).Handle))
                : null,
        };
        await SavePreferenceAsync(() => _settingsManager.SaveSidePanelPositionAsync(_positionSettings), FreeSidePanelMovementToggle);
    }

    private async void OnResetSidePanelPosition(object sender, RoutedEventArgs e)
    {
        if (_lastGameRect is not { } game)
        {
            ShowToast("未找到明日方舟窗口，暂时无法重置侧边栏位置");
            return;
        }
        _userHeightDip = CaptureSidePanelPlacement().HeightDip;
        _positionSettings = _positionSettings with { FreeMovement = false, FollowOffsetDip = null };
        FreeSidePanelMovementToggle.IsChecked = false;
        UpdatePosition(game, _wasFullscreen);
        _positionSettings = _positionSettings with { Placement = CaptureSidePanelPlacement() };
        await SavePreferenceAsync(() => _settingsManager.SaveSidePanelPositionAsync(_positionSettings), FreeSidePanelMovementToggle);
    }

    private async void OnUiThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (UiThemeSelector.SelectedIndex < 0) return;
        _appearance.Theme = (GameShopTheme)UiThemeSelector.SelectedIndex;
        if (_appearance.FollowModeTheme) return;
        ApplyAppearance();
        await SaveAppearanceAsync(UiThemeSelector);
    }

    private async void OnGameThemeChanged(object sender, SelectionChangedEventArgs e)
    {
        if (GameThemeSelector.SelectedIndex < 0) return;
        GameTheme = (GameShopTheme)GameThemeSelector.SelectedIndex;
        GameThemeChanged?.Invoke(GameTheme);
        if (_appearance.FollowModeTheme) ApplyAppearance();
        await SavePreferenceAsync(() => _settingsManager.SaveGameShopThemeAsync(GameTheme), GameThemePanel);
    }

    public void InitAutomaticGameTheme(bool automatic)
    {
        AutomaticThemeToggle.IsChecked = automatic;
        AutomaticThemeToggle.Checked += OnAutomaticGameThemeChanged;
        AutomaticThemeToggle.Unchecked += OnAutomaticGameThemeChanged;
    }

    private async void OnAutomaticGameThemeChanged(object sender, RoutedEventArgs e)
    {
        AutomaticGameThemeChanged?.Invoke(AutomaticGameTheme);
        await SavePreferenceAsync(() => _settingsManager.SaveAutomaticGameThemeAsync(AutomaticGameTheme), AutomaticThemeToggle);
    }

    /// <summary>Follows the theme recognised in the shop while automatic detection is on.</summary>
    public void ApplyDetectedGameTheme(GameShopTheme theme)
    {
        if (!AutomaticGameTheme) return;
        GameThemeSelector.SelectedIndex = (int)theme;
    }

    /// <summary>Raised when the user turns the in-game board overlay on or off.</summary>
    public event Action<bool>? BoardOverlayToggled;

    public void InitBoardOverlay(bool enabled)
    {
        BoardOverlayToggle.IsChecked = enabled;
        BoardOverlayToggle.Checked += OnBoardOverlayChanged;
        BoardOverlayToggle.Unchecked += OnBoardOverlayChanged;
    }

    /// <summary>Raised while the user drags the in-game drawing's opacity (0.1–1).</summary>
    public event Action<double>? BoardOpacityChanged;
    private CancellationTokenSource? _opacitySave;

    public void InitBoardOpacity(double opacity)
    {
        BoardOpacitySlider.Value = Math.Round(Math.Clamp(opacity, 0.1, 1.0) * 100);
        BoardOpacityText.Text = $"{BoardOpacitySlider.Value:0}%";
        BoardOpacitySlider.ValueChanged += OnBoardOpacityChanged;
    }

    private async void OnBoardOpacityChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        double opacity = e.NewValue / 100;
        BoardOpacityText.Text = $"{e.NewValue:0}%";
        BoardOpacityChanged?.Invoke(opacity);
        // Saved once the slider rests for a moment, not on every step of a drag.
        _opacitySave?.Cancel();
        var cts = _opacitySave = new CancellationTokenSource();
        try { await Task.Delay(400, cts.Token); }
        catch (TaskCanceledException) { return; }
        await SavePreferenceAsync(() => _settingsManager.SaveBoardOverlayOpacityAsync(opacity), BoardOpacitySlider);
    }

    private (string? Map, bool ShopOpen, string? Routes) _boardStatus;

    /// <summary>Shows what the board recognizer currently sees under the route toggle.</summary>
    public void SetBoardStatus(string? mapName, bool shopOpen)
    {
        _boardStatus = (mapName, shopOpen, _boardStatus.Routes);
        ShowBoardStatus();
    }

    /// <summary>This round's routes as drawn (第 5 回合 · 地面 14 · 飞行 18), or why none are drawn; null = nothing to say.</summary>
    public void SetRouteStatus(string? text)
    {
        _boardStatus = _boardStatus with { Routes = text };
        ShowBoardStatus();
    }

    private void ShowBoardStatus()
    {
        var (map, open, routes) = _boardStatus;
        BoardStatusText.Text = map is null ? "休整期在棋盘上画出本回合敌人怎么走"
            : $"{map} · 商店{(open ? "展开" : "收起")}" + (routes is null ? "" : $" · {routes}");
    }

    private async void OnBoardOverlayChanged(object sender, RoutedEventArgs e)
    {
        bool enabled = BoardOverlayToggle.IsChecked == true;
        BoardOverlayToggled?.Invoke(enabled);
        await SavePreferenceAsync(() => _settingsManager.SaveShowBoardOverlayAsync(enabled), BoardOverlayToggle);
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
        if (!ConfirmDialog.Ask(this, "退出确认", "确定要关闭鸿雪·卫戍助手吗？\n关闭后识别、悬浮提示与快捷键都会停止。", "关闭"))
            return;

        User32.UnregisterHotKey(new WindowInteropHelper(this).Handle, HotkeyIdManualScan);
        User32.UnregisterHotKey(new WindowInteropHelper(this).Handle, HotkeyIdTogglePanel);
        Application.Current.Shutdown();
    }

    // ── Lineup feature wiring ──────────────────────────────────────────────

    private void WireLineupView()
    {
        LineupView.NewLineupRequested += () => OpenEditor(null);
        LineupView.EditLineupRequested += lineup => OpenEditor(lineup);
        LineupView.DeleteLineupRequested += async lineup =>
        {
            if (!ConfirmDialog.Ask(this, "删除阵容", $"确定删除阵容“{lineup.Name}”？此操作不可撤销。", "删除", danger: true))
                return;
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
                ShowToast("正在准备分享链接…");
                url = await _lineupShareService.ExportAsync(lineup);
            }
            catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException or InvalidOperationException
                or IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
            {
                AppLogger.Warn("Lineup", $"Share failed: {ex.Message}");
                ShowToast($"生成分享链接失败：{ex.Message}");
                return;
            }
            HideToast();
            new ShareLinkWindow(lineup.Name, url, Clipboard.SetText) { Owner = this }.ShowDialog();
        };

        LineupView.ImportLineupRequested += async () =>
        {
            string clipboard = "";
            try { clipboard = Clipboard.GetText() ?? ""; } catch { /* ignore clipboard race */ }

            var dlg = new InputDialog(
                "导入阵容",
                "粘贴森空岛攻略链接、dpaste.com 短链接或阵容 JSON：",
                clipboard.Trim())
            {
                Owner = this,
                ShowGuideLink = true,
            };
            if (dlg.ShowDialog() != true) return;
            var input = dlg.InputText.Trim();
            if (string.IsNullOrEmpty(input)) return;

            LineupView.IsEnabled = false;
            try
            {
                ShowToast("正在导入…");
                if (SklandGuideImporter.IsOfficialGuideUrl(input))
                {
                    // 官方攻略只生成草稿：在编辑器里核对后保存才加入我的阵容。自选干员也在 _lineupOperators 里。
                    var imported = await _lineupShareService.ImportOfficialGuideAsync(input, _lineupOperators, _allStrategies);
                    HideToast();
                    OpenEditor(imported.Lineup, imported.Notice);
                    return;
                }
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
            finally { LineupView.IsEnabled = true; }
        };

        LineupView.TrackLineupRequested += lineup =>
        {
            try
            {
                var added = TrackingVm.TrackLineup(lineup);
                var transition = TrackingVm.TrackedOperators.Count(e => e.IsTransition && !e.IsAcquired);
                ShowToast(transition > 0
                    ? $"已追踪阵容“{lineup.Name}”：新增 {added.Operators} 干员（{transition} 名过渡）/ {added.Equipment} 装备"
                    : $"已追踪阵容“{lineup.Name}”：新增 {added.Operators} 干员 / {added.Equipment} 装备");
            }
            catch (Exception ex)
            {
                ShowToast($"追踪失败：{ex.Message}");
            }
        };
        LineupView.RepairLineupRequested += rec =>
        {
            ShowLineupsSegment(planner: true);
            PlannerVm.BeginRepair(rec.Lineup, LineupVm.TargetsOf(rec.Lineup), rec.Banned);
        };
        WirePlannerView();
        UpdateLineupRunInfo();
    }

    /// <summary>本局推荐 follows the opening bans: none recorded → no recommendations.</summary>
    private void UpdateLineupRunInfo()
    {
        var bannedCovenants = BanVm.BannedCovenants;
        if (BanVm.BannedNames.Count == 0 && bannedCovenants.Count == 0)
        {
            LineupVm.SetRunInfo(null, null);
            return;
        }
        var byName = _lineupOperators.GroupBy(o => o.Name).ToDictionary(g => g.Key, g => g.First());
        bool Unavailable(string name) => BanVm.IsBanned(name)
            || (byName.TryGetValue(name, out var op) && CovenantBanRules.IsExcluded(op, bannedCovenants));
        LineupVm.SetRunInfo(Unavailable, new HashSet<string>(bannedCovenants), [.. BanVm.BannedNames.OrderBy(n => n, StringComparer.Ordinal)]);
    }

    /// <summary>Opens the 阵容 tab on 我的阵容 (with 本局推荐 in view) or on 规划.</summary>
    public void ShowLineupsSegment(bool planner)
    {
        MainTabs.SelectedItem = LineupsTab;
        LineupsSegment.SelectedIndex = planner ? 1 : 0;
        if (!planner) LineupView.ShowRecommendations();
    }

    private void OnCodexSegmentChanged(object sender, SelectionChangedEventArgs e)
    {
        e.Handled = true;
        if (OperatorView is null || EquipmentView is null) return;
        bool equipment = CodexSegment.SelectedIndex == 1;
        OperatorView.Visibility = equipment ? Visibility.Collapsed : Visibility.Visible;
        EquipmentView.Visibility = equipment ? Visibility.Visible : Visibility.Collapsed;
        if (IsLoaded) PageTransition.SlideIn(equipment ? EquipmentView : OperatorView, fromY: 8);
    }

    private void OnLineupsSegmentChanged(object sender, SelectionChangedEventArgs e)
    {
        e.Handled = true;
        if (LineupView is null || PlannerView is null) return;
        bool planner = LineupsSegment.SelectedIndex == 1;
        LineupView.Visibility = planner ? Visibility.Collapsed : Visibility.Visible;
        PlannerView.Visibility = planner ? Visibility.Visible : Visibility.Collapsed;
        if (IsLoaded) PageTransition.SlideIn(planner ? PlannerView : LineupView, fromY: 8);
    }

    private void WirePlannerView()
    {
        PlannerVm = new LineupPlannerViewModel(_lineupPlanner, _allOperators, BanVm, LineupVm.Calculator.EquipmentRules)
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
        PlannerVm.OverwriteRequested += async lineup =>
        {
            try
            {
                await LineupVm.AddOrUpdateAsync(lineup);
                LineupView.RefreshAllBadges();
                ShowToast($"已更新阵容“{lineup.Name}”");
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
        PlannerVm.TrackRouteRequested += route =>
        {
            try
            {
                var added = TrackingVm.TrackRoute(route, LineupVm.Calculator.EquipmentRules);
                ShowToast($"已追踪道中路线：新增 {added.Final} 名大成干员 / {added.Transition} 名过渡干员");
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

    private async void OpenEditor(Lineup? source, string? importNotice = null)
    {
        var calc = LineupVm.Calculator;
        var editorVm = new LineupEditorViewModel(source, _lineupOperators, _allEquipment, calc);
        var window = new LineupEditorWindow(editorVm, _allStrategies) { Owner = this };
        if (importNotice is not null) window.ShowImportNotice(importNotice);
        await MinimizableDialog.ShowAsync(window, PanelContent);
        if (window.Result is null) return;

        _ = LineupVm.AddOrUpdateAsync(window.Result).ContinueWith(_ =>
        {
            Dispatcher.Invoke(() =>
            {
                LineupView.RefreshAllBadges();
                ShowToast(source is null || importNotice is not null ? "已创建阵容" : "已保存阵容");
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
