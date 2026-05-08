using System;
using Microsoft.Toolkit.Uwp.Notifications;

namespace PrintDesktopClient.Services
{
    public class NotificationService
    {
        public event Action<string, bool>? OnNotification;

        public void Notify(string message, bool isError = false)
        {
            OnNotification?.Invoke(message, isError);
            
            try
            {
                new ToastContentBuilder()
                    .AddText("Auto-Print Agent")
                    .AddText(message)
                    .Show();
            }
            catch
            {
                // Fallback if toast fails
            }
        }
    }
}
