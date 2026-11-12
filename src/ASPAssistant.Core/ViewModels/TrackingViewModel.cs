// Modified for ASP Assistant Regulation on 2026-11-13; see NOTICE.
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Core.ViewModels;

public partial class TrackingViewModel : ObservableObject
{
    [ObservableProperty]
    private bool _trackMultipleLineups;

    [ObservableProperty]
    private ObservableCollection<TrackingEntry> _trackedOperators = [];

    [ObservableProperty]
    private ObservableCollection<TrackingEntry> _trackedEquipment = [];

    [ObservableProperty]
    private Dictionary<string, int> _covenantCounts = new();

    [ObservableProperty]
    private object? _gameState;

    public IReadOnlyList<TrackingEntry> AllEntries =>
        [.. TrackedOperators, .. TrackedEquipment];

    /// <summary>Entries the shop should mark: 过渡 operators whose route stages are behind the player are left out.</summary>
    public IReadOnlyList<TrackingEntry> ActiveEntries => [.. AllEntries.Where(e => !e.IsExpired)];

    // ── Terminal level ──────────────────────────────────────────────────────

    /// <summary>Level read from the shop's upgrade card (null = not seen yet this run).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TerminalLevel), nameof(TerminalLevelText))]
    private int? _detectedTerminalLevel;

    /// <summary>True while the level is the one saved by the last session and the shop has not shown it yet.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TerminalLevelText))]
    private bool _isTerminalLevelRestored;

    /// <summary>Player override (null = 自动).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(TerminalLevel), nameof(TerminalLevelText), nameof(TerminalLevelChoice))]
    private int? _manualTerminalLevel;

    /// <summary>Effective level; 0 = unknown (nothing expires).</summary>
    /// <summary>Round, funds and upgrade price read from the shop HUD; null until a shop has been seen this game.</summary>
    [ObservableProperty]
    private ShopEconomy? _economy;

    public int TerminalLevel => ManualTerminalLevel ?? DetectedTerminalLevel ?? 0;

    public string TerminalLevelText => TerminalLevel == 0 ? "终端等级：未识别"
        : ManualTerminalLevel is not null ? $"终端 {TerminalLevel} 级（手动）"
        : IsTerminalLevelRestored ? $"终端 {TerminalLevel} 级（上次记录，打开商店后确认）"
        : $"终端 {TerminalLevel} 级（自动识别）";

    /// <summary>Choices for the override box: 0 = 自动, 1–6.</summary>
    public IReadOnlyList<int> TerminalLevelChoices { get; } = [0, 1, 2, 3, 4, 5, 6];

    public int TerminalLevelChoice
    {
        get => ManualTerminalLevel ?? 0;
        set => ManualTerminalLevel = value is >= 1 and <= 6 ? value : null;
    }

    partial void OnDetectedTerminalLevelChanged(int? value)
    {
        // The terminal only goes up within a run: once the reading catches up with a manual value, the override has
        // done its job and automatic tracking takes over again (otherwise it would pin the level for the whole run).
        if (ManualTerminalLevel is { } manual && value >= manual) ManualTerminalLevel = null;
        RefreshExpiry();
    }
    partial void OnManualTerminalLevelChanged(int? value) => RefreshExpiry();

    /// <summary>New run: forget the level read in the previous one.</summary>
    public void ResetTerminalLevel()
    {
        ManualTerminalLevel = null;
        DetectedTerminalLevel = null;
        IsTerminalLevelRestored = false;
    }

    /// <summary>Marks 过渡 entries whose stages are behind the current terminal level (replaced so lists and saves refresh).</summary>
    private void RefreshExpiry()
    {
        var level = TerminalLevel;
        for (var i = 0; i < TrackedOperators.Count; i++)
        {
            var e = TrackedOperators[i];
            var expired = e.ExpiredAt(level);
            if (e.IsExpired == expired) continue;
            TrackedOperators[i] = Copy(e, expired);
        }
        OnPropertyChanged(nameof(ActiveEntries));
    }

    private static TrackingEntry Copy(TrackingEntry e, bool expired) => new()
    {
        Name = e.Name, Type = e.Type, TransformedCovenant = e.TransformedCovenant, Equipments = [.. e.Equipments],
        CandidateEquipments = [.. e.CandidateEquipments.Select(c => c.Clone())], EquipmentKind = e.EquipmentKind,
        IsElite = e.IsElite, IsManual = e.IsManual, Role = e.Role, Stages = e.Stages, ExpiresAt = e.ExpiresAt, IsExpired = expired,
        IsAcquired = e.IsAcquired
    };

    public (int Operators, int Equipment) TrackLineup(Lineup lineup)
    {
        if (!TrackMultipleLineups) ClearAllTracking();
        var previousOperators = TrackedOperators.Where(e => !e.IsAcquired).Select(e => e.Name).ToHashSet();
        var previousEquipment = TrackedEquipment.Where(e => !e.IsAcquired).Select(e => e.Name).ToHashSet();
        foreach (var slot in lineup.Slots.Concat(lineup.MidSlots).Concat(lineup.EarlySlots).Concat(lineup.SupportSlots)
            .DistinctBy(slot => slot.OperatorName))
        {
            if (!string.IsNullOrEmpty(slot.OperatorName))
            {
                var existing = TrackedOperators.FirstOrDefault(e => e.Name == slot.OperatorName);
                var entry = new TrackingEntry
                {
                    Name = slot.OperatorName, Type = TrackingType.Operator,
                    TransformedCovenant = slot.TransformedCovenant, Equipments = [.. slot.Equipments], IsElite = slot.IsElite,
                    CandidateEquipments = [.. slot.CandidateEquipments.Where(c => !slot.Equipments.Contains(c.Name)).Select(c => c.Clone())]
                };
                if (existing is null) TrackedOperators.Add(entry);
                else if (existing.IsAcquired || existing.TransformedCovenant != slot.TransformedCovenant || existing.IsElite != slot.IsElite
                    || !existing.Equipments.SequenceEqual(slot.Equipments) || existing.Role != TrackingRole.Final
                    || !SameCandidates(existing.CandidateEquipments, entry.CandidateEquipments))
                    TrackedOperators[TrackedOperators.IndexOf(existing)] = entry;
            }
        }
        RefreshEquipmentRequirements();
        return (TrackedOperators.Count(e => !e.IsAcquired && !previousOperators.Contains(e.Name)), TrackedEquipment.Count(e => !e.IsAcquired && !previousEquipment.Contains(e.Name)));
    }

    /// <summary>
    /// Tracks every operator of a 道中路线 in one list: 大成 operators as <see cref="TrackingRole.Final"/>,
    /// operators only used on the way as <see cref="TrackingRole.Transition"/> (with the stages that use them).
    /// An operator already tracked as 大成 stays 大成. Returns (new 大成, new 过渡) counts.
    /// </summary>
    public (int Final, int Transition) TrackRoute(PlannedRoute route, LineupEquipmentService? equipmentRules = null)
    {
        if (!TrackMultipleLineups) ClearAllTracking();
        var finalStage = route.Stages.LastOrDefault(s => s.IsFinal) ?? route.Stages.LastOrDefault();
        if (finalStage is null) return (0, 0);
        var transforms = finalStage.Operators.ToDictionary(o => o.Name, o => o.TransformedCovenant ?? "", StringComparer.Ordinal);
        int newFinal = 0, newTransition = 0;
        foreach (var op in finalStage.Operators)
        {
            var existing = TrackedOperators.FirstOrDefault(e => e.Name == op.Name);
            var entry = new TrackingEntry { Name = op.Name, Type = TrackingType.Operator, Role = TrackingRole.Final };
            // A planned transformation is tracked as its equipment (变形同构体 + the covenant's job-change gear).
            var covenant = transforms[op.Name];
            if (covenant.Length > 0 && equipmentRules?.CanTransformInto(covenant) == true)
                entry.Equipments = [LineupEquipmentService.Shapeshifter, equipmentRules.EquipmentFor(covenant)];
            else if (covenant.Length > 0)
                entry.TransformedCovenant = covenant;
            if (existing is null) { TrackedOperators.Add(entry); newFinal++; }
            else if (existing.IsAcquired || existing.Role != TrackingRole.Final || existing.TransformedCovenant != entry.TransformedCovenant
                     || !existing.Equipments.SequenceEqual(entry.Equipments))
                TrackedOperators[TrackedOperators.IndexOf(existing)] = entry;
        }

        // Transition operators in route order (开局 first), with the stages that use them. Operators only used
        // by stages that are already behind the current terminal level are not tracked at all.
        var transitionStages = new Dictionary<string, (List<string> Stages, int ExpiresAt)>(StringComparer.Ordinal);
        for (var i = 0; i < route.Stages.Count; i++)
        {
            var stage = route.Stages[i];
            if (ReferenceEquals(stage, finalStage)) continue;
            var expiresAt = route.ExpiresAt(i);
            foreach (var op in stage.Operators.Where(o => !transforms.ContainsKey(o.Name)))
            {
                if (!transitionStages.TryGetValue(op.Name, out var info)) info = ([], -1);
                info.Stages.Add(stage.Definition.Name);
                // Kept as long as its latest stage is (0 = never expires wins).
                var at = info.ExpiresAt == 0 || expiresAt == 0 ? 0 : Math.Max(info.ExpiresAt, expiresAt);
                transitionStages[op.Name] = (info.Stages, at);
            }
        }
        var level = TerminalLevel;
        foreach (var (name, (stages, expiresAt)) in transitionStages)
        {
            var existing = TrackedOperators.FirstOrDefault(e => e.Name == name);
            if (existing is { Role: TrackingRole.Final }) continue;
            var entry = new TrackingEntry
            {
                Name = name, Type = TrackingType.Operator, Role = TrackingRole.Transition,
                Stages = string.Join(" · ", stages), ExpiresAt = expiresAt
            };
            if (entry.ExpiredAt(level))
            {
                if (existing is not null) TrackedOperators.Remove(existing);
                continue;
            }
            if (existing is null) { TrackedOperators.Add(entry); newTransition++; }
            else TrackedOperators[TrackedOperators.IndexOf(existing)] = entry;
        }
        RefreshEquipmentRequirements();
        return (newFinal, newTransition);
    }

    public void AddTracking(string name, TrackingType type)
    {
        var entries = type == TrackingType.Operator ? TrackedOperators : TrackedEquipment;
        var existing = entries.FirstOrDefault(e => e.Name == name);
        if (existing is null)
        {
            entries.Add(new TrackingEntry { Name = name, Type = type });
            return;
        }
        if (!existing.IsAcquired && (type != TrackingType.Equipment || existing.IsManual)) return;
        // Tracked again (or an automatic equipment entry tracked by hand): keep 大成 / 过渡 and the stages.
        var again = Copy(existing, existing.IsExpired);
        again.IsAcquired = false;
        again.IsManual = true;
        entries[entries.IndexOf(existing)] = again;
        if (type == TrackingType.Operator) RefreshEquipmentRequirements();
    }

    public void ToggleAcquired(string name)
    {
        var entries = TrackedOperators.Any(e => e.Name == name) ? TrackedOperators : TrackedEquipment;
        var entry = entries.Single(e => e.Name == name);
        if (entry.IsAcquired)
        {
            AddTracking(name, entry.Type);
            return;
        }
        var acquired = Copy(entry, entry.IsExpired);
        acquired.IsAcquired = true;
        entries[entries.IndexOf(entry)] = acquired;
        if (entry.Type == TrackingType.Operator)
        {
            RefreshEquipmentRequirements();
            return;
        }
        RemoveEquipmentRequirement(name);
    }

    public void RemoveTracking(string name)
    {
        var opEntry = TrackedOperators.FirstOrDefault(e => e.Name == name);
        if (opEntry != null)
        {
            TrackedOperators.Remove(opEntry);
            RefreshEquipmentRequirements();
            return;
        }

        var eqEntry = TrackedEquipment.FirstOrDefault(e => e.Name == name);
        if (eqEntry != null)
        {
            TrackedEquipment.Remove(eqEntry);
            RemoveEquipmentRequirement(name);
        }
    }

    private void RemoveEquipmentRequirement(string name)
    {
        foreach (var entry in TrackedOperators.Where(e => e.Equipments.Contains(name) || e.CandidateEquipments.Any(c => c.Name == name)).ToList())
        {
            var without = Copy(entry, entry.IsExpired);
            without.Equipments = entry.Equipments.Where(e => e != name).ToList();
            without.CandidateEquipments.RemoveAll(c => c.Name == name);
            TrackedOperators[TrackedOperators.IndexOf(entry)] = without;
        }
    }

    private static bool SameCandidates(IReadOnlyList<CandidateEquipment> a, IReadOnlyList<CandidateEquipment> b)
        => a.Count == b.Count && a.Zip(b).All(p => p.First.Name == p.Second.Name && p.First.IsPriority == p.Second.IsPriority);

    /// <summary>Operators who want <paramref name="equipment"/>, and how (for the tracking list's subtitle).</summary>
    public IReadOnlyList<(string Operator, TrackedEquipmentKind Kind)> EquipmentWanters(string equipment)
        => [.. TrackedOperators.Where(e => !e.IsAcquired).SelectMany(e => e.Equipments.Contains(equipment)
                ? [(e.Name, TrackedEquipmentKind.Planned)]
                : e.CandidateEquipments.Where(c => c.Name == equipment)
                    .Select(c => (e.Name, c.IsPriority ? TrackedEquipmentKind.Priority : TrackedEquipmentKind.Candidate)))];

    public void ClearAllTracking()
    {
        TrackedOperators.Clear();
        TrackedEquipment.Clear();
    }

    public void ClearPlannedTransformation(string name, string equipmentName, LineupEquipmentService equipmentRules)
    {
        var entry = TrackedOperators.Single(e => e.Name == name);
        var slot = new LineupSlot { OperatorName = name, Equipments = [.. entry.Equipments], IsElite = entry.IsElite };
        if (equipmentName.Length > 0)
        {
            var index = equipmentRules.Transformations(slot).FindIndex(t => t.EquipmentName == equipmentName);
            equipmentRules.SetTransformation(slot, index, "");
        }
        var cleared = Copy(entry, entry.IsExpired);
        cleared.TransformedCovenant = null;
        cleared.Equipments = slot.Equipments;
        TrackedOperators[TrackedOperators.IndexOf(entry)] = cleared;
        RefreshEquipmentRequirements();
    }

    /// <summary>
    /// Equipment entries follow the tracked operators: slot equipment is 装备, candidates are 优先 / 候选, and the
    /// strongest reason wins when several operators want the same piece. Manual and acquired entries are left alone.
    /// </summary>
    private void RefreshEquipmentRequirements()
    {
        var required = new Dictionary<string, TrackedEquipmentKind>(StringComparer.Ordinal);
        void Want(string name, TrackedEquipmentKind kind)
        {
            if (!required.TryGetValue(name, out var had) || kind < had) required[name] = kind;
        }
        foreach (var op in TrackedOperators.Where(e => !e.IsAcquired))
        {
            foreach (var name in op.Equipments) Want(name, TrackedEquipmentKind.Planned);
            foreach (var c in op.CandidateEquipments)
                Want(c.Name, c.IsPriority ? TrackedEquipmentKind.Priority : TrackedEquipmentKind.Candidate);
        }
        foreach (var entry in TrackedEquipment.Where(e => !e.IsManual && !e.IsAcquired && !required.ContainsKey(e.Name)).ToList())
            TrackedEquipment.Remove(entry);
        foreach (var (name, kind) in required)
        {
            var existing = TrackedEquipment.FirstOrDefault(e => e.Name == name);
            if (existing is null)
                TrackedEquipment.Add(new TrackingEntry { Name = name, Type = TrackingType.Equipment, IsManual = false, EquipmentKind = kind });
            else if (!existing.IsManual && existing.EquipmentKind != kind)
            {
                var updated = Copy(existing, existing.IsExpired);
                updated.EquipmentKind = kind;
                TrackedEquipment[TrackedEquipment.IndexOf(existing)] = updated;
            }
        }
    }

    public bool IsTracked(string name)
    {
        return TrackedOperators.Any(e => e.Name == name && !e.IsAcquired)
            || TrackedEquipment.Any(e => e.Name == name && !e.IsAcquired);
    }

    public void LoadEntries(List<TrackingEntry> entries)
    {
        TrackedOperators.Clear();
        TrackedEquipment.Clear();
        foreach (var entry in entries)
        {
            entry.Stages = RouteStageNames.Normalize(entry.Stages);
            if (entry.Type == TrackingType.Operator)
                TrackedOperators.Add(entry);
            else
                TrackedEquipment.Add(entry);
        }
        RefreshEquipmentRequirements();
        RefreshExpiry();
    }
}
