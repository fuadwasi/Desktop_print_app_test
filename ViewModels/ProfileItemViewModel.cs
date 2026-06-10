using CommunityToolkit.Mvvm.ComponentModel;
using PrintDesktopClient.Services;

namespace PrintDesktopClient.ViewModels
{
    public partial class ProfileItemViewModel : ObservableObject
    {
        public ProfileSettings Profile { get; }

        [ObservableProperty] private string _name;
        [ObservableProperty] private bool _isEnabled;
        [ObservableProperty] private bool _showPrintPreview;
        [ObservableProperty] private string _mqttStatus = "Disconnected";

        public ProfileItemViewModel(ProfileSettings profile, string initialStatus = "Disconnected")
        {
            Profile = profile;
            _name = profile.Name;
            _isEnabled = profile.IsEnabled;
            _showPrintPreview = profile.ShowPrintPreview;
            _mqttStatus = initialStatus;
        }

        partial void OnNameChanged(string value)
        {
            Profile.Name = value;
        }

        partial void OnIsEnabledChanged(bool value)
        {
            Profile.IsEnabled = value;
        }

        partial void OnShowPrintPreviewChanged(bool value)
        {
            Profile.ShowPrintPreview = value;
        }
    }
}
