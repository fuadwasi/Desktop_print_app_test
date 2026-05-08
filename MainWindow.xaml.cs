using System.Windows;
using PrintDesktopClient.ViewModels;
using System.Threading.Tasks;
using System;
using System.Drawing;

namespace PrintDesktopClient
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;

            // Ensure the icon is set after the window is loaded
            this.Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                // Use a standard system icon as a reliable source
                MyNotifyIcon.Icon = SystemIcons.Information;
                
                // Force visibility just in case
                MyNotifyIcon.Visibility = Visibility.Visible;
            }
            catch (Exception ex)
            {
                var viewModel = (MainViewModel)DataContext;
                viewModel.Logs.Add($"Tray Icon Error: {ex.Message}");
            }
        }

        private async void DropZone_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                var viewModel = (MainViewModel)DataContext;
                foreach (var file in files)
                {
                    await Task.Run(() => viewModel.PrintFile(file));
                }
            }
        }

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                this.Hide();
                this.ShowInTaskbar = false;
                
                var viewModel = (MainViewModel)DataContext;
                viewModel.Logs.Add("App minimized to tray.");
            }
        }

        private void MyNotifyIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e)
        {
            RestoreWindow();
        }

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            RestoreWindow();
        }

        private void Exit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void RestoreWindow()
        {
            this.Show();
            this.WindowState = WindowState.Normal;
            this.ShowInTaskbar = true;
            this.Activate();
        }
    }
}