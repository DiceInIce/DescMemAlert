using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using global::MemAlerts.Shared.Models;
using MemAlerts.Client.Extensions;
using MemAlerts.Client.Services;

namespace MemAlerts.Client.ViewModels;

public sealed partial class MainViewModel
{
    private Task LoadRequestsAsync() =>
        RunBusyOperationAsync(
            "Загружаем историю...",
            async () =>
            {
                var requests = await _service.GetActiveRequestsAsync();
                _requestsInternal.ReplaceWith(
                    requests.Select(r => new HistoryItemViewModel(r, r.ViewerName != UserLogin)));

                StatusMessage = "История обновлена";
            },
            errorMessageFactory: ex => $"Ошибка истории: {ex.Message}");

    private async Task SubmitRequestAsync()
    {
        if (SelectedVideo is null)
        {
            StatusMessage = "Выберите клип";
            return;
        }

        if (SelectedFriend is null)
        {
            StatusMessage = "Выберите получателя";
            return;
        }

        IsBusy = true;
        StatusMessage = "Отправляем запрос...";

        try
        {
            var senderName = UserLogin ?? ViewerName;

            var preparedVideo = await PrepareVideoForSendingAsync(SelectedVideo);

            var request = await _service.SubmitRequestAsync(
                preparedVideo,
                senderName,
                CustomMessage,
                TipAmount);

            var requestToSend = new AlertRequest
            {
                Id = request.Id,
                Video = request.Video,
                ViewerName = senderName,
                Message = request.Message,
                TipAmount = request.TipAmount,
                SubmittedAt = request.SubmittedAt,
                Status = request.Status,
                RecipientUserId = SelectedFriendUserId
            };

            _requestsInternal.Insert(0, new HistoryItemViewModel(requestToSend, isIncoming: false));
            StatusMessage = "Запрос доставлен ✉️";
            CustomMessage = string.Empty;

            if (IsConnected)
            {
                await _peerMessenger.SendRequestAsync(requestToSend);
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Не удалось отправить: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private bool CanSubmit() =>
        !IsBusy &&
        IsConnected &&
        _peerMessenger.IsAuthenticated &&
        SelectedVideo is not null &&
        SelectedFriend is not null &&
        !string.IsNullOrWhiteSpace(ViewerName);

    private void OnPeerRequestReceived(object? sender, AlertRequest e)
    {
        _ = HandleIncomingAlertAsync(e);
    }

    private async Task HandleIncomingAlertAsync(AlertRequest request)
    {
        try
        {
            var preparedVideo = await EnsureVideoReadyForPlaybackAsync(request.Video);
            var finalRequest = ReferenceEquals(preparedVideo, request.Video)
                ? request
                : CloneAlertRequest(request, preparedVideo);

            Application.Current.Dispatcher.Invoke(() =>
            {
                _requestsInternal.Insert(0, new HistoryItemViewModel(finalRequest, isIncoming: true));
                StatusMessage = $"Новая заявка от {finalRequest.ViewerName}";
                _overlayManager.ShowAlert(finalRequest);
            });
        }
        catch (Exception ex)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                StatusMessage = $"Ошибка получения алерта: {ex.Message}";
            });
        }
    }

    private async Task<AlertVideo> EnsureVideoReadyForPlaybackAsync(AlertVideo video)
    {
        if (video.Source.IsFile && File.Exists(video.Source.LocalPath))
        {
            return video;
        }

        var inlineUri = await TryRestoreInlineVideoAsync(video);
        if (inlineUri != null)
        {
            return CloneAlertVideo(video, sourceOverride: inlineUri, inlineDataOverride: null);
        }

        var downloadedUri = await TryDownloadFromOriginalAsync(video);
        if (downloadedUri != null)
        {
            return CloneAlertVideo(video, sourceOverride: downloadedUri, inlineDataOverride: null);
        }

        return video;
    }

    private async Task<Uri?> TryRestoreInlineVideoAsync(AlertVideo video)
    {
        if (video.InlineData is { Length: > 0 })
        {
            return await _localVideoService.SaveInlineVideoAsync(video.InlineData, video.InlineFileName);
        }

        return null;
    }

    private async Task<Uri?> TryDownloadFromOriginalAsync(AlertVideo video)
    {
        var originalUrl = ResolveOriginalUrl(video);
        if (string.IsNullOrWhiteSpace(originalUrl) || !VideoUrlHelper.ShouldForceDownload(originalUrl))
        {
            return null;
        }

        var cacheKey = $"{video.Id}:{originalUrl}";
        if (_downloadedVideoCache.TryGetValue(cacheKey, out var cachedUri))
        {
            if (cachedUri.IsFile && File.Exists(cachedUri.LocalPath))
            {
                return cachedUri;
            }

            _downloadedVideoCache.TryRemove(cacheKey, out _);
        }

        var incomingDir = _localVideoService.GetIncomingCacheDirectory();
        var localPath = await _videoDownloader.DownloadVideoAsync(originalUrl, incomingDir);
        var uri = new Uri(localPath);
        _downloadedVideoCache[cacheKey] = uri;
        return uri;
    }

    private async Task<AlertVideo> PrepareVideoForSendingAsync(AlertVideo video)
    {
        var originalUrl = ResolveOriginalUrl(video);
        var canShareViaUrl = !string.IsNullOrWhiteSpace(originalUrl) && VideoUrlHelper.ShouldForceDownload(originalUrl);

        byte[]? inlineData = null;
        string? inlineFileName = null;

        if (video.Source.IsFile && File.Exists(video.Source.LocalPath) && !canShareViaUrl)
        {
            inlineData = await File.ReadAllBytesAsync(video.Source.LocalPath);
            inlineFileName = Path.GetFileName(video.Source.LocalPath);
        }

        return CloneAlertVideo(
            video,
            sourceOverride: video.Source,
            inlineDataOverride: inlineData,
            inlineFileNameOverride: inlineFileName,
            originalUrlOverride: originalUrl);
    }

    private static AlertRequest CloneAlertRequest(AlertRequest source, AlertVideo videoOverride)
    {
        return new AlertRequest
        {
            Id = source.Id,
            Video = videoOverride,
            ViewerName = source.ViewerName,
            Message = source.Message,
            TipAmount = source.TipAmount,
            SubmittedAt = source.SubmittedAt,
            Status = source.Status,
            RecipientUserId = source.RecipientUserId
        };
    }

    private static AlertVideo CloneAlertVideo(
        AlertVideo source,
        Uri? sourceOverride = null,
        byte[]? inlineDataOverride = null,
        string? inlineFileNameOverride = null,
        string? originalUrlOverride = null)
    {
        return new AlertVideo
        {
            Id = source.Id,
            Title = source.Title,
            Description = source.Description,
            Category = source.Category,
            Duration = source.Duration,
            Price = source.Price,
            Source = sourceOverride ?? source.Source,
            Thumbnail = source.Thumbnail,
            IsCommunityFavorite = source.IsCommunityFavorite,
            IsCustom = source.IsCustom,
            OriginalUrl = originalUrlOverride ?? source.OriginalUrl,
            InlineData = inlineDataOverride ?? source.InlineData,
            InlineFileName = inlineFileNameOverride ?? source.InlineFileName
        };
    }

    private static string? ResolveOriginalUrl(AlertVideo video)
    {
        if (!string.IsNullOrWhiteSpace(video.OriginalUrl))
        {
            return video.OriginalUrl;
        }

        if (VideoUrlHelper.IsWebVideo(video.Source))
        {
            return video.Source.ToString();
        }

        if (VideoUrlHelper.IsHttpUrl(video.Description))
        {
            return video.Description;
        }

        return null;
    }

    private Task OpenHistoryLinkAsync(HistoryItemViewModel? item)
    {
        if (item?.IsWebVideo == true && !string.IsNullOrWhiteSpace(item.OriginalUrl))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = item.OriginalUrl,
                    UseShellExecute = true
                });
            }
            catch
            {
                StatusMessage = "Не удалось открыть ссылку";
            }
        }
        return Task.CompletedTask;
    }
}
