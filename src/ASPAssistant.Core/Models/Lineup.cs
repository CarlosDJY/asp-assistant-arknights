// Modified for ASP Assistant Arknights on 2026-11-13; see NOTICE.
using System.Text.Json.Serialization;

namespace ASPAssistant.Core.Models;

/// <summary>
/// 阵容槽位标签：玩家手动给某个干员标记，仅作分类/筛选/可视化用，不影响盟约激活计算。
/// </summary>
public enum LineupTag
{
    叠层,
    输出,
    控场,
    经济,
    阵容核心,
}

public enum LineupPhase
{
    前期,
    中期,
    后期,
    挂件,
}

public class LineupSlot
{
    [JsonPropertyName("operatorName")]
    public string OperatorName { get; set; } = "";

    /// <summary>只用于读取旧版数据；导入时转换为实际装备后清除。</summary>
    [JsonPropertyName("transformedCovenant")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TransformedCovenant { get; set; }

    [JsonPropertyName("isElite")]
    public bool IsElite { get; set; }

    /// <summary>普通干员最多 2 件；暴躁铁皮普通 4 件、精锐 6 件。</summary>
    [JsonPropertyName("equipments")]
    public List<string> Equipments { get; set; } = [];

    [JsonPropertyName("coreEquipments")]
    public List<string> CoreEquipments { get; set; } = [];

    /// <summary>
    /// 候选装备：数量不限，不参与转职和盟约计算；追踪阵容时一起追踪。不含已在 <see cref="Equipments"/> 里的装备。
    /// </summary>
    [JsonPropertyName("candidateEquipments")]
    public List<CandidateEquipment> CandidateEquipments { get; set; } = [];

    /// <summary>标签列表，最多 2 个。</summary>
    [JsonPropertyName("tags")]
    public List<LineupTag> Tags { get; set; } = [];
}

/// <summary>一件候选装备；优先 = 先拿的那件。</summary>
public class CandidateEquipment
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("isPriority")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsPriority { get; set; }

    public CandidateEquipment Clone() => new() { Name = Name, IsPriority = IsPriority };
}

/// <summary>阵容想凑的一个盟约档位。</summary>
public class TargetCovenant
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>1 或 2。</summary>
    [JsonPropertyName("tier")]
    public int Tier { get; set; } = 1;

    public TargetCovenant Clone() => new() { Name = Name, Tier = Tier };
}

public class Lineup
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = Guid.NewGuid().ToString("N");

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>后期阵容（最多 9 个），用于列表预览；追踪包含四个阶段，同名干员优先使用后期配置。</summary>
    [JsonPropertyName("slots")]
    public List<LineupSlot> Slots { get; set; } = [];

    [JsonPropertyName("earlySlots")]
    public List<LineupSlot> EarlySlots { get; set; } = [];

    [JsonPropertyName("midSlots")]
    public List<LineupSlot> MidSlots { get; set; } = [];

    [JsonPropertyName("supportSlots")]
    public List<LineupSlot> SupportSlots { get; set; } = [];

    [JsonPropertyName("strategies")]
    public List<LineupStrategySlot> Strategies { get; set; } = [];

    /// <summary>
    /// 目标盟约（Ⅰ / Ⅱ 档）：筛选「我的阵容」和本局推荐用。为空时按后期配置推算（<see cref="Services.LineupTargets.Derive"/>）。
    /// </summary>
    [JsonPropertyName("targetCovenants")]
    public List<TargetCovenant> TargetCovenants { get; set; } = [];

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [JsonPropertyName("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public enum StrategyTag
{
    阵容核心,
    优选,
    次选,
}

public class LineupStrategySlot
{
    [JsonPropertyName("strategyId")]
    public string StrategyId { get; set; } = "";

    [JsonPropertyName("tags")]
    public List<StrategyTag> Tags { get; set; } = [];
}

/// <summary>
/// 单条盟约定义（来自 data/covenants.json，可被 covenants.user.json 覆盖）。
/// </summary>
public class CovenantInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>"SEASON" = 核心盟约（赛季限定）；"REGULAR" = 普通附加盟约。</summary>
    [JsonPropertyName("bondType")]
    public string BondType { get; set; } = "REGULAR";

    /// <summary>激活该盟约所需的同盟约干员数；null 表示数据缺失，应在 user 配置中补齐。</summary>
    [JsonPropertyName("activateCount")]
    public int? ActivateCount { get; set; }

    /// <summary>效果说明，来自手工维护的 data/covenant_effects.json（不写回 covenants.json）。</summary>
    [JsonIgnore]
    public List<CovenantEffect> Effects { get; set; } = [];
}

/// <summary>
/// 一条盟约效果。<see cref="Condition"/> 为空表示激活后的基础效果，否则是游戏内 &lt;…&gt; 里的额外条件，
/// 例如「在场6名不同的【炎】干员」「每叠加25层」。
/// </summary>
public class CovenantEffect
{
    [JsonPropertyName("condition")]
    public string Condition { get; set; } = "";

    [JsonPropertyName("text")]
    public string Text { get; set; } = "";

    [JsonIgnore]
    public bool HasCondition => !string.IsNullOrWhiteSpace(Condition);
}

/// <summary>
/// 阵容某盟约的实时统计：用于 UI 展示「已激活/层数/阈值」。
/// </summary>
public record CovenantStat(
    string Name,
    string BondType,
    int Count,
    int? ActivateCount,
    bool Activated);
