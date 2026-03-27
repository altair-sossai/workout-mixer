using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WorkoutMixer.Configuration.Extensions;

namespace WorkoutMixer.DependencyInjection;

public static class AppInjection
{
    public static void AddApp(this IServiceCollection serviceCollection, IConfiguration configuration, params Assembly[] assemblies)
    {
        serviceCollection.Scan(scan => scan
            .FromAssemblies(assemblies)
            .AddClasses()
            .AsImplementedInterfaces(type => assemblies.Contains(type.Assembly)));

        serviceCollection.AddChartOptions(configuration);
        serviceCollection.AddAppOptions(configuration);

        serviceCollection.AddSingleton<MainWindow>();
    }
}