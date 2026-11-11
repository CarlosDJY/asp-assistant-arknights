// Modified for ASP Assistant Arknights on 2026-11-12; see NOTICE.
using System.Globalization;
using System.Windows.Data;

namespace ASPAssistant.App.Converters;

/// <summary>"on" when every bound value is equal (used as a Tag for "this choice is selected"), otherwise null.</summary>
public sealed class EqualityMultiConverter : IMultiValueConverter
{
    public object? Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        => values.Length > 1 && values.Skip(1).All(v => Equals(v, values[0])) ? "on" : null;

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
