using WorkoutMixer.Models;
using WorkoutMixer.Models.Factories;

namespace WorkoutMixer.Configuration.Extensions;

internal static class ChartOptionsExtensions
{
    public static List<ChartZone> ToChartZones(this ChartOptions chartOptions)
    {
        return chartOptions.Zones
            .Select(ChartZoneFactory.CreateChartZone)
            .ToList();
    }
}