using Microsoft.Extensions.Options;

namespace WorkoutMixer.Configuration.Validators;

internal sealed class WorkoutDefaultsOptionsValidator : IValidateOptions<WorkoutDefaultsOptions>
{
    public ValidateOptionsResult Validate(string? name, WorkoutDefaultsOptions options)
    {
        if (options.SegmentDurationMinutes <= 0)
            return ValidateOptionsResult.Fail("Workout default segment duration must be greater than zero.");

        if (options.SegmentRpm <= 0)
            return ValidateOptionsResult.Fail("Workout default segment RPM must be greater than zero.");

        return ValidateOptionsResult.Success;
    }
}
