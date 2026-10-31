using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace FM.App.Converters;

/// <summary>
/// Compares value to ConverterParameter (as string). Returns Visible when equal, Collapsed otherwise.
/// </summary>
public class EqualityToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value == null && parameter == null) return Visibility.Visible;
        if (value == null || parameter == null) return Visibility.Collapsed;

        string valStr = value.ToString() ?? string.Empty;
        string paramStr = parameter.ToString() ?? string.Empty;

        return string.Equals(valStr, paramStr, StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>
/// Inverts boolean to Visibility (true -> Collapsed, false -> Visible).
/// </summary>
public class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool b = value is bool flag && flag;
        return b ? Visibility.Collapsed : Visibility.Visible;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

