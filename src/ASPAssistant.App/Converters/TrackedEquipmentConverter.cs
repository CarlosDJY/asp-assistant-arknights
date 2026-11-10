// Modified for ASP Assistant Regulation on 2026-11-10; see MODIFICATIONS.md.
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using ASPAssistant.Core.Models;

namespace ASPAssistant.App.Converters;

public sealed class TrackedEquipmentConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values[0] is not TrackingEntry entry || values[1] is not IReadOnlyDictionary<string, Equipment> equipment)
            return DependencyProperty.UnsetValue;
        return equipment.TryGetValue(entry.Name, out var item) ? item : entry;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
