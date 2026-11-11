using System.Globalization;
using System.Windows.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.Services;

namespace ASPAssistant.App.Converters;

public sealed class TrackedTransformationsConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        // WPF resolves the entry and the view's equipment rules in separate binding passes.
        if (values[0] is not TrackingEntry entry || values[1] is not LineupEquipmentService rules)
            return Array.Empty<EquipmentTransformation>();
        return rules.Transformations(new LineupSlot { OperatorName = entry.Name, Equipments = entry.Equipments, IsElite = entry.IsElite });
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
