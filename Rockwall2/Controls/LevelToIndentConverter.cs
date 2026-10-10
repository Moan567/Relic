using Avalonia;
using Avalonia.Data.Converters;
using System;
using System.Globalization;

namespace Rockwall2.Controls;
public class LevelToIndentConverter : IValueConverter
{
    public static readonly LevelToIndentConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var level = value is int i ? i : 0;
        var indent = parameter is string s && double.TryParse(s, out var d) ? d : 16;
        return new Thickness(level * indent, 0, 0, 0);
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}