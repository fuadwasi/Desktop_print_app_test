using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace PrintDesktopClient
{
    public partial class AboutWindow : Window
    {
        public AboutWindow()
        {
            InitializeComponent();

            // Read version from the assembly so it always stays in sync with the .csproj Version
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            VersionLabel.Text = version != null
                ? $"Version {version.Major}.{version.Minor}.{version.Build}"
                : "Version 1.2.0";
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
            => Close();

        // Opens mailto: or https:// links in the default system browser / mail client
        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
            catch { /* best-effort */ }
            e.Handled = true;
        }
    }
}
