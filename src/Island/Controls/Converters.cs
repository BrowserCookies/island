using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Island.Services;

namespace Island.Controls;

/// <summary>"#RRGGBB" → SolidColorBrush (Transparent if empty/invalid).</summary>
public sealed class ColorBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        ThemeService.ParseColor(value as string) is { } c ? new SolidColorBrush(c) : Brushes.Transparent;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

public sealed class BoolToVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }
    public bool Hidden { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool v = value switch
        {
            bool b => b,
            string s => !string.IsNullOrWhiteSpace(s),
            null => false,
            _ => true,
        };
        if (Invert) v = !v;
        return v ? Visibility.Visible : Hidden ? Visibility.Hidden : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Visibility vis && (vis == Visibility.Visible) != Invert;
}

/// <summary>Compares an enum value to the parameter (for radio buttons bound to enums).</summary>
public sealed class EnumEqualsConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is not null && parameter is not null && string.Equals(value.ToString(), parameter.ToString(), StringComparison.Ordinal);

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true && parameter is not null ? Enum.Parse(targetType, parameter.ToString()!) : Binding.DoNothing;
}

/// <summary>Shows the enum's friendly name for action types.</summary>
public sealed class ActionTypeNameConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is Core.Models.ActionType t ? Core.Models.ActionFactory.DisplayName(t) : value?.ToString() ?? "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Visible when the enum value equals the parameter (comma-separated list allowed).</summary>
public sealed class EnumToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is null || parameter is null) return Visibility.Collapsed;
        var v = value.ToString();
        return parameter.ToString()!.Split(',').Any(p => string.Equals(p.Trim(), v, StringComparison.Ordinal))
            ? Visibility.Visible
            : Visibility.Collapsed;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}

/// <summary>Formats a 0..1 double as a percentage.</summary>
public sealed class PercentConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is double d ? $"{d * 100:0}%" : "";

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => Binding.DoNothing;
}
