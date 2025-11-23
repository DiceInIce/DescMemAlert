using System.Windows;
using global::MemAlerts.Shared.Models;

namespace MemAlerts.Client.Alerts;

public sealed class AlertOverlayManager
{
    public void ShowAlert(AlertRequest request)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var window = new AlertOverlayWindow(request);
            window.Show();
        });
    }
}

