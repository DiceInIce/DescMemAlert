using System;
using System.Text.RegularExpressions;

namespace MemAlerts.Client.Services;

public static class VideoUrlHelper
{
    public static bool IsWebVideo(Uri uri)
    {
        if (!uri.IsAbsoluteUri) return false;
        return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
    }

    public static string? TryGetYoutubeId(string url)
    {
        var shortsMatch = Regex.Match(url, @"youtube\.com/shorts/([^""&?\/\s]{11})");
        if (shortsMatch.Success)
        {
            return shortsMatch.Groups[1].Value;
        }
        
        var youtubeMatch = Regex.Match(url, @"(?:youtube\.com\/(?:[^\/]+\/.+\/|(?:v|e(?:mbed)?)\/|.*[?&]v=)|youtu\.be\/)([^""&?\/\s]{11})");
        if (youtubeMatch.Success)
        {
            return youtubeMatch.Groups[1].Value;
        }
        return null;
    }

    public static bool IsYouTubeShorts(string url)
    {
        return url.Contains("youtube.com/shorts/", StringComparison.OrdinalIgnoreCase);
    }

    public static Uri GetEmbedUri(Uri sourceUri, bool autoplay = true)
    {
        var url = sourceUri.ToString();
        var videoId = TryGetYoutubeId(url);

        if (videoId != null)
        {
            return new Uri($"http://localhost:5055/embed?v={videoId}&autoplay={(autoplay ? "1" : "0")}");
        }

        return sourceUri;
    }
}
