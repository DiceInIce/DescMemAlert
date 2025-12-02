using System;
using System.Windows.Media;
using global::MemAlerts.Shared.Models;

namespace MemAlerts.Client.ViewModels;

public class HistoryItemViewModel : ObservableObject
{
    private readonly AlertRequest _request;
    private readonly bool _isIncoming;

    public AlertRequest Request => _request;

    public string ViewerName => _request.ViewerName;
    public string VideoTitle => _request.Video.Title;
    public string Status => _request.Status.ToString();
    public string DirectionLabel => _isIncoming ? "Входящий" : "Исходящий";
    public Brush DirectionColor => _isIncoming 
        ? new SolidColorBrush(Color.FromRgb(76, 175, 80)) // Green
        : new SolidColorBrush(Color.FromRgb(33, 150, 243)); // Blue

    public bool IsWebVideo => GetOriginalUrl() != null;

    public string? OriginalUrl => GetOriginalUrl();

    public HistoryItemViewModel(AlertRequest request, bool isIncoming)
    {
        _request = request;
        _isIncoming = isIncoming;
    }

    private string? GetOriginalUrl()
    {
        if (!string.IsNullOrWhiteSpace(_request.Video.OriginalUrl))
        {
            return _request.Video.OriginalUrl;
        }

        if (MemAlerts.Client.Services.VideoUrlHelper.IsWebVideo(_request.Video.Source))
        {
            return _request.Video.Source.ToString();
        }

        if (!string.IsNullOrWhiteSpace(_request.Video.Description) &&
            MemAlerts.Client.Services.VideoUrlHelper.IsHttpUrl(_request.Video.Description))
        {
            return _request.Video.Description;
        }

        return null;
    }
}

