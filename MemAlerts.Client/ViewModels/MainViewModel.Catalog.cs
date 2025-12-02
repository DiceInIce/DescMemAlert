using System;
using System.Collections.Generic;
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
    public async void LoadCustomVideo(Uri fileUri, string title)
    {
        try
        {
            var video = await _localVideoService.AddLocalVideoAsync(fileUri.LocalPath, title);
            AddVideoToCatalog(video);
            StatusMessage = "Видео добавлено в коллекцию";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка добавления видео: {ex.Message}";
        }
    }

    public async void LoadUrlVideo(string url)
    {
        try
        {
            var video = await _localVideoService.AddUrlVideoAsync(url, "Web Video");
            AddVideoToCatalog(video);
            StatusMessage = "Ссылка добавлена";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка добавления ссылки: {ex.Message}";
        }
    }

    public async void DownloadAndAddVideo(string url)
    {
        await RunBusyOperationAsync(
            "Загрузка видео... (это может занять время)",
            async () =>
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var userId = UserLogin ?? "default";
                var videosDir = Path.Combine(appData, "MemAlerts", "Users", userId, "Videos");

                var filePath = await _videoDownloader.DownloadVideoAsync(url, videosDir);

                var title = "Downloaded Video";
                try
                {
                    title = Path.GetFileNameWithoutExtension(filePath);
                }
                catch
                {
                }

                var video = await _localVideoService.AddDownloadedVideoAsync(filePath, url, title);
                AddVideoToCatalog(video);
                StatusMessage = "Видео успешно скачано и добавлено";
            },
            errorMessageFactory: ex =>
            {
                MessageBox.Show($"Не удалось скачать видео: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                return $"Ошибка загрузки: {ex.Message}";
            });
    }

    private void AddVideoToCatalog(AlertVideo video)
    {
        if (IsGlobalLibrary)
        {
            IsGlobalLibrary = false;
        }
        else
        {
            InsertOrUpdateCatalog(video);
        }

        SelectedVideo = video;
    }

    private Task LoadCatalogAsync() =>
        RunBusyOperationAsync(
            IsGlobalLibrary ? "Загружаем глобальный каталог..." : "Загружаем ваши видео...",
            async () =>
            {
                List<AlertVideo> videos;

                if (IsGlobalLibrary)
                {
                    videos = (await _service.GetCatalogAsync())
                        .OrderByDescending(v => v.IsCommunityFavorite)
                        .ThenBy(v => v.Title)
                        .ToList();
                }
                else
                {
                    videos = (await _localVideoService.GetVideosAsync())
                        .OrderByDescending(v => v.Id)
                        .ToList();
                }

                _allVideos = videos;
                ApplyFilters();

                StatusMessage = !_catalogInternal.Any()
                    ? IsGlobalLibrary ? "Глобальный каталог пуст" : "У вас пока нет видео"
                    : $"Загружено {_catalogInternal.Count} видео";

                SelectedVideo ??= _catalogInternal.FirstOrDefault();
            },
            errorMessageFactory: ex => $"Ошибка каталога: {ex.Message}");

    private void InsertOrUpdateCatalog(AlertVideo video)
    {
        var existingIndex = _allVideos.FindIndex(v => v.Id == video.Id);
        if (existingIndex >= 0)
        {
            _allVideos[existingIndex] = video;
        }
        else
        {
            _allVideos.Insert(0, video);
        }

        ApplyFilters();
    }

    private void ApplyFilters()
    {
        var filtered = string.IsNullOrWhiteSpace(SearchText)
            ? _allVideos
            : _allVideos.Where(MatchesSearchText).ToList();

        _catalogInternal.ReplaceWith(filtered);

        if (!filtered.Any())
        {
            SelectedVideo = null;
            return;
        }

        if (SelectedVideo is null || !filtered.Contains(SelectedVideo))
        {
            SelectedVideo = filtered.First();
        }
    }

    private bool MatchesSearchText(AlertVideo video)
    {
        return video.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || video.Description.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || video.Category.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    private async Task DeleteVideoAsync(AlertVideo? video)
    {
        if (video == null) return;

        var result = MessageBox.Show($"Вы уверены, что хотите удалить \"{video.Title}\"?", "Удаление видео", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (result != MessageBoxResult.Yes) return;

        try
        {
            await _localVideoService.DeleteVideoAsync(video.Id);
            _allVideos.Remove(video);
            ApplyFilters();
            StatusMessage = "Видео удалено";
            if (SelectedVideo == video) SelectedVideo = null;
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка удаления: {ex.Message}";
        }
    }

    private bool CanDeleteVideo(AlertVideo? video)
    {
        return video != null && video.IsCustom && !IsGlobalLibrary;
    }

    private async Task ClearCacheAsync()
    {
        if (!_localVideoService.IsInitialized)
        {
            StatusMessage = "Локальное хранилище недоступно";
            return;
        }

        var confirmation = MessageBox.Show(
            "Очистить кэш? Все загруженные видео будут удалены из списка и с диска.",
            "Очистка кэша",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (confirmation != MessageBoxResult.Yes)
        {
            return;
        }

        await RunBusyOperationAsync(
            "Очищаем кэш...",
            async () =>
            {
                await _localVideoService.ClearCachedVideosAsync();

                if (!IsGlobalLibrary)
                {
                    _allVideos.Clear();
                    ApplyFilters();
                }

                StatusMessage = "Кэш успешно очищен";
            },
            errorMessageFactory: ex => $"Ошибка очистки кэша: {ex.Message}");
    }
}
