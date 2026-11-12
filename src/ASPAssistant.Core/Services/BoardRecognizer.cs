using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

public sealed record BoardMatch(GarrisonMap Map, BoardCamera Camera, double Score, double Margin);

/// <summary>
/// Recognises which Garrison Protocol map is on screen, and whether the shop is folded or open, by
/// comparing per-cell colour samples against fingerprints from build_garrison_map_refs.py. All maps share
/// one camera, so a frame is sampled once per camera and compared with every map; the closest wins.
/// Sample points avoid the cell centre and top-left corner, where the board overlay draws.
/// </summary>
public sealed class BoardRecognizer
{
    // Must match build_garrison_map_refs.py.
    private const int FirstColumn = -1, LastColumn = 7, FirstRow = 0, LastRow = 4;
    private static readonly (double X, double Y)[] SamplePoints = [(0.32, 0.72), (0.72, 0.32)];
    private const double PatchRadius = 0.1;
    /// <summary>Per-sample colour distance cap, so a few covered cells (operators, markers) cannot dominate.</summary>
    private const double DistanceCap = 45;

    private readonly GarrisonBoardData _data;

    public BoardRecognizer(GarrisonBoardData data) => _data = data;

    /// <summary>Accept when the trimmed mean capped distance is at most this.</summary>
    public double MaxScore { get; init; } = 11;
    /// <summary>…and the best other map is at least this much further away.</summary>
    public double MinMargin { get; init; } = 5;

    public BoardMatch? Recognize(byte[] frame)
    {
        var pixels = BmpPixels.TryRead(frame);
        return pixels is null ? null : Recognize(pixels);
    }

    public BoardMatch? Recognize(BmpPixels pixels)
    {
        if (_data.Fingerprints.Count == 0) return null;
        var samples = new Dictionary<BoardCamera, byte[][]>();
        foreach (var (camera, projection) in _data.Cameras)
            samples[camera] = Sample(pixels, projection);

        var scored = new List<(GarrisonMap Map, BoardCamera Camera, double Score, double Trimmed)>();
        foreach (var map in _data.Maps)
        {
            if (!_data.Fingerprints.TryGetValue(map.Id, out var refs) || refs.Count == 0) continue;
            foreach (var (camera, sample) in samples)
            {
                // A map without a reference for this camera falls back to its other one (slightly worse match).
                var reference = refs.TryGetValue(camera, out var r) ? r : refs.Values.First();
                var (full, trimmed) = Distance(sample, reference);
                scored.Add((map, camera, full, trimmed));
            }
        }
        if (scored.Count == 0) return null;
        // The full mean tells the maps apart (they differ on a few cells only); the trimmed mean decides whether
        // the frame is a board at all, so operators standing on cells do not make a correct match fail.
        var best = scored.MinBy(s => s.Score);
        var others = scored.Where(s => s.Map.Id != best.Map.Id).Select(s => s.Score).DefaultIfEmpty(double.MaxValue).Min();
        double margin = others - best.Score;
        return best.Trimmed <= MaxScore && margin >= MinMargin
            ? new BoardMatch(best.Map, best.Camera, best.Trimmed, margin)
            : null;
    }

    private static byte[][] Sample(BmpPixels px, BoardProjection projection)
    {
        var result = new List<byte[]>();
        for (int r = FirstRow; r <= LastRow; r++)
            for (int c = FirstColumn; c <= LastColumn; c++)
                foreach (var (sx, sy) in SamplePoints)
                {
                    var (x, y) = projection.Project(c + sx, r + sy);
                    var (x2, _) = projection.Project(c + sx + PatchRadius, r + sy);
                    int rad = Math.Max(2, (int)(Math.Abs(x2 - x) * px.Width));
                    result.Add(px.Mean((int)(x * px.Width), (int)(y * px.Height), rad));
                }
        return result.ToArray();
    }

    /// <summary>
    /// Mean distance over all samples, and over the closest <see cref="KeptShare"/> of them. Deployed operators cover whole
    /// cells (8–9 of the 45 sampled ones in a normal lineup), so the worst samples are dropped instead of letting
    /// them push a correct match over <see cref="MaxScore"/>; a wrong map still differs on most cells.
    /// </summary>
    private static (double Full, double Trimmed) Distance(byte[][] a, byte[][] b)
    {
        int n = Math.Min(a.Length, b.Length);
        if (n == 0) return (double.MaxValue, double.MaxValue);
        var d = new double[n];
        for (int i = 0; i < n; i++)
        {
            double d0 = a[i][0] - b[i][0], d1 = a[i][1] - b[i][1], d2 = a[i][2] - b[i][2];
            d[i] = Math.Min(Math.Sqrt(d0 * d0 + d1 * d1 + d2 * d2), DistanceCap);
        }
        double total = d.Sum();
        Array.Sort(d);
        int keep = Math.Max(1, (int)(n * KeptShare));
        double sum = 0;
        for (int i = 0; i < keep; i++) sum += d[i];
        return (total / n, sum / keep);
    }

    /// <summary>Share of samples kept by <see cref="Distance"/>.</summary>
    private const double KeptShare = 0.7;
}

/// <summary>
/// Keeps the board overlay steady: a new map / camera must be seen twice in a row before it replaces the
/// current one, and the board is dropped only after several frames without a match (menus, battle effects).
/// </summary>
public sealed class BoardMatchStabilizer
{
    private BoardMatch? _pending;
    private int _misses;

    public int MissesBeforeClear { get; init; } = 4;
    public BoardMatch? Current { get; private set; }

    /// <summary>Feeds one frame's result; returns true when <see cref="Current"/> changed.</summary>
    public bool Observe(BoardMatch? match)
    {
        if (match is null)
        {
            _pending = null;
            if (Current is null || ++_misses < MissesBeforeClear) return false;
            Current = null;
            return true;
        }
        _misses = 0;
        if (Same(match, Current)) { _pending = null; return false; }
        if (Same(match, _pending))
        {
            Current = match;
            _pending = null;
            return true;
        }
        _pending = match;
        return false;
    }

    public void Reset()
    {
        _pending = null;
        _misses = 0;
        Current = null;
    }

    private static bool Same(BoardMatch? a, BoardMatch? b) =>
        a is not null && b is not null && a.Map.Id == b.Map.Id && a.Camera == b.Camera;
}

/// <summary>Direct pixel access to the uncompressed 24/32-bit BMP frames produced by ScreenCaptureService.</summary>
public sealed class BmpPixels
{
    private readonly byte[] _data;
    private readonly int _offset, _stride, _bytesPerPixel;
    private readonly bool _bottomUp;

    private BmpPixels(byte[] data, int offset, int width, int height, int stride, int bpp, bool bottomUp)
    {
        _data = data; _offset = offset; Width = width; Height = height;
        _stride = stride; _bytesPerPixel = bpp; _bottomUp = bottomUp;
    }

    public int Width { get; }
    public int Height { get; }

    public static BmpPixels? TryRead(byte[] data)
    {
        if (data.Length < 54 || data[0] != (byte)'B' || data[1] != (byte)'M') return null;
        int offset = BitConverter.ToInt32(data, 10);
        int width = BitConverter.ToInt32(data, 18);
        int rawHeight = BitConverter.ToInt32(data, 22);
        int bits = BitConverter.ToInt16(data, 28);
        int compression = BitConverter.ToInt32(data, 30);
        if (bits is not (24 or 32) || compression is not (0 or 3) || width <= 0 || rawHeight == 0) return null;
        int bpp = bits / 8;
        int stride = (width * bpp + 3) & ~3;
        int height = Math.Abs(rawHeight);
        if (offset + (long)stride * height > data.Length) return null;
        return new BmpPixels(data, offset, width, height, stride, bpp, rawHeight > 0);
    }

    /// <summary>Mean RGB of the square patch around (x, y), clipped to the image.</summary>
    public byte[] Mean(int x, int y, int radius)
    {
        long r = 0, g = 0, b = 0, n = 0;
        for (int yy = Math.Max(0, y - radius); yy <= Math.Min(Height - 1, y + radius); yy++)
        {
            int row = _offset + (_bottomUp ? Height - 1 - yy : yy) * _stride;
            for (int xx = Math.Max(0, x - radius); xx <= Math.Min(Width - 1, x + radius); xx++)
            {
                int i = row + xx * _bytesPerPixel;
                b += _data[i]; g += _data[i + 1]; r += _data[i + 2]; n++;
            }
        }
        return n == 0 ? [0, 0, 0] : [(byte)(r / n), (byte)(g / n), (byte)(b / n)];
    }
}
