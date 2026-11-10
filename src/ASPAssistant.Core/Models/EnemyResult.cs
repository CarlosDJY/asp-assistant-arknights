// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
namespace ASPAssistant.Core.Models;

/// <summary>
/// Result of a single "识别本场敌人" pass.
/// Empty or unknown names are retained so the page can report partial recognition.
/// </summary>
public record EnemyResult(string BossName, IReadOnlyList<string> EnemyTypes, IReadOnlyList<string> RawTexts);
