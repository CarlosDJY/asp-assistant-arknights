using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace ASPAssistant.App.Converters;

/// <summary>
/// Converts a relative <c>data/</c>-directory path string (e.g.
/// <c>icons/enemies/enemy_1041_lazerd.png</c>) to a WPF <see cref="BitmapImage"/>.
///
/// The full path is resolved as
/// <c>AppContext.BaseDirectory + "data" + iconPath</c>.
/// Returns <c>null</c> when the path is empty or the file does not exist.
/// </summary>
[ValueConversion(typeof(string), typeof(BitmapImage))]
public sealed class LocalPathToImageConverter : IValueConverter
{
    /// <summary>
    /// Icons are frozen and shared: lists and planner results re-create their templates often
    /// (filters, re-plans, tooltips), and each one used to read and decode the PNG from disk again.
    /// </summary>
    private static readonly ConcurrentDictionary<string, BitmapImage?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string iconPath || string.IsNullOrEmpty(iconPath))
            return null;
        return LoadCached(iconPath);
    }

    /// <summary>Cached, frozen icon for a <c>data/</c>-relative path (null when empty or missing).</summary>
    public static BitmapImage? LoadCached(string? iconPath)
        => string.IsNullOrEmpty(iconPath) ? null : Cache.GetOrAdd(iconPath, Load);

    private static BitmapImage? Load(string iconPath)
    {
        var fullPath = Path.Combine(AppContext.BaseDirectory, "data", iconPath);
        if (!File.Exists(fullPath))
            return null;

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource   = new Uri(fullPath, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    public object? ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
