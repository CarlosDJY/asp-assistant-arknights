using System.Text.Json.Serialization;

namespace ASPAssistant.Core.Models;

public enum TrackingType
{
    Operator,
    Equipment
}

public class TrackingEntry
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("type")]
    public TrackingType Type { get; set; }

    [JsonPropertyName("transformedCovenant")]
    public string TransformedCovenant { get; set; } = "";
}
