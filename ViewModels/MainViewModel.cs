using CommunityToolkit.Mvvm.ComponentModel;
using System.Collections.ObjectModel;

namespace PrintDesktopClient.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        [ObservableProperty]
        private string _statusText = "Ready";

        [ObservableProperty]
        private string _mqttStatus = "Disconnected";

        [ObservableProperty]
        private string _selectedPrinter = string.Empty;

        public ObservableCollection<string> AvailablePrinters { get; } = new();
        public ObservableCollection<string> Logs { get; } = new();

        public MainViewModel()
        {
            Logs.Add("Application started.");
        }
    }
}
