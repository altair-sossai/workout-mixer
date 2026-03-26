namespace WorkoutMixer.Configuration;

public sealed class ChartZoneOptions
{
    public string Name { get; init; } = string.Empty;
    public string Color { get; init; } = string.Empty;
    public double MaxValue { get; init; }
}