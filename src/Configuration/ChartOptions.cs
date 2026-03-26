namespace WorkoutMixer.Configuration;

public sealed class ChartOptions
{
    public const string SectionName = "Chart";
    public List<ChartZoneOptions> Zones { get; init; } = [];
}