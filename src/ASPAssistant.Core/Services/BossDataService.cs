// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

/// <summary>
/// Loads Garrison Protocol boss data from the pre-generated local
/// <c>data/bosses.json</c> (produced by <c>fetch_spdatabase.py</c>).
///
/// Call <see cref="LoadAsync"/> once on startup; after that use
/// <see cref="Lookup"/> to resolve an OCR-detected boss name to its
/// <see cref="BossInfo"/>.
/// </summary>
public sealed class BossDataService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private Dictionary<string, BossInfo>? _byName;

    private List<(string Key, string CanonicalName)> _bossOcrMatchPairs = [];


    /// <summary>
    /// Loads <c>bosses.json</c> from the given data directory.
    /// Safe to call from a background thread; the result is written atomically.
    /// </summary>
    public async Task LoadAsync(string dataDirectory)
    {
        var path = Path.Combine(dataDirectory, "bosses.json");
        if (!File.Exists(path))
        {
            AppLogger.Warn("BossData", $"bosses.json not found at {path}");
            _byName = [];
            _bossOcrMatchPairs = [];
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(path);
            var file = JsonSerializer.Deserialize<BossDataFile>(json, JsonOptions);

            var bosses = file?.Bosses?
                .Where(b => !string.IsNullOrEmpty(b.Name))
                .OrderBy(b => b.EnemyId, StringComparer.Ordinal)
                .ToList() ?? [];

            _byName = [];
            foreach (var b in bosses)
            {
                if (!_byName.ContainsKey(b.Name))
                    _byName[b.Name] = b;
                foreach (var alias in b.Aliases ?? [])
                {
                    if (string.IsNullOrWhiteSpace(alias))
                        continue;
                    var key = alias.Trim();
                    if (_byName.ContainsKey(key))
                        continue;
                    _byName[key] = b;
                }
            }

            var seenKeys = new HashSet<string>(StringComparer.Ordinal);
            _bossOcrMatchPairs = [];
            foreach (var b in bosses)
            {
                AddOcrMatchKey(b.Name, b.Name, seenKeys, _bossOcrMatchPairs);
                foreach (var alias in b.Aliases ?? [])
                {
                    if (string.IsNullOrWhiteSpace(alias))
                        continue;
                    AddOcrMatchKey(alias.Trim(), b.Name, seenKeys, _bossOcrMatchPairs);
                }
            }

            _bossOcrMatchPairs.Sort((a, b) => b.Key.Length.CompareTo(a.Key.Length));


            AppLogger.Info("BossData",
                $"Loaded {_byName.Count} boss lookup keys, {_bossOcrMatchPairs.Count} OCR match keys from {path} ({bosses.Count} entries)");
        }
        catch (Exception ex)
        {
            AppLogger.Warn("BossData", $"Failed to load bosses.json: {ex.Message}");
            _byName = [];
            _bossOcrMatchPairs = [];
        }
    }

    private static void AddOcrMatchKey(
        string key,
        string canonicalName,
        HashSet<string> seenKeys,
        List<(string Key, string CanonicalName)> target)
    {
        if (string.IsNullOrWhiteSpace(key) || key.Length < 2)
            return;
        if (!seenKeys.Add(key))
            return;
        target.Add((key, canonicalName));
    }

    /// <summary>Matches complete names/aliases across OCR lines, ignoring punctuation.
    /// Do not use one-character fuzzy matching: 假想敌：鳍 must not become 假想敌：胄.
    /// </summary>
    public bool TryMatchBossFromOcr(IReadOnlyList<string> ocrLines, [NotNullWhen(true)] out string? canonicalName)
    {
        canonicalName = null;
        var text = new string(string.Concat(ocrLines).Where(char.IsLetterOrDigit).ToArray());
        foreach (var (key, canonical) in _bossOcrMatchPairs)
        {
            var normalized = new string(key.Where(char.IsLetterOrDigit).ToArray());
            if (text.Contains(normalized, StringComparison.Ordinal))
            {
                canonicalName = canonical;
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Looks up boss info by exact display name or alias.
    /// </summary>
    public BossInfo? Lookup(string bossName)
    {
        if (_byName is null) return null;
        _byName.TryGetValue(bossName, out var info);
        return info;
    }

    // ── JSON wrapper ──────────────────────────────────────────────────────────

    private sealed class BossDataFile
    {
        public List<BossInfo>? Bosses { get; set; }
    }
}
