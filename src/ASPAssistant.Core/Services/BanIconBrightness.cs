using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ASPAssistant.Core.Services;

/// <summary>
/// Decides whether a covenant icon on the "确认本局信息" screen is available or banned.
///
/// The game renders an available covenant as a bright green disc and a banned one as the
/// same glyph on a desaturated grey disc. Measured on 1920×1080 captures: available icons
/// hold 0.42–0.54 "green-dominant" pixels in the sampled band, banned icons hold 0.000
/// (the silver glyph is not green-dominant at all). The threshold therefore separates the
/// two states with a wide margin and does not depend on absolute brightness.
///
/// <see cref="BanIconGrid"/> is the primary path (icons are found from pixels first and OCR
/// names are bound to them); this class also keeps the name-box based fallback used when a
/// name cannot be bound to any detected slot.
/// </summary>
public static class BanIconBrightness
{
    /// <summary>
    /// Fraction of green-dominant pixels below which an icon counts as banned.
    /// </summary>
    public const double BannedThreshold = 0.15;

    /// <summary>Height of the sampled band above a covenant name, in text heights.</summary>
    private const double BandHeightInTextHeights = 5.0;

    /// <summary>
    /// Gap between the name box top and the bottom of the sampled band, in text heights.
    /// Measured on 1920×1080 captures: the icon disc sits about 0.2–4.9 text heights above
    /// the name, so the band covers the whole disc without reaching the row above.
    /// </summary>
    private const double BandBottomGapInTextHeights = 0.2;

    /// <summary>Half width of the sampled band, as a fraction of screenshot width.</summary>
    private const double BandHalfWidthPercent = 0.019;

    /// <summary>
    /// Measures one green-pixel fraction per covenant name (same order as <paramref name="names"/>).
    /// The sample is taken from the icon disc directly above each OCR name box.
    /// </summary>
    public static double[] MeasureGreenFractions(
        byte[] png, int imgWidth, int imgHeight, IReadOnlyList<OcrTextResult> names)
    {
        if (names.Count == 0) return new double[names.Count];
        using var pixels = BanScreenPixels.Load(png);
        return pixels is null
            ? new double[names.Count]
            : MeasureGreenFractions(pixels, imgWidth, imgHeight, names);
    }

    /// <summary>复用已解码像素的版本（省一次 PNG 解码）。</summary>
    public static double[] MeasureGreenFractions(
        BanScreenPixels pixels, int imgWidth, int imgHeight, IReadOnlyList<OcrTextResult> names)
    {
        var fractions = new double[names.Count];
        if (names.Count == 0)
            return fractions;

        int width = pixels.Width;
        imgWidth = imgWidth > 0 ? Math.Min(imgWidth, width) : width;
        int half = Math.Max(10, (int)Math.Round(imgWidth * BandHalfWidthPercent));

        for (int i = 0; i < names.Count; i++)
        {
            var box = names[i].Box;
            int textHeight = Math.Max(8, box.H);
            int centerX = box.X + box.W / 2;
            int bottom = box.Y - Math.Max(1, (int)(textHeight * BandBottomGapInTextHeights));
            int top = bottom - (int)(textHeight * BandHeightInTextHeights);

            fractions[i] = pixels.GreenFraction(centerX - half, top, centerX + half, bottom);
        }

        return fractions;
    }
}
