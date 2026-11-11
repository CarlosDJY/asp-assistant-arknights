using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

public sealed record EquipmentTransformation(string EquipmentName, string Covenant);

public sealed class LineupEquipmentService(IDictionary<string, string> equipmentToCovenant)
{
    public const string Shapeshifter = "变形同构体";
    public const string MechName = "“暴躁铁皮”";
    public const string MechStrategy = "band_mech";
    public IReadOnlyDictionary<string, string> EquipmentToCovenant { get; } = new Dictionary<string, string>(equipmentToCovenant);

    public static int Capacity(LineupSlot slot) => slot.OperatorName == MechName ? slot.IsElite ? 6 : 4 : 2;
    public static int VisibleEquipmentSlots(LineupSlot slot) => Math.Max(slot.OperatorName == MechName ? 6 : 2, slot.Equipments.Count);
    public static int VisibleTransformationSlots(LineupSlot slot) => slot.OperatorName == MechName ? 5 : 1;

    public List<EquipmentTransformation> Transformations(LineupSlot slot) => !slot.Equipments.Contains(Shapeshifter) ? [] :
        slot.Equipments.Where(EquipmentToCovenant.ContainsKey).Distinct()
            .Select(e => new EquipmentTransformation(e, EquipmentToCovenant[e])).ToList();

    public string EquipmentFor(string covenant) => EquipmentToCovenant.First(p => p.Value == covenant).Key;
    public bool CanTransformInto(string covenant) => EquipmentToCovenant.Values.Contains(covenant);

    public void SetTransformation(LineupSlot slot, int index, string equipmentName)
    {
        var current = Transformations(slot);
        slot.TransformedCovenant = null;
        var replacing = index < current.Count;
        if (replacing && current[index].EquipmentName == equipmentName) return;
        if (replacing)
        {
            slot.Equipments.Remove(current[index].EquipmentName);
            slot.CoreEquipments.Remove(current[index].EquipmentName);
        }
        if (equipmentName.Length > 0)
        {
            if (!slot.Equipments.Contains(Shapeshifter)) slot.Equipments.Insert(0, Shapeshifter);
            if (!slot.Equipments.Contains(equipmentName)) slot.Equipments.Add(equipmentName);
        }
        else if (replacing && !slot.Equipments.Any(EquipmentToCovenant.ContainsKey))
        {
            slot.Equipments.Remove(Shapeshifter);
            slot.CoreEquipments.Remove(Shapeshifter);
        }
    }

    // v1.5.0 used a separate single-covenant field. Convert it once without discarding existing gear.
    public bool UpgradeSlot(LineupSlot slot)
    {
        if (slot.TransformedCovenant is null) return false;
        if (slot.TransformedCovenant.Length == 0) { slot.TransformedCovenant = null; return true; }
        if (!CanTransformInto(slot.TransformedCovenant)) return false;
        var equipment = EquipmentFor(slot.TransformedCovenant);
        if (!slot.Equipments.Contains(Shapeshifter)) slot.Equipments.Insert(0, Shapeshifter);
        if (!slot.Equipments.Contains(equipment)) slot.Equipments.Add(equipment);
        slot.TransformedCovenant = null;
        return true;
    }

    public bool Upgrade(Lineup lineup)
    {
        var changed = false;
        foreach (var slot in lineup.Slots.Concat(lineup.EarlySlots).Concat(lineup.MidSlots).Concat(lineup.SupportSlots))
            changed |= UpgradeSlot(slot);
        return changed;
    }
}
