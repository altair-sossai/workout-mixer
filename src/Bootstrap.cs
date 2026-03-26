using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using Serilog;

namespace WorkoutMixer;

public static class Bootstrap
{
    [ModuleInitializer]
    public static void Initializer()
    {
        var culture = new CultureInfo("en-US");
        Thread.CurrentThread.CurrentCulture = culture;
        Thread.CurrentThread.CurrentUICulture = culture;

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.Console()
            .WriteTo.File("logs/log-.txt", rollingInterval: RollingInterval.Day, retainedFileCountLimit: 10)
            .CreateLogger();

        Directory.SetCurrentDirectory(AppContext.BaseDirectory);
    }
}