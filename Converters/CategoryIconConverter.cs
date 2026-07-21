using System;
using System.Globalization;
using System.Windows.Data;

namespace ParkToggleWpf.Converters;

public class CategoryIconConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is string category)
        {
            if (category.Contains("CPU", StringComparison.OrdinalIgnoreCase)) return "🖥";
            if (category.Contains("GPU", StringComparison.OrdinalIgnoreCase)) return "🎮";
            if (category.Contains("Memory", StringComparison.OrdinalIgnoreCase) || category.Contains("RAM", StringComparison.OrdinalIgnoreCase)) return "🧠";
            if (category.Contains("Storage", StringComparison.OrdinalIgnoreCase) || category.Contains("Disk", StringComparison.OrdinalIgnoreCase)) return "💾";
            if (category.Contains("Motherboard", StringComparison.OrdinalIgnoreCase)) return "📟";
            if (category.Contains("Network", StringComparison.OrdinalIgnoreCase)) return "🌐";
        }
        return "⚡";
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
