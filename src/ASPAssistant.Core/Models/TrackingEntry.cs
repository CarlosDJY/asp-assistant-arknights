using System.Text.Json.Serialization;

namespace ASPAssistant.Core.Models;

public enum TrackingType
{
    Operator,
    Equipment
}

/// <summary>大成 = part of the final lineup (green); 过渡 = only used on the way (orange).</summary>
public enum TrackingRole
{
    Final,
    Transition
}

public class TrackingEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("type")]
    public TrackingType Type { get; set; }

    [JsonPropertyName("transformedCovenant")]
    public string TransformedCovenant { get; set; } = "";

    [JsonPropertyName("role")]
    public TrackingRole Role { get; set; } = TrackingRole.Final;

    /// <summary>Route stages that use this operator (route import only), e.g. “开局 · 前期”.</summary>
    [JsonPropertyName("stages")]
    public string Stages { get; set; } = "";

    /// <summary>
    /// 过渡 only: terminal level from which every route stage using this operator is behind the player
    /// (see <see cref="PlannedRoute.ExpiresAt"/>); 0 = never.
    /// </summary>
    [JsonPropertyName("expiresAt")]
    public int ExpiresAt { get; set; }

    [JsonIgnore]
    public bool IsTransition => Role == TrackingRole.Transition;

    /// <summary>Set by the tracking list from the current terminal level; expired entries get no shop marker.</summary>
    [JsonIgnore]
    public bool IsExpired { get; set; }

    public bool ExpiredAt(int terminalLevel) => IsTransition && ExpiresAt > 0 && terminalLevel >= ExpiresAt;
}
