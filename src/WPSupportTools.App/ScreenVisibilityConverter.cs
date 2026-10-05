using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace WPSupportTools.App;

/// <summary>Visível quando o valor do enum está na lista do parâmetro (ex.: "Running,Completed").</summary>
public sealed class ScreenVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var names = (parameter as string ?? "").Split(',', StringSplitOptions.TrimEntries);
        return value is not null && names.Contains(value.ToString()) ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
