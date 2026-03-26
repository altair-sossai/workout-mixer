using Microsoft.Extensions.Options;

namespace WorkoutMixer.Configuration.Validators;

internal sealed class ChartOptionsValidator : IValidateOptions<ChartOptions>
{
    public ValidateOptionsResult Validate(string? name, ChartOptions options)
    {
        if (options.Zones.Count == 0)
            return ValidateOptionsResult.Fail("At least one chart zone must be configured.");

        var invalidZone = options.Zones.FirstOrDefault(zone =>
            string.IsNullOrWhiteSpace(zone.Name) ||
            string.IsNullOrWhiteSpace(zone.Color) ||
            zone.MaxValue is < 0 or > 1);

        if (invalidZone is not null)
            return ValidateOptionsResult.Fail("Each chart zone must define a name, a color, and a max value between 0 and 1.");

        return ValidateOptionsResult.Success;
    }
}