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

    public static bool IsYouTubeUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return url.Contains("youtube.com", StringComparison.OrdinalIgnoreCase) ||
               url.Contains("youtu.be", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsTikTokUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return url.Contains("tiktok.com", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsHttpUrl(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return false;
        return value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
               value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
    }

    public static bool ShouldForceDownload(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        return IsYouTubeShorts(url) || IsTikTokUrl(url);
    }

    public static Uri GetEmbedUri(Uri sourceUri, bool autoplay = true, string? localServerBaseUrl = null)
    {
        var url = sourceUri.ToString();
        var videoId = TryGetYoutubeId(url);

        if (videoId != null)
        {
            var baseUrl = string.IsNullOrWhiteSpace(localServerBaseUrl) ? "http://localhost:5055" : localServerBaseUrl;
            return new Uri($"{baseUrl.TrimEnd('/')}/embed?v={videoId}&autoplay={(autoplay ? "1" : "0")}");
        }

        return sourceUri;
    }
}
