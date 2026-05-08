using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using PrintDesktopClient.Services;
using System.Linq;

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
