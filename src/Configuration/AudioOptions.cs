using NAudio.Lame;

namespace WorkoutMixer.Configuration;

public sealed class AudioOptions
{
    public const string SectionName = "Audio";

    public int TrackOverlapSeconds { get; init; } = 5;
    public int TargetSampleRate { get; init; } = 44100;
    public int TargetChannels { get; init; } = 2;
    public string Mp3Preset { get; init; } = nameof(LAMEPreset.VBR_90);
}
