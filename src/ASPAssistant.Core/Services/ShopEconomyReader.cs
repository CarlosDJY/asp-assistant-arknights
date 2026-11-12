// Added for ASP Assistant Arknights on 2026-11-13; see NOTICE.
// The reading regions follow StrongholdAssistant's EconomyReader (LBEILC/StrongholdAssistant, Apache-2.0).
using System.Text.RegularExpressions;

namespace ASPAssistant.Core.Services;

/// <summary>
/// What the shop phase shows besides the cards: round, funds, the upgrade price and the mode name.
/// Any part that was not readable is null.
/// </summary>
public sealed record ShopEconomy(int? Round, int? Money, int? UpgradeCost, bool UpgradeMaxed, string? Mode, bool IsPreparation)
{
    public bool HasAny => Round is not null || Money is not null || UpgradeCost is not null || UpgradeMaxed;
}

/// <summary>
/// Reads the shop HUD from the same frame as the shop cards.
/// <list type="bullet">
/// <item>Top bar: 「回合 N」 left of the phase title (休息一下 / 休整 during preparation); the mode name
/// (规程等级 N / …模拟) sits at the top left.</item>
/// <item>Bottom: the upgrade card's price badge above 升级, and the funds badge at the right edge on the same row.</item>
/// </list>
/// The bottom HUD is anchored to the bottom edge and grows with the in-game UI size (90–100), so its scale is
/// measured from where 升级 sits instead of assumed from the frame height.
/// </summary>
public sealed class ShopEconomyReader(IOcrEngine ocr)
{
    // 1080p at UI size 90: 升级 text top is 101 px above the bottom edge; badges are 38 px tall, 273 px up.
    private const double ButtonFromBottom = 101;
    private const double BadgeFromBottom = 273;
    private const double BadgeHeight = 38;

    private static readonly Regex RoundInline = new(@"回合\D{0,2}(\d{1,2})$", RegexOptions.Compiled);
    private static readonly Regex ModeName = new(@"^(规程等级\d{1,2}|[一-龥]{2}模拟)$", RegexOptions.Compiled);

    public async Task<ShopEconomy> ReadAsync(byte[] png, int width, int height)
    {
        double s = Math.Min(width / 1920.0, height / 1080.0);
        var (round, mode, preparation) = await ReadTopAsync(png, width, height, s);

        // Upgrade card: find 升级 / 已满级 in the lower-left band, then derive the HUD scale from its height above the bottom.
        var bottom = await ocr.RecognizeRegionAsync(png, 0, (int)(height - 160 * s * 1.15), (int)(width * 0.6), (int)(160 * s * 1.15));
        var button = bottom.FirstOrDefault(t => t.Text.Contains("满级", StringComparison.Ordinal))
                     ?? bottom.FirstOrDefault(t => t.Text.Contains("升级", StringComparison.Ordinal))
                     ?? await ProbeButtonAsync(png, width, s);
        // UI size 90 → 1.0, 100 → 10/9. OCR boxes wobble by a few pixels, so small deviations count as 90.
        double ratio = button is null ? 1 : (height - button.Box.Y) / (ButtonFromBottom * s);
        double k = s * (ratio < 1.04 ? 1 : Math.Min(ratio, 10.0 / 9));
        bool maxed = button?.Text.Contains("满级", StringComparison.Ordinal) == true;

        int? cost = null;
        if (button is not null && !maxed)
        {
            int center = button.Box.X + button.Box.W / 2;
            cost = await ReadBadgeAsync(png, center - (int)(26 * k), height, k, (int)(52 * k));
        }
        // Skip the coin icon at the badge's left; it pulls the recogniser towards letters.
        var money = await ReadBadgeAsync(png, width - (int)(120 * k), height, k, (int)(48 * k));
        return new(round, money, cost, maxed, mode, preparation);
    }

    /// <summary>
    /// A number inside a hexagonal badge. Single stylised digits are easy to misread (7 → 2 / Z, 0 → D / 8) and
    /// the badge outline reads as a letter when the crop touches it, so a few slightly shifted crops vote:
    /// the number at least two agree on wins, otherwise nothing is reported. A wrong price is worse than none.
    /// </summary>
    private async Task<int?> ReadBadgeAsync(byte[] png, int x, int height, double k, int w)
    {
        var votes = new List<int>();
        foreach (var (dx, dy, grow) in new[] { (0, 0, 0), (0, 3, 6), (0, -3, 0), (-3, 0, 6), (3, 0, 0) })
        {
            int y = (int)(height - (BadgeFromBottom + dy) * k);
            if (Number(await ocr.RecognizeLineAsync(png, x + (int)(dx * k), y, w + (int)(grow * k), (int)(BadgeHeight * k))) is { } n)
                votes.Add(n);
        }
        var best = votes.GroupBy(v => v).OrderByDescending(g => g.Count()).FirstOrDefault();
        return best is not null && best.Count() >= 2 && best.Count() * 2 > votes.Count ? best.Key : null;
    }

    /// <summary>
    /// The greyed 升级 of some skins is missed by text detection; read the few spots the card can occupy
    /// (left of 6 / 5 / 4 / 3 shop slots, 218 px apart at UI size 90), as <see cref="TerminalLevelReader"/> does.
    /// </summary>
    private async Task<OcrTextResult?> ProbeButtonAsync(byte[] png, int width, double s)
    {
        for (int slots = 6; slots >= 3; slots--)
        {
            int c = (int)Math.Round(width / 2.0 + (-685 + 218 * (6 - slots)) * s);
            int x = c - (int)(55 * s), y = (int)(962 * s);
            if (x < 0) continue;
            var line = await ocr.RecognizeLineAsync(png, x, y, (int)(110 * s), (int)(40 * s));
            if (line.FirstOrDefault(t => t.Text.Contains("升级", StringComparison.Ordinal) || t.Text.Contains("满级", StringComparison.Ordinal)) is { } hit)
                return hit with { Box = (x, y + (int)(17 * s), (int)(110 * s), (int)(35 * s)) };
        }
        return null;
    }

    private async Task<(int? Round, string? Mode, bool Preparation)> ReadTopAsync(byte[] png, int width, int height, double s)
    {
        var top = await ocr.RecognizeRegionAsync(png, 0, 0, (int)(width * 0.6), (int)(height * 0.09));
        string? mode = top.Select(t => t.Text.Trim()).FirstOrDefault(t => ModeName.IsMatch(t));
        bool preparation = top.Any(t => t.Text.Contains("休息", StringComparison.Ordinal) || t.Text.Contains("休整", StringComparison.Ordinal));

        int? round = null;
        foreach (var t in top)
            if (RoundInline.Match(t.Text.Trim()) is { Success: true } m) round = int.Parse(m.Groups[1].Value);
        var label = top.FirstOrDefault(t => t.Text.Trim() == "回合");
        if (round is null && label is not null)
        {
            // The number sits in its own box to the right; read that box alone (detection often drops it).
            var line = await ocr.RecognizeLineAsync(png, label.Box.X + label.Box.W + (int)(5 * s), label.Box.Y - (int)(2 * s),
                (int)(52 * s * 1.15), label.Box.H + (int)(4 * s));
            round = Number(line);
            round ??= Number(top.Where(t => t.Box.X > label.Box.X + label.Box.W && t.Box.X < label.Box.X + label.Box.W + 80 * s
                && Math.Abs(t.Box.Y - label.Box.Y) < 20 * s).OrderBy(t => t.Box.X).Take(1).ToList());
        }
        return (round is > 0 and < 100 ? round : null, mode, preparation);
    }

    /// <summary>A 1–3 digit number made of the line's text; common OCR slips (O→0, l/I→1) are fixed.</summary>
    public static int? Number(IReadOnlyList<OcrTextResult> line)
    {
        var text = string.Concat(line.Select(t => t.Text)).Trim()
            .Replace('O', '0').Replace('o', '0').Replace('l', '1').Replace('I', '1').Replace('|', '1');
        return Regex.IsMatch(text, @"^\d{1,3}$") ? int.Parse(text) : null;
    }
}
