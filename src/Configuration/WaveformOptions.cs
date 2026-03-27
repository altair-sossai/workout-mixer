namespace WorkoutMixer.Configuration;

public sealed class WaveformOptions
{
    public const string SectionName = "Waveform";

    public int FinalChartSampleSeconds { get; init; } = 2;
    public int PointReductionFactor { get; init; } = 3;
    public int SmoothingRadius { get; init; } = 2;
}
