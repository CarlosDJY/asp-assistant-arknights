using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace ASPAssistant.Core.Services;

/// <summary>One icon slot found on the ban screen, with its availability state.</summary>
public sealed record BanIconSlot(
    int Row,
    double CenterX,
    double CenterY,
    int Top,
    int Bottom,
    double GreenFraction);

/// <summary>
/// Finds the covenant icon grid on the "确认本局信息" screen from pixels alone — no OCR.
///
/// This is the second direction of the recognition: OCR names are bound to the icons found
/// here, and icons that receive no name are reported instead of silently disappearing.
///
/// Measured on 1920×1080 captures: icon rows are ~90 px tall bands of light pixels
/// (bright green discs and grey discs whose glyph/ring is still much lighter than the
/// background), columns sit on a 116 px pitch (0.0604 × width). Rows of plain text
/// (section headers, the ban list title) are rejected because their column gaps are not
/// consistent with a single pitch.
/// </summary>
public static class BanIconGrid
{
    private const int LumaThreshold = 60;

    /// <summary>Row band height limits, as fractions of the screenshot height.</summary>
    private const double BandMinHeightPercent = 0.05;
    private const double BandMaxHeightPercent = 0.13;

    /// <summary>A row counts as an icon row when at least this share of its scanning width is light.</summary>
    private const double BandMinLightShare = 0.05;

    /// <summary>Column gap consistency and size limits.</summary>
    private const double GapTolerance = 0.25;
    private const double MinPitchPercent = 0.04;

    private const int MinColumnsPerRow = 4;

    /// <summary>
    /// 单人模式把核心盟约排成“左组 + 右组”，中间的空隙明显大于列距。
    /// 相邻两列的间距超过该倍数的中位列距时，视为分组边界；行内不再要求整体等距。
    /// </summary>
    private const double LargeGapRatio = 1.7;

    /// <summary>分组后的最小列数：4+4、3+3、2+2 都是合法的一行。</summary>
    private const int MinColumnsPerGroup = 2;

    /// <summary>Horizontal scanning window, as fractions of the screenshot width.</summary>
    private const double ScanLeftPercent = 0.00;
    private const double ScanRightPercent = 0.70;

    /// <summary>Half width of the green sample, as a fraction of the column pitch.</summary>
    private const double SampleHalfPitch = 0.30;

    public static bool HasClippedBottomRow(BanScreenPixels pixels)
    {
        var centers = new List<double>();
        int start = -1;
        for (var x = 0; x <= (int)(pixels.Width * .70); x++)
        {
            var light = 0;
            for (var y = (int)(pixels.Height * .96); y < pixels.Height; y++)
                if (pixels.Luma(x, y) > 25) light++;
            if (light > 2 && start < 0) start = x;
            if (light > 2 || start < 0) continue;
            if (x - start > pixels.Width * .0035)
            {
                var center = (start + x) / 2.0;
                if (centers.Count > 0 && center - centers[^1] < pixels.Width * .025)
                    centers[^1] = (centers[^1] + center) / 2;
                else centers.Add(center);
            }
            start = -1;
        }
        return IsRegular(centers, pixels.Width);
    }

    /// <summary>
    /// Detects every icon slot whose vertical centre falls inside [minYPercent, maxYPercent].
    /// Returns an empty list when the image cannot be decoded or no grid is found.
    /// </summary>
    public static IReadOnlyList<BanIconSlot> Detect(
        byte[] png, int imgWidth, int imgHeight, double minYPercent = 0, double maxYPercent = 1)
    {
        using var pixels = BanScreenPixels.Load(png);
        if (pixels == null)
        {
            AppLogger.Warn("BanIconGrid", "could not decode screenshot");
            return [];
        }

        return Detect(pixels, minYPercent, maxYPercent);
    }

    /// <summary>
    /// 同一个分析里有核心/附加两块区域，复用一份已解码的像素可以省掉一次 PNG 解码
    /// （1920×1080 约 100 ms）。<see cref="BanScreenPixels"/> 读操作是只读的，可并行共享。
    /// </summary>
    public static IReadOnlyList<BanIconSlot> Detect(
        BanScreenPixels pixels, double minYPercent = 0, double maxYPercent = 1)
    {
        int width = pixels.Width;
        int height = pixels.Height;

        int scanLeft = Math.Max(0, (int)(ScanLeftPercent * width));
        int scanRight = Math.Min(width, (int)(ScanRightPercent * width));
        if (scanRight - scanLeft < 100)
            return [];

        int bandTop = Math.Max(0, (int)(minYPercent * height));
        int bandBottom = Math.Min(height, (int)(maxYPercent * height));

        var rows = new List<(int Top, int Bottom)>();
        int minLight = (int)Math.Max(20, (scanRight - scanLeft) * BandMinLightShare);
        int minHeight = (int)(BandMinHeightPercent * height);
        int maxHeight = (int)(BandMaxHeightPercent * height);

        int rowStart = -1;
        for (int y = bandTop; y <= bandBottom; y++)
        {
            bool light = false;
            if (y < bandBottom)
            {
                int count = 0;
                for (int x = scanLeft; x < scanRight; x++)
                    if (pixels.Luma(x, y) > LumaThreshold) count++;
                light = count > minLight;
            }

            if (light && rowStart < 0)
            {
                rowStart = y;
            }
            else if (!light && rowStart >= 0)
            {
                int bandHeight = y - rowStart;
                if (bandHeight >= minHeight && bandHeight <= maxHeight)
                    rows.Add((rowStart, y));
                rowStart = -1;
            }
        }

        var slots = new List<BanIconSlot>();
        int rowIndex = 0;
        foreach (var (top, bottom) in rows)
        {
            var centers = DetectColumns(pixels, scanLeft, scanRight, top, bottom, width);
            if (centers.Count < MinColumnsPerRow)
                continue;

            double pitch = MedianPitch(centers);
            if (pitch <= 0 || pitch < MinPitchPercent * width)
                continue;

            foreach (var group in SplitByLargeGaps(centers, pitch))
            {
                if (group.Count < MinColumnsPerGroup || !IsRegular(group, width, MinColumnsPerGroup))
                    continue;

                double groupPitch = MedianPitch(group);
                if (groupPitch <= 0)
                    groupPitch = pitch;

                int half = Math.Max(12, (int)(groupPitch * SampleHalfPitch));
                foreach (var centerX in group)
                {
                    double green = pixels.GreenFraction(
                        (int)centerX - half, top, (int)centerX + half, bottom);
                    slots.Add(new BanIconSlot(
                        rowIndex, centerX, (top + bottom) / 2.0, top, bottom, green));
                }
            }
            rowIndex++;
        }

        return slots;
    }

    private static List<double> DetectColumns(
        BanScreenPixels pixels, int scanLeft, int scanRight, int top, int bottom, int width)
    {
        // 图标盘的宽度约为屏宽的 5.6%；2.5% 能滤掉 “SELECTED” 水印这类
        // 窄亮条（实测 24 px），又不会误伤小分辨率下的真实图标（960 px 宽时约 54 px）。
        int minSpan = Math.Max(10, (int)(0.025 * width));
        var centers = new List<double>();

        int runStart = -1;
        for (int x = scanLeft; x <= scanRight; x++)
        {
            bool light = false;
            if (x < scanRight)
            {
                int count = 0;
                for (int y = top; y < bottom; y++)
                    if (pixels.Luma(x, y) > LumaThreshold) count++;
                light = count > 3;
            }

            if (light && runStart < 0)
            {
                runStart = x;
            }
            else if (!light && runStart >= 0)
            {
                if (x < scanRight && x - runStart > minSpan)
                    centers.Add((runStart + x) / 2.0);
                runStart = -1;
            }
        }

        return centers;
    }

    /// <summary>
    /// 按明显大于列距的空隙把一行拆成若干组。单人页的“左 3 + 右 3”“左 2 + 右 2”
    /// 就是这种排布；多人页没有大空隙，仍然得到单独一组，行为不变。
    /// </summary>
    private static List<List<double>> SplitByLargeGaps(List<double> centers, double pitch)
    {
        var groups = new List<List<double>> { new() { centers[0] } };
        double limit = pitch * LargeGapRatio;

        for (int i = 1; i < centers.Count; i++)
        {
            if (centers[i] - centers[i - 1] > limit)
                groups.Add([]);
            groups[^1].Add(centers[i]);
        }

        return groups;
    }

    /// <summary>
    /// A real icon row has evenly spaced columns; text lines and HUD elements produce
    /// irregular gaps and are rejected here.
    /// </summary>
    private static bool IsRegular(List<double> centers, int width, int minColumns = MinColumnsPerRow)
    {
        if (centers.Count < minColumns)
            return false;

        var gaps = new List<double>();
        for (int i = 1; i < centers.Count; i++)
            gaps.Add(centers[i] - centers[i - 1]);

        // 只有两列的分组没有可校验的列距；它能通过分组边界检查本身就说明间距
        // 与整行中位列距一致。
        if (gaps.Count == 0)
            return true;

        double median = Median(gaps);
        if (median < MinPitchPercent * width)
            return false;

        return gaps.All(g => Math.Abs(g - median) <= median * GapTolerance);
    }

    private static double MedianPitch(List<double> centers)
    {
        if (centers.Count < 2)
            return 0;

        var gaps = new List<double>();
        for (int i = 1; i < centers.Count; i++)
            gaps.Add(centers[i] - centers[i - 1]);
        return Median(gaps);
    }

    private static double Median(List<double> values)
    {
        if (values.Count == 0)
            return 0;

        var sorted = values.OrderBy(v => v).ToList();
        int middle = sorted.Count / 2;
        return sorted.Count % 2 == 1
            ? sorted[middle]
            : (sorted[middle - 1] + sorted[middle]) / 2.0;
    }
}

/// <summary>
/// Decoded screenshot pixels shared by the icon grid and the brightness sampling.
/// </summary>
public sealed class BanScreenPixels : IDisposable
{
    private readonly Bitmap _bitmap;
    private readonly BitmapData _data;
    private readonly byte[] _buffer;

    public int Width => _bitmap.Width;
    public int Height => _bitmap.Height;

    private BanScreenPixels(Bitmap bitmap, BitmapData data, byte[] buffer)
    {
        _bitmap = bitmap;
        _data = data;
        _buffer = buffer;
    }

    /// <summary>Decodes the PNG; returns <c>null</c> when the bytes are not a readable image.</summary>
    public static BanScreenPixels? Load(byte[] png)
    {
        try
        {
            using var stream = new MemoryStream(png);
            var bitmap = new Bitmap(stream);
            var rect = new Rectangle(0, 0, bitmap.Width, bitmap.Height);
            var data = bitmap.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var buffer = new byte[data.Stride * bitmap.Height];
            Marshal.Copy(data.Scan0, buffer, 0, buffer.Length);
            return new BanScreenPixels(bitmap, data, buffer);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("BanScreenPixels", $"decode failed: {ex.Message}");
            return null;
        }
    }

    public int Luma(int x, int y)
    {
        if (x < 0 || y < 0 || x >= _bitmap.Width || y >= _bitmap.Height)
            return 0;

        int offset = y * _data.Stride + x * 4;
        int b = _buffer[offset];
        int g = _buffer[offset + 1];
        int r = _buffer[offset + 2];
        return (r * 299 + g * 587 + b * 114) / 1000;
    }

    /// <summary>Fraction of green-dominant pixels in the given rectangle.</summary>
    public double GreenFraction(int x0, int y0, int x1, int y1)
    {
        x0 = Math.Max(0, x0);
        y0 = Math.Max(0, y0);
        x1 = Math.Min(_bitmap.Width, x1);
        y1 = Math.Min(_bitmap.Height, y1);
        if (x1 <= x0 || y1 <= y0)
            return 0;

        int total = 0;
        int green = 0;
        for (int y = y0; y < y1; y++)
        {
            int row = y * _data.Stride;
            for (int x = x0; x < x1; x++)
            {
                int offset = row + x * 4;
                int b = _buffer[offset];
                int g = _buffer[offset + 1];
                int r = _buffer[offset + 2];
                total++;
                if (g > 90 && g * 10 > r * 13 && g * 10 > b * 13)
                    green++;
            }
        }

        return total == 0 ? 0 : (double)green / total;
    }

    public void Dispose()
    {
        try
        {
            _bitmap.UnlockBits(_data);
        }
        finally
        {
            _bitmap.Dispose();
        }
    }
}
