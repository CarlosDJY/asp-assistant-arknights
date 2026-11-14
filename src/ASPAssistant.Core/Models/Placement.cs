// Modified for ASP Assistant Regulation on 2026-11-14; see NOTICE.
namespace ASPAssistant.Core.Models;

/// <summary>What the placement planner optimises: combat only, a mix, or stacking first.</summary>
public enum PlacementMode { Combat, Balanced, Stacking }

/// <summary>How an operator is used on the board; decides where it should stand and what buffs it values.</summary>
public enum PlacementRole { Damage, Tank, Support, Stacker }

public static class PlacementRoleText
{
    public static string Of(PlacementRole role) => role switch
    {
        PlacementRole.Damage => "输出",
        PlacementRole.Tank => "承伤",
        PlacementRole.Support => "辅助",
        _ => "叠层",
    };
}

/// <summary>
/// One operator of the pool the planner may use. The game allows the same operator on the field more than once;
/// each copy is its own candidate (<paramref name="Copy"/> 0, 1, …) with its own <see cref="Key"/>.
/// </summary>
public sealed record PlacementCandidate(string Name, bool IsElite, int Copy = 0)
{
    public string Key => PlacementKeys.Of(Name, Copy);
    /// <summary>
    /// Covenants this operator holds through 转职 (变形同构体 + a 转职 item it carries). They go wherever the operator
    /// goes: on the field they count, in the 整备区 they do not.
    /// </summary>
    public IReadOnlyList<string> Transforms { get; init; } = [];
}

/// <summary>
/// A 转职 worth adding: <see cref="Covenant"/> has <see cref="Have"/> of the <see cref="Need"/> operators it takes to
/// activate; carried by <see cref="Holder"/> (an operator on the field) it would be active. Gain: the plan's score
/// change.
/// </summary>
/// <summary>An operator's share of the team's damage dealt, damage taken and healing in the simulated fight (0–1).</summary>
public sealed record CombatShare(double Damage, double Taken, double Healing);

public sealed record TransformHint(string Covenant, int Have, int Need, string Holder, double Gain);

public static class PlacementKeys
{
    public static string Of(string name, int copy) => copy == 0 ? name : $"{name}#{copy + 1}";
    public static string NameOf(string key) { int i = key.LastIndexOf('#'); return i > 0 ? key[..i] : key; }
}

/// <summary>A cell and facing the player fixed by hand; the planner keeps it and arranges the others around it.</summary>
public readonly record struct PlacementPin(BoardCell Cell, Facing Facing);

public sealed record PlacedOperator(
    string Name, BoardCell Cell, Facing Facing, PlacementRole Role, bool IsPinned, IReadOnlyList<string> Notes, string Key)
{
    public string CellCode => PlacementCells.Code(Cell);
}

public sealed record BenchedOperator(string Name, string Note);

/// <summary>One ground lane: cells from spawn to defence, its share of the walking enemies, where it is blocked.</summary>
public sealed record PlacementLane(string Label, double Share, IReadOnlyList<BoardCell> Cells, BoardCell? Block);

/// <summary>A quick pass / fail line (阻挡够不够、核心盟约人数…); <paramref name="IsHint"/> = information only.</summary>
/// <summary>One line under the plan: a short text, and the full story for its tooltip (null: the text says it all).</summary>
public sealed record PlacementCheck(string Text, bool Ok, bool IsHint = false, string? Detail = null);

/// <summary>Another cell the selected operator could use, scored against the current plan.</summary>
public sealed record PlacementAlternative(BoardCell Cell, Facing Facing, double Delta, string Text)
{
    public string CellCode => PlacementCells.Code(Cell);
}

public sealed class PlacementPlan
{
    public required GarrisonMap Map { get; init; }
    public required IReadOnlyList<PlacedOperator> Placed { get; init; }
    public required IReadOnlyList<BenchedOperator> Bench { get; init; }
    public required IReadOnlyList<string> ActiveCovenants { get; init; }
    public required IReadOnlyList<PlacementCheck> Checks { get; init; }
    public double Combat { get; init; }
    public double Stacking { get; init; }
    public double Total { get; init; }
    /// <summary>The value the search maximised for this placement (with the simulation); null without it.</summary>
    public double? Objective { get; init; }
    /// <summary>Ground lanes the plan defends, with their share of the round's walking enemies and block point.</summary>
    public IReadOnlyList<PlacementLane> Lanes { get; init; } = [];
    /// <summary>What the plan is based on: this round's routes (ground / flying counts) or the map's fixed routes.</summary>
    public string Basis { get; init; } = "";
    /// <summary>Weight of stacking against this round's fight (mode × how many rounds are left).</summary>
    public double StackWeight { get; init; } = 1;
    /// <summary>Whether flying enemies were planned for.</summary>
    public bool Flying { get; init; }
    /// <summary>The simulated steady stream against this placement (null when the simulation is off).</summary>
    public ASPAssistant.Core.Services.SimResult? Simulation { get; init; }
    /// <summary>The (calibrated) stream the simulation used; pass it back to compare other placements fairly.</summary>
    public ASPAssistant.Core.Services.WaveScenario? Scenario { get; init; }
    /// <summary>With real streams: the HP scale the search used (pass it back to compare other placements fairly).</summary>
    public double? StreamFactor { get; init; }
    /// <summary>Estimated layers each active covenant gains per round with this placement (from the stacking rules).</summary>
    public IReadOnlyDictionary<string, double> LayerGains { get; init; } = new Dictionary<string, double>();
    /// <summary>With real streams: each simulated stream at its real strength.</summary>
    public IReadOnlyList<StreamOutcome> StreamResults { get; init; } = [];
    /// <summary>
    /// Per placed operator (its summon included), its share of the simulated fight: damage dealt, damage taken and
    /// healing done, each as a fraction of the team's total (averaged over the streams). Empty without the simulation.
    /// </summary>
    public IReadOnlyDictionary<string, CombatShare> Shares { get; init; } = new Dictionary<string, CombatShare>();
    /// <summary>转职 that would activate a covenant one operator short, best first.</summary>
    public IReadOnlyList<TransformHint> TransformHints { get; init; } = [];

    /// <summary>By key (a copy of a duplicated operator has its own key).</summary>
    public PlacedOperator? Find(string key) => Placed.FirstOrDefault(p => p.Key == key);
}

public static class PlacementCells
{
    /// <summary>Row letter A–E plus column + 3 (the board's left-most column −2 is 1), e.g. D7.</summary>
    public static string Code(BoardCell cell) => $"{(char)('A' + cell.Row)}{cell.Col + 3}";

    public static string FacingText(Facing f) => f switch
    {
        Facing.Right => "朝右",
        Facing.Down => "朝下",
        Facing.Left => "朝左",
        _ => "朝上",
    };

    public static (int Col, int Row) Vector(Facing f) => f switch
    {
        Facing.Right => (1, 0),
        Facing.Down => (0, 1),
        Facing.Left => (-1, 0),
        _ => (0, -1),
    };
}

/// <summary>One real stream of the round against the plan (label like 飞行·妖怪群, its chance, the simulated fight).</summary>
public sealed record StreamOutcome(string Label, string Type, double Weight, ASPAssistant.Core.Services.SimResult Result);
