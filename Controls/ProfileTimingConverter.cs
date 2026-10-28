using System.Collections;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;
using OneTunnel.Core;

namespace OneTunnel.Controls;

public sealed class ProfileTimingConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
    {
        var row = values.Length >= 3 && values[0] is Guid id && values[2] is not true && values[1] is IEnumerable rows
            ? rows.Cast<object>().OfType<GroupResultDisplayRow>().FirstOrDefault(row => row.ID == id.ToString()) : null;
        long? latency = row?.HTTPSLatencyMilliseconds ?? row?.DNSLatencyMilliseconds;
        if (parameter as string == "brush") return new SolidColorBrush((Color)ColorConverter.ConvertFromString(row?.Success == false || latency > 150 ? "#FF6572" : latency <= 80 ? "#80B2FF" : "#B2BECD"));
        return row == null ? "—" : !row.Success ? "Недоступен" : latency is { } value ? value + (row.HTTPSLatencyMilliseconds == null ? " мс DNS" : " мс") : "—";
    }
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class FavoriteStateConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture) => values.Length >= 2 && values[0] is Guid id && (parameter as string == "active" ? values[1] is Guid active && active == id : values[1] is IEnumerable<Guid> ids && ids.Contains(id));
    public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
