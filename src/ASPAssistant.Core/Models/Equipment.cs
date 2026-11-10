// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
using System.Text.Json.Serialization;

namespace ASPAssistant.Core.Models;

public enum EquipmentType { Normal, JobChange }

public class EquipmentVariant
{
    [JsonPropertyName("effectDescription")]
    public string EffectDescription { get; set; } = "";
}

public class Equipment
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("tier")]
    public int Tier { get; set; }

    [JsonPropertyName("normal")]
    public EquipmentVariant Normal { get; set; } = new();

    [JsonPropertyName("elite")]
    public EquipmentVariant Elite { get; set; } = new();

    [JsonPropertyName("iconPath")]
    public string IconPath { get; set; } = "";

    [JsonIgnore]
    public EquipmentType Type { get; set; } = EquipmentType.Normal;

    [JsonIgnore]
    public string JobChangeCovenant { get; set; } = "";

    /// <summary>
    /// 功能分类（攻击/防御/生存/技力/经济/盟约/其他），来自 data/equipment_categories.json，
    /// 由 <c>JsonDataStore.LoadEquipmentAsync</c> 加载时填充；一件装备可属多类。
    /// </summary>
    [JsonIgnore]
    public List<string> Categories { get; set; } = [];
}
