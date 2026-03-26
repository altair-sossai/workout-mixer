using System.Windows.Media;
using WorkoutMixer.Configuration;

namespace WorkoutMixer.Models.Factories;

internal static class ChartZoneFactory
{
    public static ChartZone CreateChartZone(ChartZoneOptions zoneOptions)
    {
        var brushConverter = new BrushConverter();

        if (brushConverter.ConvertFromString(zoneOptions.Color) is not Brush brush)
            throw new InvalidOperationException($"The configured chart zone color '{zoneOptions.Color}' is invalid.");

        if (brush.CanFreeze)
            brush.Freeze();

        return new ChartZone(zoneOptions.Name, brush, zoneOptions.MaxValue);
    }
}