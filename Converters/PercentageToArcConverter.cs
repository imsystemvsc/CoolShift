using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace CoolShift.Converters;

public class PercentageToArcConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        double percentage = 0;
        if (value is double d) percentage = d;
        else if (value is float f) percentage = f;
        else if (value is int i) percentage = i;
        else if (value != null && double.TryParse(value.ToString(), out var parsed)) percentage = parsed;

        percentage = Math.Clamp(percentage, 0.0, 100.0);

        const double cx = 25.0;
        const double cy = 25.0;
        const double radius = 21.0;

        if (percentage <= 0.0)
        {
            return Geometry.Empty;
        }

        if (percentage >= 100.0)
        {
            return new EllipseGeometry(new Point(cx, cy), radius, radius);
        }

        double angle = (percentage / 100.0) * 360.0;
        double radians = (angle - 90.0) * (Math.PI / 180.0);

        Point startPoint = new(cx, cy - radius);
        Point endPoint = new(cx + radius * Math.Cos(radians), cy + radius * Math.Sin(radians));

        bool isLargeArc = angle > 180.0;

        PathFigure figure = new()
        {
            StartPoint = startPoint,
            IsClosed = false,
            IsFilled = false
        };

        figure.Segments.Add(new ArcSegment
        {
            Point = endPoint,
            Size = new Size(radius, radius),
            IsLargeArc = isLargeArc,
            SweepDirection = SweepDirection.Clockwise
        });

        PathGeometry geometry = new();
        geometry.Figures.Add(figure);
        return geometry;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotImplementedException();
    }
}
