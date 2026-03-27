using System.Reflection;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using WorkoutMixer.DependencyInjection;

namespace WorkoutMixer;

public partial class App
{
    private static readonly Type Type = typeof(App);
    private static readonly Assembly Assembly = Type.Assembly;
    private readonly IHost _host;

    public App()
    {
        try
        {
            Dispatcher.UnhandledException += OnDispatcherUnhandledException;

            var hostBuilder = CreateHostBuilder();

            _host = hostBuilder.Build();
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Failed to initialize the application host. The application will shut down.");
            throw;
        }
    }

    public static IServiceProvider Services => ((App)Current)._host.Services;

    protected override async void OnStartup(StartupEventArgs e)
    {
        try
        {
            base.OnStartup(e);

            await _host.StartAsync();

            Current.MainWindow = _host.Services.GetRequiredService<MainWindow>();
            Current.MainWindow.Show();
        }
        catch (Exception exception)
        {
            Log.Fatal(exception, "Failed to start the application. The application will shut down.");
            Shutdown(-1);
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        try
        {
            await _host.StopAsync();
            base.OnExit(e);
        }
        catch (Exception exception)
        {
            Log.Error(exception, "An error occurred while stopping the application host");
        }
        finally
        {
            _host.Dispose();
            await Log.CloseAndFlushAsync();
        }
    }

    private static IHostBuilder CreateHostBuilder()
    {
        return Host.CreateDefaultBuilder()
            .UseContentRoot(AppContext.BaseDirectory)
            .ConfigureServices((context, services) => services.AddApp(context.Configuration, Assembly));
    }

    private static void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        Log.Error(e.Exception, "An unhandled error occurred");

        MessageBox.Show($"An unhandled error occurred: {e.Exception.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);

        e.Handled = true;
    }
}