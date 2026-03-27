namespace WorkoutMixer.Configuration;

public sealed class ExportOptions
{
    public const string SectionName = "Export";

    public string FinalMixDefaultFileName { get; init; } = "workout-mix.mp3";
    public string IntensityReportDefaultFileName { get; init; } = "workout-intensities.txt";
}