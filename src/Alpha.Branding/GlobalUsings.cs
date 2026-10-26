global using System;
global using System.Globalization;
global using System.IO;
global using System.Linq;
global using System.Windows.Data;

namespace Alpha.Branding;

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is bool busy && !busy;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is bool b && b ? System.Windows.Visibility.Collapsed : System.Windows.Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class CountToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        int count = value switch
        {
            int i => i,
            System.Collections.ICollection c => c.Count,
            _ => 0
        };
        bool isNonEmpty = count > 0;
        if (parameter is string p && p.Equals("Inverse", StringComparison.OrdinalIgnoreCase))
            isNonEmpty = !isNonEmpty;
        return isNonEmpty ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public sealed class EmptyToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        int count = value switch
        {
            int i => i,
            System.Collections.ICollection c => c.Count,
            _ => 0
        };
        return count == 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

/// <summary>
/// Letter-spaced uppercase label. WPF has no tracking, so thin spaces (U+2009) are interleaved
/// between the characters; the automation name keeps the plain text for screen readers.
/// </summary>
public static class Tracked
{
    public static readonly System.Windows.DependencyProperty TextProperty =
        System.Windows.DependencyProperty.RegisterAttached(
            "Text", typeof(string), typeof(Tracked), new System.Windows.PropertyMetadata(null, OnTextChanged));

    public static string? GetText(System.Windows.DependencyObject element) => (string?)element.GetValue(TextProperty);
    public static void SetText(System.Windows.DependencyObject element, string? value) => element.SetValue(TextProperty, value);

    private static void OnTextChanged(System.Windows.DependencyObject d, System.Windows.DependencyPropertyChangedEventArgs e)
    {
        if (d is not System.Windows.Controls.TextBlock block) return;
        var text = e.NewValue as string ?? string.Empty;
        block.Text = string.Join(' ', text.ToUpperInvariant().Select(c => c.ToString()));
        System.Windows.Automation.AutomationProperties.SetName(block, text);
    }
}
