// Modified for ASP Assistant Arknights on 2026-11-11; see NOTICE.
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

/// <summary>
/// Which operators a run's covenant bans remove from the pool. Used by both the ban-screen analysis
/// (干员页的本局不可用) and the planner, so the two always agree.
/// </summary>
public static class CovenantBanRules
{
    /// <summary>
    /// An operator is unavailable only when every one of its covenants is banned: a single-covenant operator whose
    /// covenant is banned, 霍尔海雅 with 精准 + 奥术 both banned, 纯烬艾雅法拉 only with 莱塔尼亚 + 聚合 + 远见 all banned.
    /// </summary>
    public static bool IsExcluded(Operator op, IReadOnlySet<string> bannedCovenants)
        => IsExcluded(op.CoreCovenants, op.AdditionalCovenants, bannedCovenants);

    public static bool IsExcluded(IEnumerable<string> core, IEnumerable<string> additional, IReadOnlySet<string> bannedCovenants)
    {
        if (bannedCovenants.Count == 0) return false;
        var all = core.Concat(additional).Where(c => !string.IsNullOrWhiteSpace(c)).Distinct(StringComparer.Ordinal).ToList();
        return all.Count > 0 && all.All(bannedCovenants.Contains);
    }
}
