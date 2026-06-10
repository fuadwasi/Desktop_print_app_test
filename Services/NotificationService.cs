using System;

namespace PrintDesktopClient.Services
{
    public class NotificationService
    {
        /// <summary>
        /// Raised when a notification should be displayed.
        /// Subscribers (e.g. MainWindow) show a tray balloon or snackbar.
        /// </summary>
        public event Action<string, bool>? OnNotification;

        /// <param name="message">Text to display.</param>
        /// <param name="isError">True for error-level notifications.</param>
        public void Notify(string message, bool isError = false)
        {
            OnNotification?.Invoke(message, isError);
            // Tray balloon is shown by MainWindow via OnNotification subscription.
            // UWP toast (Microsoft.Toolkit.Uwp.Notifications) has been removed —
            // it requires Windows 10 and is incompatible with Windows 7.
        }
    }
}
