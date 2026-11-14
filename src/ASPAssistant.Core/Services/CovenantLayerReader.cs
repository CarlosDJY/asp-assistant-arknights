// Added for ASP Assistant Regulation on 2026-11-14; see NOTICE.
using System.Text.RegularExpressions;

namespace ASPAssistant.Core.Services;

/// <summary>
/// Reads the covenants' layers from the badge strip at the top of the battle screen: each active covenant is a round
/// badge with its layer count under the icon and its name below (covenants that do not stack show no number). The
/// strip is centred and can scroll, so only the badges in view are read. Coordinates are for a 16:9 client scaled
/// from 1080p.
/// </summary>
public sealed partial class CovenantLayerReader(IOcrEngine ocr, IEnumerable<string> covenantNames)
{
    private readonly string[] _names = [.. covenantNames.Where(n => n.Length > 0).OrderByDescending(n => n.Length)];

    // The strip (1080p): numbers at y ≈ 144, names at y ≈ 166; badges from x ≈ 570 to ≈ 1370 (centred).
    private const double Left = 0.27, Right = 0.76, Top = 125, Height = 62;
    private const int MaxLayers = 5000;

    [GeneratedRegex(@"^\s*(\d{1,4})?\s*(.*?)\s*$")]
    private static partial Regex NumberAndName();

    /// <summary>Layers by covenant for the badges in view; empty when the strip is not on screen.</summary>
    public async Task<IReadOnlyDictionary<string, int>> ReadAsync(byte[] png, int width, int height)
    {
        double s = height / 1080.0;
        int x0 = (int)(width * Left), y0 = (int)(Top * s);
        var texts = await ocr.RecognizeRegionAsync(png, x0, y0, (int)(width * (Right - Left)), (int)(Height * s));
        return Parse(texts, s);
    }

    /// <summary>Pairs each covenant name with the number just above it (or read together with it).</summary>
    public IReadOnlyDictionary<string, int> Parse(IEnumerable<OcrTextResult> texts, double scale)
    {
        var names = new List<(string Name, double X, double Y, int? Value)>();
        var numbers = new List<(int Value, double X, double Y)>();
        foreach (var t in texts)
        {
            var m = NumberAndName().Match(t.Text.Replace(" ", ""));
            double cx = t.Box.X + t.Box.W / 2.0, cy = t.Box.Y + t.Box.H / 2.0;
            int? value = m.Groups[1].Success && int.TryParse(m.Groups[1].Value, out var v) && v <= MaxLayers ? v : null;
            string rest = m.Groups[2].Value;
            string? name = rest.Length > 0 ? Match(rest) : null;
            if (name is not null) names.Add((name, cx, cy, value));
            else if (value is int n && rest.Length == 0) numbers.Add((n, cx, cy));
        }
        var layers = new Dictionary<string, int>();
        foreach (var (name, x, y, own) in names)
        {
            if (own is int direct) { layers[name] = direct; continue; }
            // The number sits right above the name, within half a badge's width.
            var above = numbers.Where(n => Math.Abs(n.X - x) <= 55 * scale && n.Y < y && y - n.Y <= 45 * scale)
                .OrderBy(n => Math.Abs(n.X - x)).ToList();
            if (above.Count > 0) layers[name] = above[0].Value;
        }
        return layers;
    }

    private string? Match(string text)
    {
        foreach (var n in _names)
            if (text == n || text.Length >= 2 && (n.Contains(text, StringComparison.Ordinal) || text.Contains(n, StringComparison.Ordinal)))
                return n;
        return null;
    }
}
