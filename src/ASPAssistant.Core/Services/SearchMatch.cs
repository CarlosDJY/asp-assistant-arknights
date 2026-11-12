// Added for ASP Assistant Arknights on 2026-11-13; see NOTICE.
using System.Text.Json;

namespace ASPAssistant.Core.Services;

/// <summary>
/// Search by name or pinyin initials (冻河 ← "dh", 集结指示 ← "jjzs"). The initials come from data/search_keys.json,
/// built by data/build_search_keys.py; names missing from it only match by text.
/// </summary>
public static class SearchMatch
{
    private static IReadOnlyDictionary<string, string> _initials = new Dictionary<string, string>();

    public static void Load(IReadOnlyDictionary<string, string> initials) => _initials = initials;

    public static void LoadFile(string path)
    {
        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (!doc.RootElement.TryGetProperty("initials", out var map)) return;
            _initials = map.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetString() ?? "", StringComparer.Ordinal);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            AppLogger.Warn("SearchMatch", $"Failed to load {path}: {ex.Message}");
        }
    }

    public static string InitialsOf(string text) => _initials.GetValueOrDefault(text, "");

    /// <summary>True when <paramref name="query"/> is empty, part of any text, or the start of any text's initials.</summary>
    public static bool Matches(string query, params string?[] texts)
    {
        query = query.Trim();
        if (query.Length == 0) return true;
        var latin = query.All(c => c < 128 && char.IsLetterOrDigit(c));
        foreach (var text in texts)
        {
            if (string.IsNullOrEmpty(text)) continue;
            if (text.Contains(query, StringComparison.OrdinalIgnoreCase)) return true;
            if (latin && InitialsOf(text).StartsWith(query, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }
}
