// Modified for ASP Assistant Regulation on 2026-11-13; see NOTICE.
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using ASPAssistant.Core.Models;
using CommunityToolkit.Mvvm.ComponentModel;

namespace ASPAssistant.Core.ViewModels;

public partial class EnemyViewModel : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRecognize))]
    private bool _isRecognizing;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRecognize))]
    private bool _isBanAnalysisRunning;

    public bool CanRecognize => !IsRecognizing && !IsBanAnalysisRunning;

    [ObservableProperty]
    private string _recognitionStatus = "请先将游戏置前并打开本局信息页，再点击识别。";

    public bool HasRecordedResult { get; private set; }

    [ObservableProperty]
    private ObservableCollection<string> _unmatchedEntries = [];

    [ObservableProperty]
    private ObservableCollection<string> _rawTexts = [];

    // ── OCR fields ────────────────────────────────────────────────────────────

    [ObservableProperty]
    private string _bossName = "";

    [ObservableProperty]
    private ObservableCollection<string> _enemyTypes = [];

    // ── Boss detail fields (populated from BossDataService) ───────────────────

    /// <summary>
    /// Relative icon path (e.g. <c>icons/bosses/enemy_9006_actoxi.png</c>),
    /// resolved at view layer via <c>AppContext.BaseDirectory + "data"</c>.
    /// <c>null</c> when no boss info is available.
    /// </summary>
    [ObservableProperty]
    private string? _avatarPath;

    /// <summary>
    /// <c>true</c> when <see cref="AvatarPath"/> points to an existing file under <c>data/</c>.
    /// Drives portrait vs placeholder when JSON path is set but PNG was never shipped.
    /// </summary>
    [ObservableProperty]
    private bool _bossAvatarImageAvailable;

    /// <summary>Display index from <c>bosses.json</c>, e.g. <c>SP01</c>.</summary>
    [ObservableProperty]
    private string _bossIndex = "";

    /// <summary>Short Chinese summary of <see cref="BossInfo.DamageTypes"/>.</summary>
    [ObservableProperty]
    private string _bossDamageSummary = "";

    [ObservableProperty]
    private string _bossDescription = "";

    [ObservableProperty]
    private ObservableCollection<string> _bossAbilities = [];

    /// <summary>单人：极速模拟与规程等级 1–20。</summary>
    [ObservableProperty]
    private ObservableCollection<BossHpCell> _bossHpSingle = [];

    /// <summary>多人：标准 / 险境 / 绝境 / 终极模拟。</summary>
    [ObservableProperty]
    private ObservableCollection<BossHpCell> _bossHpMulti = [];

    [ObservableProperty]
    private bool _bossHasHiddenForm;

    /// <summary>Mode name read from the shop HUD (规程等级 N / …模拟); highlights that HP cell.</summary>
    [ObservableProperty]
    private string? _currentMode;

    partial void OnCurrentModeChanged(string? value) => MarkCurrentMode();

    private void MarkCurrentMode()
    {
        foreach (var list in new[] { BossHpSingle, BossHpMulti })
            for (int i = 0; i < list.Count; i++)
                if (list[i].IsCurrent != (list[i].Name == CurrentMode))
                    list[i] = list[i] with { IsCurrent = list[i].Name == CurrentMode };
    }

    // ── Enemy type detail cards (populated from EnemyTypeDataService) ──────────

    /// <summary>
    /// Enriched data for each OCR-detected special-enemy type.
    /// Bound to the type-card list in the view.
    /// </summary>
    [ObservableProperty]
    private ObservableCollection<EnemyTypeEntry> _enemyTypeDetails = [];

    // ── Public methods ────────────────────────────────────────────────────────

    /// <summary>Clears all enemy info before a new recognition run.</summary>
    public void Clear()
    {
        HasRecordedResult = false;
        BossName           = "";
        AvatarPath         = null;
        BossIndex          = "";
        BossDamageSummary  = "";
        BossDescription    = "";
        EnemyTypes.Clear();
        BossAbilities.Clear();
        BossHpSingle.Clear();
        BossHpMulti.Clear();
        BossHasHiddenForm = false;
        EnemyTypeDetails.Clear();
        UnmatchedEntries.Clear();
        RawTexts.Clear();
    }

    public void BeginRecognition()
    {
        IsRecognizing = true;
        RecognitionStatus = HasRecordedResult
            ? "正在重新识别，当前显示上次录入结果…"
            : "正在识别本局首领和特训敌人…";
    }

    public void RecognitionFailed(string message)
    {
        IsRecognizing = false;
        RecognitionStatus = HasRecordedResult ? $"{message} 当前保留上次录入结果。" : message;
    }

    /// <summary>
    /// Populates the view model from a completed recognition result.
    /// Null entries mean that the recognized enemy is absent from the local catalogue.
    /// </summary>
    public void Update(
        EnemyResult result,
        BossInfo? bossInfo,
        IReadOnlyList<EnemyTypeEntry?> typeEntries)
    {
        if (result.BossName.Length == 0 && result.EnemyTypes.Count == 0
            || HasRecordedResult && (result.BossName.Length == 0 || result.EnemyTypes.Count == 0))
        {
            RecognitionFailed("本次敌人信息未完整读出，请保持本局信息页可见后重试。");
            return;
        }
        Clear();
        BossName = result.BossName;
        RawTexts = new ObservableCollection<string>(result.RawTexts);

        foreach (var t in result.EnemyTypes)
            EnemyTypes.Add(t);

        // Boss details
        BossDescription = bossInfo?.Description ?? "";
        BossIndex       = bossInfo?.EnemyIndex ?? "";
        BossDamageSummary = FormatDamageTypes(bossInfo?.DamageTypes);
        if (bossInfo is not null)
            foreach (var a in bossInfo.AbilityList)
                BossAbilities.Add(a);
        BossHpSingle.Clear();
        BossHpMulti.Clear();
        foreach (var level in bossInfo?.Hp ?? [])
            (level.Group == "multi" ? BossHpMulti : BossHpSingle).Add(BossHpCell.From(level));
        BossHasHiddenForm = bossInfo?.Hp.Any(l => l.HiddenHp is not null) == true;
        MarkCurrentMode();
        AvatarPath = string.IsNullOrWhiteSpace(bossInfo?.IconPath)
            ? null
            : bossInfo!.IconPath.Trim();

        // Enemy type cards
        for (int i = 0; i < typeEntries.Count; i++)
        {
            if (typeEntries[i] is { } entry)
                EnemyTypeDetails.Add(entry);
            else
                UnmatchedEntries.Add($"{result.EnemyTypes[i]}：资料未收录");
        }
        if (BossName.Length > 0 && bossInfo is null)
            UnmatchedEntries.Insert(0, $"首领“{BossName}”：资料未收录或名称待核对");

        var missing = new List<string>();
        if (BossName.Length == 0) missing.Add("首领未读出");
        if (EnemyTypes.Count == 0) missing.Add("特训敌人未读出");
        if (UnmatchedEntries.Count > 0) missing.Add("部分资料未收录");
        RecognitionStatus = BossName.Length == 0 && EnemyTypes.Count == 0
            ? "未识别到敌人信息，请确认本局信息页面完整可见后重试。"
            : missing.Count > 0 ? $"部分识别：{string.Join("；", missing)}。"
            : $"识别完成：{BossName}，{EnemyTypes.Count} 类特训敌人。";
        IsRecognizing = false;
        HasRecordedResult = true;
    }

    private static string FormatDamageTypes(IReadOnlyList<string>? types)
    {
        if (types is null || types.Count == 0)
            return "";

        static string Map(string code) => code.ToUpperInvariant() switch
        {
            "MAGIC"   => "法术",
            "PHYSIC"  => "物理",
            "TRUE"    => "真实",
            _         => code
        };

        return string.Join(" · ", types.Distinct(StringComparer.OrdinalIgnoreCase).Select(Map));
    }

    partial void OnAvatarPathChanged(string? value) =>
        BossAvatarImageAvailable = IsBossAvatarFilePresent(value);

    private static bool IsBossAvatarFilePresent(string? relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath))
            return false;
        var trimmed = relativePath.Trim().Replace('/', Path.DirectorySeparatorChar);
        var full    = Path.Combine(AppContext.BaseDirectory, "data", trimmed);
        return File.Exists(full);
    }
}

/// <summary>One cell of the boss HP table.</summary>
public sealed record BossHpCell(string Label, string Name, string Hp, string? HiddenHp, bool IsCurrent = false)
{
    public static BossHpCell From(BossHpLevel level) => new(ShortLabel(level.Name), level.Name,
        FormatHp(level.Hp), level.HiddenHp is { } hidden ? FormatHp(hidden) : null);

    /// <summary>规程等级12 → 12；极速模拟 → 极速；绝境模拟 → 绝境.</summary>
    public static string ShortLabel(string name) => name.StartsWith("规程等级") ? name["规程等级".Length..]
        : name.EndsWith("模拟") ? name[..^2] : name;

    /// <summary>283680 → 28.4 万；1182000 → 118 万.</summary>
    public static string FormatHp(long hp) => hp < 10_000 ? hp.ToString()
        : hp < 1_000_000 ? $"{hp / 10_000.0:0.#} 万" : $"{hp / 10_000.0:0} 万";
}
