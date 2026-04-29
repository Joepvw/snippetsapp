using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace SnippetLauncher.App.Converters;

/// <summary>Shows Visible when string length > 0, Collapsed otherwise.</summary>
[ValueConversion(typeof(int), typeof(Visibility))]
public sealed class LengthToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int n && n > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Returns Collapsed when value is null, Visible otherwise.</summary>
[ValueConversion(typeof(object), typeof(Visibility))]
public sealed class NullToCollapsedConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is null ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Returns a filled brush for active wizard step dots (bool → Brush).</summary>
[ValueConversion(typeof(bool), typeof(Brush))]
public sealed class BoolToBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true
            ? new SolidColorBrush(Color.FromRgb(0x15, 0x65, 0xC0))  // active: blue
            : new SolidColorBrush(Color.FromRgb(0xCC, 0xCC, 0xCC)); // inactive: grey

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Returns red brush on error, green on success (bool HasError → Brush).</summary>
[ValueConversion(typeof(bool), typeof(Brush))]
public sealed class BoolToErrorBrushConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is true
            ? new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28))  // error: red
            : new SolidColorBrush(Color.FromRgb(0x2E, 0x7D, 0x32)); // success: green

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
