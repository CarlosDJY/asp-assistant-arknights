using System.Text.Json.Serialization;

namespace ASPAssistant.Core.Models;

/// <summary>
/// What the player owns this run, for the placement page. Tracked operators are always listed (greyed when not
/// owned); operators outside the tracking are added by hand.
/// </summary>
public sealed class DeployPoolState
{
    [JsonPropertyName("owned")] public List<string> Owned { get; set; } = [];
    [JsonPropertyName("elite")] public List<string> Elite { get; set; } = [];
    /// <summary>Owned copies beyond one (the same operator can be on the field more than once); absent = 1.</summary>
    [JsonPropertyName("counts")] public Dictionary<string, int> Counts { get; set; } = [];
    /// <summary>Added by hand (not tracked); listed under 其他 until removed.</summary>
    [JsonPropertyName("extra")] public List<string> Extra { get; set; } = [];
    [JsonPropertyName("fieldCount")] public int FieldCount { get; set; } = 8;
    [JsonPropertyName("mode")] public PlacementMode Mode { get; set; } = PlacementMode.Balanced;
    /// <summary>Map picked by hand (null = follow recognition).</summary>
    [JsonPropertyName("manualMap")] public string? ManualMap { get; set; }
    /// <summary>Draw the plan on the game window (off until the player turns it on).</summary>
    [JsonPropertyName("showInGame")] public bool ShowInGame { get; set; }

    public bool IsOwned(string name) => Owned.Contains(name);
    public bool IsElite(string name) => Elite.Contains(name);
    /// <summary>Owned copies (0 when not owned).</summary>
    public int CountOf(string name) => !IsOwned(name) ? 0 : Math.Max(1, Counts.GetValueOrDefault(name, 1));

    /// <summary>Sets the owned copies; 0 marks the operator as not owned.</summary>
    public void SetCount(string name, int count)
    {
        count = Math.Clamp(count, 0, 9);
        SetOwned(name, count > 0);
        if (count > 1) Counts[name] = count;
        else Counts.Remove(name);
    }

    public void SetOwned(string name, bool owned)
    {
        Owned.Remove(name);
        if (owned) Owned.Add(name);
        else { Elite.Remove(name); Counts.Remove(name); }
    }

    public void SetElite(string name, bool elite)
    {
        Elite.Remove(name);
        if (elite) Elite.Add(name);
    }
}

public enum DeployPoolGroupKind { Final, Transition, Extension, Other }

public sealed record DeployPoolItem(string Name, Operator? Operator, string Sub, bool Owned, bool Elite, bool Removable, int Count = 0)
{
    public int Tier => Operator?.Tier ?? 0;
}

public sealed record DeployPoolGroup(DeployPoolGroupKind Kind, string Title, string Sub, IReadOnlyList<DeployPoolItem> Items);

public static class DeployPoolBuilder
{
    /// <summary>
    /// 最终 / 过渡 come from the tracking; 扩展池 = untracked operators sharing a covenant with the 大成 operators
    /// (so the player rarely needs to search); 其他 = added by hand.
    /// </summary>
    public static IReadOnlyList<DeployPoolGroup> Build(IEnumerable<TrackingEntry> tracked,
        IReadOnlyDictionary<string, Operator> operators, DeployPoolState state)
    {
        var entries = tracked.Where(e => e.Type == TrackingType.Operator).DistinctBy(e => e.Name).ToList();
        var trackedNames = entries.Select(e => e.Name).ToHashSet();
        Operator? Op(string n) => operators.GetValueOrDefault(n);
        string Covs(Operator? o) => o is null ? "" : string.Join(" · ", o.CoreCovenants.Concat(o.AdditionalCovenants));
        DeployPoolItem Item(string n, string sub, bool removable) =>
            new(n, Op(n), sub, state.IsOwned(n), state.IsElite(n), removable, state.CountOf(n));

        var finals = entries.Where(e => !e.IsTransition).Select(e => e.Name)
            .OrderByDescending(n => Op(n)?.Tier ?? 0).ThenBy(n => n, StringComparer.Ordinal)
            .Select(n => Item(n, Covs(Op(n)), false)).ToList();
        var transitions = entries.Where(e => e.IsTransition)
            .OrderBy(e => e.ExpiresAt == 0 ? int.MaxValue : e.ExpiresAt).ThenByDescending(e => Op(e.Name)?.Tier ?? 0)
            .Select(e => Item(e.Name, string.Join(" · ", new[] { e.Stages, Covs(Op(e.Name)) }.Where(s => s.Length > 0)), false))
            .ToList();

        var finalCovenants = entries.Where(e => !e.IsTransition).Select(e => Op(e.Name)).Where(o => o is not null)
            .SelectMany(o => o!.CoreCovenants.Concat(o.AdditionalCovenants)).ToHashSet();
        var extra = state.Extra.Where(n => !trackedNames.Contains(n)).ToHashSet();
        var extension = operators.Values
            .Where(o => !trackedNames.Contains(o.Name) && !extra.Contains(o.Name)
                        && o.CoreCovenants.Concat(o.AdditionalCovenants).Any(finalCovenants.Contains))
            // By tier (the UI splits them into one band per tier), then the ones sharing more covenants first.
            .OrderBy(o => o.Tier)
            .ThenByDescending(o => o.CoreCovenants.Concat(o.AdditionalCovenants).Count(finalCovenants.Contains))
            .ThenBy(o => o.Name, StringComparer.Ordinal)
            .Select(o => Item(o.Name, Covs(o), false)).ToList();
        var listed = trackedNames.Concat(extension.Select(i => i.Name)).ToHashSet();
        // Hand-added operators, plus anything still marked owned that is no longer listed (e.g. untracked since).
        var others = state.Extra.Where(extra.Contains)
            .Concat(state.Owned.Where(n => !listed.Contains(n) && !extra.Contains(n) && operators.ContainsKey(n)))
            .Distinct().Select(n => Item(n, Covs(Op(n)), true)).ToList();

        var groups = new List<DeployPoolGroup>();
        if (finals.Count > 0) groups.Add(new(DeployPoolGroupKind.Final, "最终阵容", "追踪中的大成干员", finals));
        if (transitions.Count > 0) groups.Add(new(DeployPoolGroupKind.Transition, "过渡", "按阶段 · 不会被移除", transitions));
        if (extension.Count > 0)
            groups.Add(new(DeployPoolGroupKind.Extension, "扩展池",
                "没追踪，但有" + string.Join("、", finalCovenants.Take(3)) + (finalCovenants.Count > 3 ? " 等" : "") + "盟约", extension));
        if (others.Count > 0) groups.Add(new(DeployPoolGroupKind.Other, "其他", "手动添加 · 卖掉可删除", others));
        return groups;
    }
}
