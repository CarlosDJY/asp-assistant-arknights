// Modified for ASP Assistant Arknights on 2026-11-09; see NOTICE.
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Text.Json;
using MaaFramework.Binding;
using MaaFramework.Binding.Buffers;
using MaaFramework.Binding.Custom;

namespace ASPAssistant.Core.Services;

/// <summary>
/// OCR engine backed by MaaFramework / PaddleOCR.
/// Requires <c>det.onnx</c>, <c>rec.onnx</c>, and <c>keys.txt</c> to be present
/// under <c>{resourcePath}/model/ocr/</c>.
/// Unlike the Windows built-in OCR engine this implementation:
/// <list type="bullet">
///   <item>Has no system language-pack dependency.</item>
///   <item>Returns a bounding box for each individual matched text segment,
///         enabling precise per-item overlay marker placement.</item>
/// </list>
/// </summary>
public sealed class MaaOcrEngine : IOcrEngine, IDisposable
{
    /// <summary>
    /// Character-level replacements applied by the OCR engine before text
    /// comparison. Each entry is a <c>(From, To)</c> pair; "From" is the
    /// incorrect glyph the model tends to produce and "To" is the correct
    /// character.
    ///
    /// Populate this table from logs: when a card is not matched and the
    /// log shows e.g. <c>ocrResults=["榭拉格"]</c> instead of "谢拉格",
    /// add <c>("榭", "谢")</c> here.
    ///
    /// M24aaFramework applies these replacements inside the engine, before
    /// the <c>text</c> filter runs, so they also correct raw
    /// <see cref="RecognizeRegionAsync"/> results.
    /// </summary>
    public List<(string From, string To)> GlyphReplacements { get; init; } =
    [
        // ── Add entries here as OCR errors are discovered from logs ──────
        ("榭", "谢"),
        // 部分字体把「萨」渲染得接近繁体「薩」，PaddleOCR 会直接输出繁体字。
        ("薩", "萨"),
        // ("垫", "蛰"),
    ];

    /// <summary>
    /// Upscale factor applied to the text crop before it is passed to the
    /// PaddleOCR recognition model in <see cref="FindCandidateInRegionAsync"/>.
    ///
    /// PaddleOCR internally resizes its input to a fixed height (32 px).
    /// When the source character strip is small (e.g. 20 px at 1280-wide
    /// window), the internal resize discards fine stroke detail, causing
    /// visually similar characters (e.g. "谢"↔"榭") to look identical to
    /// the model.  Pre-upscaling with high-quality bicubic interpolation
    /// preserves stroke details so the model can distinguish glyphs before
    /// the final resize step.
    ///
    /// Recommended values: 2–3.  1 = disabled (no upscaling).
    /// </summary>
    public float GlyphUpscaleFactor { get; init; } = 2.0f;

    private readonly MaaTasker _tasker;
    private readonly NullController _nullController;

    /// <param name="resourcePath">
    /// Directory that contains the <c>model/ocr/</c> folder with PaddleOCR ONNX files.
    /// Typically this is the application's <c>data/</c> directory.
    /// </param>
    public MaaOcrEngine(string resourcePath)
    {
        _nullController = new NullController();

        var controller = new MaaCustomController(_nullController);
        controller.LinkStart().Wait();

        var ocrModelPath = Path.Combine(resourcePath, "model", "ocr");
        var resource = new MaaResource();
        resource.AppendOcrModel(ocrModelPath).Wait();

        _tasker = new MaaTasker(controller, resource, DisposeOptions.All);
    }

    /// <inheritdoc/>
    public async Task<IReadOnlyList<OcrTextResult>> RecognizeRegionAsync(
        byte[] pngBytes, int x, int y, int width, int height)
    {
        return await Task.Run(() => RunOcr(pngBytes, x, y, width, height, textFilter: null, onlyRecognition: false));
    }

    public async Task<IReadOnlyList<OcrTextResult>> RecognizeLineAsync(
        byte[] pngBytes, int x, int y, int width, int height)
    {
        return await Task.Run(() => RunOcr(pngBytes, x, y, width, height, textFilter: null, onlyRecognition: true));
    }

    /// <inheritdoc/>
    public async Task<string?> FindCandidateInRegionAsync(
        byte[] pngBytes, int x, int y, int width, int height,
        IReadOnlyList<string> candidates)
    {
        if (candidates.Count == 0)
            return null;

        return await Task.Run(() =>
        {
            // Pre-upscale the text strip with bicubic interpolation so that
            // PaddleOCR's internal resize (→32 px) operates on higher-fidelity
            // glyph information. This helps the model distinguish visually
            // similar characters that would otherwise look identical at small sizes.
            byte[] ocrInput = pngBytes;
            int ocrX = x, ocrY = y, ocrW = width, ocrH = height;

            if (GlyphUpscaleFactor > 1.0f)
            {
                ocrInput = UpscaleCrop(pngBytes, x, y, width, height,
                    GlyphUpscaleFactor, out int scaledW, out int scaledH);
                // The upscaled image contains exactly the crop, so ROI = full image.
                ocrX = 0; ocrY = 0; ocrW = scaledW; ocrH = scaledH;
            }

            var results = RunOcr(ocrInput, ocrX, ocrY, ocrW, ocrH, textFilter: candidates, onlyRecognition: false);

            foreach (var candidate in candidates)
                if (results.Any(r => r.Text.Contains(candidate, StringComparison.Ordinal)))
                    return candidate;

            return null;
        });
    }

    /// <summary>
    /// Extracts the rectangle <c>(x,y,w,h)</c> from a PNG, scales it by
    /// <paramref name="factor"/> using bicubic interpolation, and returns the
    /// result as a PNG byte array.  Bicubic preserves fine stroke transitions
    /// better than bilinear for Chinese character glyphs.
    /// </summary>
    private static byte[] UpscaleCrop(
        byte[] pngBytes, int x, int y, int w, int h, float factor,
        out int scaledW, out int scaledH)
    {
        scaledW = Math.Max(1, (int)(w * factor));
        scaledH = Math.Max(1, (int)(h * factor));

        using var ms = new MemoryStream(pngBytes);
        using var src = new Bitmap(ms);

        var dst = new Bitmap(scaledW, scaledH, PixelFormat.Format32bppArgb);
        try
        {
            using var g = Graphics.FromImage(dst);
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode   = PixelOffsetMode.HighQuality;
            g.SmoothingMode     = SmoothingMode.HighQuality;
            g.DrawImage(src,
                destRect:   new Rectangle(0, 0, scaledW, scaledH),
                srcX:       x, srcY: y, srcWidth: w, srcHeight: h,
                srcUnit:    GraphicsUnit.Pixel);

            return ImageBytes.Encode(dst);
        }
        finally
        {
            dst.Dispose();
        }
    }

    private IReadOnlyList<OcrTextResult> RunOcr(
        byte[] pngBytes, int x, int y, int width, int height,
        IReadOnlyList<string>? textFilter, bool onlyRecognition)
    {
        try
        {
            using var imgBuf = new MaaImageBuffer();
            if (!imgBuf.TrySetEncodedData(pngBytes))
                return [];

            var paramJson = BuildOcrParamJson(x, y, width, height, textFilter, onlyRecognition);

            var job = _tasker.AppendRecognition("OCR", paramJson, imgBuf);
            var status = job.Wait();
            if (status != MaaJobStatus.Succeeded)
                return [];

            _tasker.GetTaskDetail(job.Id, out _, out long[]? nodeIds, out _);
            if (nodeIds is not { Length: > 0 })
                return [];

            _tasker.GetNodeDetail(nodeIds[0], out _, out long recoId, out _, out _);
            _tasker.GetRecognitionDetail(recoId, out _, out _, out _,
                null, out string detailJson, null, null);

            return ParseOcrDetail(detailJson);
        }
        catch
        {
            return [];
        }
    }

    /// <summary>
    /// Builds the OCR recognition parameter JSON.
    /// Includes <c>replace</c> when <see cref="GlyphReplacements"/> is non-empty,
    /// and <c>text</c> when <paramref name="textFilter"/> is provided.
    /// </summary>
    private string BuildOcrParamJson(
        int x, int y, int width, int height,
        IReadOnlyList<string>? textFilter, bool onlyRecognition)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append($"{{\"roi\":[{x},{y},{width},{height}]");
        if (onlyRecognition) sb.Append(",\"only_rec\":true");

        if (textFilter is { Count: > 0 })
        {
            sb.Append(",\"text\":[");
            for (int i = 0; i < textFilter.Count; i++)
            {
                if (i > 0) sb.Append(',');
                sb.Append('"');
                sb.Append(System.Text.Json.JsonEncodedText.Encode(textFilter[i]));
                sb.Append('"');
            }
            sb.Append(']');
        }

        if (GlyphReplacements.Count > 0)
        {
            sb.Append(",\"replace\":[");
            for (int i = 0; i < GlyphReplacements.Count; i++)
            {
                if (i > 0) sb.Append(',');
                var (from, to) = GlyphReplacements[i];
                sb.Append('[');
                sb.Append('"');
                sb.Append(System.Text.Json.JsonEncodedText.Encode(from));
                sb.Append('"');
                sb.Append(',');
                sb.Append('"');
                sb.Append(System.Text.Json.JsonEncodedText.Encode(to));
                sb.Append('"');
                sb.Append(']');
            }
            sb.Append(']');
        }

        sb.Append('}');
        return sb.ToString();
    }

    /// <summary>
    /// Parses the JSON returned by MaaFramework OCR recognition detail.
    /// Expected shape: <c>{"all":[{"text":"...","score":0.9,"box":[x,y,w,h]},...]}</c>
    /// </summary>
    private static IReadOnlyList<OcrTextResult> ParseOcrDetail(string detailJson)
    {
        if (string.IsNullOrWhiteSpace(detailJson))
            return [];

        try
        {
            using var doc = JsonDocument.Parse(detailJson);
            if (!doc.RootElement.TryGetProperty("all", out var all))
                return [];

            var results = new List<OcrTextResult>();
            foreach (var item in all.EnumerateArray())
            {
                var text = item.GetProperty("text").GetString() ?? string.Empty;
                if (string.IsNullOrEmpty(text))
                    continue;

                var box = item.GetProperty("box");
                int bx = box[0].GetInt32();
                int by = box[1].GetInt32();
                int bw = box[2].GetInt32();
                int bh = box[3].GetInt32();
                results.Add(new OcrTextResult(text, (bx, by, bw, bh)));
            }
            return results;
        }
        catch
        {
            return [];
        }
    }

    public void Dispose()
    {
        _tasker.Dispose();
        _nullController.Dispose();
    }

    private sealed class NullController : IMaaCustomController
    {
        public string Name { get; set; } = "NullController";

        public bool Connect() => true;
        public bool Connected() => true;
        public bool RequestUuid(IMaaStringBuffer uuidBuffer) => true;
        public ControllerFeatures GetFeatures() => ControllerFeatures.None;
        public bool GetInfo(IMaaStringBuffer infoBuffer) => true;
        public bool Inactive() => true;

        public bool Screencap(IMaaImageBuffer imageBuffer) => false;
        public bool Click(int x, int y) => false;
        public bool Swipe(int x1, int y1, int x2, int y2, int duration) => false;
        public bool TouchDown(int contact, int x, int y, int pressure) => false;
        public bool TouchMove(int contact, int x, int y, int pressure) => false;
        public bool TouchUp(int contact) => false;
        public bool ClickKey(int key) => false;
        public bool KeyDown(int key) => false;
        public bool KeyUp(int key) => false;
        public bool InputText(string text) => false;
        public bool StartApp(string intent) => false;
        public bool StopApp(string intent) => false;
        public bool Scroll(int x, int y) => false;

        public void Dispose() { }
    }
}
