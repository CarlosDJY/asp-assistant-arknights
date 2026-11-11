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
                else if (existing.TransformedCovenant != slot.TransformedCovenant)
                    TrackedOperators[TrackedOperators.IndexOf(existing)] = entry;
            }
            foreach (var equipment in slot.Equipments)
                if (!string.IsNullOrEmpty(equipment))
                    AddTracking(equipment, TrackingType.Equipment);
        }
        return (TrackedOperators.Count - previousOperators, TrackedEquipment.Count - previousEquipment);
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
        TrackedOperators[TrackedOperators.IndexOf(entry)] = new TrackingEntry { Name = name, Type = TrackingType.Operator };
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
    }
}
