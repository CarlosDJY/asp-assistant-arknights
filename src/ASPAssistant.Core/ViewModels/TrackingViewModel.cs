// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ASPAssistant.Core.Models;

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
        Name = e.Name, Type = e.Type, TransformedCovenant = e.TransformedCovenant, Role = e.Role,
        Stages = e.Stages, ExpiresAt = e.ExpiresAt, IsExpired = expired
    };

    public (int Operators, int Equipment) TrackLineup(Lineup lineup)
    {
        if (!TrackMultipleLineups) ClearAllTracking();
        var previousOperators = TrackedOperators.Count;
        var previousEquipment = TrackedEquipment.Count;
        foreach (var slot in lineup.Slots)
        {
            if (!string.IsNullOrEmpty(slot.OperatorName))
            {
                var existing = TrackedOperators.FirstOrDefault(e => e.Name == slot.OperatorName);
                var entry = new TrackingEntry
                {
                    Name = slot.OperatorName, Type = TrackingType.Operator,
                    TransformedCovenant = slot.TransformedCovenant
                };
                if (existing is null) TrackedOperators.Add(entry);
                else if (existing.TransformedCovenant != slot.TransformedCovenant || existing.Role != TrackingRole.Final)
                    TrackedOperators[TrackedOperators.IndexOf(existing)] = entry;
            }
            foreach (var equipment in slot.Equipments)
                if (!string.IsNullOrEmpty(equipment))
                    AddTracking(equipment, TrackingType.Equipment);
        }
        return (TrackedOperators.Count - previousOperators, TrackedEquipment.Count - previousEquipment);
    }

    /// <summary>
    /// Tracks every operator of a 道中路线 in one list: 大成 operators as <see cref="TrackingRole.Final"/>,
    /// operators only used on the way as <see cref="TrackingRole.Transition"/> (with the stages that use them).
    /// An operator already tracked as 大成 stays 大成. Returns (new 大成, new 过渡) counts.
    /// </summary>
    public (int Final, int Transition) TrackRoute(PlannedRoute route)
    {
        if (!TrackMultipleLineups) ClearAllTracking();
        var finalStage = route.Stages.LastOrDefault(s => s.IsFinal) ?? route.Stages.LastOrDefault();
        if (finalStage is null) return (0, 0);
        var transforms = finalStage.Operators.ToDictionary(o => o.Name, o => o.TransformedCovenant ?? "", StringComparer.Ordinal);
        int newFinal = 0, newTransition = 0;
        foreach (var op in finalStage.Operators)
        {
            var existing = TrackedOperators.FirstOrDefault(e => e.Name == op.Name);
            var entry = new TrackingEntry
            {
                Name = op.Name, Type = TrackingType.Operator, Role = TrackingRole.Final,
                TransformedCovenant = transforms[op.Name]
            };
            if (existing is null) { TrackedOperators.Add(entry); newFinal++; }
            else if (existing.Role != TrackingRole.Final || existing.TransformedCovenant != entry.TransformedCovenant)
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
        return (newFinal, newTransition);
    }

    public void AddTracking(string name, TrackingType type)
    {
        if (IsTracked(name))
            return;

        var entry = new TrackingEntry { Name = name, Type = type };
        if (type == TrackingType.Operator)
            TrackedOperators.Add(entry);
        else
            TrackedEquipment.Add(entry);
    }

    public void RemoveTracking(string name)
    {
        var opEntry = TrackedOperators.FirstOrDefault(e => e.Name == name);
        if (opEntry != null)
        {
            TrackedOperators.Remove(opEntry);
            return;
        }

        var eqEntry = TrackedEquipment.FirstOrDefault(e => e.Name == name);
        if (eqEntry != null)
            TrackedEquipment.Remove(eqEntry);
    }

    public void ClearAllTracking()
    {
        TrackedOperators.Clear();
        TrackedEquipment.Clear();
    }

    public void ClearPlannedTransformation(string name)
    {
        var entry = TrackedOperators.Single(e => e.Name == name);
        TrackedOperators[TrackedOperators.IndexOf(entry)] = new TrackingEntry
        {
            Name = name, Type = TrackingType.Operator, Role = entry.Role, Stages = entry.Stages,
            ExpiresAt = entry.ExpiresAt, IsExpired = entry.IsExpired
        };
    }

    public bool IsTracked(string name)
    {
        return TrackedOperators.Any(e => e.Name == name)
            || TrackedEquipment.Any(e => e.Name == name);
    }

    public void LoadEntries(List<TrackingEntry> entries)
    {
        TrackedOperators.Clear();
        TrackedEquipment.Clear();
        foreach (var entry in entries)
        {
            if (entry.Type == TrackingType.Operator)
                TrackedOperators.Add(entry);
            else
                TrackedEquipment.Add(entry);
        }
        RefreshExpiry();
    }
}
