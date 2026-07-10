using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WorkRecordAssistant.Converters;

public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var count = value is int i ? i : 0;
        var hasItems = count > 0;
        if (parameter?.ToString() == "Inverse")
            hasItems = !hasItems;

        return hasItems ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
