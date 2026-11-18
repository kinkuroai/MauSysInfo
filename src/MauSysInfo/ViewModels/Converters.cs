using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;

namespace MauSysInfo.ViewModels;

public static class Converters
{
    public static IValueConverter NotEmptyToVisibility { get; } = new NotEmptyToVisibilityConverter();

    private sealed class NotEmptyToVisibilityConverter : IValueConverter
    {
        public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            value is string s && !string.IsNullOrWhiteSpace(s) ? Visibility.Visible : Visibility.Collapsed;

        public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
            throw new NotSupportedException();
    }
}

/// <summary>Window-level commands that need the window (file dialog, clipboard).</summary>
public static class WindowCommands
{
    public static RoutedUICommand SaveReport { get; } = new("Save report", nameof(SaveReport), typeof(WindowCommands));
    public static RoutedUICommand CopyReport { get; } = new("Copy report", nameof(CopyReport), typeof(WindowCommands));
}
