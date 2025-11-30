using System;

namespace MemAlerts.Client.Services;

public static class VideoAspectHelper
{
    /// <summary>
    /// Determines if a video is likely vertical (portrait) based on URL or category
    /// </summary>
    public static bool IsLikelyVerticalVideo(Uri source, string? category = null)
    {
        // TikTok videos are typically vertical
        if (category == "TikTok" || source.ToString().Contains("tiktok.com", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        
        // YouTube Shorts are typically vertical
        if (category == "YouTube Shorts" || source.ToString().Contains("youtube.com/shorts/", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }
        
        // For local files, we can't determine without opening, so return false
        // The actual aspect ratio will be determined from MediaElement.NaturalVideoWidth/Height
        return false;
    }
    
    /// <summary>
    /// Calculates aspect ratio from width and height
    /// </summary>
    public static double GetAspectRatio(int width, int height)
    {
        if (height == 0) return 16.0 / 9.0; // Default to 16:9
        return (double)width / height;
    }
    
    /// <summary>
    /// Determines if video is vertical (portrait) based on aspect ratio
    /// </summary>
    public static bool IsVertical(double aspectRatio)
    {
        return aspectRatio < 1.0; // Height > Width
    }
    
    /// <summary>
    /// Calculates optimal container size for vertical video
    /// </summary>
    public static (double width, double height) CalculateVerticalVideoSize(double maxWidth, double maxHeight, double aspectRatio)
    {
        // For vertical videos, fit to height
        var height = maxHeight;
        var width = height * aspectRatio;
        
        // If width exceeds max, scale down
        if (width > maxWidth)
        {
            width = maxWidth;
            height = width / aspectRatio;
        }
        
        return (width, height);
    }
    
    /// <summary>
    /// Calculates optimal container size for horizontal video
    /// </summary>
    public static (double width, double height) CalculateHorizontalVideoSize(double maxWidth, double maxHeight, double aspectRatio)
    {
        // For horizontal videos, fit to width
        var width = maxWidth;
        var height = width / aspectRatio;
        
        // If height exceeds max, scale down
        if (height > maxHeight)
        {
            height = maxHeight;
            width = height * aspectRatio;
        }
        
        return (width, height);
    }
}

