using System.Configuration;
using System.Data;
using System.Windows;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.IO;
using Microsoft.Extensions.DependencyInjection;
using PrintDesktopClient.Services;
using PrintDesktopClient.ViewModels;

namespace PrintDesktopClient;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public static IHost? AppHost { get; private set; }

    public App()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Debug()
            .WriteTo.File(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PrintDesktopClient", "logs", "log.txt"), rollingInterval: RollingInterval.Day)
            .CreateLogger();

        AppHost = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices((hostContext, services) =>
            {
                // ViewModels
                services.AddSingleton<MainViewModel>();

                // Views
                services.AddSingleton<MainWindow>();

                // Services
                services.AddSingleton<PrinterService>();
                services.AddSingleton<MqttListenerService>();
                services.AddSingleton<DocumentProcessingService>();
                services.AddSingleton<NotificationService>();
                services.AddSingleton<ConfigurationService>();
            })
            .Build();
    }

    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        await AppHost!.StartAsync();

        var mainWindow = AppHost.Services.GetRequiredService<MainWindow>();
        mainWindow.Show();
    }

    private async void Application_Exit(object sender, ExitEventArgs e)
    {
        await AppHost!.StopAsync();
        AppHost.Dispose();
    }
}
