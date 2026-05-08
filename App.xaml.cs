using System;
using System.Configuration;
using System.Threading;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PrintDesktopClient.Services;
using PrintDesktopClient.ViewModels;
using Serilog;
using System.IO;

namespace PrintDesktopClient;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application
{
    public static IHost? AppHost { get; private set; }

    private static Mutex? _singleInstanceMutex;

    public App()
    {
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Debug()
            .WriteTo.File(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "PrintDesktopClient", "logs", "log.txt"),
                rollingInterval: RollingInterval.Day)
            .CreateLogger();

        AppHost = Host.CreateDefaultBuilder()
            .UseSerilog()
            .ConfigureServices((hostContext, services) =>
            {
                // HTTP Client
                services.AddHttpClient("PrintAgent");

                // ViewModels
                services.AddSingleton<MainViewModel>();

                // Views
                services.AddSingleton<MainWindow>();

                // Services
                services.AddSingleton<ConfigurationService>();
                services.AddSingleton<PrinterService>();
                services.AddSingleton<ApiService>();
                services.AddSingleton<MqttListenerService>();
                services.AddHostedService<MqttListenerService>(p => p.GetRequiredService<MqttListenerService>());
                services.AddSingleton<DocumentProcessingService>();
                services.AddSingleton<NotificationService>();
            })
            .Build();
    }

    private async void Application_Startup(object sender, StartupEventArgs e)
    {
        // ── Single-instance enforcement ────────────────────────────────────────
        _singleInstanceMutex = new Mutex(true, "PrintDesktopClient_SingleInstance", out bool isFirstInstance);

        if (!isFirstInstance)
        {
            // Another instance is already running; bring it to the foreground and exit
            MessageBox.Show("Auto-Print Agent is already running.", "Already Running",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        await AppHost!.StartAsync();

        // ── Wire revocation across ApiService & MqttListenerService ───────────
        var apiService  = AppHost.Services.GetRequiredService<ApiService>();
        var mqttService = AppHost.Services.GetRequiredService<MqttListenerService>();
        var config      = AppHost.Services.GetRequiredService<ConfigurationService>();
        var viewModel   = AppHost.Services.GetRequiredService<MainViewModel>();

        apiService.OnUnauthorized += () => Dispatcher.Invoke(() => viewModel.TriggerRevocation());
        mqttService.OnRevokeCommand += () => Dispatcher.Invoke(() => viewModel.TriggerRevocation());

        // ── --background launch mode: tray-only, no window ────────────────────
        bool background = e.Args.Length > 0 && e.Args[0].Equals("--background", StringComparison.OrdinalIgnoreCase);

        var mainWindow = AppHost.Services.GetRequiredService<MainWindow>();

        if (!background)
        {
            mainWindow.Show();
        }
        // When running in background mode the tray icon (defined in MainWindow.xaml) is
        // still alive because MainWindow is instantiated; we just don't Show() it.
    }

    private async void Application_Exit(object sender, ExitEventArgs e)
    {
        await AppHost!.StopAsync();
        AppHost.Dispose();
        _singleInstanceMutex?.ReleaseMutex();
    }
}
