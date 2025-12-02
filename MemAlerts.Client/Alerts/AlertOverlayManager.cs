using System.Windows;
using global::MemAlerts.Shared.Models;
using MemAlerts.Client.Services;

namespace MemAlerts.Client.Alerts;

public sealed class AlertOverlayManager
{
    private readonly WebVideoPlayerService _webVideoPlayerService;

    public AlertOverlayManager(WebVideoPlayerService webVideoPlayerService)
    {
        _webVideoPlayerService = webVideoPlayerService;
    }

    public void ShowAlert(AlertRequest request)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            var window = new AlertOverlayWindow(request, _webVideoPlayerService);
            window.Show();
        });
    }
}

