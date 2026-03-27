namespace WorkoutMixer.Configuration;

public sealed class WorkoutDefaultsOptions
{
    public const string SectionName = "WorkoutDefaults";

    public double SegmentDurationMinutes { get; init; } = 5;
    public int SegmentRpm { get; init; } = 65;
}
