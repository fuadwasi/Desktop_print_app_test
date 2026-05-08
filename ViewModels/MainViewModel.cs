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
        private readonly NotificationService _notificationService;

        [ObservableProperty]
        private string _statusText = "Ready";

        [ObservableProperty]
        private string _mqttStatus = "Disconnected";

        [ObservableProperty]
        private string _selectedPrinter = string.Empty;

        public ObservableCollection<string> AvailablePrinters { get; } = new();
        public ObservableCollection<string> Logs { get; } = new();
        public SnackbarMessageQueue MessageQueue { get; } = new();

        public MainViewModel(
            PrinterService printerService, 
            ConfigurationService configurationService, 
            MqttListenerService mqttService,
            NotificationService notificationService)
        {
            _printerService = printerService;
            _configurationService = configurationService;
            _mqttService = mqttService;
            _notificationService = notificationService;

            _mqttService.OnMessageReceived += msg => {
                App.Current.Dispatcher.Invoke(() => {
                    Logs.Add($"MQTT Job: {msg}");
                    _notificationService.Notify("New MQTT print job received.");
                });
            };

            _mqttService.StatusChanged += status => {
                App.Current.Dispatcher.Invoke(() => {
                    MqttStatus = status;
                    if (status == "Disconnected")
                    {
                        _notificationService.Notify("MQTT Connection lost!", true);
                    }
                });
            };

            _notificationService.OnNotification += (msg, isError) => {
                App.Current.Dispatcher.Invoke(() => {
                    MessageQueue.Enqueue(msg);
                    StatusText = msg;
                });
            };

            RefreshPrinters();
            
            // Load saved printer
            if (!string.IsNullOrEmpty(_configurationService.SelectedPrinter) && AvailablePrinters.Contains(_configurationService.SelectedPrinter))
            {
                SelectedPrinter = _configurationService.SelectedPrinter;
            }
            else if (AvailablePrinters.Any())
            {
                SelectedPrinter = AvailablePrinters.First();
            }

            Logs.Add("Application initialized.");
        }

        [RelayCommand]
        public void RefreshPrinters()
        {
            var printers = _printerService.GetAvailablePrinters();
            var currentSelection = SelectedPrinter;
            
            AvailablePrinters.Clear();
            foreach (var p in printers)
            {
                AvailablePrinters.Add(p);
            }

            if (AvailablePrinters.Contains(currentSelection))
            {
                SelectedPrinter = currentSelection;
            }
            
            Logs.Add($"Refreshed printer list. Found {printers.Count} printers.");
        }

        [RelayCommand]
        public async Task BrowseAndPrint()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Supported Files (*.pdf;*.txt;*.jpg;*.png;*.docx)|*.pdf;*.txt;*.jpg;*.png;*.docx|All Files (*.*)|*.*"
            };

            if (dialog.ShowDialog() == true)
            {
                await Task.Run(() => PrintFile(dialog.FileName));
            }
        }

        public void PrintFile(string filePath)
        {
            if (string.IsNullOrEmpty(SelectedPrinter))
            {
                _notificationService.Notify("No printer selected!", true);
                return;
            }

            App.Current.Dispatcher.Invoke(() => Logs.Add($"Queueing: {Path.GetFileName(filePath)}"));
            
            bool success = _printerService.PrintFile(filePath, SelectedPrinter);
            
            if (success)
            {
                _notificationService.Notify($"Sent to printer: {Path.GetFileName(filePath)}");
            }
            else
            {
                _notificationService.Notify($"Printing failed: {Path.GetFileName(filePath)}", true);
            }
        }

        partial void OnSelectedPrinterChanged(string value)
        {
            if (!string.IsNullOrEmpty(value))
            {
                _configurationService.SelectedPrinter = value;
                Logs.Add($"Printer selected: {value}");
            }
        }
    }
}
