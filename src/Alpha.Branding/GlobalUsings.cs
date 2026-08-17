global using System;
global using System.Globalization;
global using System.IO;
global using System.Windows.Data;

namespace Alpha.Branding;

public sealed class InverseBooleanConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => value is bool busy && !busy;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
