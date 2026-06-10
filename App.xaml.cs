using System;
using System.IO;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PrintDesktopClient.Services;
using PrintDesktopClient.ViewModels;
using Serilog;

namespace PrintDesktopClient
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        public static IHost? AppHost { get; private set; }

        private static Mutex? _singleInstanceMutex;

        public App()
        {
            // ── Fallback crash logger (runs before Serilog / DI is ready) ──────────
            // Captures crashes that happen during startup before any logger is initialized.
            // Log path: %LOCALAPPDATA%\PrintDesktopClient\logs\crash.txt
            var logDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "PrintDesktopClient", "logs");
            Directory.CreateDirectory(logDir);
            var crashLogPath = Path.Combine(logDir, "crash.txt");

            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
            {
                var msg = string.Format("[{0:yyyy-MM-dd HH:mm:ss}] UNHANDLED EXCEPTION (IsTerminating={1}){2}{3}{2}{4}{2}",
                    DateTime.Now, args.IsTerminating, Environment.NewLine,
                    args.ExceptionObject, new string('-', 80));
                try { File.AppendAllText(crashLogPath, msg); } catch { /* best-effort */ }
            };

            DispatcherUnhandledException += (_, args) =>
            {
                var msg = string.Format("[{0:yyyy-MM-dd HH:mm:ss}] DISPATCHER UNHANDLED EXCEPTION{1}{2}{1}{3}{1}",
                    DateTime.Now, Environment.NewLine, args.Exception, new string('-', 80));
                try { File.AppendAllText(crashLogPath, msg); } catch { /* best-effort */ }
                // Do NOT set args.Handled = true — let it propagate so Serilog also catches it.
            };

            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                var msg = string.Format("[{0:yyyy-MM-dd HH:mm:ss}] UNOBSERVED TASK EXCEPTION{1}{2}{1}{3}{1}",
                    DateTime.Now, Environment.NewLine, args.Exception, new string('-', 80));
                try { File.AppendAllText(crashLogPath, msg); } catch { /* best-effort */ }
                args.SetObserved(); // Prevent process termination for fire-and-forget task faults.
            };

            // ── TLS 1.2 fix (Windows 7 defaults to TLS 1.0) ──────────────────
            ServicePointManager.SecurityProtocol =
                SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;

            // ── Serilog (structured file logger) ─────────────────────────────────
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.Debug()
                .WriteTo.File(
                    Path.Combine(logDir, "log.txt"),
                    rollingInterval: RollingInterval.Day)
                .CreateLogger();

            Log.Information("Application starting. OS: {OS} | CLR: {CLR}",
                Environment.OSVersion.ToString(),
                Environment.Version.ToString());

            AppHost = Host.CreateDefaultBuilder()
                .UseSerilog()
                .ConfigureServices((hostContext, services) =>
                {
                    // HTTP Client
                    services.AddHttpClient("PrintAgent");

                    // ViewModels
                    services.AddSingleton<MainViewModel>();

                    // Views — MainWindow now takes NotificationService via DI
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
                MessageBox.Show("Auto-Print Agent is already running.", "Already Running",
                    MessageBoxButton.OK, MessageBoxImage.Information);
                Shutdown();
                return;
            }

            await AppHost!.StartAsync();

            // ── Wire revocation across ApiService & MqttListenerService ───────────
            var apiService  = AppHost.Services.GetRequiredService<ApiService>();
            var mqttService = AppHost.Services.GetRequiredService<MqttListenerService>();
            var viewModel   = AppHost.Services.GetRequiredService<MainViewModel>();

            apiService.OnUnauthorized  += () => Dispatcher.Invoke(() => viewModel.TriggerRevocation());
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
}
