using System.Text.Json.Serialization;

namespace ASPAssistant.Core.Models;

public class TraitEntry
{
    [JsonPropertyName("traitType")]
    public string TraitType { get; set; } = "";

    [JsonPropertyName("traitDescription")]
    public string TraitDescription { get; set; } = "";
}

public class OperatorVariant
{
    [JsonPropertyName("traits")]
    public List<TraitEntry> Traits { get; set; } = [];

    /// <summary>Skill of this variant (from data/operator_profiles.json; null when unknown).</summary>
    [JsonIgnore]
    public OperatorSkill? Skill { get; set; }

    [JsonIgnore]
    public bool HasSkill => Skill is not null;
}

public class Operator
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("tier")]
    public int Tier { get; set; }

    [JsonPropertyName("requiredStrategyId")]
    public string RequiredStrategyId { get; set; } = "";

    [JsonPropertyName("coreCovenants")]
    public List<string> CoreCovenants { get; set; } = [];

    /// <summary>Primary core covenant (first entry), empty string if none.</summary>
    [JsonIgnore]
    public string CoreCovenant => CoreCovenants.FirstOrDefault() ?? "";

    [JsonPropertyName("additionalCovenants")]
    public List<string> AdditionalCovenants { get; set; } = [];

    [JsonPropertyName("normal")]
    public OperatorVariant Normal { get; set; } = new();

    [JsonPropertyName("elite")]
    public OperatorVariant Elite { get; set; } = new();

    [JsonPropertyName("iconPath")]
    public string IconPath { get; set; } = "";

    /// <summary>Class, skill and planner roles (from data/operator_profiles.json; null when unknown).</summary>
    [JsonIgnore]
    public OperatorProfile? Profile { get; set; }

    [JsonIgnore]
    public bool HasProfile => Profile is not null;
}
