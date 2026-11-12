namespace ASPAssistant.Core.Models;

/// <summary>What a deployment cell allows (from data/garrison_maps.json).</summary>
public enum BoardCellType
{
    /// <summary>Not deployable (obstacles, decoration, the enemy-side patterned tiles).</summary>
    Blocked,
    /// <summary>Ground: melee and ranged operators.</summary>
    Ground,
    /// <summary>Special ground: deployable like ground, with a map-specific effect.</summary>
    Special,
    /// <summary>High ground: ranged operators only.</summary>
    High,
    Defense,
    Spawn,
}

/// <summary>Camera state of the preparation board: the shop folded (pushed in) or open.</summary>
public enum BoardCamera { Collapsed, Expanded }

public readonly record struct BoardCell(int Col, int Row);

/// <summary>
/// Maps board grid coordinates (col, row; cell (c, r) spans c..c+1, r..r+1) to normalized
/// client-area coordinates (0..1) with a homography fitted to four grid corners.
/// </summary>
public sealed class BoardProjection
{
    private readonly double[] _h; // row-major 3×3 with h[8] = 1
    private readonly double[] _inv;

    public BoardProjection(IReadOnlyList<((double Col, double Row) Grid, (double X, double Y) Screen)> corners)
    {
        if (corners.Count != 4) throw new ArgumentException("需要 4 组对应点", nameof(corners));
        var a = new double[8, 9];
        for (int i = 0; i < 4; i++)
        {
            var ((c, r), (x, y)) = corners[i];
            double[] r1 = [c, r, 1, 0, 0, 0, -x * c, -x * r, x];
            double[] r2 = [0, 0, 0, c, r, 1, -y * c, -y * r, y];
            for (int k = 0; k < 9; k++) { a[2 * i, k] = r1[k]; a[2 * i + 1, k] = r2[k]; }
        }
        for (int i = 0; i < 8; i++)
        {
            int p = i;
            for (int k = i + 1; k < 8; k++) if (Math.Abs(a[k, i]) > Math.Abs(a[p, i])) p = k;
            for (int k = 0; k < 9; k++) (a[i, k], a[p, k]) = (a[p, k], a[i, k]);
            for (int k = 0; k < 8; k++)
            {
                if (k == i) continue;
                double f = a[k, i] / a[i, i];
                for (int j = i; j < 9; j++) a[k, j] -= f * a[i, j];
            }
        }
        _h = new double[9];
        for (int i = 0; i < 8; i++) _h[i] = a[i, 8] / a[i, i];
        _h[8] = 1;
        _inv = Invert(_h);
    }

    public (double X, double Y) Project(double col, double row) => Apply(_h, col, row);

    /// <summary>Grid position under a normalized client point.</summary>
    public (double Col, double Row) Unproject(double x, double y) => Apply(_inv, x, y);

    public BoardCell CellAt(double x, double y)
    {
        var (c, r) = Unproject(x, y);
        return new BoardCell((int)Math.Floor(c), (int)Math.Floor(r));
    }

    /// <summary>The cell's four corners, clockwise from top-left, normalized.</summary>
    public (double X, double Y)[] CellCorners(BoardCell cell) =>
    [
        Project(cell.Col, cell.Row), Project(cell.Col + 1, cell.Row),
        Project(cell.Col + 1, cell.Row + 1), Project(cell.Col, cell.Row + 1),
    ];

    private static (double, double) Apply(double[] m, double u, double v)
    {
        double w = m[6] * u + m[7] * v + m[8];
        return ((m[0] * u + m[1] * v + m[2]) / w, (m[3] * u + m[4] * v + m[5]) / w);
    }

    private static double[] Invert(double[] m)
    {
        double a = m[0], b = m[1], c = m[2], d = m[3], e = m[4], f = m[5], g = m[6], h = m[7], i = m[8];
        double A = e * i - f * h, B = -(d * i - f * g), C = d * h - e * g;
        double det = a * A + b * B + c * C;
        return
        [
            A / det, -(b * i - c * h) / det, (b * f - c * e) / det,
            B / det, (a * i - c * g) / det, -(a * f - c * d) / det,
            C / det, -(a * h - b * g) / det, (a * e - b * d) / det,
        ];
    }
}

/// <summary>One Garrison Protocol map: deployment cells and the fixed ground routes.</summary>
public sealed class GarrisonMap
{
    public GarrisonMap(string id, string name, int firstColumn, BoardCellType[,] cells,
        IReadOnlyList<IReadOnlyList<BoardCell>> routes)
    {
        Id = id;
        Name = name;
        FirstColumn = firstColumn;
        Cells = cells;
        Routes = routes;
    }

    public string Id { get; }
    public string Name { get; }
    /// <summary>Column of <c>Cells[row, 0]</c> (the grid starts left of column 0).</summary>
    public int FirstColumn { get; }
    /// <summary>[row, column - FirstColumn].</summary>
    public BoardCellType[,] Cells { get; }
    /// <summary>Ordered ground routes, each from a spawn to the defense point.</summary>
    public IReadOnlyList<IReadOnlyList<BoardCell>> Routes { get; }

    public int RowCount => Cells.GetLength(0);
    public int ColumnCount => Cells.GetLength(1);

    public BoardCellType At(BoardCell cell)
    {
        int c = cell.Col - FirstColumn;
        return cell.Row < 0 || cell.Row >= RowCount || c < 0 || c >= ColumnCount
            ? BoardCellType.Blocked
            : Cells[cell.Row, c];
    }

    public IEnumerable<(BoardCell Cell, BoardCellType Type)> AllCells()
    {
        for (int r = 0; r < RowCount; r++)
            for (int c = 0; c < ColumnCount; c++)
                yield return (new BoardCell(c + FirstColumn, r), Cells[r, c]);
    }

    public static bool CanDeploy(BoardCellType type, bool melee) =>
        type is BoardCellType.Ground or BoardCellType.Special || type == BoardCellType.High && !melee;
}

/// <summary>Everything in data/garrison_maps.json plus the recognition fingerprints.</summary>
public sealed class GarrisonBoardData
{
    public required IReadOnlyDictionary<BoardCamera, BoardProjection> Cameras { get; init; }
    public required IReadOnlyList<GarrisonMap> Maps { get; init; }
    /// <summary>Bench (整备区) row and inclusive column range.</summary>
    public required (int Row, int FirstColumn, int LastColumn) Bench { get; init; }
    /// <summary>map id → camera → fingerprint (RGB per sample point); may be empty.</summary>
    public required IReadOnlyDictionary<string, IReadOnlyDictionary<BoardCamera, byte[][]>> Fingerprints { get; init; }

    public GarrisonMap? Find(string id) => Maps.FirstOrDefault(m => m.Id == id);
}
