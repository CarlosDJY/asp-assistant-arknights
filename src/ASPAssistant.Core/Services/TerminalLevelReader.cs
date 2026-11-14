// Modified for ASP Assistant Arknights on 2026-11-11; see NOTICE.
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

/// <summary>
/// Reads the terminal level from the upgrade card left of the shop (“LEVEL IV / 升级”, or “已满级” at 6).
/// <para>
/// The card moves with the number of shop slots, so it is found by OCR of its button text. The big Roman
/// numeral is then read from its shape: PaddleOCR misreads the stylised Ursus / Closure numerals (“V” for
/// “IV”, “W”…), while the strokes themselves are clean — I is a straight or slanted bar, V is a wedge (or,
/// when its point fades as in Closure, two arms leaning towards each other).
/// </para>
/// </summary>
public sealed class TerminalLevelReader(IOcrEngine ocr, Func<byte[], int, int, int, int, int[]?>? cropLuma = null)
{
    public const int MaxLevel = 6;

    private static readonly Dictionary<string, int> Numerals = new(StringComparer.Ordinal)
    {
        ["I"] = 1, ["II"] = 2, ["III"] = 3, ["IV"] = 4, ["V"] = 5, ["VI"] = 6,
    };

    private readonly Func<byte[], int, int, int, int, int[]?> _cropLuma = cropLuma ?? CropLuma;

    /// <summary>Row-major brightness of a crop of the PNG (System.Drawing; tests may substitute a decoder).</summary>
    private static int[]? CropLuma(byte[] png, int x0, int y0, int w, int h)
    {
        using var pixels = BanScreenPixels.Load(png);
        if (pixels is null) return null;
        var luma = new int[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                luma[y * w + x] = pixels.Luma(x0 + x, y0 + y);
        return luma;
    }

    /// <summary>Returns the level, or null when the shop's upgrade card is not visible / not readable.</summary>
    public async Task<int?> ReadAsync(byte[] png, int width, int height, GameShopTheme theme)
    {
        double s = height / 1080.0;
        // The 升级 / 已满级 text sits at y≈980 (1080p) in every skin. The card sits left of the shop slots, so its
        // x depends on the slot count (6 / 5 / 4 / 3 slots ↔ 218 px steps). Reading those few spots without text
        // detection is ~10× cheaper than detecting over the whole bottom band, which is only the fallback.
        (string Text, int CenterX)? button = null;
        for (int slots = 6; slots >= 3 && button is null; slots--)
        {
            int c = (int)Math.Round(width / 2.0 + (-685 + 218 * (6 - slots)) * s);
            int bx = c - (int)(55 * s);
            if (bx < 0) continue;
            var line = await ocr.RecognizeLineAsync(png, bx, (int)(962 * s), (int)(110 * s), (int)(40 * s));
            var hit = line.FirstOrDefault(t => IsButton(t.Text));
            if (hit is not null) button = (hit.Text, c);
        }
        if (button is null)
        {
            var texts = await ocr.RecognizeRegionAsync(png, 0, (int)(950 * s), (int)(width * 0.75), (int)(80 * s));
            var hit = texts.FirstOrDefault(t => t.Text.Contains("满级", StringComparison.Ordinal))
                      ?? texts.FirstOrDefault(t => IsButton(t.Text));
            if (hit is not null) button = (hit.Text, hit.Box.X + hit.Box.W / 2);
        }
        if (button is not { } found) return null;
        if (found.Text.Contains("满级", StringComparison.Ordinal)) return MaxLevel;

        // Closure's dark strokes sit above their fading reflections; include the full glyph at window scale.
        int x0 = found.CenterX - (int)((theme == GameShopTheme.Closure ? 80 : 60) * s), y0 = (int)((theme == GameShopTheme.Closure ? 845 : 870) * s);
        int w = (int)((theme == GameShopTheme.Closure ? 160 : 120) * s), h = (int)((theme == GameShopTheme.Closure ? 110 : 95) * s);
        if (x0 < 0 || x0 + w > width) return null;

        var luma = _cropLuma(png, x0, y0, w, h);
        if (luma is null) return null;
        var numeral = RomanNumeralShape.Read(luma, w, h, darkGlyph: theme == GameShopTheme.Closure);
        if (Numerals.TryGetValue(numeral, out var level)) return level;

        // Fallback: plain OCR of the numeral (works for the default terminal skin).
        var texts2 = await ocr.RecognizeRegionAsync(png, x0, y0, w, h);
        foreach (var t in texts2)
            if (Numerals.TryGetValue(NormalizeOcr(t.Text), out level)) return level;
        return null;
    }

    private static bool IsButton(string text)
        => text.Contains("升级", StringComparison.Ordinal) || text.Contains("满级", StringComparison.Ordinal);

    private static string NormalizeOcr(string text)
    {
        var chars = text.Trim()
            .Replace("Ⅰ", "I").Replace("Ⅱ", "II").Replace("Ⅲ", "III").Replace("Ⅳ", "IV").Replace("Ⅴ", "V").Replace("Ⅵ", "VI")
            .Select(c => c is 'l' or '|' or '1' or 'i' ? 'I' : c is 'v' ? 'V' : c);
        return new string(chars.ToArray());
    }
}

/// <summary>Reads a Roman numeral (I–VI) from the strokes of a cropped glyph area.</summary>
public static class RomanNumeralShape
{
    private sealed record Stroke(int X0, int X1, int Height, double TopWidth, double BottomWidth, double Slope);

    /// <param name="luma">Row-major 0–255 brightness of the crop around the numeral.</param>
    /// <param name="darkGlyph">True when the numeral is darker than its background (Closure skin).</param>
    public static string Read(int[] luma, int width, int height, bool darkGlyph)
    {
        // Inner area: skips the card frame and most of the “LEVEL” caption.
        int ix0 = (int)(width * 0.12), ix1 = (int)(width * 0.88);
        int iy0 = (int)(height * 0.10), iy1 = (int)(height * 0.92);
        int w = ix1 - ix0, h = iy1 - iy0;
        if (w < 8 || h < 8) return "";

        var values = new int[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
                values[y * w + x] = luma[(iy0 + y) * width + ix0 + x];
        var sorted = (int[])values.Clone();
        Array.Sort(sorted);
        int p1 = sorted[(int)(sorted.Length * 0.01)], p99 = sorted[(int)(sorted.Length * 0.99)];
        var mask = new bool[w * h];
        for (int i = 0; i < mask.Length; i++)
            mask[i] = darkGlyph ? values[i] <= p1 + 60 : values[i] >= p99 - 35;

        var strokes = Strokes(mask, w, h);
        if (strokes.Count == 0) return "";
        var tallest = strokes.Max(s => s.Height);
        strokes = strokes.Where(s => s.Height >= 0.7 * tallest).OrderBy(s => s.X0).ToList();

        var result = new System.Text.StringBuilder();
        for (int i = 0; i < strokes.Count; i++)
        {
            var c = strokes[i];
            if (c.TopWidth > 1.7 * c.BottomWidth && c.TopWidth > 0.65 * c.Height)
            {
                result.Append('V');
                continue;
            }
            // A V whose point is lost: left arm leans right, right arm leans left, close together.
            if (i + 1 < strokes.Count && c.Slope > 0.2 && strokes[i + 1].Slope < -0.2
                && strokes[i + 1].X0 - c.X1 < 0.6 * c.Height)
            {
                result.Append('V');
                i++;
                continue;
            }
            result.Append('I');
        }
        return result.ToString();
    }

    private static List<Stroke> Strokes(bool[] mask, int w, int h)
    {
        var label = new int[w * h];
        var strokes = new List<Stroke>();
        var queue = new Queue<int>();
        var pts = new List<int>();
        int next = 0;
        for (int start = 0; start < mask.Length; start++)
        {
            if (!mask[start] || label[start] != 0) continue;
            next++;
            pts.Clear();
            label[start] = next;
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                int p = queue.Dequeue();
                pts.Add(p);
                int py = p / w, px = p % w;
                // Vertical gaps of 2 px are bridged (scan lines / glitch stripes in the skins).
                for (int dy = -2; dy <= 2; dy++)
                    for (int dx = -1; dx <= 1; dx++)
                    {
                        int ny = py + dy, nx = px + dx;
                        if (ny < 0 || ny >= h || nx < 0 || nx >= w) continue;
                        int q = ny * w + nx;
                        if (mask[q] && label[q] == 0) { label[q] = next; queue.Enqueue(q); }
                    }
            }

            int minX = int.MaxValue, maxX = int.MinValue, minY = int.MaxValue, maxY = int.MinValue;
            foreach (var p in pts)
            {
                int x = p % w, y = p / w;
                minX = Math.Min(minX, x); maxX = Math.Max(maxX, x);
                minY = Math.Min(minY, y); maxY = Math.Max(maxY, y);
            }
            int ch = maxY - minY + 1, cw = maxX - minX + 1;
            // The LEVEL capsule can share the crop, but is much wider than a numeral stroke.
            if (cw > 2 * ch) continue;
            if (ch < (int)(0.18 * h) || cw > 0.7 * w || minX == 0 || maxX == w - 1) continue;
            if (pts.Count < 0.75 * ch) continue;
            int band = Math.Max(1, (int)(ch * 0.25));
            var (topWidth, topCenter) = RowStats(pts, w, minY, minY + band);
            var (bottomWidth, bottomCenter) = RowStats(pts, w, maxY - band, maxY);
            strokes.Add(new Stroke(minX, maxX, ch, topWidth, bottomWidth, (bottomCenter - topCenter) / ch));
        }
        return strokes;
    }

    /// <summary>Mean row width and mean x of the stroke's pixels between rows a and b.</summary>
    private static (double Width, double Center) RowStats(List<int> pts, int w, int a, int b)
    {
        var rows = new Dictionary<int, (int Min, int Max)>();
        double sumX = 0;
        int n = 0;
        foreach (var p in pts)
        {
            int y = p / w, x = p % w;
            if (y < a || y > b) continue;
            rows[y] = rows.TryGetValue(y, out var r) ? (Math.Min(r.Min, x), Math.Max(r.Max, x)) : (x, x);
            sumX += x;
            n++;
        }
        return n == 0 ? (0, 0) : (rows.Values.Average(r => r.Max - r.Min + 1), sumX / n);
    }
}
