// Added for ASP Assistant Arknights on 2026-11-13; see NOTICE.
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using ASPAssistant.Core.Models;
using ASPAssistant.Core.ViewModels;

namespace ASPAssistant.App.Converters;

/// <summary>Tracked equipment → who wants it, e.g. 「优先：冻河 · 候选：角峰」.</summary>
public sealed class EquipmentWantersConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        if (values[0] is not TrackingEntry entry || values[1] is not TrackingViewModel vm) return DependencyProperty.UnsetValue;
        var parts = vm.EquipmentWanters(entry.Name).GroupBy(w => w.Kind).OrderBy(g => g.Key)
            .Select(g => $"{Label(g.Key)}：{string.Join("、", g.Select(w => w.Operator))}").ToList();
        return parts.Count == 0 ? DependencyProperty.UnsetValue : string.Join(" · ", parts);
    }

    private static string Label(TrackedEquipmentKind kind) => kind switch
    {
        TrackedEquipmentKind.Planned => "装备格",
        TrackedEquipmentKind.Priority => "优先",
        _ => "候选",
    };

    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
