using Microsoft.Extensions.Options;

namespace WorkoutMixer.Configuration.Validators;

internal sealed class WaveformOptionsValidator : IValidateOptions<WaveformOptions>
{
    public ValidateOptionsResult Validate(string? name, WaveformOptions options)
    {
        if (options.FinalChartSampleSeconds <= 0)
            return ValidateOptionsResult.Fail("Waveform final chart sample seconds must be greater than zero.");

        if (options.PointReductionFactor <= 0)
            return ValidateOptionsResult.Fail("Waveform point reduction factor must be greater than zero.");

        if (options.SmoothingRadius < 0)
            return ValidateOptionsResult.Fail("Waveform smoothing radius must be zero or greater.");

        return ValidateOptionsResult.Success;
    }
}
