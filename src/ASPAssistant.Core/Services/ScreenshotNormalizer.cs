// Modified for ASP Assistant Regulation on 2026-11-10; see NOTICE.
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace ASPAssistant.Core.Services;

/// <summary>
/// Normalises screenshots before OCR/icon analysis.
///
/// The covenant panels are read with percentage-based regions, so layout survives
/// scaling — but the name labels are only ~12 px tall at 1080p. A screenshot that was
/// scaled down before capture (e.g. 532×300 from an archived clip) shrinks them to
/// ~7 px and OCR silently loses them. Upscaling to at least 1920 px wide restores the
/// glyph size the OCR models were tuned for.
/// </summary>
public static class ScreenshotNormalizer
{
    /// <summary>Width at or above which the screenshot is used unchanged.</summary>
    public const int MinimumAnalysisWidth = 1600;

    /// <summary>上采样上限，避免把 320×180 放大成超大图。</summary>
    public const int MaximumUpscaleFactor = 4;

    /// <summary>
    /// Returns PNG bytes whose width is at least <see cref="MinimumAnalysisWidth"/>
    /// together with the dimensions to use for every downstream step.
    /// </summary>
    public static (byte[] Png, int Width, int Height) EnsureAnalysable(byte[] png, int width, int height)
    {
        if (width <= 0 || height <= 0) return (png, width, height);
        if (width >= MinimumAnalysisWidth) return (png, width, height);

        var factor = (int)Math.Ceiling((double)MinimumAnalysisWidth / width);
        factor = Math.Clamp(factor, 2, MaximumUpscaleFactor);

        using var source = Decode(png);
        if (source is null) return (png, width, height);

        var targetW = source.Width * factor;
        var targetH = source.Height * factor;
        using var resized = new Bitmap(targetW, targetH, PixelFormat.Format32bppArgb);
        using (var graphics = Graphics.FromImage(resized))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            graphics.DrawImage(source, 0, 0, targetW, targetH);
        }

        using var buffer = new MemoryStream();
        resized.Save(buffer, ImageFormat.Png);
        return (buffer.ToArray(), targetW, targetH);
    }

    private static Bitmap? Decode(byte[] png)
    {
        try
        {
            using var stream = new MemoryStream(png);
            using var decoded = Image.FromStream(stream);
            return new Bitmap(decoded);
        }
        catch (Exception ex) when (ex is ArgumentException or OutOfMemoryException)
        {
            return null;
        }
    }
}
