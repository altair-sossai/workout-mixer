using Microsoft.Extensions.Options;
using NAudio.Lame;

namespace WorkoutMixer.Configuration.Validators;

internal sealed class AudioOptionsValidator : IValidateOptions<AudioOptions>
{
    public ValidateOptionsResult Validate(string? name, AudioOptions options)
    {
        if (options.TrackOverlapSeconds < 0)
            return ValidateOptionsResult.Fail("Audio track overlap must be zero or greater.");

        if (options.TargetSampleRate <= 0)
            return ValidateOptionsResult.Fail("Audio target sample rate must be greater than zero.");

        if (options.TargetChannels <= 0)
            return ValidateOptionsResult.Fail("Audio target channels must be greater than zero.");

        if (!Enum.TryParse<LAMEPreset>(options.Mp3Preset, true, out _))
            return ValidateOptionsResult.Fail("Audio MP3 preset must match a valid LAME preset name.");

        return ValidateOptionsResult.Success;
    }
}