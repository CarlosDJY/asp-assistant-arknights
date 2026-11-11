// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

/// <summary>
/// Reads the shop's bottom name strip, independently of card colour and slot count.
/// Uses the selected game's name band and colour preprocessing.
/// Gold light on an operator is not evidence of an elite upgrade.
/// </summary>
public sealed class ShopReader(IOcrEngine engine, IReadOnlyList<string> catalogNames)
{
    private readonly string[] _names = catalogNames.OrderByDescending(n => n.Length).ToArray();
    private volatile GameShopTheme _theme = GameShopTheme.Terminal;
    public GameShopTheme Theme { get => _theme; set => _theme = value; }

    public async Task<IReadOnlyList<ShopItem>> ReadAsync(
        byte[] screenshot, int width, int height, IReadOnlyList<string> trackedNames)
    {
        if (trackedNames.Count == 0)
            return [];
        var theme = Theme;

        using var stream = new MemoryStream(screenshot);
        using var source = new Bitmap(stream);
        // Small captures lose thin strokes in the frozen white labels. Normalize
        // their working height before cropping; output coordinates remain ratios.
        using var enlarged = height < 1080 ? new Bitmap((int)Math.Round(width * 1080.0 / height), 1080) : null;
        if (enlarged != null)
        {
            using var graphics = Graphics.FromImage(enlarged);
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(source, 0, 0, enlarged.Width, enlarged.Height);
            width = enlarged.Width;
            height = enlarged.Height;
        }
        var workingImage = enlarged ?? source;
        // Keep the whole bottom quarter: UI scaling moves both the names and
        // the shop control. A thin fixed strip cuts their strokes at 100% UI.
        int canvasTop = height * 3 / 4;
        using var strip = new Bitmap(width * 2, (height - canvasTop) * 2, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(strip))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(workingImage, new Rectangle(0, 0, strip.Width, strip.Height),
                0, canvasTop, width, height - canvasTop, GraphicsUnit.Pixel);
        }
        // 24-bit BMP: no PNG encode/decode on every scan tick (see ImageBytes)
        var encoded = ImageBytes.Encode(strip);
        var texts = (await engine.RecognizeRegionAsync(encoded, 0, 0, strip.Width, strip.Height)).ToList();
        var control = texts.FirstOrDefault(t => t.Text.Contains("收起", StringComparison.Ordinal)
            && t.Box.X + t.Box.W > width * 1.6);
        if (control is null)
        {
            // Side padding on ultrawide captures can put the control left of 80%.
            int controlLeft = Math.Max((int)(strip.Width * .65), strip.Width - height);
            var controls = await engine.RecognizeRegionAsync(encoded, controlLeft, 0,
                strip.Width - controlLeft, strip.Height);
            control = controls.FirstOrDefault(t => t.Text.Contains("收起", StringComparison.Ordinal));
        }
        if (control is null)
            return [];

        // Anchor the name line to actual text, or to the adjacent shop control
        // when freezing has hidden every catalogue name from the first pass.
        var items = MatchNames(texts.Where(t => Math.Abs(t.Box.Y + t.Box.H / 2.0
            - control.Box.Y - control.Box.H / 2.0) < control.Box.H * 1.5).ToList(),
            width, height, canvasTop, _names).ToList();
        var centers = items.Select(i => (i.OcrRegion.Y * height - canvasTop
            + i.OcrRegion.Height * height / 2) * 2).Order().ToArray();
        var heights = items.Select(i => i.OcrRegion.Height * height * 2).Order().ToArray();
        double nameCenter = centers.Length == 0 ? control.Box.Y + control.Box.H * 1.1 : centers[centers.Length / 2];
        double textHeight = heights.Length == 0 ? control.Box.H * .8 : heights[heights.Length / 2];
        int nameTop = Math.Max(0, (int)(nameCenter - textHeight * .65));
        int nameHeight = Math.Min(strip.Height - nameTop, (int)Math.Ceiling(textHeight * 1.3));
        // Repeated vertical card edges provide the pitch at the current UI size.
        // Portrait details vary by row; a card border persists across most rows.
        var edges = new List<double>();
        int edgeStart = -1, edgeLast = -1;
        int edgeTop = (int)(source.Height * .77), edgeBottom = (int)(source.Height * .87);
        for (int x = (int)(source.Width * .15); x < source.Width * .90; x++)
        {
            int votes = 0;
            for (int y = edgeTop; y < edgeBottom; y++)
            {
                var a = source.GetPixel(x - 1, y);
                var b = source.GetPixel(x, y);
                if (Math.Abs(a.R - b.R) + Math.Abs(a.G - b.G) + Math.Abs(a.B - b.B) > 100)
                    votes++;
            }
            if (votes <= (edgeBottom - edgeTop) * .45)
                continue;
            if (edgeStart >= 0 && x - edgeLast > source.Height * .014)
            {
                edges.Add((edgeStart + edgeLast) / 2.0);
                edgeStart = -1;
            }
            if (edgeStart < 0) edgeStart = x;
            edgeLast = x;
        }
        if (edgeStart >= 0) edges.Add((edgeStart + edgeLast) / 2.0);
        var pitches = edges.Zip(edges.Skip(1), (a, b) => b - a)
            .Where(gap => gap > source.Height * .15 && gap < source.Height * .25).Order().ToArray();
        var cardLefts = new List<int>();
        double pitch = 0;
        if (pitches.Length >= 2)
        {
            pitch = pitches[pitches.Length / 2];
            var aligned = edges.Select(anchor => edges.Where(edge => Math.Abs((edge - anchor) / pitch
                - Math.Round((edge - anchor) / pitch)) < .08).ToArray()).OrderByDescending(row => row.Length).First();
            if (aligned.Length >= 4)
            {
                for (double left = aligned[0]; left < aligned[^1] - pitch * .5; left += pitch)
                    cardLefts.Add((int)Math.Round(left * width / source.Width));
            }
            pitch *= width / (double)source.Width;
        }
        if (items.Count == 0 && cardLefts.Count > 0 && theme != GameShopTheme.Closure)
        {
            // Frozen names can all disappear. Their band still has a fixed
            // position inside the detected card, scaled by its actual pitch.
            nameTop = (int)Math.Round((height - canvasTop) * 2
                - pitch * (theme == GameShopTheme.Terminal ? .56 : .673));
            nameHeight = (int)Math.Round(pitch * (theme == GameShopTheme.Terminal ? .236 : .273));
        }
        using (var original = (Bitmap)strip.Clone())
        using (var graphics = Graphics.FromImage(strip))
        {
            graphics.Clear(Color.Black);
            graphics.DrawImage(original, new Rectangle(0, nameTop, strip.Width, nameHeight),
                0, nameTop, strip.Width, nameHeight, GraphicsUnit.Pixel);
            if (theme != GameShopTheme.Closure)
            {
                // Red-channel contrast removes the blue freezing veil without
                // discarding the original dark lettering used by other skins.
                using var attributes = new ImageAttributes();
                float offset = theme == GameShopTheme.Terminal ? -1.3f : -1.72f;
                attributes.SetColorMatrix(new ColorMatrix([
                    [5, 5, 5, 0, 0], [0, 0, 0, 0, 0], [0, 0, 0, 0, 0],
                    [0, 0, 0, 1, 0], [offset, offset, offset, 0, 1]]));
                for (int i = 0; i < cardLefts.Count; i++)
                {
                    int left = cardLefts[i] * 2;
                    int cardWidth = (int)Math.Round(pitch * 2);
                    graphics.DrawImage(original, new Rectangle(left, i % 2 == 0 ? 60 : 200, cardWidth, nameHeight),
                        left, nameTop, cardWidth, nameHeight, GraphicsUnit.Pixel, attributes);
                }
            }
        }
        encoded = ImageBytes.Encode(strip);
        var recovered = await engine.RecognizeRegionAsync(encoded, 0, 0, strip.Width, strip.Height);
        var mapped = recovered.Select(t => t.Box.Y + t.Box.H / 2.0 < nameTop
            ? t with { Box = (t.Box.X, t.Box.Y + nameTop
                - (t.Box.Y + t.Box.H / 2.0 < 170 ? 60 : 200), t.Box.W, t.Box.H) } : t).ToList();
        foreach (var item in MatchNames(mapped, width, height, canvasTop, _names))
        {
            if (items.Any(i => i.Name == item.Name
                && Math.Max(i.OcrRegion.X, item.OcrRegion.X)
                    < Math.Min(i.OcrRegion.X + i.OcrRegion.Width, item.OcrRegion.X + item.OcrRegion.Width)))
                continue;
            items.Add(item);
        }
        if (theme != GameShopTheme.Closure)
        {
            for (int slot = 0; slot < cardLefts.Count; slot++)
            {
                int left = cardLefts[slot];
                if (items.Any(i => (i.OcrRegion.X + i.OcrRegion.Width / 2) * width >= left
                    && (i.OcrRegion.X + i.OcrRegion.Width / 2) * width < left + pitch))
                    continue;
                foreach (double fraction in new[] { .82, .50 })
                {
                    int lineLeft = (int)Math.Round((left + pitch * .14) * 2);
                    int lineWidth = (int)Math.Round(pitch * fraction * 2);
                    var lines = await engine.RecognizeLineAsync(encoded, lineLeft,
                        nameTop, lineWidth, nameHeight);
                    var matches = MatchNames(lines.Select(t => new OcrTextResult(
                        t.Text.Replace("斯卡菜", "斯卡蒂").Replace("复泰", "复奏"),
                        (lineLeft, nameTop, lineWidth, nameHeight))).ToList(), width, height, canvasTop, _names);
                    if (matches.Count == 0) continue;
                    items.AddRange(matches);
                    break;
                }
            }
        }
        var occurrences = new Dictionary<string, int>();
        items.Sort((a, b) => a.OcrRegion.X.CompareTo(b.OcrRegion.X));
        foreach (var item in items)
        {
            occurrences.TryGetValue(item.Name, out int occurrence);
            item.Id = $"{item.Name}@{occurrence}";
            occurrences[item.Name] = occurrence + 1;
        }
        return items.Where(i => trackedNames.Contains(i.Name)).ToList();
    }

    public IReadOnlyList<ShopItem> Match(IReadOnlyList<OcrTextResult> texts,
        int width, int height, int stripTop, IReadOnlyList<string> trackedNames)
    {
        // Do not leave markers on menus/closed shops with unrelated bottom text.
        if (!texts.Any(t => t.Text.Contains("收起", StringComparison.Ordinal) && t.Box.X + t.Box.W > width * 1.6))
            return [];
        return MatchNames(texts, width, height, stripTop, trackedNames);
    }

    private IReadOnlyList<ShopItem> MatchNames(IReadOnlyList<OcrTextResult> texts,
        int width, int height, int stripTop, IReadOnlyList<string> trackedNames)
    {
        var items = new List<ShopItem>();
        var occurrences = new Dictionary<string, int>();
        foreach (var text in texts.OrderBy(t => t.Box.X))
        {
            var value = text.Text.Replace(" ", "").Replace("傑", "杰").Replace("奧", "奥")
                .Replace("卫成推荐信", "卫戍推荐信").Replace("野繫", "野鬃")
                .Replace("卫成推荐值", "卫戍推荐信")
                .Replace("野紫", "野鬃").Replace("云边", "云迹");
            // Resolve against the whole catalogue before filtering tracking. Otherwise
            // tracking 维式重锤 would also match an untracked 终极维式重锤.
            var matches = new List<(string Name, int Start, int Length)>();
            foreach (var name in _names)
            {
                var candidate = name.Replace(" ", "");
                int start = value.IndexOf(candidate, StringComparison.Ordinal);
                while (start >= 0)
                {
                    if ((candidate.Length > 1 || value.Trim('M', 'X', 'A', '△', '入', 'λ', '×') == candidate)
                        && !matches.Any(m => start < m.Start + m.Length && start + candidate.Length > m.Start))
                        matches.Add((name, start, candidate.Length));
                    start = value.IndexOf(candidate, start + candidate.Length, StringComparison.Ordinal);
                }
            }

            // Wide images can merge neighbouring labels (or 鸿雪 + 收起). Split
            // their OCR bounds by character span instead of discarding a slot.
            foreach (var match in matches.OrderBy(m => m.Start))
            {
                if (!trackedNames.Contains(match.Name))
                    continue;
                // The shopkeeper panel can resemble a one-character operator
                // (e.g. "山"); only accept names within the merchandise area.
                if (text.Box.X + text.Box.W * (match.Start + match.Length / 2.0) / value.Length
                    > width + height * 1440 / 1080.0)
                    continue;
                occurrences.TryGetValue(match.Name, out int occurrence);
                occurrences[match.Name] = occurrence + 1;
                items.Add(new ShopItem
                {
                    Id = $"{match.Name}@{occurrence}",
                    Name = match.Name,
                    OcrRegion = ((text.Box.X + text.Box.W * match.Start / (double)value.Length) / (2 * width),
                        (stripTop + text.Box.Y / 2.0) / height,
                        text.Box.W * match.Length / (double)value.Length / (2 * width), text.Box.H / (2.0 * height)),
                });
            }
        }
        return items;
    }
}
