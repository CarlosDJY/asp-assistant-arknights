using System.Text.Json;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Tests;

internal static class EquipmentTestData
{
    public static LineupEquipmentService Rules { get; } = Load();

    private static LineupEquipmentService Load()
    {
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "TestData", "Regulation", "covenants.json")));
        return new LineupEquipmentService(json.RootElement.GetProperty("jobChangeEquipmentToCovenant")
            .EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString()!));
    }
}
