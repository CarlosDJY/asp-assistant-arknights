// Modified for ASP Assistant Regulation on 2026-11-11; see NOTICE.
using System.Threading.Channels;
using System.Timers;
using ASPAssistant.Core.GameModes;
using ASPAssistant.Core.Models;
using Timer = System.Timers.Timer;

namespace ASPAssistant.Core.Services;

public class OcrScannerService : IDisposable
{
    private readonly Func<byte[]?> _captureScreen;
    private readonly Func<GameConnectionService>? _getConnection;
    private readonly IOcrStrategy _ocrStrategy;
    private readonly ShopReader _shopReader;
    private readonly IOcrEngine _ocrEngine;
    private readonly GameState.GameState _gameState;
    private readonly IReadOnlyList<string> _shopNames;
    private readonly Timer _scanTimer;
    private readonly ShopStateStabilizer _stabilizer = new();
    private int _scanning; // re-entry guard (0=idle, 1=busy)

    // Accumulated ban set for manual screenshot scans.

    // Manual screenshot scan queue — button-triggered scans execute sequentially.

    // Ban screen detection state machine.
    private bool _banScreenActive;
    private InformationScreenState _informationState = new();
    private GameConnectionService? _lastConnection;
    private readonly InformationScreenDetector _informationDetector;
    private int _banScreenAbsentTicks;
    private volatile bool _pauseBanScreenDetection;
    private const int BanLostThreshold   = 3;

    public event Action<GameState.GameState>? GameStateUpdated;

    /// <summary>Raised when the (confirmed) terminal level read from the shop's upgrade card changes.</summary>
    public event Action<int>? TerminalLevelChanged;

    /// <summary>Optional: reads the terminal level every <see cref="TerminalReadInterval"/> while the shop is visible.</summary>
    public TerminalLevelReader? TerminalReader { get; init; }

    /// <summary>Recognises the preparation board (map + camera) for the deployment overlay; optional.</summary>
    public BoardRecognizer? BoardRecognizer { get; init; }
    private readonly BoardMatchStabilizer _board = new();

    /// <summary>The recognised board changed (null: no board on screen).</summary>
    public event Action<BoardMatch?>? BoardChanged;
    public BoardMatch? CurrentBoard => _board.Current;
    public Func<GameShopTheme>? ThemeProvider { get; init; }
    public TimeSpan TerminalReadInterval { get; init; } = TimeSpan.FromSeconds(4);
    private readonly TerminalLevelTracker _terminalTracker = new();
    private DateTime _nextTerminalRead = DateTime.MinValue;

    /// <summary>New run: forget the previous run's terminal level.</summary>
    public void ResetTerminalLevel()
    {
        _terminalTracker.Reset();
        _gameState.TerminalLevel = 0;
        _nextTerminalRead = DateTime.MinValue;
    }

    /// <summary>Starts from the level the last session saved; the shop's upgrade card confirms or replaces it.</summary>
    public void RestoreTerminalLevel(int level)
    {
        _terminalTracker.Restore(level);
        _gameState.TerminalLevel = _terminalTracker.Level ?? 0;
    }

    /// <summary>
    /// Raised when the periodic scan first detects that the game is on the ban selection screen.
    /// Fires once per entry; resets after the screen is left.
    /// </summary>
    public event Action? BanScreenDetected;

    /// <summary>
    /// Raised when the ban selection screen is no longer detected.
    /// </summary>
    public event Action? BanScreenLost;

    public OcrScannerService(
        Func<byte[]?> captureScreen,
        IOcrStrategy ocrStrategy,
        ShopReader shopReader,
        IOcrEngine ocrEngine,
        GameState.GameState gameState,
        IReadOnlyList<string> shopNames,
        int intervalMs = 3000,
        Func<GameConnectionService>? getConnection = null)
    {
        _captureScreen = captureScreen;
        _getConnection = getConnection;
        _ocrStrategy = ocrStrategy;
        _shopReader = shopReader;
        _ocrEngine = ocrEngine;
        _informationDetector = new InformationScreenDetector(ocrEngine, ocrStrategy);
        _gameState = gameState;
        _shopNames = shopNames;
        _scanTimer = new Timer(intervalMs);
        _scanTimer.Elapsed += OnScanTick;
    }

    public void Start() => _scanTimer.Start();

    /// <summary>
    /// Temporarily pauses ban-screen state-machine OCR checks. This is used while
    /// app-controlled fullscreen overlays are shown, so those overlays do not
    /// accidentally trigger BanScreenLost/BanScreenDetected flapping.
    /// </summary>
    public void SetBanScreenDetectionPaused(bool paused)
    {
        _pauseBanScreenDetection = paused;
        if (paused)
        {
            // Freeze state transitions while overlays cover the game window.
            // Keep _banScreenActive as-is, but clear accumulated edge counters
            // to avoid immediate false lost/detected transitions after resume.
            _informationState.Suspend();
            _banScreenAbsentTicks = 0;
        }
        AppLogger.Info("BanScreen", $"Ban screen detection paused={paused}");
    }

    private async void OnScanTick(object? sender, ElapsedEventArgs e) => await ScanAsync();

    public Task ScanNowAsync() => Task.Run(ScanAsync);

    private async Task ScanAsync()
    {
        // Skip if a previous scan is still running.
        if (Interlocked.CompareExchange(ref _scanning, 1, 0) != 0)
            return;

        try
        {
            var connection = _getConnection?.Invoke();
            if (!ReferenceEquals(connection, _lastConnection))
            {
                _lastConnection = connection;
                _stabilizer.Reset();
                _informationState = new InformationScreenState();
                _banScreenActive = false;
                _banScreenAbsentTicks = 0;
                _gameState.ShopItems = [];
                GameStateUpdated?.Invoke(_gameState);
            }
            var screenshot = _captureScreen();
            if (!ReferenceEquals(connection, _getConnection?.Invoke())) return;
            if (screenshot == null)
            {
                _informationState.Suspend();
                if (connection?.IsAdb == true)
                    PublishShopItems([]);
                else
                    // 切换到辅助窗口时暂时无法截图，保留最后一次识别供追踪切换使用。
                    _stabilizer.Reset();
                return;
            }

            // Derive dimensions from the actual PNG bytes so that every downstream
            // coordinate (ROI, card boxes, normalization) lives in the same pixel
            // space as the image MAA will process. Using GetClientRect here was
            // wrong when ScreenCaptureService downscales the capture to MaxCaptureWidth.
            var (imgWidth, imgHeight) = GetPngDimensions(screenshot);
            if (imgWidth <= 0 || imgHeight <= 0)
                return;

            // Ban screen detection runs every tick unless explicitly paused
            // (e.g. when app-owned overlays cover the game window).
            if (!_pauseBanScreenDetection)
                await DetectBanScreenAsync(screenshot, imgWidth, imgHeight, connection);

            if (!ReferenceEquals(connection, _getConnection?.Invoke())) return;

            if (_pauseBanScreenDetection || _banScreenActive)
            {
                PublishShopItems([]);
                ObserveBoard(null);
                return;
            }

            // MuMu / ADB frames are PNG; the board reader samples raw BMP pixels.
            ObserveBoard(BoardRecognizer is { } board ? board.Recognize(ImageBytes.ToBmp(screenshot)) : null);
            var rawItems = await _shopReader.ReadAsync(screenshot, imgWidth, imgHeight, _shopNames);
            if (!ReferenceEquals(connection, _getConnection?.Invoke())) return;
            PublishShopItems(rawItems);
            await ReadTerminalLevelAsync(screenshot, imgWidth, imgHeight);
        }
        catch (Exception ex)
        {
            // Swallow unexpected errors (e.g. capture failure during window state change)
            // so the timer-driven async void method never propagates an unhandled exception.
            AppLogger.Error("OcrScanner", "Scan tick failed", ex);
        }
        finally
        {
            Interlocked.Exchange(ref _scanning, 0);
        }
    }

    private async Task ReadTerminalLevelAsync(byte[] screenshot, int width, int height)
    {
        if (TerminalReader is null || DateTime.UtcNow < _nextTerminalRead) return;
        var level = await TerminalReader.ReadAsync(screenshot, width, height, ThemeProvider?.Invoke() ?? GameShopTheme.Terminal);
        // Not visible (battle, menus): look again a little later. Confirming a change reads again soon.
        _nextTerminalRead = DateTime.UtcNow + (level is null ? TerminalReadInterval * 2 : TerminalReadInterval);
        if (level is not null && (level != _terminalTracker.Level || _terminalTracker.IsRestored))
            _nextTerminalRead = DateTime.UtcNow + TimeSpan.FromSeconds(1);
        if (_terminalTracker.Observe(level))
        {
            AppLogger.Info("Terminal", $"Terminal level {_terminalTracker.Level}");
            _gameState.TerminalLevel = _terminalTracker.Level ?? 0;
            TerminalLevelChanged?.Invoke(_terminalTracker.Level!.Value);
        }
    }

    private void ObserveBoard(BoardMatch? match)
    {
        if (BoardRecognizer is null || !_board.Observe(match)) return;
        var current = _board.Current;
        AppLogger.Info("Board", current is null ? "Board lost"
            : $"Board {current.Map.Id} ({current.Camera}) score={current.Score:F1} margin={current.Margin:F1}");
        BoardChanged?.Invoke(current);
    }

    private void PublishShopItems(IReadOnlyList<ShopItem> items)
    {
        if (!_stabilizer.TryUpdate(items, out var stableItems))
            return;
        _gameState.ShopItems = stableItems.ToList();
        GameStateUpdated?.Invoke(_gameState);
    }

    /// <summary>Width and height of a captured frame (BMP or PNG).</summary>
    private static (int Width, int Height) GetPngDimensions(byte[] image) => ImageBytes.GetDimensions(image);

    /// <summary>Successful manual recording also consumes this opening screen.</summary>
    public void MarkInformationRecorded() => _informationState.MarkRecorded();

    private async Task DetectBanScreenAsync(byte[] screenshot, int width, int height, GameConnectionService? connection)
    {
        bool opening = await _informationDetector.IsOpeningScreenAsync(screenshot, width, height);
        bool gameplay = !opening && _informationState.IsConsumed
            && await _informationDetector.IsGameplayAsync(screenshot, width, height);
        if (_pauseBanScreenDetection || !ReferenceEquals(connection, _getConnection?.Invoke())) return;

        if (opening)
        {
            _banScreenActive = true;
            _banScreenAbsentTicks = 0;
        }
        else if (_banScreenActive && ++_banScreenAbsentTicks >= BanLostThreshold)
        {
            _banScreenActive = false;
            BanScreenLost?.Invoke();
        }
        if (_informationState.Observe(opening, gameplay))
        {
            AppLogger.Info("BanScreen", "Validated opening info screen detected");
            BanScreenDetected?.Invoke();
        }
    }

    public void Dispose()
    {
        _scanTimer.Stop();
        _scanTimer.Dispose();
    }
}
