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

    public bool IsWebVideo
    {
        get
        {
            if (MemAlerts.Client.Services.VideoUrlHelper.IsWebVideo(_request.Video.Source))
            {
                return true;
            }
            
            if (_request.Video.Source.IsFile && !string.IsNullOrWhiteSpace(_request.Video.Description))
            {
                var desc = _request.Video.Description;
                if (desc.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
                    desc.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            
            return false;
        }
    }

    public string? OriginalUrl
    {
        get
        {
            if (MemAlerts.Client.Services.VideoUrlHelper.IsWebVideo(_request.Video.Source))
            {
                return _request.Video.Source.ToString();
            }
            
            if (_request.Video.Source.IsFile && !string.IsNullOrWhiteSpace(_request.Video.Description))
            {
                var desc = _request.Video.Description;
                if (desc.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || 
                    desc.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    return desc;
                }
            }
            
            return null;
        }
    }

    public HistoryItemViewModel(AlertRequest request, bool isIncoming)
    {
        _request = request;
        _isIncoming = isIncoming;
    }
}

