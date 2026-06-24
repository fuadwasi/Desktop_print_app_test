using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using PrintDesktopClient.Services;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System;
using System.Net.Http;
using MaterialDesignThemes.Wpf;
using PrintDesktopClient.Models;
using System.Collections.Generic;

namespace PrintDesktopClient.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly PrinterService _printerService;
        private readonly ConfigurationService _configurationService;
        private readonly ProfileSessionManager _sessionManager;
        private readonly NotificationService _notificationService;

        // ── Observable Properties ─────────────────────────────────────────────

        public string AppVersion => System.Reflection.Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "1.0.0";

        [ObservableProperty] private string _statusText = "Ready";
        [ObservableProperty] private string _mqttStatus = "Disconnected";
        [ObservableProperty] private string _selectedPrinter = string.Empty;
        [ObservableProperty] private bool _editingShowPrintPreview = false;
        [ObservableProperty] private bool _editingDeleteTempFileAfterPrint = true;

        // Margin Mode settings (bound to the profile form)
        [ObservableProperty] private MarginMode _editingMarginMode = MarginMode.Default;
        [ObservableProperty] private int  _editingMarginTop    = 0;
        [ObservableProperty] private int  _editingMarginBottom = 0;
        [ObservableProperty] private int  _editingMarginLeft   = 0;
        [ObservableProperty] private int  _editingMarginRight  = 0;

        // Visibility helper: true when Custom margin mode is selected
        public bool IsCustomMarginVisible => EditingMarginMode == MarginMode.Custom;
        partial void OnEditingMarginModeChanged(MarginMode value) => OnPropertyChanged(nameof(IsCustomMarginVisible));

        // Form View visibility bindings
        [ObservableProperty] private bool _isProfileListViewVisible = true;
        [ObservableProperty] private bool _isProfileFormViewVisible = false;
        [ObservableProperty] private string _formTitle = "Create Profile";

        // MQTT settings (bound to the form inputs)
        [ObservableProperty] private string _mqttBroker = string.Empty;
        [ObservableProperty] private string _mqttUsername = string.Empty;
        [ObservableProperty] private string _mqttPassword = string.Empty;
        [ObservableProperty] private string _mqttTopic = string.Empty;
        [ObservableProperty] private int    _reconnectIntervalSeconds = 10;

        // Cloud / API settings (bound to the form inputs)
        [ObservableProperty] private string _apiBaseUrl = string.Empty;
        [ObservableProperty] private string _deviceAccountId = string.Empty;
        [ObservableProperty] private string _apiSecret = string.Empty;
        [ObservableProperty] private string _deviceGuid = string.Empty;

        // Connection test results
        [ObservableProperty] private string _apiTestResult = string.Empty;
        [ObservableProperty] private string _mqttTestResult = string.Empty;
        [ObservableProperty] private bool   _isRevoked = false;

        // Profile Selection & Form State
        [ObservableProperty] private ProfileItemViewModel? _editingProfileVM;
        [ObservableProperty] private string _editingProfileName = string.Empty;

        public ObservableCollection<ProfileItemViewModel> ProfilesList { get; } = new();
        public ObservableCollection<string> AvailablePrinters { get; } = new();
        public ObservableCollection<string> Logs { get; } = new();
        public SnackbarMessageQueue MessageQueue { get; } = new();

        // ── Constructor ───────────────────────────────────────────────────────

        public MainViewModel(
            PrinterService printerService,
            ConfigurationService configurationService,
            ProfileSessionManager sessionManager,
            NotificationService notificationService)
        {
            _printerService       = printerService;
            _configurationService = configurationService;
            _sessionManager       = sessionManager;
            _notificationService  = notificationService;

            // Load profiles list into ViewModel wrappers
            foreach (var p in _configurationService.Profiles)
            {
                var session = _sessionManager.GetSession(p.Id);
                var status = session != null ? session.MqttStatus : "Disconnected";
                ProfilesList.Add(new ProfileItemViewModel(p, status));
            }

            // Subscribe to session manager events
            _sessionManager.SessionAdded += RegisterSessionEvents;
            _sessionManager.SessionMqttStatusChanged += (session, status) => Dispatch(() =>
            {
                UpdateMqttStatusSummary();
                var item = ProfilesList.FirstOrDefault(x => x.Profile.Id == session.Profile.Id);
                if (item != null)
                {
                    item.MqttStatus = status;
                }

                if (status == "Disconnected" && session.Profile.IsEnabled)
                {
                    _notificationService.Notify($"[{session.Profile.Name}] MQTT Connection lost!", isError: true);
                }
                else if (status == "Connected")
                {
                    _notificationService.Notify($"[{session.Profile.Name}] MQTT Connected!");
                }
            });

            // Register existing sessions
            foreach (var session in _sessionManager.Sessions)
            {
                RegisterSessionEvents(session);
            }

            // Notifications pipe → Snackbar
            _notificationService.OnNotification += (msg, _) => Dispatch(() =>
            {
                MessageQueue.Enqueue(msg);
                StatusText = msg;
            });

            RefreshPrinters();

            // Restore printer selection using the first profile's target printer if available
            var firstProfile = _configurationService.Profiles.FirstOrDefault();
            if (firstProfile != null && !string.IsNullOrEmpty(firstProfile.SelectedPrinter) &&
                AvailablePrinters.Contains(firstProfile.SelectedPrinter))
                SelectedPrinter = firstProfile.SelectedPrinter;
            else if (AvailablePrinters.Any())
                SelectedPrinter = AvailablePrinters.First();

            UpdateMqttStatusSummary();
            Logs.Add("Application initialized with profile list architecture.");
        }

        private void RegisterSessionEvents(ProfileSession session)
        {
            session.MqttService.OnMessageReceived += msg => Dispatch(() =>
            {
                Logs.Add($"[{session.Profile.Name}] [MQTT TEXT] {msg}");
                _notificationService.Notify($"[{session.Profile.Name}] New MQTT text message.");
            });

            session.MqttService.OnPrintCommand += async (jobId, printerName) =>
            {
                Dispatch(() => Logs.Add($"[{session.Profile.Name}] [MQTT PRINT] JobId: {jobId} Printer: {printerName}"));
                await ExecutePrintJobAsync(session, jobId, printerName);
            };

            session.MqttService.OnSyncCommand += async () =>
            {
                Dispatch(() => Logs.Add($"[{session.Profile.Name}] [MQTT SYNC] Printer sync commanded by cloud."));
                await SyncPrintersToCloud(session);
            };

            session.MqttService.OnRevokeCommand += () => Dispatch(() =>
            {
                TriggerRevocation(session.Profile.Id);
            });

            session.ApiService.OnUnauthorized += () => Dispatch(() =>
            {
                TriggerRevocation(session.Profile.Id);
            });
        }

        private void UpdateMqttStatusSummary()
        {
            var active = ProfilesList.Count(p => p.IsEnabled);
            var connected = _sessionManager.Sessions.Count(s => s.MqttStatus == "Connected" && s.Profile.IsEnabled);
            MqttStatus = $"{connected}/{active} Connected";
        }

        // ── Navigation & Form Actions ─────────────────────────────────────────

        [RelayCommand]
        public void NavigateToCreate()
        {
            FormTitle = "Create Profile";
            EditingProfileVM = null;

            // Load default/empty form values
            EditingProfileName = string.Empty;
            MqttBroker = "mqttserver.test";
            MqttUsername = "mqttuser";
            MqttPassword = string.Empty;
            MqttTopic = "home/printer/print";
            ReconnectIntervalSeconds = 10;
            EditingShowPrintPreview = false;
            EditingDeleteTempFileAfterPrint = true;
            EditingMarginMode   = MarginMode.Default;
            EditingMarginTop    = 0;
            EditingMarginBottom = 0;
            EditingMarginLeft   = 0;
            EditingMarginRight  = 0;
            ApiBaseUrl = "https://localhost:5001";
            DeviceAccountId = string.Empty;
            ApiSecret = string.Empty;
            DeviceGuid = Guid.NewGuid().ToString();

            ApiTestResult = string.Empty;
            MqttTestResult = string.Empty;

            // Switch view
            IsProfileListViewVisible = false;
            IsProfileFormViewVisible = true;
        }

        [RelayCommand]
        public void NavigateToEdit(ProfileItemViewModel profileVM)
        {
            FormTitle = "Edit Profile";
            EditingProfileVM = profileVM;

            // Load values from settings
            EditingProfileName = profileVM.Profile.Name;
            MqttBroker = profileVM.Profile.MqttBroker;
            MqttUsername = profileVM.Profile.MqttUsername;
            MqttPassword = _configurationService.GetMqttPassword(profileVM.Profile.Id);
            MqttTopic = profileVM.Profile.MqttTopic;
            ReconnectIntervalSeconds = profileVM.Profile.ReconnectIntervalSeconds;
            EditingShowPrintPreview = profileVM.Profile.ShowPrintPreview;
            EditingDeleteTempFileAfterPrint = profileVM.Profile.DeleteTempFileAfterPrint;
            EditingMarginMode   = profileVM.Profile.MarginMode;
            EditingMarginTop    = profileVM.Profile.MarginTop;
            EditingMarginBottom = profileVM.Profile.MarginBottom;
            EditingMarginLeft   = profileVM.Profile.MarginLeft;
            EditingMarginRight  = profileVM.Profile.MarginRight;
            ApiBaseUrl = profileVM.Profile.ApiBaseUrl;
            DeviceAccountId = profileVM.Profile.DeviceAccountId;
            ApiSecret = _configurationService.GetApiSecret(profileVM.Profile.Id);
            DeviceGuid = profileVM.Profile.DeviceGuid;

            ApiTestResult = string.Empty;
            MqttTestResult = string.Empty;

            // Switch view
            IsProfileListViewVisible = false;
            IsProfileFormViewVisible = true;
        }

        [RelayCommand]
        public void NavigateBack()
        {
            IsProfileListViewVisible = true;
            IsProfileFormViewVisible = false;
            EditingProfileVM = null;
        }

        [RelayCommand]
        public async Task SaveProfile()
        {
            if (string.IsNullOrWhiteSpace(EditingProfileName))
            {
                _notificationService.Notify("Profile Name is required.", isError: true);
                return;
            }

            // Check duplicate names
            bool duplicate = _configurationService.Profiles.Any(p => 
                p.Name.Equals(EditingProfileName, StringComparison.OrdinalIgnoreCase) && 
                (EditingProfileVM == null || p.Id != EditingProfileVM.Profile.Id));

            if (duplicate)
            {
                _notificationService.Notify("A profile with this name already exists.", isError: true);
                return;
            }

            ProfileSettings profile;

            if (EditingProfileVM == null)
            {
                // Creating a new profile
                profile = new ProfileSettings
                {
                    Id = Guid.NewGuid().ToString(),
                    IsEnabled = false // Inactive by default — user must configure and enable manually
                };
            }
            else
            {
                // Editing existing
                profile = EditingProfileVM.Profile;
            }

            profile.Name = EditingProfileName;
            profile.MqttBroker = MqttBroker;
            profile.MqttUsername = MqttUsername;
            profile.MqttTopic = MqttTopic;
            profile.ReconnectIntervalSeconds = ReconnectIntervalSeconds;
            profile.ShowPrintPreview = EditingShowPrintPreview;
            profile.DeleteTempFileAfterPrint = EditingDeleteTempFileAfterPrint;
            profile.MarginMode   = EditingMarginMode;
            profile.MarginTop    = EditingMarginTop;
            profile.MarginBottom = EditingMarginBottom;
            profile.MarginLeft   = EditingMarginLeft;
            profile.MarginRight  = EditingMarginRight;
            profile.ApiBaseUrl = ApiBaseUrl;
            profile.DeviceAccountId = DeviceAccountId;
            profile.DeviceGuid = DeviceGuid;

            _configurationService.SaveMqttPassword(profile.Id, MqttPassword);
            _configurationService.SaveApiSecret(profile.Id, ApiSecret);

            if (EditingProfileVM == null)
            {
                _configurationService.Profiles.Add(profile);
                _configurationService.SaveSettings();

                var newVM = new ProfileItemViewModel(profile);
                ProfilesList.Add(newVM);
                
                await _sessionManager.StartSessionAsync(profile);
                Logs.Add($"Profile '{profile.Name}' created and connected.");
            }
            else
            {
                _configurationService.SaveSettings();

                EditingProfileVM.Name = profile.Name;
                EditingProfileVM.ShowPrintPreview = profile.ShowPrintPreview;
                
                await _sessionManager.RestartSessionAsync(profile);
                Logs.Add($"Profile '{profile.Name}' updated and session restarted.");
            }

            UpdateMqttStatusSummary();
            NavigateBack();
        }

        [RelayCommand]
        public async Task DeleteProfile(ProfileItemViewModel profileVM)
        {
            if (ProfilesList.Count <= 1)
            {
                _notificationService.Notify("At least one profile is required.", isError: true);
                return;
            }

            var result = System.Windows.MessageBox.Show(
                $"Are you sure you want to delete profile '{profileVM.Name}'?",
                "Delete Profile", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);

            if (result == System.Windows.MessageBoxResult.Yes)
            {
                await _sessionManager.StopSessionAsync(profileVM.Profile.Id);
                _configurationService.Profiles.Remove(profileVM.Profile);
                _configurationService.WipeCredentials(profileVM.Profile.Id);
                _configurationService.SaveSettings();
                ProfilesList.Remove(profileVM);

                UpdateMqttStatusSummary();
                Logs.Add($"Profile '{profileVM.Name}' deleted.");
            }
        }

        [RelayCommand]
        public async Task ToggleProfileEnabled(ProfileItemViewModel profileVM)
        {
            _configurationService.SaveSettings();
            if (profileVM.IsEnabled)
            {
                await _sessionManager.StartSessionAsync(profileVM.Profile);
            }
            else
            {
                await _sessionManager.StopSessionAsync(profileVM.Profile.Id);
                profileVM.MqttStatus = "Disconnected";
            }
            UpdateMqttStatusSummary();
            Logs.Add($"Profile '{profileVM.Name}' toggled active: {profileVM.IsEnabled}");
        }

        [RelayCommand]
        public void ToggleProfileShowPrintPreview(ProfileItemViewModel profileVM)
        {
            _configurationService.SaveSettings();
            Logs.Add($"Profile '{profileVM.Name}' toggled ShowPrintPreview: {profileVM.ShowPrintPreview}");
        }

        // ── Connection Testing ────────────────────────────────────────────────

        [RelayCommand]
        public async Task TestMqttConnection()
        {
            MqttTestResult = "Testing connection...";
            bool ok = await MqttListenerService.TestConnectionAsync(MqttBroker, MqttUsername, MqttPassword);
            MqttTestResult = ok ? "✅ Connection Successful" : "❌ Connection Failed";
            Logs.Add($"MQTT connection test for {MqttBroker}: {MqttTestResult}");
        }

        [RelayCommand]
        public async Task TestApiConnection()
        {
            ApiTestResult = "Testing reachability...";
            try
            {
                var tempProfile = new ProfileSettings
                {
                    ApiBaseUrl = ApiBaseUrl,
                    DeviceAccountId = DeviceAccountId,
                    Id = EditingProfileVM?.Profile.Id ?? Guid.NewGuid().ToString()
                };

                // Temporarily cache secret for testing
                _configurationService.SaveApiSecret(tempProfile.Id, ApiSecret);

                var tempLogger = Microsoft.Extensions.Logging.Abstractions.NullLogger<ApiService>.Instance;
                var tempApiService = new ApiService(new HttpClientFactoryShim(), _configurationService, tempProfile, tempLogger);
                
                bool ok = await tempApiService.TestApiConnectionAsync();
                ApiTestResult = ok ? "✅ API Reachable" : "❌ Cannot reach API";
            }
            catch (Exception ex)
            {
                ApiTestResult = $"❌ Error: {ex.Message}";
            }
            Logs.Add($"API test result for {ApiBaseUrl}: {ApiTestResult}");
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
            bool userConfirmed = true;
            AdvancedPrintOptions? printOptions = null;

            var activeProfile = _sessionManager.Sessions.FirstOrDefault(s => s.Profile.IsEnabled)?.Profile 
                               ?? _configurationService.Profiles.FirstOrDefault();
            bool showPreview = activeProfile?.ShowPrintPreview ?? false;

            if (showPreview && filePath.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
            {
                Dispatch(() =>
                {
                    try
                    {
                        var pdfBytes = File.ReadAllBytes(filePath);
                        var previewWindow = new PrintPreviewWindow(pdfBytes, AvailablePrinters.ToList(), SelectedPrinter, activeProfile);
                        userConfirmed = previewWindow.ShowDialog() == true;
                        if (userConfirmed)
                        {
                            printOptions = previewWindow.SelectedOptions;
                        }
                    }
                    catch (Exception ex)
                    {
                        Logs.Add($"Preview failed: {ex.Message}");
                        userConfirmed = false;
                    }
                });
            }

            if (!userConfirmed)
            {
                Dispatch(() => Logs.Add($"Print cancelled by user: {Path.GetFileName(filePath)}"));
                return;
            }

            Dispatch(() => Logs.Add($"Queueing: {Path.GetFileName(filePath)}"));
            bool ok = false;
            
            if (printOptions != null)
            {
                var pdfBytes = File.ReadAllBytes(filePath);
                ok = _printerService.PrintPdfBytes(pdfBytes, printOptions);
            }
            else
            {
                ok = _printerService.PrintFile(filePath, SelectedPrinter, activeProfile);
            }

            _notificationService.Notify(ok
                ? $"Sent to printer: {Path.GetFileName(filePath)}"
                : $"Print failed: {Path.GetFileName(filePath)}", !ok);
        }

        // ── Cloud Printer Sync ────────────────────────────────────────────────

        [RelayCommand]
        public async Task SyncPrinters()
        {
            // Sync using first active profile session
            var firstActive = _sessionManager.Sessions.FirstOrDefault(s => s.Profile.IsEnabled);
            if (firstActive != null)
            {
                await SyncPrintersToCloud(firstActive);
            }
            else
            {
                _notificationService.Notify("No active profiles running to sync printers.", isError: true);
            }
        }

        private async Task SyncPrintersToCloud(ProfileSession session)
        {
            var printers = _printerService.GetAvailablePrinters();
            bool ok = await session.ApiService.SyncPrintersAsync(printers);
            _notificationService.Notify(ok
                ? $"[{session.Profile.Name}] Synced {printers.Count} printers to cloud."
                : $"[{session.Profile.Name}] Printer sync failed.", !ok);
            Dispatch(() => Logs.Add(ok ? $"[{session.Profile.Name}] Printer sync OK ({printers.Count} printers)." : $"[{session.Profile.Name}] Printer sync FAILED."));
        }

        // ── Cloud Print Job Execution ─────────────────────────────────────────

        private async Task ExecutePrintJobAsync(ProfileSession session, string jobId, string jobPrinterName)
        {
            var selectedPrinter = string.IsNullOrEmpty(jobPrinterName)? session.Profile.SelectedPrinter : jobPrinterName;
            if (string.IsNullOrEmpty(selectedPrinter))
            {
                selectedPrinter = SelectedPrinter;
            }

            if (string.IsNullOrEmpty(selectedPrinter))
            {
                Dispatch(() => Logs.Add($"[{session.Profile.Name}] [JOB {jobId}] No printer selected – skipping."));
                await session.ApiService.UpdateJobStatusAsync(jobId, false, "No printer selected on device.");
                return;
            }

            Dispatch(() => Logs.Add($"[{session.Profile.Name}] [JOB {jobId}] Downloading..."));
            try
            {
                var pdfBytes = await session.ApiService.DownloadJobAsync(jobId);

                if (pdfBytes == null || pdfBytes.Length == 0)
                {
                    Dispatch(() => Logs.Add($"[{session.Profile.Name}] [JOB {jobId}] Download failed."));
                    await session.ApiService.UpdateJobStatusAsync(jobId, false, "PDF download failed.");
                    _notificationService.Notify($"[{session.Profile.Name}] Job {jobId}: download failed.", isError: true);
                    return;
                }

                bool userConfirmed = true;
                AdvancedPrintOptions? printOptions = null;

                if (session.Profile.ShowPrintPreview)
                {
                    Dispatch(() =>
                    {
                        try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
                        var previewWindow = new PrintPreviewWindow(pdfBytes, AvailablePrinters.ToList(), selectedPrinter, session.Profile);
                        userConfirmed = previewWindow.ShowDialog() == true;
                        if (userConfirmed)
                        {
                            printOptions = previewWindow.SelectedOptions;
                        }
                    });
                }

                if (!userConfirmed)
                {
                    Dispatch(() => Logs.Add($"[{session.Profile.Name}] [JOB {jobId}] Cancelled by user in preview."));
                    await session.ApiService.UpdateJobStatusAsync(jobId, false, "Cancelled by user at preview stage.");
                    _notificationService.Notify($"[{session.Profile.Name}] Job {jobId}: Cancelled.");
                    return;
                }

                Dispatch(() => Logs.Add($"[{session.Profile.Name}] [JOB {jobId}] Downloaded {pdfBytes.Length:N0} bytes. Printing..."));
                
                bool printed = false;
                bool deleteTmp = session.Profile.DeleteTempFileAfterPrint;
                if (printOptions != null)
                {
                    printed = _printerService.PrintPdfBytes(pdfBytes, printOptions, deleteTmp);
                }
                else
                {
                    printed = _printerService.PrintPdfBytes(pdfBytes, selectedPrinter, session.Profile, deleteTmp);
                }

                await session.ApiService.UpdateJobStatusAsync(jobId, printed,
                    printed ? string.Empty : "Printing returned failure.");

                _notificationService.Notify(printed
                    ? $"[{session.Profile.Name}] Job {jobId}: printed successfully."
                    : $"[{session.Profile.Name}] Job {jobId}: print FAILED.", !printed);

                Dispatch(() => Logs.Add($"[{session.Profile.Name}] [JOB {jobId}] {(printed ? "Done." : "FAILED.")}"));
            }
            catch (Exception ex)
            {
                Dispatch(() => Logs.Add($"[{session.Profile.Name}] [JOB {jobId}] Error: {ex.Message}"));
            }
        }

        // ── Revocation ────────────────────────────────────────────────────────

        [RelayCommand]
        public void WipeCredentials()
        {
            if (EditingProfileVM == null) return;

            var result = System.Windows.MessageBox.Show(
                $"This will permanently delete credentials for profile '{EditingProfileVM.Name}' and disconnect it. Are you sure?",
                "Disconnect Profile", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Warning);

            if (result == System.Windows.MessageBoxResult.Yes)
                TriggerRevocation(EditingProfileVM.Profile.Id);
        }

        public void TriggerRevocation(string profileId)
        {
            var p = ProfilesList.FirstOrDefault(x => x.Profile.Id == profileId);
            if (p == null) return;

            Logs.Add($"[REVOKE] Profile '{p.Name}' revoked by administrator. Wiping credentials...");
            _configurationService.WipeCredentials(p.Profile.Id);

            Dispatch(() =>
            {
                if (EditingProfileVM?.Profile.Id == p.Profile.Id)
                {
                    DeviceAccountId = string.Empty;
                    ApiSecret       = string.Empty;
                    DeviceGuid      = p.Profile.DeviceGuid;
                }

                _notificationService.Notify($"Profile '{p.Name}' Revoked by Administrator. Re-enter credentials.", isError: true);
            });
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        partial void OnSelectedPrinterChanged(string value)
        {
            if (!string.IsNullOrEmpty(value) && ProfilesList.Count > 0)
            {
                var firstProfile = ProfilesList.First().Profile;
                firstProfile.SelectedPrinter = value;
                _configurationService.SaveSettings();
                Logs.Add($"Printer selected globally/default: {value}");
            }
        }


        private static void Dispatch(Action action)
        {
            // Application.Current can be null when the app is shutting down and a
            // background MQTT thread fires an event after WPF has begun teardown.
            // Guard against this to avoid NullReferenceException.
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher == null) return;
            dispatcher.Invoke(action);
        }

        private class HttpClientFactoryShim : IHttpClientFactory
        {
            public System.Net.Http.HttpClient CreateClient(string name) => new System.Net.Http.HttpClient();
        }
    }
}
