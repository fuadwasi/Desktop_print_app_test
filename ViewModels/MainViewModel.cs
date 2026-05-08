using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using PrintDesktopClient.Services;
using System.Linq;
using System.IO;
using System.Threading.Tasks;

namespace PrintDesktopClient.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        private readonly PrinterService _printerService;
        private readonly ConfigurationService _configurationService;

        [ObservableProperty]
        private string _statusText = "Ready";

        [ObservableProperty]
        private string _mqttStatus = "Disconnected";

        [ObservableProperty]
        private string _selectedPrinter = string.Empty;

        public ObservableCollection<string> AvailablePrinters { get; } = new();
        public ObservableCollection<string> Logs { get; } = new();

        public MainViewModel(PrinterService printerService, ConfigurationService configurationService)
        {
            _printerService = printerService;
            _configurationService = configurationService;

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
                StatusText = "Printing...";
                await Task.Run(() => PrintFile(dialog.FileName));
                StatusText = "Ready";
            }
        }

        public void PrintFile(string filePath)
        {
            if (string.IsNullOrEmpty(SelectedPrinter))
            {
                App.Current.Dispatcher.Invoke(() => Logs.Add("Error: No printer selected."));
                return;
            }

            App.Current.Dispatcher.Invoke(() => Logs.Add($"Queueing: {Path.GetFileName(filePath)}"));
            
            bool success = _printerService.PrintFile(filePath, SelectedPrinter);
            
            App.Current.Dispatcher.Invoke(() => {
                if (success)
                {
                    Logs.Add($"Success: {Path.GetFileName(filePath)} sent to printer.");
                }
                else
                {
                    Logs.Add($"Failure: Could not print {Path.GetFileName(filePath)}.");
                }
            });
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
