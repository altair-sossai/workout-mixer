using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WorkoutMixer.Configuration.Validators;

namespace WorkoutMixer.Configuration.Extensions;

public static class AppOptionsServiceCollectionExtensions
{
    public static void AddAppOptions(this IServiceCollection serviceCollection, IConfiguration configuration)
    {
        serviceCollection.AddSingleton<IValidateOptions<AudioOptions>, AudioOptionsValidator>();
        serviceCollection.AddSingleton<IValidateOptions<ExportOptions>, ExportOptionsValidator>();
        serviceCollection.AddSingleton<IValidateOptions<WaveformOptions>, WaveformOptionsValidator>();
        serviceCollection.AddSingleton<IValidateOptions<WorkoutDefaultsOptions>, WorkoutDefaultsOptionsValidator>();

        serviceCollection
            .AddOptions<AudioOptions>()
            .Bind(configuration.GetSection(AudioOptions.SectionName))
            .ValidateOnStart();

        serviceCollection
            .AddOptions<ExportOptions>()
            .Bind(configuration.GetSection(ExportOptions.SectionName))
            .ValidateOnStart();

        serviceCollection
            .AddOptions<WaveformOptions>()
            .Bind(configuration.GetSection(WaveformOptions.SectionName))
            .ValidateOnStart();

        serviceCollection
            .AddOptions<WorkoutDefaultsOptions>()
            .Bind(configuration.GetSection(WorkoutDefaultsOptions.SectionName))
            .ValidateOnStart();
    }
}