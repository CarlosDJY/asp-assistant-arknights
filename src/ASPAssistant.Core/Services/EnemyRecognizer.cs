// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
using System.Text.RegularExpressions;
using ASPAssistant.Core.GameModes;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

/// <summary>Reads the Regulation info screen's right-hand enemy panel.</summary>
public sealed class EnemyRecognizer(IOcrEngine ocr, IOcrStrategy ocrStrategy, BossDataService bossData)
{
    public async Task<EnemyResult?> RecognizeAsync(byte[] screenshot, int imgWidth, int imgHeight)
    {
        var region = ocrStrategy.GetScanRegions().Single(r => r.Name == "EnemySectionScan");
        var texts = await ocr.RecognizeRegionAsync(screenshot,
            (int)(region.XPercent * imgWidth), (int)(region.YPercent * imgHeight),
            (int)(region.WidthPercent * imgWidth), (int)(region.HeightPercent * imgHeight));
        var anchor = texts.FirstOrDefault(t => Regex.Replace(t.Text, @"\s+", "").Contains("敌方领袖"));
        if (anchor is null)
            return null;

        // Exclude the portrait and map: only text below the leader heading and
        // within the name column belongs to this result.
        var rows = new List<List<OcrTextResult>>();
        foreach (var text in texts
                     .Where(t => t.Box.X >= anchor.Box.X - imgWidth * .035
                         && t.Box.Y > anchor.Box.Y + anchor.Box.H / 2.0)
                     .OrderBy(t => t.Box.Y + t.Box.H / 2.0))
        {
            var row = rows.LastOrDefault();
            if (row is not null && Math.Abs(text.Box.Y + text.Box.H / 2.0
                    - row[0].Box.Y - row[0].Box.H / 2.0) < Math.Max(text.Box.H, row[0].Box.H) * .6)
                row.Add(text);
            else
                rows.Add([text]);
        }
        var lines = rows.Select(row => string.Concat(row.OrderBy(t => t.Box.X).Select(t => t.Text.Trim())))
            .Select(text => Regex.Replace(text, @"\s+", "")).ToList();
        var bossLines = new List<string>();
        var types = new List<string>();
        bool readingTypes = false;
        foreach (var line in lines)
        {
            var parts = Regex.Split(line, "特训[敌致]人?");
            if (parts.Length == 1)
            {
                if (!readingTypes) bossLines.Add(line);
                continue;
            }
            readingTypes = true;
            foreach (var part in parts.Skip(1))
            {
                var name = new string(part.Where(c => c is >= '\u4e00' and <= '\u9fff').ToArray());
                if (name.Length > 0)
                    types.Add($"特训敌人·{name}");
            }
        }
        string boss = string.Concat(bossLines);
        if (bossData.TryMatchBossFromOcr(bossLines, out var matched))
            boss = matched;
        var result = new EnemyResult(boss, types.Distinct(StringComparer.Ordinal).ToList(), lines);
        AppLogger.Info("EnemyRecognizer", $"Boss='{result.BossName}' types=[{string.Join(", ", result.EnemyTypes)}]");
        return result;
    }
}
