using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WorkoutMixer.Configuration.Validators;

namespace WorkoutMixer.Configuration.Extensions;

public static class ChartOptionsServiceCollectionExtensions
{
    public static void AddChartOptions(this IServiceCollection serviceCollection, IConfiguration configuration)
    {
        serviceCollection
            .AddSingleton<IValidateOptions<ChartOptions>, ChartOptionsValidator>();

        serviceCollection
            .AddOptions<ChartOptions>()
            .Bind(configuration.GetSection(ChartOptions.SectionName))
            .ValidateOnStart();
    }
}