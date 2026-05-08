using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows;
using PrintDesktopClient.ViewModels;

namespace PrintDesktopClient
{
    public partial class MainWindow : Window
    {
        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();
            DataContext = viewModel;
            this.Loaded += MainWindow_Loaded;
        }

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                MyNotifyIcon.Icon = SystemIcons.Information;
                MyNotifyIcon.Visibility = Visibility.Visible;
            }
            catch { /* best-effort */ }
        }

        // ── Drag & Drop ───────────────────────────────────────────────────────

        private async void DropZone_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                var vm = (MainViewModel)DataContext;
                foreach (var file in files)
                    await Task.Run(() => vm.PrintFile(file));
            }
        }

        // ── Minimize to Tray ──────────────────────────────────────────────────

        private void Window_StateChanged(object sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
            {
                Hide();
                ShowInTaskbar = false;
                ((MainViewModel)DataContext).Logs.Add("App minimized to system tray.");
            }
        }

        // ── Tray Icon Events ──────────────────────────────────────────────────

        private void MyNotifyIcon_TrayMouseDoubleClick(object sender, RoutedEventArgs e)
            => RestoreWindow();

        private void Open_Click(object sender, RoutedEventArgs e)
            => RestoreWindow();

        private async void SyncPrinters_Click(object sender, RoutedEventArgs e)
            => await ((MainViewModel)DataContext).SyncPrinters();

        private void Exit_Click(object sender, RoutedEventArgs e)
            => Application.Current.Shutdown();

        // ── Restore ───────────────────────────────────────────────────────────

        private void RestoreWindow()
        {
            Show();
            WindowState   = WindowState.Normal;
            ShowInTaskbar = true;
            Activate();
        }
    }
}