using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using PrintDesktopClient.Services;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System;
using MaterialDesignThemes.Wpf;

namespace PrintDesktopClient.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly PrinterService _printerService;
        private readonly ConfigurationService _configurationService;
        private readonly MqttListenerService _mqttService;
        private readonly ApiService _apiService;
        private readonly NotificationService _notificationService;

        // ── Observable Properties ─────────────────────────────────────────────

        [ObservableProperty] private string _statusText = "Ready";
        [ObservableProperty] private string _mqttStatus = "Disconnected";
        [ObservableProperty] private string _selectedPrinter = string.Empty;

        // MQTT settings
        [ObservableProperty] private string _mqttBroker = string.Empty;
        [ObservableProperty] private string _mqttUsername = string.Empty;
        [ObservableProperty] private string _mqttPassword = string.Empty;
        [ObservableProperty] private string _mqttTopic = string.Empty;
        [ObservableProperty] private int    _reconnectIntervalMinutes = 5;

        // Cloud / API settings
        [ObservableProperty] private string _apiBaseUrl = string.Empty;
        [ObservableProperty] private string _deviceAccountId = string.Empty;
        [ObservableProperty] private string _apiSecret = string.Empty;
        [ObservableProperty] private string _deviceGuid = string.Empty;

        // Status indicators
        [ObservableProperty] private string _apiTestResult = string.Empty;
        [ObservableProperty] private bool   _isRevoked = false;

        public ObservableCollection<string> AvailablePrinters { get; } = new();
        public ObservableCollection<string> Logs { get; } = new();
        public SnackbarMessageQueue MessageQueue { get; } = new();

        // ── Constructor ───────────────────────────────────────────────────────

        public MainViewModel(
            PrinterService printerService,
            ConfigurationService configurationService,
            MqttListenerService mqttService,
            ApiService apiService,
            NotificationService notificationService)
        {
            _printerService       = printerService;
            _configurationService = configurationService;
            _mqttService          = mqttService;
            _apiService           = apiService;
            _notificationService  = notificationService;

            // Load persisted values into VM
            _mqttBroker               = _configurationService.MqttBroker;
            _mqttUsername             = _configurationService.MqttUsername;
            _mqttPassword             = _configurationService.GetMqttPassword();
            _mqttTopic                = _configurationService.MqttTopic;
            _reconnectIntervalMinutes = _configurationService.ReconnectIntervalMinutes;
            _apiBaseUrl               = _configurationService.ApiBaseUrl;
            _deviceAccountId          = _configurationService.DeviceAccountId;
            _apiSecret                = _configurationService.GetApiSecret();
            _deviceGuid               = _configurationService.DeviceGuid;

            // ── MQTT event bindings ────────────────────────────────────────────

            _mqttService.OnMessageReceived += msg => Dispatch(() =>
            {
                Logs.Add($"[MQTT TEXT] {msg}");
                _notificationService.Notify("New MQTT text message received.");
            });

            _mqttService.StatusChanged += status => Dispatch(() =>
            {
                MqttStatus = status;
                if (status == "Disconnected")
                    _notificationService.Notify("MQTT Connection lost!", isError: true);
            });

            // Print command: download job PDF and print silently
            _mqttService.OnPrintCommand += async (jobId, printerName) =>
            {
                Dispatch(() => Logs.Add($"[MQTT PRINT] JobId: {jobId} Printer: {printerName}"));
                await ExecutePrintJobAsync(jobId, printerName);
            };

            // Sync command: push printers to cloud
            _mqttService.OnSyncCommand += async () =>
            {
                Dispatch(() => Logs.Add("[MQTT SYNC] Printer sync commanded by cloud."));
                await SyncPrintersToCloud();
            };

            // Notifications pipe → Snackbar
            _notificationService.OnNotification += (msg, _) => Dispatch(() =>
            {
                MessageQueue.Enqueue(msg);
                StatusText = msg;
            });

            RefreshPrinters();

            // Restore printer selection
            if (!string.IsNullOrEmpty(_configurationService.SelectedPrinter) &&
                AvailablePrinters.Contains(_configurationService.SelectedPrinter))
                SelectedPrinter = _configurationService.SelectedPrinter;
            else if (AvailablePrinters.Any())
                SelectedPrinter = AvailablePrinters.First();

            Logs.Add($"Application initialized. DeviceGuid: {_deviceGuid}");
        }

        // ── Printer Commands ──────────────────────────────────────────────────

        [RelayCommand]
        public void RefreshPrinters()
        {
            var printers = _printerService.GetAvailablePrinters();
            var current  = SelectedPrinter;
            AvailablePrinters.Clear();
            foreach (var p in printers) AvailablePrinters.Add(p);
            if (AvailablePrinters.Contains(current)) SelectedPrinter = current;
            Logs.Add($"Printer list refreshed – {printers.Count} found.");
        }

        [RelayCommand]
        public async Task BrowseAndPrint()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Supported Files (*.pdf;*.txt;*.jpg;*.png)|*.pdf;*.txt;*.jpg;*.png|All Files (*.*)|*.*"
            };
            if (dialog.ShowDialog() == true)
                await Task.Run(() => PrintFile(dialog.FileName));
        }

        public void PrintFile(string filePath)
        {
            if (string.IsNullOrEmpty(SelectedPrinter))
            {
                _notificationService.Notify("No printer selected!", isError: true);
                return;
            }
            Dispatch(() => Logs.Add($"Queueing: {Path.GetFileName(filePath)}"));
            bool ok = _printerService.PrintFile(filePath, SelectedPrinter);
            _notificationService.Notify(ok
                ? $"Sent to printer: {Path.GetFileName(filePath)}"
                : $"Print failed: {Path.GetFileName(filePath)}", !ok);
        }

        // ── MQTT Settings ─────────────────────────────────────────────────────

        [RelayCommand]
        public async Task SaveMqttSettings()
        {
            _configurationService.MqttBroker              = MqttBroker;
            _configurationService.MqttUsername            = MqttUsername;
            _configurationService.MqttTopic               = MqttTopic;
            _configurationService.ReconnectIntervalMinutes = ReconnectIntervalMinutes;
            _configurationService.SaveMqttPassword(MqttPassword);

            Logs.Add("MQTT settings saved. Re-initializing connection...");
            await _mqttService.InitializeClientAsync();
            _notificationService.Notify("MQTT Settings Saved & Applied.");
        }

        [RelayCommand]
        public async Task ManualConnect()
        {
            Logs.Add("Manual connection attempt started...");
            await _mqttService.ManualConnectAsync();
        }

        // ── Cloud / API Settings ──────────────────────────────────────────────

        [RelayCommand]
        public async Task SaveApiSettings()
        {
            _configurationService.ApiBaseUrl      = ApiBaseUrl;
            _configurationService.DeviceAccountId = DeviceAccountId;
            _configurationService.SaveApiSecret(ApiSecret);

            Logs.Add("API settings saved. Authenticating...");
            bool ok = await _apiService.AuthenticateAsync();

            if (ok)
            {
                // Re-init MQTT so it uses the new device topic
                await _mqttService.InitializeClientAsync();
                _notificationService.Notify("Authentication successful! MQTT re-connected.");
            }
            else
            {
                _notificationService.Notify("Authentication failed. Check credentials.", isError: true);
            }
        }

        [RelayCommand]
        public async Task TestApiConnection()
        {
            ApiTestResult = "Testing…";
            bool ok = await _apiService.TestApiConnectionAsync();
            ApiTestResult = ok ? "✅ API Reachable" : "❌ Cannot reach API";
            Logs.Add($"API test result: {ApiTestResult}");
        }

        // ── Cloud Printer Sync ────────────────────────────────────────────────

        [RelayCommand]
        public async Task SyncPrinters()
        {
            Logs.Add("Syncing printers to cloud...");
            await SyncPrintersToCloud();
        }

        private async Task SyncPrintersToCloud()
        {
            var printers = _printerService.GetAvailablePrinters();
            bool ok = await _apiService.SyncPrintersAsync(printers);
            _notificationService.Notify(ok
                ? $"Synced {printers.Count} printers to cloud."
                : "Printer sync failed.", !ok);
            Dispatch(() => Logs.Add(ok ? $"Printer sync OK ({printers.Count} printers)." : "Printer sync FAILED."));
        }

        // ── Cloud Print Job Execution (Tasks 5.1 + 5.3) ───────────────────────

        private async Task ExecutePrintJobAsync(string jobId, string jobPrinterName)
        {
            var selectedPrinter = string.IsNullOrEmpty(jobPrinterName)? SelectedPrinter : jobPrinterName;
            if (string.IsNullOrEmpty(selectedPrinter))
            {
                Dispatch(() => Logs.Add($"[JOB {jobId}] No printer selected – skipping."));
                await _apiService.UpdateJobStatusAsync(jobId, false, "No printer selected on device.");
                return;
            }

            Dispatch(() => Logs.Add($"[JOB {jobId}] Downloading..."));
            try
            {

                var pdfBytes = await _apiService.DownloadJobAsync(jobId);

                if (pdfBytes == null || pdfBytes.Length == 0)
                {
                    Dispatch(() => Logs.Add($"[JOB {jobId}] Download failed."));
                    await _apiService.UpdateJobStatusAsync(jobId, false, "PDF download failed.");
                    _notificationService.Notify($"Job {jobId}: download failed.", isError: true);
                    return;
                }

                Dispatch(() => Logs.Add($"[JOB {jobId}] Downloaded {pdfBytes.Length:N0} bytes. Printing..."));
                bool printed = _printerService.PrintPdfBytes(pdfBytes, selectedPrinter);

                await _apiService.UpdateJobStatusAsync(jobId, printed,
                    printed ? string.Empty : "Printing returned failure.");

                _notificationService.Notify(printed
                    ? $"Job {jobId}: printed successfully."
                    : $"Job {jobId}: print FAILED.", !printed);

                Dispatch(() => Logs.Add($"[JOB {jobId}] {(printed ? "Done." : "FAILED.")}"));
            }
            catch (Exception ex)
            {

                throw;
            }
        }

        // ── Revocation (Task 6.1) ─────────────────────────────────────────────

        [RelayCommand]
        public void WipeCredentials()
        {
            var result = System.Windows.MessageBox.Show(
                "This will permanently delete all device credentials and disconnect from the cloud. Are you sure?",
                "Disconnect Device", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);

            if (result == System.Windows.MessageBoxResult.Yes)
                TriggerRevocation();
        }

        public void TriggerRevocation()

        {
            Logs.Add("[REVOKE] Device revoked by administrator. Wiping credentials...");
            _configurationService.WipeCredentials();

            // Reset UI fields
            DeviceAccountId = string.Empty;
            ApiSecret       = string.Empty;
            DeviceGuid      = string.Empty;
            IsRevoked       = true;

            _notificationService.Notify("Device Revoked by Administrator. Re-enter credentials.", isError: true);
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        partial void OnSelectedPrinterChanged(string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                _configurationService.SelectedPrinter = value;
                Logs.Add($"Printer selected: {value}");
            }
        }

        private static void Dispatch(Action action) =>
            System.Windows.Application.Current.Dispatcher.Invoke(action);
    }
}
