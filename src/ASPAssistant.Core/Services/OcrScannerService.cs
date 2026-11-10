// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
using System.Threading.Channels;
using System.Timers;
using ASPAssistant.Core.GameModes;
using ASPAssistant.Core.Models;
using Timer = System.Timers.Timer;

namespace ASPAssistant.Core.Services;

public class OcrScannerService : IDisposable
{
    private readonly ScreenCaptureService _captureService;
    private readonly IOcrStrategy _ocrStrategy;
    private readonly ShopReader _shopReader;
    private readonly IOcrEngine _ocrEngine;
    private readonly GameState.GameState _gameState;
    private readonly Func<IReadOnlyList<string>> _getTrackedNames;
    private readonly Func<IReadOnlyList<(string Name, IReadOnlyList<string> CoreCovenants, IReadOnlyList<string> AdditionalCovenants, IReadOnlyList<string> TemplateNames)>>? _getAllOperators;
    private readonly MaaBanIconMatcher? _iconMatcher;
    private readonly Timer _scanTimer;
    private readonly ShopStateStabilizer _stabilizer = new();
    private int _scanning; // re-entry guard (0=idle, 1=busy)

    // Accumulated ban set for manual screenshot scans.
    private readonly HashSet<string> _accumulatedBans = [];

    // Manual screenshot scan queue — button-triggered scans execute sequentially.
    private readonly Channel<byte[]> _manualScanChannel = Channel.CreateUnbounded<byte[]>();
    private int _pendingManualScans;

    // Ban screen detection state machine.
    private bool _banScreenActive;
    private readonly InformationScreenState _informationState = new();
    private readonly InformationScreenDetector _informationDetector;
    private int _banScreenAbsentTicks;
    private volatile bool _pauseBanScreenDetection;
    private const int BanLostThreshold   = 3;

    public event Action<GameState.GameState>? GameStateUpdated;

    /// <summary>
    /// Raised when the periodic scan first detects that the game is on the ban selection screen.
    /// Fires once per entry; resets after the screen is left.
    /// </summary>
    public event Action? BanScreenDetected;

    /// <summary>
    /// Raised when the ban selection screen is no longer detected.
    /// </summary>
    public event Action? BanScreenLost;

    /// <summary>
    /// Raised when manual screenshot matching finds newly banned operators.
    /// The argument is the full accumulated list of banned operator names.
    /// </summary>
    public event Action<IReadOnlyList<string>>? BansDetected;

    /// <summary>
    /// Raised when the number of pending manual screenshot scans changes.
    /// Argument is the new pending count (0 = all done).
    /// </summary>
    public event Action<int>? ManualScanQueueChanged;

    public OcrScannerService(
        ScreenCaptureService captureService,
        IOcrStrategy ocrStrategy,
        ShopReader shopReader,
        IOcrEngine ocrEngine,
        GameState.GameState gameState,
        Func<IReadOnlyList<string>> getTrackedNames,
        Func<IReadOnlyList<(string Name, IReadOnlyList<string> CoreCovenants, IReadOnlyList<string> AdditionalCovenants, IReadOnlyList<string> TemplateNames)>>? getAllOperators = null,
        MaaBanIconMatcher? iconMatcher = null,
        int intervalMs = 3000)
    {
        _captureService = captureService;
        _ocrStrategy = ocrStrategy;
        _shopReader = shopReader;
        _ocrEngine = ocrEngine;
        _informationDetector = new InformationScreenDetector(ocrEngine, ocrStrategy);
        _gameState = gameState;
        _getTrackedNames = getTrackedNames;
        _getAllOperators = getAllOperators;
        _iconMatcher = iconMatcher;
        _scanTimer = new Timer(intervalMs);
        _scanTimer.Elapsed += OnScanTick;

        // Start the background consumer for button-triggered manual scans.
        _ = Task.Run(ProcessManualScansAsync);
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

    private async void OnScanTick(object? sender, ElapsedEventArgs e)
    {
        // Skip if a previous scan is still running.
        if (Interlocked.CompareExchange(ref _scanning, 1, 0) != 0)
            return;

        try
        {
            var screenshot = _captureService.CaptureScreen();
            if (screenshot == null)
            {
                _informationState.Suspend();
                PublishShopItems([]);
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
                await DetectBanScreenAsync(screenshot, imgWidth, imgHeight);

            if (_pauseBanScreenDetection || _banScreenActive)
            {
                PublishShopItems([]);
                return;
            }

            var rawItems = await _shopReader.ReadAsync(screenshot, imgWidth, imgHeight, _getTrackedNames());
            PublishShopItems(rawItems);
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

    private void PublishShopItems(IReadOnlyList<ShopItem> items)
    {
        if (!_stabilizer.TryUpdate(items, out var stableItems))
            return;
        _gameState.ShopItems = stableItems.ToList();
        GameStateUpdated?.Invoke(_gameState);
    }

    /// <summary>
    /// Captures the current game window screenshot and enqueues it for ban-screen matching.
    /// The scan runs on a background consumer thread so multiple calls queue up sequentially.
    /// </summary>
    public void EnqueueManualScan()
    {
        var screenshot = _captureService.CaptureScreen();
        if (screenshot == null)
        {
            AppLogger.Warn("OcrScanner", "EnqueueManualScan: screenshot capture returned null");
            return;
        }
        var pending = Interlocked.Increment(ref _pendingManualScans);
        AppLogger.Info("OcrScanner", $"Manual scan enqueued (pending={pending}, png={screenshot.Length} bytes)");
        ManualScanQueueChanged?.Invoke(pending);
        _manualScanChannel.Writer.TryWrite(screenshot);
    }

    private async Task ProcessManualScansAsync()
    {
        var regions = _ocrStrategy.GetScanRegions();
        await foreach (var screenshot in _manualScanChannel.Reader.ReadAllAsync())
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                var (w, h) = GetPngDimensions(screenshot);
                AppLogger.Info("OcrScanner", $"Manual scan started (img={w}x{h})");
                await RunBanMatchCoreAsync(screenshot, regions, w, h);
                AppLogger.Info("OcrScanner", $"Manual scan completed in {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                AppLogger.Error("OcrScanner", "Manual scan failed", ex);
            }
            finally
            {
                var remaining = Interlocked.Decrement(ref _pendingManualScans);
                ManualScanQueueChanged?.Invoke(remaining);
            }
        }
    }

    /// <summary>
    /// Reads width and height from a PNG file's IHDR chunk (bytes 16–23).
    /// No dependencies beyond the raw byte array.
    /// </summary>
    private static (int Width, int Height) GetPngDimensions(byte[] png)
    {
        int w = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        int h = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
        return (w, h);
    }

    /// <summary>
    /// Core ban-screen matching: faction OCR → candidate filter → batch FeatureMatch.
    /// Accumulates hits in <see cref="_accumulatedBans"/> and fires <see cref="BansDetected"/>.
    /// </summary>
    private async Task RunBanMatchCoreAsync(
        byte[] screenshot,
        IReadOnlyList<OcrRegionDefinition> regions,
        int imgWidth,
        int imgHeight)
    {
        // ── Faction OCR: detect which covenant groups are visible in the left panel ──────────
        var allOperators = _getAllOperators!();
        var allCovenants = allOperators
            .SelectMany(o => o.CoreCovenants.Concat(o.AdditionalCovenants))
            .Where(c => !string.IsNullOrEmpty(c))
            .ToHashSet();

        var visibleFactions = new HashSet<string>();
        var factionRegion = regions.FirstOrDefault(r => r.Name == "BanFactionPanel");
        if (factionRegion != null)
        {
            int fx = (int)(factionRegion.XPercent * imgWidth);
            int fy = (int)(factionRegion.YPercent * imgHeight);
            int fw = (int)(factionRegion.WidthPercent * imgWidth);
            int fh = (int)(factionRegion.HeightPercent * imgHeight);
            var factionOcr = await _ocrEngine.RecognizeRegionAsync(screenshot, fx, fy, fw, fh);
            var ocrTexts = factionOcr.Select(r => r.Text).ToList();
            AppLogger.Info("BanMatch", $"Faction OCR ({ocrTexts.Count} results): [{string.Join(", ", ocrTexts)}]");
            foreach (var result in factionOcr)
                foreach (var covenant in allCovenants)
                    if (result.Text.Contains(covenant))
                        visibleFactions.Add(covenant);
            AppLogger.Info("BanMatch", $"Visible factions: [{string.Join(", ", visibleFactions)}]");
        }
        else
        {
            AppLogger.Warn("BanMatch", "BanFactionPanel region not found, no faction pre-filter applied");
        }

        // Build candidate list, skipping already-identified bans and filtering by visible faction.
        var buildSw = System.Diagnostics.Stopwatch.StartNew();
        var totalOperators = allOperators.Count;
        var alreadyBanned = allOperators.Count(x => _accumulatedBans.Contains(x.Name));
        var candidateSnapshot = allOperators
            .Where(x => !_accumulatedBans.Contains(x.Name))
            .Where(x => visibleFactions.Count == 0
                || x.CoreCovenants.Any(c => visibleFactions.Contains(c))
                || x.AdditionalCovenants.Any(c => visibleFactions.Contains(c)))
            .Select(x => (x.Name, x.TemplateNames))
            .ToList();
        buildSw.Stop();
        AppLogger.Info("BanMatch",
            $"Candidates: {candidateSnapshot.Count} built in {buildSw.ElapsedMilliseconds}ms " +
            $"(total={totalOperators}, already-banned={alreadyBanned}, " +
            $"templates={candidateSnapshot.Sum(c => c.TemplateNames.Count)})");

        bool anyNewMatch = false;
        var newlyDetected = new List<string>();
        var matchSw = System.Diagnostics.Stopwatch.StartNew();
        var candidateSw = System.Diagnostics.Stopwatch.StartNew();
        long minCandidateMs = long.MaxValue, maxCandidateMs = 0, sumCandidateMs = 0;
        int candidateIndex = 0;
        foreach (var candidate in candidateSnapshot)
        {
            candidateSw.Restart();
            var tuple = await _iconMatcher!.FindBestInSlotAsync(screenshot, candidate, imgWidth, imgHeight);
            var candidateMs = candidateSw.ElapsedMilliseconds;
            sumCandidateMs += candidateMs;
            if (candidateMs < minCandidateMs) minCandidateMs = candidateMs;
            if (candidateMs > maxCandidateMs) maxCandidateMs = candidateMs;
            candidateIndex++;

            // Log a progress heartbeat every 20 candidates so we can see scan is alive.
            if (candidateIndex % 20 == 0)
                AppLogger.Info("BanMatch",
                    $"Progress: {candidateIndex}/{candidateSnapshot.Count} — " +
                    $"elapsed={matchSw.ElapsedMilliseconds}ms avg={sumCandidateMs/candidateIndex}ms/op max={maxCandidateMs}ms/op");

            if (tuple == null)
                continue;

            var (name, hit, hitBox) = tuple.Value;
            if (hit && !_accumulatedBans.Contains(name))
            {
                _accumulatedBans.Add(name);
                newlyDetected.Add(name);
                anyNewMatch = true;
            }
        }
        matchSw.Stop();
        long avgMs = candidateSnapshot.Count > 0 ? sumCandidateMs / candidateSnapshot.Count : 0;
        AppLogger.Info("BanMatch",
            $"Match done: elapsed={matchSw.ElapsedMilliseconds}ms " +
            $"candidates={candidateSnapshot.Count} avg={avgMs}ms/op min={minCandidateMs}ms/op max={maxCandidateMs}ms/op — " +
            $"new={newlyDetected.Count} [{string.Join(", ", newlyDetected)}], " +
            $"accumulated={_accumulatedBans.Count} [{string.Join(", ", _accumulatedBans)}]");

        if (anyNewMatch)
            BansDetected?.Invoke(_accumulatedBans.ToList());
    }

    /// <summary>
    /// Mirrors a manual ban/unban into <see cref="_accumulatedBans"/> so subsequent scans
    /// don't re-detect (and re-ban) an operator the user has deliberately unbanned.
    /// </summary>
    public void SetManualBan(string name, bool banned)
    {
        if (banned) _accumulatedBans.Add(name);
        else _accumulatedBans.Remove(name);
        AppLogger.Info("OcrScanner", $"Manual ban: {name} -> {(banned ? "banned" : "unbanned")} (accumulated={_accumulatedBans.Count})");
    }

    /// <summary>Successful manual recording also consumes this opening screen.</summary>
    public void MarkInformationRecorded() => _informationState.MarkRecorded();

    private async Task DetectBanScreenAsync(byte[] screenshot, int width, int height)
    {
        bool opening = await _informationDetector.IsOpeningScreenAsync(screenshot, width, height);
        bool gameplay = !opening && _informationState.IsConsumed
            && await _informationDetector.IsGameplayAsync(screenshot, width, height);
        if (_pauseBanScreenDetection) return;

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
        _manualScanChannel.Writer.TryComplete();
    }
}
