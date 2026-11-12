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
}

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

/// <summary>A quick pass / fail line (阻挡够不够、核心盟约人数…); <paramref name="IsHint"/> = information only.</summary>
public sealed record PlacementCheck(string Text, bool Ok, bool IsHint = false);

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
