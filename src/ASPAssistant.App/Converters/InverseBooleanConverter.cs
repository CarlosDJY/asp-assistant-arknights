// Modified for ASP Assistant Arknights on 2026-11-12; see NOTICE.
using System.Globalization;
using System.Windows.Data;

namespace ASPAssistant.App.Converters;

/// <summary>true ↔ false (e.g. IsEnabled = !IsBlocked).</summary>
public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is not true;
}
