// Modified for ASP Assistant Regulation on 2026-11-09; see NOTICE.
using ASPAssistant.Core.GameModes;

namespace ASPAssistant.Core.Services;

/// <summary>An icon slot that no OCR name could be bound to.</summary>
public sealed record BanIconPosition(double X, double Y, bool IsBanned);

/// <summary>
/// Result of reading the two covenant panels: named covenants plus icons whose label could
/// not be read (reported instead of being silently dropped).
/// </summary>
public sealed record BanCovenantReadResult(
    IReadOnlyList<CovenantBanState> Core,
    IReadOnlyList<CovenantBanState> Additional,
    IReadOnlyList<BanIconPosition> UnnamedCore,
    IReadOnlyList<BanIconPosition> UnnamedAdditional,
    int CoreSlots = 0,
    int AdditionalSlots = 0,
    bool SawCoreHeader = false,
    bool SawAdditionalHeader = false,
    bool HasIncompleteCoreRows = false,
    bool HasIncompleteAdditionalRows = false,
    bool NeedsScroll = false)
{
    public bool HasIncompleteRows => HasIncompleteCoreRows || HasIncompleteAdditionalRows;
    public static readonly BanCovenantReadResult Empty = new([], [], [], []);
}

/// <summary>
/// Reads the 核心盟约 / 附加盟约 panels of the "确认本局信息" screen from one screenshot.
///
/// Recognition runs in both directions:
///   icons → pixel grid (<see cref="BanIconGrid"/>) yields every slot and its state;
///   text  → OCR yields covenant names;
///   binding → each name is snapped to its nearest icon slot and the state is read from the
///   icon itself. Icons that receive no name are reported in
///   <see cref="BanCovenantReadResult.UnnamedCore"/> / <see cref="BanCovenantReadResult.UnnamedAdditional"/>.
/// </summary>
public static class BanCovenantReader
{
    public const string CoreRegionName = "BanCoreSection";
    public const string AdditionalRegionName = "BanAdditionalSection";
    public const string CoreHeaderText = "核心盟约";
    public const string AdditionalHeaderText = "附加盟约";
    public const string BanListHeaderText = "本局禁用干员";

    private sealed record Candidate(OcrTextResult Ocr, string Name, int Score);

    /// <summary>
    /// Reads both covenant panels. Falls back to name-box measurement when the icon grid
    /// cannot be detected at all.
    /// </summary>
    public static async Task<BanCovenantReadResult> ReadAsync(
        IOcrEngine ocr,
        IOcrStrategy ocrStrategy,
        byte[] screenshot,
        int imgWidth,
        int imgHeight,
        IReadOnlyList<string> coreCovenantNames,
        IReadOnlyList<string> additionalCovenantNames,
        double greenThreshold = BanIconBrightness.BannedThreshold)
    {
        var regions = ocrStrategy.GetScanRegions();
        var coreRegion = regions.FirstOrDefault(r => r.Name == CoreRegionName);
        var additionalRegion = regions.FirstOrDefault(r => r.Name == AdditionalRegionName);

        if (coreRegion == null || additionalRegion == null)
        {
            AppLogger.Warn("BanAnalyzer",
                $"region '{CoreRegionName}'/'{AdditionalRegionName}' not found in OCR strategy");
            return BanCovenantReadResult.Empty;
        }

        // 两块区域共用一个像素缓冲：少解一次 PNG，读操作只读所以可以并行。
        using var pixels = BanScreenPixels.Load(screenshot);

        // Scan the whole covenant panel once: solo layouts can put the additional title
        // below both core rows, and a scrolled screenshot can hide the core title.
        var panel = await ocr.RecognizeRegionAsync(screenshot, 0, (int)(imgHeight * .13),
            (int)(imgWidth * .70), imgHeight - (int)(imgHeight * .13));
        var coreHeader = panel.Where(t => NormalizeHeader(t.Text).Contains(CoreHeaderText))
            .OrderBy(t => t.Box.Y).FirstOrDefault();
        var additionalHeader = panel.Where(t => NormalizeHeader(t.Text).Contains(AdditionalHeaderText))
            .OrderBy(t => t.Box.Y).FirstOrDefault();
        if (additionalHeader is not null
            && (coreHeader is null || additionalHeader.Box.Y > coreHeader.Box.Y))
        {
            var banHeader = panel.Where(t => t.Box.Y > additionalHeader.Box.Y
                && NormalizeHeader(t.Text).Contains(BanListHeaderText)).OrderBy(t => t.Box.Y).FirstOrDefault();
            var coreTop = coreHeader is null ? .13 : (coreHeader.Box.Y + coreHeader.Box.H) / (double)imgHeight;
            var core = await AnalyzeSectionAsync(ocr, screenshot, pixels, imgWidth, imgHeight,
                coreTop, additionalHeader.Box.Y / (double)imgHeight, panel,
                coreCovenantNames, "Core", greenThreshold);
            var additional = await AnalyzeSectionAsync(ocr, screenshot, pixels, imgWidth, imgHeight,
                (additionalHeader.Box.Y + additionalHeader.Box.H) / (double)imgHeight,
                banHeader is null ? 1 : banHeader.Box.Y / (double)imgHeight, panel,
                additionalCovenantNames, "Additional", greenThreshold);
            return new BanCovenantReadResult(core.Covenants, additional.Covenants, core.Unnamed, additional.Unnamed,
                core.Slots, additional.Slots, coreHeader is not null, true,
                core.HasIncompleteRows, additional.HasIncompleteRows,
                (core.Rows > 1 || coreHeader is null) && banHeader is null && additional.Covenants.Count < additionalCovenantNames.Count
                    && (BanIconGrid.HasClippedBottomRow(pixels!)
                    || additional.HasIncompleteRows
                    || additional.Unnamed.Any(p => p.Y / imgHeight > .88)));
        }

        // A title may be unreadable in the broad OCR pass; retry the existing narrow regions.
        var coreTask = ReadSectionAsync(ocr, screenshot, pixels, imgWidth, imgHeight, coreRegion,
            CoreHeaderText, coreCovenantNames, "Core", greenThreshold);
        var additionalTask = ReadSectionAsync(ocr, screenshot, pixels, imgWidth, imgHeight, additionalRegion,
            AdditionalHeaderText, additionalCovenantNames, "Additional", greenThreshold);
        await Task.WhenAll(coreTask, additionalTask);
        var fixedCore = await coreTask;
        var fixedAdditional = await additionalTask;
        return new BanCovenantReadResult(fixedCore.Covenants, fixedAdditional.Covenants,
            fixedCore.Unnamed, fixedAdditional.Unnamed, fixedCore.Slots, fixedAdditional.Slots,
            fixedCore.SawHeader, fixedAdditional.SawHeader,
            fixedCore.HasIncompleteRows, fixedAdditional.HasIncompleteRows);
    }

    private sealed record SectionRead(
        IReadOnlyList<CovenantBanState> Covenants, IReadOnlyList<BanIconPosition> Unnamed,
        int Slots = 0, bool SawHeader = false, bool HasIncompleteRows = false, int Rows = 0);

    private static async Task<SectionRead> ReadSectionAsync(
        IOcrEngine ocr, byte[] screenshot, BanScreenPixels? pixels, int imgWidth, int imgHeight,
        OcrRegionDefinition region, string headerText,
        IReadOnlyList<string> knownNames, string group, double greenThreshold)
    {
        int rx = (int)(region.XPercent * imgWidth);
        int ry = (int)(region.YPercent * imgHeight);
        int rw = (int)(region.WidthPercent * imgWidth);
        int rh = (int)(region.HeightPercent * imgHeight);
        var texts = await ocr.RecognizeRegionAsync(screenshot, rx, ry, rw, rh);
        var header = texts.FirstOrDefault(t => NormalizeHeader(t.Text).Contains(headerText));
        var top = header is null ? region.YPercent : (header.Box.Y + header.Box.H) / (double)imgHeight;
        var nextHeader = texts.FirstOrDefault(t => t.Box.X < imgWidth * .30 && t.Box.Y > top * imgHeight
            && NormalizeHeader(t.Text).Contains(BanListHeaderText));
        var bottom = nextHeader is null ? region.YPercent + region.HeightPercent : nextHeader.Box.Y / (double)imgHeight;
        return await AnalyzeSectionAsync(ocr, screenshot, pixels, imgWidth, imgHeight,
            top, bottom, texts, knownNames, group, greenThreshold) with { SawHeader = header is not null };
    }

    private static async Task<SectionRead> AnalyzeSectionAsync(
        IOcrEngine ocr, byte[] screenshot, BanScreenPixels? pixels, int imgWidth, int imgHeight,
        double gridTop, double gridBottom, IReadOnlyList<OcrTextResult> texts,
        IReadOnlyList<string> knownNames, string group, double greenThreshold)
    {
        var slots = pixels is null
            ? BanIconGrid.Detect(screenshot, imgWidth, imgHeight, gridTop, gridBottom)
            : BanIconGrid.Detect(pixels, gridTop, gridBottom);
        var candidates = MatchCandidates(texts.Where(t => CenterY(t) >= gridTop * imgHeight
            && CenterY(t) < gridBottom * imgHeight).ToList(), knownNames, imgWidth);
        if (slots.Count == 0)
            return candidates.Count == 0 ? new SectionRead([], [])
                : MeasureFromNameBoxes(screenshot, imgWidth, imgHeight, candidates, group, greenThreshold, pixels);

        if (candidates.Count < slots.Count)
            candidates = await FillNamesBySlotAsync(ocr, screenshot, imgWidth, imgHeight, slots, knownNames, candidates);

        var rows = slots.GroupBy(s => s.Row).Select(g => (Top: g.Min(s => s.Top), Bottom: g.Max(s => s.Bottom)))
            .OrderBy(r => r.Top).ToList();
        var heights = rows.Select(r => r.Bottom - r.Top).Order().ToList();
        var rowHeight = heights[heights.Count / 2];
        // A whole masked/missed row still leaves its vertical space before the next
        // title or screen edge. Derive that evidence from icon height, not season totals.
        var incomplete = rows[0].Top - gridTop * imgHeight > rowHeight * 1.8
            || gridBottom * imgHeight - rows[^1].Bottom > rowHeight * 1.8
            || rows.Zip(rows.Skip(1), (a, b) => b.Top - a.Bottom).Any(gap => gap > rowHeight * 1.8);
        return BindToSlots(candidates, slots, group, greenThreshold)
            with { Slots = slots.Count, HasIncompleteRows = incomplete, Rows = rows.Count };
    }

    /// <summary>
    /// 槽位兜底（批量版）：把所有还没有名字的图标槽的名字框裁出来，
    /// 竖着拼成一张图，只调用一次 OCR，再按纵向位置把结果分配回各自的槽。
    ///
    /// 之前是"每个槽调一次 OCR"（每槽约 110 ms，最坏 18 槽 ≈ 2 秒），
    /// 现在同样像素只调一次（约 150 ms），CPU 明显下降、峰值内存只多一张小合成图。
    /// </summary>
    private static async Task<List<Candidate>> FillNamesBySlotAsync(
        IOcrEngine ocr, byte[] screenshot, int imgWidth, int imgHeight,
        IReadOnlyList<BanIconSlot> slots, IReadOnlyList<string> knownNames, List<Candidate> existing)
    {
        if (slots.Count == 0 || knownNames.Count == 0) return existing;

        double pitch = slots.Count > 1
            ? slots.Zip(slots.Skip(1), (a, b) => Math.Abs(b.CenterX - a.CenterX)).Where(d => d > 1).DefaultIfEmpty(0).Min()
            : 0;
        if (pitch <= 1) pitch = Math.Max(40, imgWidth * 0.06);

        var pending = new List<(BanIconSlot Slot, int X, int Y, int W, int H)>();
        foreach (var slot in slots)
        {
            int iconHeight = Math.Max(24, slot.Bottom - slot.Top);
            if (existing.Any(c => Math.Abs(CenterX(c.Ocr) - slot.CenterX) < pitch * 0.35
                && Math.Abs(CenterY(c.Ocr) - slot.Bottom) <= iconHeight * 0.65))
                continue;

            int boxWidth = (int)(pitch * 0.95);
            int boxHeight = (int)(iconHeight * 0.45);
            int boxX = (int)(slot.CenterX - boxWidth / 2.0);
            int boxY = (int)(slot.Bottom + iconHeight * 0.02);
            if (boxX < 0 || boxY < 0 || boxX + boxWidth > imgWidth || boxY + boxHeight > imgHeight)
                continue;

            pending.Add((slot, boxX, boxY, boxWidth, boxHeight));
        }

        if (pending.Count == 0) return existing;

        var recovered = await RecognizeCompositeAsync(ocr, screenshot, pending, knownNames);
        if (recovered.Count == 0) return existing;

        return [.. existing, .. recovered];
    }

    /// <summary>把多个名字框拼成一张竖图，一次 OCR，再按纵向带回到各自的槽。</summary>
    private static async Task<List<Candidate>> RecognizeCompositeAsync(
        IOcrEngine ocr, byte[] screenshot,
        List<(BanIconSlot Slot, int X, int Y, int W, int H)> boxes, IReadOnlyList<string> knownNames)
    {
        const int scale = 2;      // 名字在 1080p 上也只有 ~24 px 高，放大两倍给 OCR
        const int gap = 10;       // 行间距，避免上下两个名字被合成同一行

        System.Drawing.Bitmap? source;
        try
        {
            using var stream = new MemoryStream(screenshot);
            using var decoded = System.Drawing.Image.FromStream(stream);
            source = new System.Drawing.Bitmap(decoded);
        }
        catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException)
        {
            return [];
        }

        using (source)
        {
            int width = boxes.Max(b => b.W) * scale;
            int totalHeight = boxes.Sum(b => b.H * scale + gap);
            using var composite = new System.Drawing.Bitmap(width, totalHeight,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using (var graphics = System.Drawing.Graphics.FromImage(composite))
            {
                graphics.Clear(System.Drawing.Color.Black);
                graphics.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.HighQuality;
                var offset = 0;
                foreach (var box in boxes)
                {
                    graphics.DrawImage(source,
                        new System.Drawing.Rectangle(0, offset, box.W * scale, box.H * scale),
                        new System.Drawing.Rectangle(box.X, box.Y, box.W, box.H),
                        System.Drawing.GraphicsUnit.Pixel);
                    offset += box.H * scale + gap;
                }
            }

            using var buffer = new MemoryStream();
            composite.Save(buffer, System.Drawing.Imaging.ImageFormat.Png);
            var texts = await ocr.RecognizeRegionAsync(buffer.ToArray(), 0, 0, width, totalHeight);

            var recovered = new List<Candidate>();
            foreach (var text in texts)
            {
                int centerY = text.Box.Y + text.Box.H / 2;
                int offset = 0;
                foreach (var box in boxes)
                {
                    int bandHeight = box.H * scale;
                    if (centerY >= offset && centerY < offset + bandHeight)
                    {
                        var match = MatchLineInText(text.Text, knownNames);
                        if (match is not null)
                        {
                            recovered.Add(new Candidate(
                                new OcrTextResult(match, (box.X, box.Y, box.W, box.H)), match, int.MaxValue));
                        }
                        break;
                    }
                    offset += bandHeight + gap;
                }
            }

            return recovered;
        }
    }

    /// <summary>把一整行文本拆开（合成图里相邻名字可能被连成一行），逐个匹配候选名。</summary>
    private static string? MatchLineInText(string text, IReadOnlyList<string> knownNames)
    {
        var best = MatchLine(new OcrTextResult(text, (0, 0, 0, 0)), knownNames)
            .OrderByDescending(c => c.Score)
            .FirstOrDefault();
        return best?.Name;
    }

    /// <summary>
    /// Primary path: every name is snapped to the nearest icon slot and the ban state is read
    /// from the icon itself. Slots without a name are returned as unnamed icons.
    /// </summary>
    private static SectionRead BindToSlots(
        List<Candidate> candidates, IReadOnlyList<BanIconSlot> slots, string group, double greenThreshold)
    {
        var tolerances = slots
            .GroupBy(s => s.Row)
            .ToDictionary(g => g.Key, g => SlotTolerance(g.Select(s => s.CenterX).ToList()));

        var bound = new Dictionary<BanIconSlot, Candidate>();
        var unbound = new List<Candidate>();

        foreach (var candidate in candidates)
        {
            double cx = CenterX(candidate.Ocr);
            double cy = CenterY(candidate.Ocr);

            // Rows share X positions, so the vertical distance decides which row a name
            // belongs to (the label always sits below its own icon).
            var slot = slots
                .Where(s => Math.Abs(s.CenterX - cx) <= tolerances[s.Row])
                .OrderBy(s => Math.Abs(s.CenterY - cy))
                .ThenBy(s => Math.Abs(s.CenterX - cx))
                .FirstOrDefault();

            if (slot == null)
            {
                unbound.Add(candidate);
                continue;
            }

            if (!bound.TryGetValue(slot, out var existing) || IsBetter(candidate, existing))
                bound[slot] = candidate;
        }

        var states = new List<CovenantBanState>(bound.Count);
        foreach (var (slot, candidate) in bound)
        {
            bool banned = slot.GreenFraction < greenThreshold;
            AppLogger.Info("BanAnalyzer",
                $"{group} '{candidate.Name}' (ocr='{candidate.Ocr.Text}') slot x≈{slot.CenterX:F0} " +
                $"green={slot.GreenFraction:F3} banned={banned}");
            states.Add(new CovenantBanState(candidate.Name, banned, slot.CenterX));
        }

        var unnamed = slots
            .Where(s => !bound.ContainsKey(s))
            .Select(s => new BanIconPosition(s.CenterX, s.CenterY, s.GreenFraction < greenThreshold))
            .OrderBy(p => p.Y)
            .ThenBy(p => p.X)
            .ToList();

        if (unbound.Count > 0)
        {
            AppLogger.Warn("BanAnalyzer",
                $"{group}: {unbound.Count} name(s) matched no icon slot: " +
                $"[{string.Join(", ", unbound.Select(c => $"{c.Name}@{CenterX(c.Ocr):F0}"))}]");
        }

        if (unnamed.Count > 0)
        {
            AppLogger.Warn("BanAnalyzer",
                $"{group}: {unnamed.Count} icon(s) without a name: " +
                $"[{string.Join(", ", unnamed.Select(p => $"x≈{p.X:F0}{(p.IsBanned ? "/禁用" : "/可用")}"))}]");
        }

        return new SectionRead(states.OrderBy(s => s.X).ToList(), unnamed);
    }

    /// <summary>
    /// Fallback path used when the icon grid is not detected: measure each name's own box.
    /// Nothing is dropped, but unnamed icons cannot be reported.
    /// </summary>
    private static SectionRead MeasureFromNameBoxes(
        byte[] screenshot, int imgWidth, int imgHeight,
        List<Candidate> candidates, string group, double greenThreshold, BanScreenPixels? pixels = null)
    {
        AppLogger.Warn("BanAnalyzer", $"{group}: icon grid not found, falling back to name boxes");

        var names = candidates.Select(c => c.Ocr).ToList();
        var fractions = pixels is null
            ? BanIconBrightness.MeasureGreenFractions(screenshot, imgWidth, imgHeight, names)
            : BanIconBrightness.MeasureGreenFractions(pixels, imgWidth, imgHeight, names);

        var states = new List<CovenantBanState>(candidates.Count);
        for (int i = 0; i < candidates.Count; i++)
        {
            double cx = CenterX(candidates[i].Ocr);
            bool banned = fractions[i] < greenThreshold;
            AppLogger.Info("BanAnalyzer",
                $"{group} '{candidates[i].Name}' (ocr='{candidates[i].Ocr.Text}') box x≈{cx:F0} y={candidates[i].Ocr.Box.Y} " +
                $"green={fractions[i]:F3} banned={banned}");
            states.Add(new CovenantBanState(candidates[i].Name, banned, cx));
        }

        return new SectionRead(states.OrderBy(s => s.X).ToList(), []);
    }

    /// <summary>
    /// Names may sit up to half a column away from their icon; derive that tolerance from the
    /// slot spacing instead of a hard-coded pixel value.
    /// </summary>
    private static double SlotTolerance(List<double> centers)
    {
        if (centers.Count < 2)
            return 58;

        var sorted = centers.OrderBy(c => c).ToList();
        var gaps = new List<double>();
        for (int i = 1; i < sorted.Count; i++)
            gaps.Add(sorted[i] - sorted[i - 1]);
        gaps.Sort();

        double median = gaps[gaps.Count / 2];
        return Math.Max(20, median * 0.5);
    }

    /// <summary>
    /// Maps OCR output to covenant names and collapses fragments that belong to the same
    /// icon slot, so one covenant is reported once even when OCR splits or merges its label.
    /// </summary>
    private static List<Candidate> MatchCandidates(
        IReadOnlyList<OcrTextResult> ocrResults, IReadOnlyList<string> knownNames, int imgWidth)
    {
        if (knownNames.Count == 0)
            return [];

        var matches = new List<Candidate>();
        foreach (var result in ocrResults)
            matches.AddRange(MatchLine(result, knownNames));

        if (matches.Count <= 1)
            return matches;

        // Fragments of the same icon are in the same visual row and within half a column
        // pitch of each other. Rows matter: the additional panel has two rows whose columns
        // share X positions, so collapsing on X alone would drop the second row.
        matches.Sort((a, b) =>
        {
            int byRow = CenterY(a.Ocr).CompareTo(CenterY(b.Ocr));
            return byRow != 0 ? byRow : CenterX(a.Ocr).CompareTo(CenterX(b.Ocr));
        });

        double toleranceX = Math.Max(24, imgWidth * 0.03);

        var collapsed = new List<Candidate> { matches[0] };
        for (int i = 1; i < matches.Count; i++)
        {
            var current = matches[i];
            var last = collapsed[^1];
            double toleranceY = Math.Max(4, 0.6 * Math.Max(last.Ocr.Box.H, current.Ocr.Box.H));

            bool sameSlot = Math.Abs(CenterX(current.Ocr) - CenterX(last.Ocr)) < toleranceX
                            && Math.Abs(CenterY(current.Ocr) - CenterY(last.Ocr)) <= toleranceY;

            if (sameSlot)
            {
                if (IsBetter(current, last))
                    collapsed[^1] = current;
            }
            else
            {
                collapsed.Add(current);
            }
        }

        return collapsed;
    }

    private static bool IsBetter(Candidate candidate, Candidate current) =>
        candidate.Score > current.Score
        || (candidate.Score == current.Score && candidate.Ocr.Text.Length > current.Ocr.Text.Length);

    private static double CenterX(OcrTextResult ocr) => ocr.Box.X + ocr.Box.W / 2.0;

    /// <summary>只修正实测出现过的盟约标题 OCR 错字，不参与盟约名称匹配。</summary>
    internal static string NormalizeHeader(string text) =>
        text.Replace(" ", "").Replace("明", "盟").Replace("約", "约")
            .Replace("核心盘约", "核心盟约").Replace("附加盟药", "附加盟约");

    private static double CenterY(OcrTextResult ocr) => ocr.Box.Y + ocr.Box.H / 2.0;

    // ── Covenant name matching ────────────────────────────────────────────────

    /// <summary>
    /// Fragments that mark a line as a section title / warning instead of a covenant name.
    /// Matched loosely, these used to be mapped onto real covenants ("本局禁用干员" → "协防干员").
    /// </summary>
    private static readonly string[] SectionNoiseFragments =
    [
        "本局禁用", "部分盟约", "所含", "阵容不完整", "核心盟约", "附加盟约",
        "已就绪", "准备就绪", "绝境模拟", "确认本局",
    ];

    /// <summary>
    /// Maps one OCR line to one or more covenant candidates.
    ///
    /// OCR regularly merges two neighbouring labels into a single line
    /// ("伊比利亚维多利亚"); mapping such a line to just one name used to hide the other
    /// covenant. The line is therefore split at the real character positions and every
    /// matched name keeps its own slice of the box, so each name snaps to its own icon.
    ///
    /// Order: exact → names contained in the line → strict character-overlap fallback.
    /// Scores: 3 exact, 2 contained, 1 fuzzy.
    /// </summary>
    private static List<Candidate> MatchLine(OcrTextResult ocr, IReadOnlyList<string> knownNames)
    {
        if (knownNames.Count == 0) return [];

        // Real opening capture: PaddleOCR drops 比 inside a merged covenant-name line.
        var clean = ocr.Text.Trim()
            .Replace("伊利亚", "伊比利亚", StringComparison.Ordinal)
            // The live 4K frame occasionally drops 戈 from 阿戈尔 inside a merged line.
            .Replace("阿尔", "阿戈尔", StringComparison.Ordinal);
        if (clean.Length == 0) return [];
        if (SectionNoiseFragments.Any(f => clean.Contains(f, StringComparison.Ordinal))) return [];

        if (knownNames.Contains(clean)) return [new Candidate(ocr, clean, 3)];

        var contained = FindContainedNames(clean, knownNames);
        if (contained.Count > 0)
        {
            return contained
                .Select(c => new Candidate(
                    contained.Count == 1 && c.Name.Length == clean.Length
                        ? ocr
                        : SliceBox(ocr, c.Start, c.Name.Length, clean.Length),
                    c.Name,
                    2))
                .ToList();
        }

        // Fuzzy fallback for single-character OCR mistakes. Requires most of the name to
        // survive; the previous 50 % rule was loose enough to match section titles.
        if (clean.Length < 2) return [];

        string? best = null;
        int bestScore = 0;
        foreach (var candidate in knownNames)
        {
            int shorter = Math.Min(clean.Length, candidate.Length);
            if (shorter < 2) continue;
            int shared = SharedCharCount(clean, candidate);
            if (shared * 5 < shorter * 4) continue;
            if (shared > bestScore)
            {
                bestScore = shared;
                best = candidate;
            }
        }

        return best == null ? [] : [new Candidate(ocr, best, 1)];
    }

    /// <summary>
    /// Finds every known name contained in <paramref name="text"/>, scanning left to right
    /// and taking the longest name at each position, so "伊比利亚维多利亚" yields both.
    /// </summary>
    private static List<(string Name, int Start)> FindContainedNames(
        string text, IReadOnlyList<string> knownNames)
    {
        var found = new List<(string Name, int Start)>();
        int index = 0;
        while (index < text.Length)
        {
            string? bestName = null;
            foreach (var name in knownNames)
            {
                if (name.Length < 2) continue;
                if (text.AsSpan(index).StartsWith(name, StringComparison.Ordinal)
                    && (bestName == null || name.Length > bestName.Length))
                {
                    bestName = name;
                }
            }

            if (bestName == null)
            {
                index++;
                continue;
            }

            found.Add((bestName, index));
            index += bestName.Length;
        }

        return found;
    }

    private static OcrTextResult SliceBox(OcrTextResult ocr, int start, int length, int totalLength)
    {
        double charWidth = (double)ocr.Box.W / Math.Max(1, totalLength);
        int x = ocr.Box.X + (int)Math.Round(charWidth * start);
        int w = Math.Max(1, (int)Math.Round(charWidth * length));
        return ocr with { Box = (x, ocr.Box.Y, w, ocr.Box.H) };
    }

    private static int SharedCharCount(string a, string b)
    {
        var freq = new Dictionary<char, int>();
        foreach (var ch in b)
            freq[ch] = freq.GetValueOrDefault(ch) + 1;

        int shared = 0;
        foreach (var ch in a)
        {
            if (freq.TryGetValue(ch, out int count) && count > 0)
            {
                shared++;
                freq[ch] = count - 1;
            }
        }
        return shared;
    }
}
