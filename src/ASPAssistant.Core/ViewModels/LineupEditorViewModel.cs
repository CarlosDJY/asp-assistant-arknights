// Modified for ASP Assistant Arknights on 2026-11-12; see NOTICE.
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace ASPAssistant.Core.ViewModels;

/// <summary>
/// 阵容编辑会话的临时 view-model：固定持有 9 个槽位，未填的槽位 OperatorName 为空。
/// 带森蚺“暴躁铁皮”策略时，每个阶段的 9 个槽位里有一个是铁皮（占 1 人口，单独显示）。
/// 所有修改后调用 <see cref="Recalculate"/> 刷新激活盟约统计。
/// </summary>
public partial class LineupEditorViewModel : ObservableObject
{
    public const int MaxSlots = 9;
    public const int MaxTagsPerSlot = 2;

    private readonly CovenantCalculator _calculator;
    private readonly string _initialName;
    private readonly Dictionary<LineupPhase, List<LineupSlot>> _initialSlots;
    private readonly Dictionary<LineupPhase, ObservableCollection<LineupSlot>> _phaseSlots = [];
    private readonly LineupPhase _initialPhase;
    private readonly List<LineupStrategySlot> _initialStrategies;

    public ObservableCollection<LineupStrategySlot> StrategySlots { get; } = [];

    public LineupPhase CurrentPhase { get; private set; }

    public IReadOnlyList<Operator> AllOperators { get; }
    public IReadOnlyList<Equipment> AllEquipment { get; }
    public LineupEquipmentService EquipmentRules => _calculator.EquipmentRules;
    public bool HasMechStrategy => StrategySlots.Any(s => s.StrategyId == LineupEquipmentService.MechStrategy);
    public bool CanSelectOperator(Operator op) => op.RequiredStrategyId.Length == 0 || StrategySlots.Any(s => s.StrategyId == op.RequiredStrategyId);

    public string ValidationError
    {
        get
        {
            foreach (var (phase, slots) in _phaseSlots)
                foreach (var slot in slots.Where(s => s.OperatorName.Length > 0))
                {
                    var op = AllOperators.FirstOrDefault(o => o.Name == slot.OperatorName);
                    if (op is not null && !CanSelectOperator(op))
                        return $"{phase}的{slot.OperatorName}需要在策略中添加森蚺的“暴躁铁皮”策略";
                    if (!string.IsNullOrEmpty(slot.TransformedCovenant))
                        return $"{slot.OperatorName}的旧转职“{slot.TransformedCovenant}”没有对应装备，请重新选择或清除";
                    if (slot.Equipments.Count > LineupEquipmentService.Capacity(slot))
                        return $"{slot.OperatorName}的装备超过{LineupEquipmentService.Capacity(slot)}件，请移除多余装备或启用精锐";
                }
            foreach (var (phase, slots) in _phaseSlots)
                if (slots.Count(s => s.OperatorName.Length > 0) > MaxSlots)
                    return $"{phase}超过 {MaxSlots} 人（暴躁铁皮也占 1 人口），请移除一名干员";
            return "";
        }
    }

    public string LineupId { get; }

    [ObservableProperty]
    private string _name = "新阵容";

    /// <summary>9 个槽位，空槽位的 OperatorName 为空字符串。</summary>
    [ObservableProperty]
    private ObservableCollection<LineupSlot> _slots = [];

    /// <summary>仅展示当前阵容内出现过的盟约（Count &gt; 0）。</summary>
    [ObservableProperty]
    private ObservableCollection<CovenantStat> _activeStats = [];

    [ObservableProperty]
    private int _activatedCount;

    [ObservableProperty]
    private int _filledSlotCount;

    public LineupEditorViewModel(
        Lineup? source,
        IEnumerable<Operator> operators,
        IEnumerable<Equipment> equipment,
        CovenantCalculator calculator)
    {
        _calculator = calculator;
        AllOperators = [.. operators];
        AllEquipment = [.. equipment];

        LineupId = source?.Id ?? Guid.NewGuid().ToString("N");
        if (!string.IsNullOrWhiteSpace(source?.Name))
            _name = source!.Name;

        foreach (var phase in Enum.GetValues<LineupPhase>())
        {
            IEnumerable<LineupSlot> sourceSlots = source is null ? [] : phase switch
            {
                LineupPhase.前期 => source.EarlySlots,
                LineupPhase.中期 => source.MidSlots,
                LineupPhase.后期 => source.Slots,
                LineupPhase.挂件 => source.SupportSlots,
                _ => throw new ArgumentOutOfRangeException(nameof(phase)),
            };
            // 暴躁铁皮 takes one of the 9 places; it is kept last and shown on its own card.
            var list = sourceSlots.ToList();
            var mech = list.FirstOrDefault(IsMech);
            var slots = new ObservableCollection<LineupSlot>(list.Where(s => !IsMech(s)).Take(MaxSlots - (mech is null ? 0 : 1)).Select(CloneSlot));
            while (slots.Count < MaxSlots - (mech is null ? 0 : 1))
                slots.Add(new LineupSlot());
            if (mech is not null) slots.Add(CloneSlot(mech));
            _phaseSlots.Add(phase, slots);
        }
        _initialName = Name;
        StrategySlots = new ObservableCollection<LineupStrategySlot>(
            (source?.Strategies ?? []).Select(s => new LineupStrategySlot { StrategyId = s.StrategyId, Tags = [.. s.Tags] }));
        while (StrategySlots.Count < MaxSlots)
            StrategySlots.Add(new LineupStrategySlot());
        SyncMechSlots();
        _initialStrategies = StrategySlots.Select(s => new LineupStrategySlot { StrategyId = s.StrategyId, Tags = [.. s.Tags] }).ToList();
        _initialSlots = _phaseSlots.ToDictionary(pair => pair.Key, pair => pair.Value.Select(CloneSlot).ToList());
        _initialPhase = source is null ? LineupPhase.前期 : LineupPhase.后期;
        SelectPhase(_initialPhase);
    }

    private static bool IsMech(LineupSlot slot) => slot.OperatorName == LineupEquipmentService.MechName;

    /// <summary>Index of the 暴躁铁皮 slot in <see cref="Slots"/> (−1 when there is none).</summary>
    public int MechSlotIndex => Slots.ToList().FindIndex(IsMech);

    /// <summary>Indices of the places other than 暴躁铁皮 (the grid).</summary>
    public IReadOnlyList<int> RegularSlotIndices => Enumerable.Range(0, Slots.Count).Where(i => !IsMech(Slots[i])).ToList();

    /// <summary>People of a phase, 暴躁铁皮 included (for the phase switch).</summary>
    public int FilledCount(LineupPhase phase) => _phaseSlots[phase].Count(s => s.OperatorName.Length > 0);

    /// <summary>
    /// With the 暴躁铁皮 strategy every phase gets the mech in one of its 9 places (an empty place makes room);
    /// without the strategy an unused mech gives its place back.
    /// </summary>
    private void SyncMechSlots()
    {
        foreach (var slots in _phaseSlots.Values)
        {
            var mech = slots.FirstOrDefault(IsMech);
            if (HasMechStrategy && mech is null)
            {
                if (slots.LastOrDefault(s => s.OperatorName.Length == 0) is { } empty) slots.Remove(empty);
                slots.Add(new LineupSlot { OperatorName = LineupEquipmentService.MechName });
            }
            else if (!HasMechStrategy && mech is not null && mech.Equipments.Count == 0)
            {
                slots.Remove(mech);
                while (slots.Count < MaxSlots) slots.Add(new LineupSlot());
            }
        }
    }

    public void SelectPhase(LineupPhase phase)
    {
        CurrentPhase = phase;
        Slots = _phaseSlots[phase];
        Recalculate();
    }

    /// <summary>设置某个槽位的干员（空字符串 = 清空槽位，包括其装备/标签）。</summary>
    public void SetSlotOperator(int slotIndex, string operatorName)
    {
        if (slotIndex < 0 || slotIndex >= Slots.Count) return;

        if (string.IsNullOrEmpty(operatorName))
        {
            Slots[slotIndex] = new LineupSlot();
        }
        else
        {
            var op = AllOperators.FirstOrDefault(o => o.Name == operatorName);
            if (op is not null && !CanSelectOperator(op))
                throw new InvalidOperationException("必须在策略中添加森蚺的“暴躁铁皮”策略");
            var slot = Slots[slotIndex];
            slot.OperatorName = operatorName;
            // 触发 ItemsControl 刷新（LineupSlot 是 POCO，集合变更通知更可靠）
            Slots[slotIndex] = CloneSlot(slot);
            if (IsMech(slot))
            {
                // Only one 暴躁铁皮: the unused automatic one gives its place back.
                var other = Slots.Where((s, i) => i != slotIndex && IsMech(s) && s.Equipments.Count == 0).ToList();
                foreach (var o in other) Slots.Remove(o);
                while (Slots.Count < MaxSlots) Slots.Add(new LineupSlot());
            }
        }
        Recalculate();
    }

    public void ClearSlotTransformation(int slotIndex, int transformationIndex)
    {
        var slot = CloneSlot(Slots[slotIndex]);
        EquipmentRules.SetTransformation(slot, transformationIndex, "");
        Slots[slotIndex] = slot;
        Recalculate();
    }

    public void ClearLegacyTransformation(int slotIndex)
    {
        var slot = CloneSlot(Slots[slotIndex]);
        slot.TransformedCovenant = null;
        Slots[slotIndex] = slot;
        Recalculate();
    }

    public LineupSlot PreviewTransformation(int slotIndex, int transformationIndex, string equipmentName)
    {
        var slot = CloneSlot(Slots[slotIndex]);
        EquipmentRules.SetTransformation(slot, transformationIndex, equipmentName);
        return slot;
    }

    public void SetSlotTransformation(int slotIndex, int transformationIndex, string equipmentName, IReadOnlyList<string> replacedEquipment)
    {
        var slot = PreviewTransformation(slotIndex, transformationIndex, equipmentName);
        slot.Equipments.RemoveAll(replacedEquipment.Contains);
        slot.CoreEquipments.RemoveAll(e => !slot.Equipments.Contains(e));
        if (slot.Equipments.Count > LineupEquipmentService.Capacity(slot))
            throw new InvalidOperationException("装备位不足，请先选择需要替换的装备");
        Slots[slotIndex] = slot;
        Recalculate();
    }

    public void SetSlotElite(int slotIndex, bool isElite)
    {
        var slot = CloneSlot(Slots[slotIndex]);
        slot.IsElite = isElite;
        Slots[slotIndex] = slot;
        Recalculate();
    }

    /// <summary>切换装备：若已装备则移除；若未装备则在不超过上限时追加。</summary>
    public void ToggleSlotEquipment(int slotIndex, string equipName)
    {
        if (slotIndex < 0 || slotIndex >= Slots.Count) return;
        if (string.IsNullOrEmpty(equipName)) return;
        var slot = CloneSlot(Slots[slotIndex]);
        if (string.IsNullOrEmpty(slot.OperatorName)) return;

        if (slot.Equipments.Contains(equipName))
        {
            slot.Equipments.Remove(equipName);
        }
        else
        {
            if (slot.Equipments.Count >= LineupEquipmentService.Capacity(slot))
                throw new InvalidOperationException("装备位已满，请选择需要替换的装备");
            slot.Equipments.Add(equipName);
        }
        slot.CoreEquipments.RemoveAll(name => !slot.Equipments.Contains(name));
        Slots[slotIndex] = slot;
        Recalculate();
    }

    public void SetSlotEquipment(int slotIndex, int equipmentIndex, string equipmentName, bool isCore)
    {
        var slot = CloneSlot(Slots[slotIndex]);
        if (equipmentName.Length > 0 && equipmentIndex >= slot.Equipments.Count
            && slot.Equipments.Count >= LineupEquipmentService.Capacity(slot))
            throw new InvalidOperationException("装备位已满，请选择需要替换的装备");
        if (equipmentIndex < slot.Equipments.Count)
            slot.Equipments.RemoveAt(equipmentIndex);
        if (equipmentName.Length > 0)
        {
            slot.Equipments.Remove(equipmentName);
            slot.Equipments.Insert(Math.Min(equipmentIndex, slot.Equipments.Count), equipmentName);
        }
        slot.CoreEquipments.RemoveAll(name => !slot.Equipments.Contains(name) || name == equipmentName);
        if (equipmentName.Length > 0 && isCore)
            slot.CoreEquipments.Add(equipmentName);
        Slots[slotIndex] = slot;
        Recalculate();
    }

    public void Reset()
    {
        Name = _initialName;
        StrategySlots.Clear();
        foreach (var slot in _initialStrategies)
            StrategySlots.Add(new LineupStrategySlot { StrategyId = slot.StrategyId, Tags = [.. slot.Tags] });
        foreach (var (phase, slots) in _initialSlots)
            _phaseSlots[phase] = new ObservableCollection<LineupSlot>(slots.Select(CloneSlot));
        SelectPhase(_initialPhase);
    }

    public void Clear()
    {
        var keepMech = HasMechStrategy;
        Slots.Clear();
        for (int i = 0; i < MaxSlots - (keepMech ? 1 : 0); i++)
            Slots.Add(new LineupSlot());
        if (keepMech) Slots.Add(new LineupSlot { OperatorName = LineupEquipmentService.MechName });
        Recalculate();
    }

    public void SetStrategy(int slotIndex, string strategyId)
    {
        StrategySlots[slotIndex] = new LineupStrategySlot
        {
            StrategyId = strategyId,
            Tags = strategyId.Length == 0 ? [] : [.. StrategySlots[slotIndex].Tags],
        };
        SyncMechSlots();
        OnPropertyChanged(nameof(HasMechStrategy));
        Recalculate();
    }

    public void ToggleStrategyTag(int slotIndex, StrategyTag tag)
    {
        var slot = StrategySlots[slotIndex];
        if (slot.Tags.Contains(tag))
            slot.Tags.Remove(tag);
        else
            slot.Tags.Add(tag);
    }

    public void ClearStrategies()
    {
        for (var i = 0; i < StrategySlots.Count; i++)
            StrategySlots[i] = new LineupStrategySlot();
        SyncMechSlots();
        OnPropertyChanged(nameof(HasMechStrategy));
        Recalculate();
    }

    public void ToggleSlotTag(int slotIndex, LineupTag tag)
    {
        if (slotIndex < 0 || slotIndex >= Slots.Count) return;
        var slot = CloneSlot(Slots[slotIndex]);
        if (string.IsNullOrEmpty(slot.OperatorName)) return;

        if (slot.Tags.Contains(tag))
        {
            slot.Tags.Remove(tag);
        }
        else
        {
            if (slot.Tags.Count >= MaxTagsPerSlot)
                slot.Tags.RemoveAt(0);
            slot.Tags.Add(tag);
        }
        Slots[slotIndex] = slot;
        Recalculate();
    }

    public void Recalculate()
    {
        var allStats = _calculator.Compute(new Lineup { Slots = [.. Slots] });
        var visible = allStats.Where(s => s.Count > 0)
                              .OrderByDescending(s => s.Activated)
                              .ThenBy(s => s.BondType == "SEASON" ? 0 : 1)
                              .ThenByDescending(s => s.Count)
                              .ThenBy(s => s.Name)
                              .ToList();
        ActiveStats = new ObservableCollection<CovenantStat>(visible);
        ActivatedCount = allStats.Count(s => s.Activated);
        FilledSlotCount = Slots.Count(s => !string.IsNullOrEmpty(s.OperatorName));
        OnPropertyChanged(nameof(ValidationError));
    }

    public Lineup ToLineup() => new()
    {
        Id = LineupId,
        Name = string.IsNullOrWhiteSpace(Name) ? "未命名阵容" : Name.Trim(),
        Slots = ExportPhase(LineupPhase.后期),
        EarlySlots = ExportPhase(LineupPhase.前期),
        MidSlots = ExportPhase(LineupPhase.中期),
        SupportSlots = ExportPhase(LineupPhase.挂件),
        Strategies = [.. StrategySlots.Where(s => s.StrategyId.Length > 0)
            .Select(s => new LineupStrategySlot { StrategyId = s.StrategyId, Tags = [.. s.Tags] })],
        UpdatedAt = DateTime.UtcNow,
    };

    // An untouched 暴躁铁皮 slot (no gear) is not worth saving.
    private List<LineupSlot> ExportPhase(LineupPhase phase) => [.. _phaseSlots[phase]
        .Where(s => !string.IsNullOrEmpty(s.OperatorName) && (!IsMech(s) || s.Equipments.Count > 0 || s.Tags.Count > 0)).Select(CloneSlot)];

    private static LineupSlot CloneSlot(LineupSlot s) => new()
    {
        OperatorName = s.OperatorName,
        TransformedCovenant = s.TransformedCovenant,
        IsElite = s.IsElite,
        Equipments   = [.. s.Equipments],
        CoreEquipments = [.. s.CoreEquipments],
        Tags         = [.. s.Tags],
    };
}
