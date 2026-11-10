// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
using ASPAssistant.Core.GameModes;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

/// <summary>
/// Represents the ban state of a single covenant.
/// </summary>
public record CovenantBanState(string Name, bool IsBanned, double X);

/// <summary>
/// Result produced by one full ban-screen analysis pass.
/// </summary>
public record BanScreenResult(
    IReadOnlyList<CovenantBanState> CoreCovenants,
    IReadOnlyList<CovenantBanState> AdditionalCovenants,
    string? EnemyFaction);

/// <summary>
/// Orchestrates the ban-screen analysis: one screenshot of the "确认本局信息" screen,
/// enemy recognition plus <see cref="BanCovenantReader"/> for both covenant panels.
///
/// Both opening and in-game info pages are supported. Incomplete scans never publish
/// replacement state and never scroll the game; the user can retry from the same page.
/// </summary>
public sealed class BanScreenAnalyzer
{
    private readonly Func<Task<byte[]?>> _captureInformationScreen;
    private readonly IOcrEngine _ocr;
    private readonly IOcrStrategy _ocrStrategy;
    private readonly EnemyRecognizer _enemyRecognizer;
    private readonly InformationScreenDetector _informationDetector;
    private readonly IReadOnlyList<Operator> _operators;
    private readonly IReadOnlyList<string> _coreCovenantNames;
    private readonly IReadOnlyList<string> _additionalCovenantNames;

    /// <summary>
    /// Raised when a full analysis pass completes successfully.
    /// </summary>
    public event Action<BanScreenResult>? AnalysisCompleted;
    public event Action<string>? AnalysisFailed;

    /// <summary>
    /// Raised when enemy recognition completes (before the full analysis finishes).
    /// Fired even if recognition returns partial results.
    /// </summary>
    public event Action<EnemyResult>? EnemyDetected;

    /// <summary>
    /// Raised when ban-screen analysis starts/ends. true = busy, false = idle.
    /// </summary>
    public event Action<bool>? AnalysisBusyChanged;

    /// <summary>
    /// Raised when auto-ban calculation for the current run completes.
    /// </summary>
    public event Action<IReadOnlyList<string>>? AutoBanListComputed;

    /// <summary>
    /// Raised when a full-screen game-window announcement should be shown/hidden.
    /// </summary>
    public event Action<string>? ShowGameOverlayMessageRequested;
    public event Action? HideGameOverlayMessageRequested;

    // Re-entry guard: only one analysis can run at a time.
    private int _running;

    public BanScreenAnalyzer(
        Func<Task<byte[]?>> captureInformationScreen,
        IOcrEngine ocr,
        IOcrStrategy ocrStrategy,
        EnemyRecognizer enemyRecognizer,
        IReadOnlyList<Operator> operators,
        IReadOnlyList<string> coreCovenantNames,
        IReadOnlyList<string> additionalCovenantNames)
    {
        _captureInformationScreen = captureInformationScreen;
        _ocr = ocr;
        _ocrStrategy = ocrStrategy;
        _enemyRecognizer = enemyRecognizer;
        _informationDetector = new InformationScreenDetector(ocr, ocrStrategy);
        _operators = operators;
        _coreCovenantNames = coreCovenantNames;
        _additionalCovenantNames = additionalCovenantNames;
    }

    // ── Public entry point ────────────────────────────────────────────────────

    /// <summary>
    /// Runs the full ban-screen analysis asynchronously.
    /// Silently returns if another analysis is already in progress.
    /// </summary>
    public async Task RunAnalysisAsync()
    {
        if (Interlocked.CompareExchange(ref _running, 1, 0) != 0)
        {
            AppLogger.Warn("BanAnalyzer", "Analysis already running, skipping");
            return;
        }

        AnalysisBusyChanged?.Invoke(true);
        try
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();
            AppLogger.Info("BanAnalyzer", "Starting ban-screen analysis");
            AppConsole.WriteLine("[BanAnalyzer] 截图...");
            var shot = await _captureInformationScreen();
            if (shot == null)
            {
                AppLogger.Warn("BanAnalyzer", "Screenshot failed");
                AnalysisFailed?.Invoke("未获取到可用的游戏画面，请将游戏置前并打开本局信息页后重试。");
                return;
            }

            var (imgWidth, imgHeight) = GetPngDimensions(shot);
            AppLogger.SaveArtifact("ban-attempt", shot, ".png");
            AppLogger.Info("BanAnalyzer", $"Screenshot ready: {imgWidth}x{imgHeight}");
            AppConsole.WriteLine($"[BanAnalyzer] 截图完成 ({imgWidth}×{imgHeight})");

            if (!await _informationDetector.IsInformationScreenAsync(shot, imgWidth, imgHeight))
            {
                AnalysisFailed?.Invoke("未找到完整的核心/附加盟约区域，请打开本局信息页并保持面板顶部可见。");
                return;
            }
            var enemy = await _enemyRecognizer.RecognizeAsync(shot, imgWidth, imgHeight);
            var read = await BanCovenantReader.ReadAsync(
                _ocr, _ocrStrategy, shot, imgWidth, imgHeight,
                _coreCovenantNames, _additionalCovenantNames);
            if (read.Core.Count != _coreCovenantNames.Count || read.Additional.Count != _additionalCovenantNames.Count
                || read.UnnamedCore.Count > 0 || read.UnnamedAdditional.Count > 0)
            {
                AnalysisFailed?.Invoke($"本次信息未完整读出（核心 {read.Core.Count}、附加 {read.Additional.Count}、未命名 {read.UnnamedCore.Count + read.UnnamedAdditional.Count}），未替换现有结果；请保持页面完整可见后重试。");
                return;
            }
            var enemyFaction = enemy?.BossName;

            sw.Stop();
            var result = new BanScreenResult(read.Core, read.Additional, enemyFaction);
            var autoBans = ComputeAutoBans(result);
            LogRecognitionSummary(result, autoBans);
            AutoBanListComputed?.Invoke(autoBans);
            if (enemy is not null && enemy.BossName.Length > 0 && enemy.EnemyTypes.Count > 0)
                EnemyDetected?.Invoke(enemy);
            else
                AppLogger.Warn("BanAnalyzer", "Covenant Ban read succeeded, but enemy region was incomplete; publishing covenant result only");
            LogResults(result, sw.ElapsedMilliseconds);

            AnalysisCompleted?.Invoke(result);
        }
        catch (Exception ex)
        {
            AppLogger.Error("BanAnalyzer", "Analysis failed", ex);
            AnalysisFailed?.Invoke("本次录入失败，未替换现有结果；详细原因已记录到日志。");
        }
        finally
        {
            AnalysisBusyChanged?.Invoke(false);
            Interlocked.Exchange(ref _running, 0);
        }
    }

    private async Task ShowTimedGameOverlayMessageAsync(string message, int durationMs)
    {
        ShowGameOverlayMessageRequested?.Invoke(message);
        await Task.Delay(durationMs);
        HideGameOverlayMessageRequested?.Invoke();
    }

    // ── Auto-ban computation ──────────────────────────────────────────────────

    private IReadOnlyList<string> ComputeAutoBans(BanScreenResult result)
    {
        var bannedCoreCovenants = result.CoreCovenants
            .Where(c => c.IsBanned)
            .Select(c => c.Name)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .ToHashSet(StringComparer.Ordinal);
        var bannedAdditionalCovenants = result.AdditionalCovenants
            .Where(c => c.IsBanned)
            .Select(c => c.Name)
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .ToHashSet(StringComparer.Ordinal);
        var bannedCovenants = bannedCoreCovenants
            .Concat(bannedAdditionalCovenants)
            .ToHashSet(StringComparer.Ordinal);

        var autoBannedOperators = new HashSet<string>(StringComparer.Ordinal);
        foreach (var op in _operators)
        {
            if (string.IsNullOrWhiteSpace(op.Name))
                continue;

            var allCovenants = op.CoreCovenants
                .Concat(op.AdditionalCovenants)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.Ordinal)
                .ToList();

            // Rule 1: ban operators that have exactly one covenant and that covenant
            // is one of the banned covenants in this run.
            bool rule1 = allCovenants.Count == 1 && bannedCovenants.Contains(allCovenants[0]);

            // Rule 2: ban operators that have at least one banned core covenant and
            // at least one banned additional covenant.
            bool hasBannedCore = op.CoreCovenants.Any(c => bannedCoreCovenants.Contains(c));
            bool hasBannedAdditional = op.AdditionalCovenants.Any(c => bannedAdditionalCovenants.Contains(c));
            bool rule2 = hasBannedCore && hasBannedAdditional;

            if (rule1 || rule2)
                autoBannedOperators.Add(op.Name);
        }

        AppLogger.Info("BanAnalyzer",
            $"Auto-ban computed: total={autoBannedOperators.Count} " +
            $"ruleContext(coreBanned={bannedCoreCovenants.Count}, additionalBanned={bannedAdditionalCovenants.Count}, banned={bannedCovenants.Count}) " +
            $"operators=[{string.Join(", ", autoBannedOperators)}]");

        return autoBannedOperators.ToList();
    }

    private static void LogRecognitionSummary(BanScreenResult result, IReadOnlyList<string> autoBans)
    {
        string enemy = string.IsNullOrWhiteSpace(result.EnemyFaction) ? "(未识别)" : result.EnemyFaction;
        string core = string.Join(", ", result.CoreCovenants
            .Select(c => $"{c.Name}:{(c.IsBanned ? "禁用" : "进入")}"));
        string additional = string.Join(", ", result.AdditionalCovenants
            .Select(c => $"{c.Name}:{(c.IsBanned ? "禁用" : "进入")}"));
        string bans = string.Join(", ", autoBans);

        AppLogger.Info("BanAnalyzer",
            $"识别结果汇总: 敌人={enemy}; 核心盟约=[{core}]; 附加盟约=[{additional}]; 自动禁用干员=[{bans}]");
    }

    // ── Results output ────────────────────────────────────────────────────────

    private static void LogResults(BanScreenResult result, long elapsedMs)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine();
        sb.AppendLine("══════════════ ban-screen analysis ══════════════");

        sb.Append("本场敌人: ");
        sb.AppendLine(result.EnemyFaction ?? "(未识别)");

        sb.AppendLine("核心盟约:");
        if (result.CoreCovenants.Count == 0)
        {
            sb.AppendLine("  (未识别)");
        }
        else
        {
            foreach (var c in result.CoreCovenants.OrderBy(c => c.X))
                sb.AppendLine($"  {c.Name,-12} {(c.IsBanned ? "【禁用】" : "  启用 ")}");
        }

        sb.AppendLine("附加盟约:");
        if (result.AdditionalCovenants.Count == 0)
        {
            sb.AppendLine("  (未识别)");
        }
        else
        {
            foreach (var c in result.AdditionalCovenants.OrderBy(c => c.X))
                sb.AppendLine($"  {c.Name,-12} {(c.IsBanned ? "【禁用】" : "  启用 ")}");
        }

        sb.Append($"耗时: {elapsedMs} ms");
        sb.AppendLine();
        sb.AppendLine("═════════════════════════════════════════════════");

        var output = sb.ToString();
        AppLogger.Info("BanAnalyzer", output);
        AppConsole.Write(output);
    }

    // ── Utility ───────────────────────────────────────────────────────────────

    private static (int Width, int Height) GetPngDimensions(byte[] png)
    {
        int w = (png[16] << 24) | (png[17] << 16) | (png[18] << 8) | png[19];
        int h = (png[20] << 24) | (png[21] << 16) | (png[22] << 8) | png[23];
        return (w, h);
    }
}
