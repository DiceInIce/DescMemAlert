using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using MemAlerts.Shared.Models;

namespace MemAlerts.Client.Services;

public class LocalVideoService
{
    private string? _userVideosPath;
    private string? _userJsonPath;
    private List<AlertVideo> _localVideos = new();
    private readonly HttpClient _httpClient = new();

    public bool IsInitialized => !string.IsNullOrEmpty(_userVideosPath);

    public void Initialize(string userId)
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var baseDir = Path.Combine(appData, "MemAlerts", "Users", userId);
        _userVideosPath = Path.Combine(baseDir, "Videos");
        _userJsonPath = Path.Combine(baseDir, "videos.json");

        if (!Directory.Exists(_userVideosPath))
        {
            Directory.CreateDirectory(_userVideosPath);
        }
    }


    public async Task<List<AlertVideo>> GetVideosAsync()
    {
        if (!IsInitialized) throw new InvalidOperationException("Service not initialized");

        if (File.Exists(_userJsonPath))
        {
            try
            {
                using var stream = File.OpenRead(_userJsonPath!);
                _localVideos = await JsonSerializer.DeserializeAsync<List<AlertVideo>>(stream) ?? new List<AlertVideo>();
            }
            catch
            {
                _localVideos = new List<AlertVideo>();
            }
        }

        return _localVideos;
    }

    public async Task<AlertVideo> AddLocalVideoAsync(string filePath, string title)
    {
        if (!IsInitialized) throw new InvalidOperationException("Service not initialized");

        var fileName = Path.GetFileName(filePath);
        var uniqueName = $"{Guid.NewGuid():N}{Path.GetExtension(fileName)}";
        var destPath = Path.Combine(_userVideosPath!, uniqueName);

        await Task.Run(() => File.Copy(filePath, destPath, true));

        // Generate thumbnail and get duration
        var thumbResult = await ThumbnailGenerator.GenerateThumbnailAsync(destPath);

        var video = new AlertVideo
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = title,
            Description = "Локальное видео",
            Category = "Local File",
            Source = new Uri(destPath),
            Thumbnail = thumbResult.Thumbnail,
            IsCustom = true,
            Duration = thumbResult.Duration,
            OriginalUrl = null,
            InlineData = null,
            InlineFileName = Path.GetFileName(filePath)
        };

        _localVideos.Insert(0, video);
        await SaveMetadataAsync();

        return video;
    }

    public async Task<AlertVideo> AddDownloadedVideoAsync(string filePath, string originalUrl, string title)
    {
        if (!IsInitialized) throw new InvalidOperationException("Service not initialized");

        var fileName = Path.GetFileName(filePath);
        var uniqueName = $"{Guid.NewGuid():N}{Path.GetExtension(fileName)}";
        var destPath = Path.Combine(_userVideosPath!, uniqueName);

        await Task.Run(() => File.Copy(filePath, destPath, true));
        var thumbResult = await ThumbnailGenerator.GenerateThumbnailAsync(destPath);

        var category = "Local File";
        var finalTitle = title;
        var thumbnail = thumbResult.Thumbnail;

        if (originalUrl.Contains("tiktok.com", StringComparison.OrdinalIgnoreCase))
        {
            category = "TikTok";
            var (fetchedTitle, fetchedThumbnail) = await FetchTikTokMetadataAsync(originalUrl);
            if (!string.IsNullOrWhiteSpace(fetchedTitle))
            {
                finalTitle = fetchedTitle;
            }
            if (fetchedThumbnail != null)
            {
                thumbnail = fetchedThumbnail;
            }
        }
        else if (VideoUrlHelper.IsYouTubeShorts(originalUrl))
        {
            category = "YouTube Shorts";
            var youtubeId = VideoUrlHelper.TryGetYoutubeId(originalUrl);
            if (youtubeId != null)
            {
                thumbnail = new Uri($"https://img.youtube.com/vi/{youtubeId}/0.jpg");
                var fetchedTitle = await FetchYouTubeTitleAsync(youtubeId);
                if (!string.IsNullOrWhiteSpace(fetchedTitle))
                {
                    finalTitle = fetchedTitle;
                }
            }
        }

        var video = new AlertVideo
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = finalTitle,
            Description = originalUrl,
            Category = category,
            Source = new Uri(destPath),
            Thumbnail = thumbnail,
            IsCustom = true,
            Duration = thumbResult.Duration,
            OriginalUrl = originalUrl,
            InlineData = null,
            InlineFileName = Path.GetFileName(filePath)
        };

        _localVideos.Insert(0, video);
        await SaveMetadataAsync();

        return video;
    }

    public async Task<AlertVideo> AddUrlVideoAsync(string url, string title)
    {
        if (!IsInitialized) throw new InvalidOperationException("Service not initialized");

        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri))
        {
            throw new ArgumentException("Invalid URL");
        }

        var finalTitle = title;
        var thumbnail = new Uri("https://placehold.co/320x180?text=URL+Video");
        var duration = TimeSpan.FromSeconds(0);
        var category = "Web";

        if (VideoUrlHelper.IsYouTubeShorts(url))
        {
            category = "YouTube Shorts";
            var shortsId = VideoUrlHelper.TryGetYoutubeId(url);
            if (shortsId != null)
            {
                thumbnail = new Uri($"https://img.youtube.com/vi/{shortsId}/0.jpg");
                finalTitle = await FetchYouTubeTitleAsync(shortsId) ?? finalTitle;
                duration = await FetchYouTubeDurationAsync(shortsId);
            }
        }
        else
        {
            var youtubeId = VideoUrlHelper.TryGetYoutubeId(url);
            if (youtubeId != null)
            {
                category = "YouTube";
                thumbnail = new Uri($"https://img.youtube.com/vi/{youtubeId}/0.jpg");
                finalTitle = await FetchYouTubeTitleAsync(youtubeId) ?? finalTitle;
                duration = await FetchYouTubeDurationAsync(youtubeId);
            }
            else if (url.Contains("tiktok.com"))
            {
                category = "TikTok";
                var (fetchedTitle, fetchedThumbnail) = await FetchTikTokMetadataAsync(url);
                if (!string.IsNullOrWhiteSpace(fetchedTitle))
                {
                    finalTitle = fetchedTitle;
                }
                if (fetchedThumbnail != null)
                {
                    thumbnail = fetchedThumbnail;
                }
            }
        }

        var video = new AlertVideo
        {
            Id = Guid.NewGuid().ToString("N"),
            Title = finalTitle,
            Description = "Видео из интернета",
            Category = category,
            Source = uri,
            Thumbnail = thumbnail,
            IsCustom = true,
            Duration = duration,
            OriginalUrl = url,
            InlineData = null,
            InlineFileName = null
        };

        _localVideos.Insert(0, video);
        await SaveMetadataAsync();

        return video;
    }

    public async Task DeleteVideoAsync(string videoId)
    {
        if (!IsInitialized) throw new InvalidOperationException("Service not initialized");

        var video = _localVideos.FirstOrDefault(v => v.Id == videoId);
        if (video == null) return;

        // Удаляем файл из AppData только для скачанных видео (добавленных по URL)
        // Локальные видео (Description == "Локальное видео") не трогаем
        var isDownloadedVideo = video.Source.IsFile && 
                                !string.IsNullOrEmpty(video.Description) &&
                                (video.Description.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                                 video.Description.StartsWith("https://", StringComparison.OrdinalIgnoreCase));

        if (isDownloadedVideo && File.Exists(video.Source.LocalPath))
        {
            try
            {
                if (video.Source.LocalPath.StartsWith(_userVideosPath!, StringComparison.OrdinalIgnoreCase))
                {
                    await Task.Run(() => File.Delete(video.Source.LocalPath));
                }
            }
            catch { }
        }

        _localVideos.Remove(video);
        await SaveMetadataAsync();
    }

    public string GetUserVideosDirectory()
    {
        if (!IsInitialized) throw new InvalidOperationException("Service not initialized");
        return _userVideosPath!;
    }

    public string GetIncomingCacheDirectory()
    {
        var baseDir = GetUserVideosDirectory();
        var incomingDir = Path.Combine(baseDir, "IncomingCache");
        if (!Directory.Exists(incomingDir))
        {
            Directory.CreateDirectory(incomingDir);
        }

        return incomingDir;
    }

    public async Task<Uri> SaveInlineVideoAsync(byte[] data, string? suggestedFileName)
    {
        if (data == null || data.Length == 0)
        {
            throw new ArgumentException("Video data is empty", nameof(data));
        }

        var incomingDir = GetIncomingCacheDirectory();
        var extension = Path.GetExtension(suggestedFileName ?? string.Empty);
        if (string.IsNullOrWhiteSpace(extension))
        {
            extension = ".mp4";
        }

        var fileName = $"{Guid.NewGuid():N}{extension}";
        var filePath = Path.Combine(incomingDir, fileName);
        await File.WriteAllBytesAsync(filePath, data);
        return new Uri(filePath);
    }

    private async Task SaveMetadataAsync()
    {
        if (_userJsonPath == null) return;
        using var stream = File.Create(_userJsonPath);
        await JsonSerializer.SerializeAsync(stream, _localVideos);
    }

    private async Task<(string? title, Uri? thumbnail)> FetchTikTokMetadataAsync(string url)
    {
        try
        {
            var oembedUrl = $"https://www.tiktok.com/oembed?url={url}";
            var json = await _httpClient.GetStringAsync(oembedUrl);
            using var doc = JsonDocument.Parse(json);
            
            string? title = null;
            Uri? thumbnail = null;

            if (doc.RootElement.TryGetProperty("title", out var titleProp))
            {
                var fetchedTitle = titleProp.GetString();
                if (!string.IsNullOrWhiteSpace(fetchedTitle))
                {
                    title = fetchedTitle.Length > 50 ? fetchedTitle.Substring(0, 47) + "..." : fetchedTitle;
                }
            }

            if (doc.RootElement.TryGetProperty("thumbnail_url", out var thumbProp))
            {
                var thumbUrl = thumbProp.GetString();
                if (!string.IsNullOrWhiteSpace(thumbUrl))
                {
                    thumbnail = new Uri(thumbUrl);
                }
            }

            return (title, thumbnail);
        }
        catch
        {
            return (null, null);
        }
    }

    private async Task<string?> FetchYouTubeTitleAsync(string videoId)
    {
        try
        {
            var watchUrl = $"https://www.youtube.com/watch?v={videoId}";
            var oembedUrl = $"https://noembed.com/embed?url={Uri.EscapeDataString(watchUrl)}";
            var json = await _httpClient.GetStringAsync(oembedUrl);
            using var doc = JsonDocument.Parse(json);
            
            if (doc.RootElement.TryGetProperty("title", out var titleProp))
            {
                return titleProp.GetString();
            }
        }
        catch { }
        
        return null;
    }

    private async Task<TimeSpan> FetchYouTubeDurationAsync(string videoId)
    {
        try
        {
            var pageHtml = await _httpClient.GetStringAsync($"https://www.youtube.com/watch?v={videoId}");
            var match = System.Text.RegularExpressions.Regex.Match(pageHtml, @"\""approxDurationMs\"":\""(\d+)\""");
            if (match.Success && long.TryParse(match.Groups[1].Value, out var ms))
            {
                return TimeSpan.FromMilliseconds(ms);
            }
        }
        catch { }
        
        return TimeSpan.Zero;
    }
}

