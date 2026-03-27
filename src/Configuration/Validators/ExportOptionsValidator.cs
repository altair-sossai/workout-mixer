using Microsoft.Extensions.Options;

namespace WorkoutMixer.Configuration.Validators;

internal sealed class ExportOptionsValidator : IValidateOptions<ExportOptions>
{
    public ValidateOptionsResult Validate(string? name, ExportOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.FinalMixDefaultFileName))
            return ValidateOptionsResult.Fail("Export final mix default file name must be configured.");

        if (string.IsNullOrWhiteSpace(options.IntensityReportDefaultFileName))
            return ValidateOptionsResult.Fail("Export intensity report default file name must be configured.");

        return ValidateOptionsResult.Success;
    }
}
