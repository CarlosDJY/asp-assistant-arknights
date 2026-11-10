// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
using System.Text.Json;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

/// <summary>
/// Loads special-enemy type data from the pre-generated local
/// <c>data/enemy_types.json</c> (produced by <c>fetch_spdatabase.py</c>).
///
/// After <see cref="LoadAsync"/> completes, use <see cref="LookupByTypeName"/> to
/// resolve an OCR-detected suffix like "飞行" or "频次" to its <see cref="EnemyTypeEntry"/>.
/// </summary>
public sealed class EnemyTypeDataService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    // Indexed by Chinese type name (e.g. "飞行"), matching the OCR suffix.
    private Dictionary<string, EnemyTypeEntry>? _byTypeName;

    /// <summary>
    /// Loads <c>enemy_types.json</c> from the given data directory.
    /// Safe to call from a background thread.
    /// </summary>
    public async Task LoadAsync(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, "enemy_types.json");
        if (!File.Exists(path))
        {
            AppLogger.Warn("EnemyTypeData", $"enemy_types.json not found at {path}");
            _byTypeName = [];
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path);
            var file = JsonSerializer.Deserialize<EnemyTypeFile>(json, JsonOptions);

            _byTypeName = file?.Types?.Values
                .Where(e => !string.IsNullOrEmpty(e.TypeName))
                .ToDictionary(e => e.TypeName)
                ?? [];

            AppLogger.Info("EnemyTypeData",
                $"Loaded {_byTypeName.Count} enemy type entries from {path}");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("EnemyTypeData", $"Failed to load enemy_types.json: {ex.Message}");
            _byTypeName = [];
        }
    }

    /// <summary>
    /// Looks up an entry by the Chinese type-name suffix extracted from the OCR result
    /// (e.g. pass "飞行" for "特训敌人·飞行").
    /// Returns <c>null</c> if not found or not yet loaded.
    /// </summary>
    public EnemyTypeEntry? LookupByTypeName(string typeName)
    {
        if (_byTypeName is null) return null;

        var normalized = new string(typeName.Where(c => c is >= '\u4e00' and <= '\u9fff').ToArray());
        _byTypeName.TryGetValue(normalized, out var entry);
        return entry;
    }

    // ── JSON wrapper ──────────────────────────────────────────────────────────

    private sealed class EnemyTypeFile
    {
        public Dictionary<string, EnemyTypeEntry>? Types { get; set; }
    }
}
