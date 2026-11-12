// Modified for ASP Assistant Arknights on 2026-11-12; see NOTICE.
using System.Drawing;
using System.Drawing.Imaging;

namespace ASPAssistant.Core.Services;

/// <summary>
/// Encoded frames passed between capture, OCR and the readers. Frames are BMP: encoding and decoding an
/// uncompressed bitmap is close to a memory copy, where PNG cost tens of milliseconds per step and every OCR
/// call decoded the frame again. Pixels are identical, so recognition results do not change.
/// </summary>
public static class ImageBytes
{
    /// <summary>Encodes as 24-bit BMP (what MAA / OpenCV and GDI+ both read natively).</summary>
    public static byte[] Encode(Bitmap bitmap)
    {
        using var ms = new MemoryStream();
        if (bitmap.PixelFormat == PixelFormat.Format24bppRgb)
            bitmap.Save(ms, ImageFormat.Bmp);
        else
        {
            using var rgb = bitmap.Clone(new Rectangle(0, 0, bitmap.Width, bitmap.Height), PixelFormat.Format24bppRgb);
            rgb.Save(ms, ImageFormat.Bmp);
        }
        return ms.ToArray();
    }

    /// <summary>Width and height from a BMP or PNG header (no decoding).</summary>
    public static (int Width, int Height) GetDimensions(byte[] image)
    {
        if (image.Length >= 26 && image[0] == (byte)'B' && image[1] == (byte)'M')
        {
            int w = BitConverter.ToInt32(image, 18);
            int h = BitConverter.ToInt32(image, 22);
            return (w, Math.Abs(h)); // negative height = top-down rows
        }
        if (image.Length >= 24 && image[0] == 0x89 && image[1] == (byte)'P')
        {
            int w = (image[16] << 24) | (image[17] << 16) | (image[18] << 8) | image[19];
            int h = (image[20] << 24) | (image[21] << 16) | (image[22] << 8) | image[23];
            return (w, h);
        }
        return (0, 0);
    }

    /// <summary>PNG copy for saved debug artifacts (a BMP frame would be ~6 MB on disk).</summary>
    public static byte[] ToPng(byte[] image)
    {
        if (image.Length < 2 || image[0] != (byte)'B' || image[1] != (byte)'M') return image;
        using var input = new MemoryStream(image);
        using var bitmap = new Bitmap(input);
        using var output = new MemoryStream();
        bitmap.Save(output, ImageFormat.Png);
        return output.ToArray();
    }

    /// <summary>BMP copy of a frame (ADB captures arrive as PNG); BMP input is returned as is.</summary>
    public static byte[] ToBmp(byte[] image)
    {
        if (image.Length >= 2 && image[0] == (byte)'B' && image[1] == (byte)'M') return image;
        using var input = new MemoryStream(image);
        using var bitmap = new Bitmap(input);
        return Encode(bitmap);
    }

    /// <summary>File extension matching the encoded bytes (for saved debug artifacts).</summary>
    public static string Extension(byte[] image) => image.Length > 1 && image[0] == (byte)'B' && image[1] == (byte)'M' ? ".bmp" : ".png";
}
