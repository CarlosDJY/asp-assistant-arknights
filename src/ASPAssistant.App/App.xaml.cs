// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
using System.IO;
using System.Windows;
using ASPAssistant.App.Windows;
using ASPAssistant.Core;
using ASPAssistant.Core.Data;
using ASPAssistant.Core.GameModes.GarrisonProtocol;
using ASPAssistant.Core.Services;
using ASPAssistant.Core.ViewModels;

namespace ASPAssistant.App;

public partial class App : Application
{
    private WindowTrackerService? _windowTracker;
    private OcrScannerService? _ocrScanner;
    private BanScreenAnalyzer? _banScreenAnalyzer;
    private SidePanelWindow? _sidePanel;
    private OverlayWindow? _overlay;
    private SettingsManager? _settingsManager;
    private UpdateStartupNotification? _updateStartup;
    private readonly SemaphoreSlim _informationScanGate = new(1, 1);
    private List<ASPAssistant.Core.Models.ShopItem> _lastShopItems = [];

    public App()
    {
        // Catches exceptions before the WPF Dispatcher is running (e.g. DllNotFoundException
        // from MaaFramework native libs if VC++ Runtime is missing, or single-file extraction failures).
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            var ex = args.ExceptionObject as Exception;
            AppLogger.Error("App", "Fatal unhandled exception",
                ex ?? new Exception(args.ExceptionObject?.ToString()));
            if (_updateStartup?.IsPending == true)
            {
                _updateStartup.ReportFailure(ex?.Message ?? "程序启动失败");
                return;
            }

            var isDllMissing = ex is DllNotFoundException
                || (ex?.InnerException is DllNotFoundException)
                || args.ExceptionObject?.ToString()?.Contains("DllNotFoundException") == true;

            var message = isDllMissing
                ? "缺少必要的运行库，程序无法启动。\n\n" +
                  "请先安装 Microsoft Visual C++ Redistributable（x64），然后重新运行：\n" +
                  "https://aka.ms/vs/17/release/vc_redist.x64.exe\n\n" +
                  $"技术详情：\n{ex?.GetType().Name}: {ex?.Message}"
                : $"程序启动时发生严重错误，请将以下信息反馈给开发者：\n\n{args.ExceptionObject}";

            MessageBox.Show(
                message,
                "ASPAssistant 崩溃",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        };

        // Prevents background Task exceptions from being silently swallowed.
        TaskScheduler.UnobservedTaskException += (_, args) =>
        {
            AppLogger.Warn("App", $"Unobserved task exception: {args.Exception}");
            args.SetObserved();
        };

        DispatcherUnhandledException += (_, args) =>
        {
            AppLogger.Error("App", "Unhandled UI exception", args.Exception);
            if (_updateStartup?.IsPending == true)
            {
                _updateStartup.ReportFailure(args.Exception.Message);
                args.Handled = true;
                Shutdown(1);
                return;
            }
            MessageBox.Show(
                $"启动时发生错误，请将以下信息反馈给开发者：\n\n{args.Exception}",
                "ASPAssistant 错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            args.Handled = true;
            Shutdown(1);
        };
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        ASPAssistant.App.Windows.SystemBorder.RegisterForAllWindows();
        ASPAssistant.App.Controls.PanelToolTip.RegisterForAllElements();
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        try
        {
            var startupResult = Environment.GetEnvironmentVariable(UpdateStartupNotification.ResultVariable);
            Environment.SetEnvironmentVariable(UpdateStartupNotification.ResultVariable, null);
            _updateStartup = new UpdateStartupNotification(startupResult);
            if (await StartupAsync(e))
                _updateStartup.ReportReady();
            else
                _updateStartup.ReportFailure("启动已取消");
        }
        catch (Exception ex)
        {
            AppLogger.Error("App", "Startup failed", ex);
            if (_updateStartup?.IsPending == true)
            {
                _updateStartup.ReportFailure(ex.Message);
                Shutdown(1);
                return;
            }

            // DllNotFoundException from MaaFramework almost always means the Visual C++
            // Redistributable (vcruntime140_1.dll) is not installed on the user's machine.
            var isDllMissing = ex is DllNotFoundException
                || (ex.InnerException is DllNotFoundException)
                || ex.ToString().Contains("DllNotFoundException");

            var message = isDllMissing
                ? "缺少必要的运行库，程序无法启动。\n\n" +
                  "请先安装 Microsoft Visual C++ Redistributable（x64），然后重新运行：\n" +
                  "https://aka.ms/vs/17/release/vc_redist.x64.exe\n\n" +
                  $"技术详情：\n{ex.GetType().Name}: {ex.Message}"
                : $"启动时发生错误，请将以下信息反馈给开发者：\n\n{ex}";

            MessageBox.Show(
                message,
                "ASPAssistant 错误",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private async Task<bool> StartupAsync(StartupEventArgs e)
    {
        // Paths
        var appDir = AppContext.BaseDirectory;
        var dataDir = Path.Combine(appDir, "data");
        var settingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ASPAssistant");

        // Initialize file logger as early as possible.
        AppLogger.Initialize(Path.Combine(appDir, "logs"));
        AppLogger.Info("App", $"appDir={appDir}");

        // Data layer
        var dataStore = new JsonDataStore(dataDir);
        _settingsManager = new SettingsManager(settingsDir);
        var appearance = await _settingsManager.LoadSidebarAppearanceAsync();
        ThemeManager.Apply(appearance.Theme, appearance.IsNight(appearance.Theme));
        var savedGameTheme = await _settingsManager.LoadGameShopThemeAsync();
        if (savedGameTheme is null)
        {
            var themeWindow = new ModeThemeWindow();
            if (themeWindow.ShowDialog() != true)
            {
                Shutdown();
                return false;
            }
            savedGameTheme = themeWindow.SelectedTheme;
            try
            {
                await _settingsManager.SaveGameShopThemeAsync(savedGameTheme.Value);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                AppLogger.Warn("Settings", $"Could not save initial theme: {ex.Message}");
                MessageBox.Show("主题已生效，但无法保存设置。下次启动时将再次提示选择。",
                    "模式主题设置", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }
        var gameTheme = savedGameTheme.Value;
        var uiTheme = appearance.ThemeFor(gameTheme);
        ThemeManager.Apply(uiTheme, appearance.IsNight(uiTheme));

        // Load data
        AppLogger.Info("App", "Loading operators...");
        var operators = await dataStore.LoadOperatorsAsync();
        AppLogger.Info("App", $"Loaded {operators.Count} operators");

        var (equipment, manualJobChangeEquipments) = await dataStore.LoadEquipmentAsync();
        AppLogger.Info("App", $"Loaded {equipment.Count} equipment items");


        var trackingEntries = await _settingsManager.LoadTrackingEntriesAsync();
        AppLogger.Info("App", $"Loaded {trackingEntries.Count} tracking entries");

        var (covenantInfos, jobChangeEquipmentToCovenant) = await dataStore.LoadCovenantsAsync();
        var strategies = await dataStore.LoadStrategiesAsync();
        AppLogger.Info("App",
            $"Loaded {covenantInfos.Count} covenants, "
            + $"{jobChangeEquipmentToCovenant.Count} job-change equipment mappings");

        var lineupStore = new LineupStore(Path.Combine(settingsDir, "lineups"));
        var lineups = await lineupStore.LoadAllAsync();
        AppLogger.Info("App", $"Loaded {lineups.Count} saved lineups");

        // Boss and enemy-type data — loaded from local JSON files generated by
        // fetch_spdatabase.py. Both catalogues must be ready before recognition starts.
        var bossDataService      = new BossDataService();
        var enemyTypeDataService = new EnemyTypeDataService();
        await Task.WhenAll(bossDataService.LoadAsync(dataDir), enemyTypeDataService.LoadAsync(dataDir));

        // ViewModels
        var banVm   = new BanViewModel();
        var enemyVm = new EnemyViewModel();

        var operatorVm = new OperatorBrowseViewModel();
        operatorVm.LoadOperators(operators);
        operatorVm.SetBanChecker(banVm.IsBanned);
        operatorVm.SetCovenantBanChecker(banVm.IsCovenantBanned);

        var covenantNames = operators
            .SelectMany(o => o.CoreCovenants.Concat(o.AdditionalCovenants))
            .Where(c => !string.IsNullOrEmpty(c))
            .Distinct()
            .ToList();

        var equipmentVm = new EquipmentBrowseViewModel();
        equipmentVm.LoadEquipment(equipment, covenantNames, manualJobChangeEquipments, jobChangeEquipmentToCovenant);

        var trackingVm = new TrackingViewModel
        {
            TrackMultipleLineups = await _settingsManager.LoadTrackMultipleLineupsAsync()
        };
        trackingVm.LoadEntries(trackingEntries);

        var gameStateVm = new GameStateViewModel();

        var covenantCalculator = new CovenantCalculator(operators, covenantInfos, jobChangeEquipmentToCovenant);
        var lineupVm = new LineupViewModel(lineupStore, covenantCalculator);
        lineupVm.LoadLineups(lineups);
        var lineupShareService = new LineupShareService();

        // GameState
        var gameState = new Core.GameState.GameState();

        // Game mode
        var garrisonMode = new GarrisonProtocolMode();

        // Services
        var captureService = new ScreenCaptureService();
        var ocrEngine = new MaaOcrEngine(dataDir);
        var shopReader = new ShopReader(ocrEngine,
            operators.Select(o => o.Name).Concat(equipment.Select(e => e.Name)).Distinct().ToList()) { Theme = gameTheme };
        // Covenant groups come from covenants.json (bondType), not from the operator roster:
        // a covenant with no operator in the local data set (a new season addition) must
        // still be recognised on the ban screen.
        var coreCovenantNames = covenantInfos
            .Where(c => string.Equals(c.BondType, "SEASON", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Name)
            .Where(c => !string.IsNullOrEmpty(c))
            .Distinct()
            .ToList();
        var additionalCovenantNames = covenantInfos
            .Where(c => !string.Equals(c.BondType, "SEASON", StringComparison.OrdinalIgnoreCase))
            .Select(c => c.Name)
            .Where(c => !string.IsNullOrEmpty(c))
            .Distinct()
            .ToList();
        AppLogger.Info("App",
            $"Covenant names: core={coreCovenantNames.Count} additional={additionalCovenantNames.Count}");

        var enemyRecognizer = new EnemyRecognizer(ocrEngine, garrisonMode.OcrStrategy, bossDataService);
        _ocrScanner = new OcrScannerService(
            captureService, garrisonMode.OcrStrategy, shopReader, ocrEngine, gameState,
            () => Dispatcher.Invoke(() => trackingVm.ActiveEntries.Select(e => e.Name).Distinct().ToList()),
            intervalMs: 500)
        {
            TerminalReader = new TerminalLevelReader(ocrEngine),
            ThemeProvider = () => shopReader.Theme,
        };

        _windowTracker = new WindowTrackerService();

        // Windows
        var sidePanelPosition = await _settingsManager.LoadSidePanelPositionAsync();
        var sidePanel = _sidePanel = new SidePanelWindow(
            operatorVm, equipmentVm, trackingVm, gameStateVm, banVm, enemyVm,
            lineupVm, lineupShareService,
            operators, equipment, strategies, _settingsManager, gameTheme, sidePanelPosition,
            new LineupPlanner(operators, covenantInfos), covenantInfos);
        sidePanel.GameThemeChanged += theme => shopReader.Theme = theme;
        var overlay = _overlay = new OverlayWindow()
        {
#if DEBUG
            ShowDebugCardBorders = false,
#endif
        };

        async Task<byte[]?> CaptureInformationScreenAsync()
        {
            var visibility = await Dispatcher.InvokeAsync(() =>
            {
                var game = Core.Interop.User32.FindArknightsWindow();
                var foreground = Core.Interop.User32.GetForegroundWindow();
                if (game == IntPtr.Zero || foreground != game
                    && foreground != new System.Windows.Interop.WindowInteropHelper(sidePanel).Handle)
                {
                    AppLogger.Warn("ScreenCapture", $"Manual capture rejected: game=0x{game.ToInt64():X}, foreground=0x{foreground.ToInt64():X}, panel=0x{new System.Windows.Interop.WindowInteropHelper(sidePanel).Handle.ToInt64():X}");
                    return (Allowed: false, Panel: false, Overlay: false);
                }
                var before = (Allowed: true, Panel: sidePanel.IsVisible, Overlay: overlay.IsVisible);
                sidePanel.Hide();
                overlay.Hide();
                Core.Interop.User32.SetForegroundWindow(game);
                return before;
            });
            if (!visibility.Allowed) return null;
            try
            {
                // CopyFromScreen sees desktop pixels, including our attached panel.
                // Give the compositor time to display the game after hiding it.
                await Task.Delay(150);
                return await Task.Run(captureService.CaptureScreen);
            }
            finally
            {
                await Dispatcher.InvokeAsync(() =>
                {
                    if (visibility.Panel)
                    {
                        bool showActivated = sidePanel.ShowActivated;
                        sidePanel.ShowActivated = false;
                        sidePanel.Show();
                        sidePanel.ShowActivated = showActivated;
                    }
                    if (visibility.Overlay && _windowTracker.IsGameFound && !_windowTracker.IsGameMinimized)
                        overlay.Show();
                });
            }
        }

        _banScreenAnalyzer = new BanScreenAnalyzer(
            CaptureInformationScreenAsync, ocrEngine, garrisonMode.OcrStrategy,
            enemyRecognizer, operators,
            coreCovenantNames, additionalCovenantNames);

        // Wire window tracker → window positioning
        _windowTracker.GameWindowMoved += rect =>
        {
            Dispatcher.Invoke(() =>
            {
                _sidePanel.UpdatePosition(rect, _windowTracker.IsFullscreen);

                var clientRect = Core.Interop.User32.GetClientRectScreen(
                    Core.Interop.User32.FindArknightsWindow());
                if (clientRect.HasValue)
                {
                    _overlay.UpdatePosition(clientRect.Value);
                    _overlay.UpdateShopItems(_lastShopItems);
                    _overlay.Show();
                }
            });
        };

        _windowTracker.GameWindowPolled += rect =>
        {
            Dispatcher.Invoke(() =>
            {
                _sidePanel.UpdatePosition(rect, _windowTracker.IsFullscreen);
                var clientRect = Core.Interop.User32.GetClientRectScreen(
                    Core.Interop.User32.FindArknightsWindow());
                if (clientRect.HasValue)
                    _overlay.UpdatePosition(clientRect.Value);
            });
        };

        _windowTracker.GameWindowLost += () =>
        {
            Dispatcher.Invoke(() =>
            {
                _sidePanel.ClearGamePosition();
                _overlay.Hide();
            });
        };

        _windowTracker.GameWindowMinimizedChanged += minimized =>
        {
            Dispatcher.Invoke(() =>
            {
                _sidePanel.SetGameMinimized(minimized);
                if (minimized)
                {
                    _overlay.Hide();
                    _overlay.UpdateShopItems([]);
                }
            });
        };

        // Wire OCR scanner → GameState → overlay markers
        _ocrScanner.GameStateUpdated += state =>
        {
            Dispatcher.Invoke(() =>
            {
                Core.GameState.GameStateUpdater.UpdateShopTracking(
                    state.ShopItems, trackingVm.ActiveEntries.ToList());

                _lastShopItems = state.ShopItems;

                state.CovenantCounts = Core.GameState.GameStateUpdater.ComputeCovenantCounts(
                    [.. state.FieldOperators, .. state.BenchOperators], operators);

                trackingVm.CovenantCounts = state.CovenantCounts;
                gameStateVm.UpdateFromGameState(state);

                _overlay.UpdateShopItems(state.ShopItems);
            });
        };


        // Enemy recognition result → update enemy tab (include boss + enemy-type data).
        void UpdateEnemyPage(Core.Models.EnemyResult result)
        {
            var bossInfo   = bossDataService.Lookup(result.BossName);
            var typeEntries = result.EnemyTypes
                .Select(t => enemyTypeDataService.LookupByTypeName(t.Split('·').Last()))
                .ToList();
            Dispatcher.Invoke(() => enemyVm.Update(result, bossInfo, typeEntries));
        }
        _banScreenAnalyzer.EnemyDetected += UpdateEnemyPage;

        _sidePanel.EnemyRecognitionRequested += async () =>
        {
            // Both manual enemy scans and full info analyses own this gate, so a
            // late automatic result cannot overwrite a newer manual scan.
            if (!_informationScanGate.Wait(0))
                return;
            enemyVm.BeginRecognition();
            sidePanel.UpdateManualScanStatus(1);
            _ocrScanner.SetBanScreenDetectionPaused(true);
            try
            {
                var shot = await CaptureInformationScreenAsync();
                if (shot is null)
                {
                    enemyVm.RecognitionFailed("未获取到可用画面，请将游戏置前并打开本局信息页后重试。");
                    return;
                }
                int width = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(shot.AsSpan(16, 4));
                int height = System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(shot.AsSpan(20, 4));
                var result = await enemyRecognizer.RecognizeAsync(shot, width, height);
                if (result is null)
                    enemyVm.RecognitionFailed("未找到“敌方领袖”区域，请打开本局信息页并保持右侧敌人信息可见。");
                else
                    UpdateEnemyPage(result);
            }
            catch (Exception ex)
            {
                // UI event boundary: report failures instead of letting async void crash WPF.
                AppLogger.Error("EnemyRecognition", "Manual enemy recognition failed", ex);
                enemyVm.RecognitionFailed("本次识别失败，请确认游戏画面可见后重试；详细原因已记录到日志。");
            }
            finally
            {
                enemyVm.IsRecognizing = false;
                sidePanel.UpdateManualScanStatus(0);
                _ocrScanner.SetBanScreenDetectionPaused(false);
                _informationScanGate.Release();
            }
        };

        _banScreenAnalyzer.AnalysisBusyChanged += busy =>
        {
            Dispatcher.Invoke(() =>
            {
                enemyVm.IsBanAnalysisRunning = busy;
                sidePanel.UpdateManualScanStatus(busy ? 1 : 0);
                if (busy) enemyVm.BeginRecognition();
                else if (enemyVm.IsRecognizing)
                    enemyVm.RecognitionFailed("本次未识别到敌人信息，请在本局信息页面使用“识别本局敌人”重试。");
                _ocrScanner?.SetBanScreenDetectionPaused(busy);
                _sidePanel?.SetBanProcessingMaskVisible(
                    busy,
                    "ban位识别中，请勿操作鼠标");
                if (busy) _overlay?.ShowBanInputBlockOverlay();
                else _overlay?.HideBanInputBlockOverlay();
            });
        };

        _banScreenAnalyzer.AnalysisFailed += message =>
            Dispatcher.Invoke(() =>
            {
                enemyVm.RecognitionFailed(message);
                sidePanel.ShowToast(message);
            });

        _banScreenAnalyzer.AutoBanListComputed += names =>
        {
            Dispatcher.Invoke(() => banVm.SetBans(names));
        };

        _banScreenAnalyzer.AnalysisCompleted += result =>
        {
            _ocrScanner.MarkInformationRecorded();
            Dispatcher.Invoke(() =>
            {
                banVm.SetCovenantBans(result.CoreCovenants.Concat(result.AdditionalCovenants).Where(c => c.IsBanned).Select(c => c.Name));
                operatorVm.SetAvailableCovenants(result.CoreCovenants.Concat(result.AdditionalCovenants)
                    .Where(c => !c.IsBanned).Select(c => c.Name));
                if (result.EnemyFaction is null)
                    enemyVm.RecognitionFailed("盟约已录入，本次敌人信息未完整读出，可在敌人页重试。");
                sidePanel.ShowToast(result.EnemyFaction is null ? "盟约已录入，敌人信息可在敌人页重试" : "本局信息已录入");
            });
        };

        _banScreenAnalyzer.ShowGameOverlayMessageRequested += message =>
        {
            Dispatcher.Invoke(() =>
            {
                if (_windowTracker.IsGameFocused) _overlay?.ShowBanTimedMessageOverlay(message);
            });
        };
        _banScreenAnalyzer.HideGameOverlayMessageRequested += () =>
        {
            Dispatcher.Invoke(() => _overlay?.HideBanTimedMessageOverlay());
        };

        // Automatic entry and the manual button use the same validated recording flow.
        async Task RecordInformationAsync()
        {
            if (!_informationScanGate.Wait(0)) return;
            try
            {
                await _banScreenAnalyzer.RunAnalysisAsync();
            }
            finally
            {
                _informationScanGate.Release();
            }
        }
        _ocrScanner.BanScreenDetected += async () =>
        {
            // The opening info screen starts a new run: the previous run's terminal level no longer applies.
            _ocrScanner.ResetTerminalLevel();
            Dispatcher.Invoke(() => { trackingVm.ResetTerminalLevel(); _sidePanel.PlannerVm.CurrentTerminalLevel = 0; });
            await _settingsManager!.SaveTerminalLevelAsync(0);
            await RecordInformationAsync();
        };
        _ocrScanner.TerminalLevelChanged += level =>
        {
            Dispatcher.Invoke(() =>
            {
                trackingVm.IsTerminalLevelRestored = false;
                trackingVm.DetectedTerminalLevel = level;
            });
            _ = _settingsManager!.SaveTerminalLevelAsync(level);
        };
        // Restarted mid-run: start from the saved level until the shop's upgrade card confirms or replaces it.
        if (await _settingsManager!.LoadTerminalLevelAsync() is { } savedLevel)
        {
            _ocrScanner.RestoreTerminalLevel(savedLevel);
            trackingVm.DetectedTerminalLevel = savedLevel;
            trackingVm.IsTerminalLevelRestored = true;
        }
        trackingVm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName != nameof(TrackingViewModel.TerminalLevel)) return;
            _sidePanel.PlannerVm.CurrentTerminalLevel = trackingVm.TerminalLevel;
            RefreshOverlayTracking(trackingVm);
        };
        _sidePanel.ManualScanRequested += async () => await RecordInformationAsync();

        // Manual ban/unban from operator card button.
        _sidePanel.BanToggleRequested += (name, banned) =>
        {
            banVm.ToggleBan(name);
        };


        // Auto-save tracking changes and refresh overlay markers immediately
        trackingVm.TrackedOperators.CollectionChanged += async (_, _) =>
        {
            RefreshOverlayTracking(trackingVm);
            try { await _settingsManager.SaveTrackingEntriesAsync(trackingVm.AllEntries.ToList()); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Failed to save tracking: {ex.Message}"); }
        };
        trackingVm.TrackedEquipment.CollectionChanged += async (_, _) =>
        {
            RefreshOverlayTracking(trackingVm);
            try { await _settingsManager.SaveTrackingEntriesAsync(trackingVm.AllEntries.ToList()); }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine($"Failed to save tracking: {ex.Message}"); }
        };

        // Show windows and start services
        MainWindow = _sidePanel;
        _sidePanel.Show();
        _overlay.Show();
        ShutdownMode = ShutdownMode.OnLastWindowClose;
        _windowTracker.Start();
        _ocrScanner.Start();
        AppLogger.Info("App", "Startup complete");

        // Start background update check
        _ = _sidePanel.StartUpdateCheckAsync();
        return true;
    }

    private void RefreshOverlayTracking(TrackingViewModel trackingVm)
    {
        if (_overlay is null || _lastShopItems.Count == 0) return;
        Core.GameState.GameStateUpdater.UpdateShopTracking(
            _lastShopItems, trackingVm.ActiveEntries.ToList());
        Dispatcher.Invoke(() => _overlay.UpdateShopItems(_lastShopItems));
    }

    protected override void OnExit(ExitEventArgs e)
    {
        AppLogger.Info("App", "Shutting down");
        _ocrScanner?.Dispose();
        _windowTracker?.Dispose();
        base.OnExit(e);
        // BanScreenAnalyzer and BanIndicatorDetector hold no unmanaged resources
        // of their own beyond what MaaTasker cleans up; no explicit Dispose needed.
    }
}
