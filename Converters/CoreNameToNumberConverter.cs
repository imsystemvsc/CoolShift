using System;
using System.Globalization;
using System.Windows.Data;

namespace ParkToggleWpf.Converters;

public class CoreNameToNumberConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string s)
        {
            var parts = s.Split(',');
            if (parts.Length == 2 && int.TryParse(parts[1], out int coreNumber))
            {
                return (coreNumber + 1).ToString();
            }
            return s;
        }
        return string.Empty;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
