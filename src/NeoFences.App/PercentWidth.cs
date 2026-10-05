using System.Globalization;
using System.Windows.Data;

namespace NeoFences.App;

/// <summary>A stats bar's fill (M25): a percent 0–100 scaled to its track (M31: as a 0–1 fraction).</summary>
public sealed class PercentWidth(double track) : IValueConverter
{
    /// <summary>0–1 for a ScaleTransform (M31: a bar as wide as its tile, whatever the tile's width).</summary>
    public static PercentWidth Fraction { get; } = new(1);

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is double percent ? Math.Clamp(percent, 0, 100) * track / 100 : 0.0;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
