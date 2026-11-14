using System.Drawing;
using System.Drawing.Drawing2D;
using System.Text.Json;
using ASPAssistant.Core.Models;

namespace ASPAssistant.Core.Services;

/// <summary>Matches the theme-specific shop control, independently of operator names and card colours.</summary>
public sealed class GameShopThemeDetector
{
    private readonly (GameShopTheme Theme, byte[] Pixels)[] _references;

    public GameShopThemeDetector(string referencePath)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(referencePath));
        var references = document.RootElement.GetProperty("references");
        _references = Enum.GetValues<GameShopTheme>()
            .Select(theme => (theme, references.GetProperty(theme.ToString()).GetBytesFromBase64())).ToArray();
        if (_references.Any(reference => reference.Pixels.Length != 24 * 40))
            throw new InvalidDataException("模式主题参考图尺寸无效");
    }

    public GameShopTheme? Detect(byte[] screenshot)
    {
        using var stream = new MemoryStream(screenshot);
        using var image = new Bitmap(stream);
        var scores = _references.Select(reference => (reference.Theme, Score: double.PositiveInfinity)).ToArray();
        // UI scaling changes the size of the control, while its bottom-right anchor stays fixed.
        foreach (var scale in new[] { 1.0, 0.85, 1.1, 1.2, 1.3, 0.75, 1.4, 1.5 })
        {
            var pixels = Fingerprint(image, scale);
            for (int theme = 0; theme < _references.Length; theme++)
            {
                int difference = 0;
                for (int i = 0; i < pixels.Length; i++)
                    difference += Math.Abs(pixels[i] - _references[theme].Pixels[i]);
                scores[theme].Score = Math.Min(scores[theme].Score, (double)difference / pixels.Length);
            }
        }
        var ranked = scores.OrderBy(score => score.Score).ToArray();
        return ranked[0].Score <= 22 && ranked[1].Score - ranked[0].Score >= 8 ? ranked[0].Theme : null;
    }

    private static byte[] Fingerprint(Bitmap image, double scale)
    {
        using var sample = new Bitmap(24, 40);
        using (var graphics = Graphics.FromImage(sample))
        {
            graphics.InterpolationMode = InterpolationMode.HighQualityBilinear;
            graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            int left = (int)(image.Width - image.Width * 0.12 * scale);
            int top = (int)(image.Height - image.Height * 0.3 * scale);
            graphics.DrawImage(image, new Rectangle(0, 0, 24, 40), left, top,
                image.Width - left, image.Height - top, GraphicsUnit.Pixel);
        }
        var luminance = new double[24 * 40];
        for (int y = 0; y < 40; y++)
            for (int x = 0; x < 24; x++)
            {
                var colour = sample.GetPixel(x, y);
                luminance[y * 24 + x] = (colour.R * 299 + colour.G * 587 + colour.B * 114) / 1000.0;
            }
        double mean = luminance.Average();
        double deviation = Math.Max(1, Math.Sqrt(luminance.Average(value => (value - mean) * (value - mean))));
        return luminance.Select(value => (byte)Math.Clamp(Math.Round((value - mean) / deviation * 40 + 128), 0, 255)).ToArray();
    }
}
