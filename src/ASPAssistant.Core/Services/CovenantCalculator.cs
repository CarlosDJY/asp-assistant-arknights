using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

/// <summary>
/// 阵容盟约激活计算器：根据干员/装备/特殊规则计算每个盟约的层数与激活状态。
///
/// 特殊规则（来自策划文档）：
///   1. 「变形同构体」装备：若一个 slot 同时装备了「变形同构体」与某个转职装备，
///      则该转职装备对应的核心盟约视为该 slot 额外获得（追加进核心盟约列表）。
///   2. 「调和」盟约：拥有「调和」的干员可视为属于任意核心盟约。
///      实现：把每个「调和」slot 对当前阵容内出现过的每个核心盟约都+1（不影响附加盟约）。
/// </summary>
public class CovenantCalculator
{
    /// <summary>「调和」盟约名（数据中实际为「调和」）。</summary>
    public const string HarmonyCovenant = "调和";

    /// <summary>「变形同构体」装备名。</summary>
    public const string ShapeshifterEquipment = "变形同构体";

    private readonly Dictionary<string, Operator> _operatorByName;
    private readonly Dictionary<string, CovenantInfo> _covenantByName;
    private readonly Dictionary<string, string> _jobChangeEquipmentToCovenant;
    private readonly HashSet<string> _coreCovenantSet;

    public IReadOnlyList<CovenantInfo> AllCovenants { get; }

    public CovenantCalculator(
        IEnumerable<Operator> operators,
        IEnumerable<CovenantInfo> covenants,
        IDictionary<string, string> jobChangeEquipmentToCovenant)
    {
        _operatorByName = operators
            .GroupBy(o => o.Name)
            .ToDictionary(g => g.Key, g => g.First());

        AllCovenants = [.. covenants];
        _covenantByName = AllCovenants
            .GroupBy(c => c.Name)
            .ToDictionary(g => g.Key, g => g.First());

        _jobChangeEquipmentToCovenant = new Dictionary<string, string>(jobChangeEquipmentToCovenant);

        _coreCovenantSet = AllCovenants
            .Where(c => c.BondType == "SEASON")
            .Select(c => c.Name)
            .ToHashSet();
    }

    public IReadOnlyList<CovenantStat> Compute(Lineup lineup)
    {
        var counts = new Dictionary<string, int>();

        // 收集每个 slot 的「有效核心盟约」（含变形同构体规则后）
        var perSlotCoreCovenants = new List<HashSet<string>>(lineup.Slots.Count);

        foreach (var slot in lineup.Slots)
        {
            var slotCore = new HashSet<string>();
            if (_operatorByName.TryGetValue(slot.OperatorName, out var op))
            {
                foreach (var c in op.CoreCovenants)
                    slotCore.Add(c);

                if (slot.Equipments.Contains(ShapeshifterEquipment))
                {
                    foreach (var eq in slot.Equipments)
                    {
                        if (eq == ShapeshifterEquipment) continue;
                        if (_jobChangeEquipmentToCovenant.TryGetValue(eq, out var cov))
                            slotCore.Add(cov);
                    }
                }

                foreach (var add in op.AdditionalCovenants)
                {
                    if (add == HarmonyCovenant) slotCore.Add(add);
                    else Add(counts, add, 1);
                }

                if (_coreCovenantSet.Contains(slot.TransformedCovenant))
                    slotCore.Add(slot.TransformedCovenant);
                else if (!string.IsNullOrEmpty(slot.TransformedCovenant)
                    && !slotCore.Contains(slot.TransformedCovenant)
                    && !op.AdditionalCovenants.Contains(slot.TransformedCovenant))
                    Add(counts, slot.TransformedCovenant, 1);
            }
            perSlotCoreCovenants.Add(slotCore);
        }

        // 先累加非「调和」核心盟约（含变形同构体追加），同时统计阵容内出现过的核心盟约集合
        var presentCoreCovenants = new HashSet<string>();
        for (int i = 0; i < perSlotCoreCovenants.Count; i++)
        {
            var slotCore = perSlotCoreCovenants[i];
            foreach (var c in slotCore)
            {
                if (c == HarmonyCovenant) continue;
                Add(counts, c, 1);
                if (_coreCovenantSet.Contains(c))
                    presentCoreCovenants.Add(c);
            }
        }

        // 「调和」通配：对每个「调和」slot，向阵容内出现过的每个核心盟约 +1。
        // 同时给「调和」自身计 1 层。
        for (int i = 0; i < perSlotCoreCovenants.Count; i++)
        {
            var slotCore = perSlotCoreCovenants[i];
            if (!slotCore.Contains(HarmonyCovenant)) continue;

            Add(counts, HarmonyCovenant, 1);
            foreach (var present in presentCoreCovenants)
                Add(counts, present, 1);
        }

        // 投影为 CovenantStat 列表（保留 AllCovenants 排序，缺失阈值时未激活）
        var result = new List<CovenantStat>(AllCovenants.Count);
        foreach (var def in AllCovenants)
        {
            counts.TryGetValue(def.Name, out var count);
            bool activated = def.ActivateCount.HasValue
                && count >= def.ActivateCount.Value
                && count > 0;
            result.Add(new CovenantStat(def.Name, def.BondType, count, def.ActivateCount, activated));
        }

        // 把统计中出现、但不在 AllCovenants 中的盟约也加进来（例如特殊数据缺失场景）
        foreach (var (name, count) in counts)
        {
            if (_covenantByName.ContainsKey(name)) continue;
            result.Add(new CovenantStat(name, "REGULAR", count, null, false));
        }

        return result;
    }

    public int CountActivated(Lineup lineup) => Compute(lineup).Count(s => s.Activated);

    private static void Add(Dictionary<string, int> counts, string key, int delta)
    {
        if (string.IsNullOrEmpty(key)) return;
        counts.TryGetValue(key, out var existing);
        counts[key] = existing + delta;
    }
}
