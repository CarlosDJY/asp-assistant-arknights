// Modified for ASP Assistant Arknights on 2026-11-09; see NOTICE.
namespace ASPAssistant.Core.Services;

/// <summary>
/// Represents a single OCR recognition result: the recognized text and its
/// pixel bounding box relative to the original (un-cropped) screenshot.
/// </summary>
public record OcrTextResult(string Text, (int X, int Y, int W, int H) Box);

/// <summary>
/// Contract for OCR engines used by <see cref="OcrScannerService"/>.
/// Returns per-segment results with individual bounding boxes instead of a
/// flat string, enabling precise per-item matching and overlay placement.
/// </summary>
public interface IOcrEngine
{
    /// <summary>
    /// Runs OCR on the specified sub-region of <paramref name="pngBytes"/>.
    /// Coordinates are pixels relative to the full image.
    /// Returns all recognized text segments with their absolute pixel boxes.
    /// </summary>
    Task<IReadOnlyList<OcrTextResult>> RecognizeRegionAsync(
        byte[] pngBytes, int x, int y, int width, int height);

    /// <summary>Reads one precisely cropped line without text detection.</summary>
    Task<IReadOnlyList<OcrTextResult>> RecognizeLineAsync(
        byte[] pngBytes, int x, int y, int width, int height);

    /// <summary>
    /// Runs OCR on the sub-region and checks whether any of
    /// <paramref name="candidates"/> appears in the result.
    ///
    /// The engine applies its glyph-replacement table before comparing, so
    /// visually similar characters (e.g. "榭" → "谢") are corrected prior
    /// to matching. This keeps all correction logic inside the OCR layer
    /// instead of in post-processing.
    /// </summary>
    /// <returns>
    /// The first candidate whose name was found in the OCR output, or
    /// <see langword="null"/> if none matched.
    /// </returns>
    Task<string?> FindCandidateInRegionAsync(
        byte[] pngBytes, int x, int y, int width, int height,
        IReadOnlyList<string> candidates);
}
