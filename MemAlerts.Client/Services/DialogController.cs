using System;
using System.IO;
using System.Reflection;
using System.Windows;
using Microsoft.Win32;
using MemAlerts.Client.Views;
using Microsoft.Extensions.DependencyInjection;

namespace MemAlerts.Client.Services;

/// <summary>
/// Инкапсулирует показ вспомогательных диалогов и окон.
/// </summary>
public sealed class DialogController
{
    private readonly IServiceProvider _serviceProvider;
    private readonly VideoDownloaderService _videoDownloader;

    public DialogController(IServiceProvider serviceProvider, VideoDownloaderService videoDownloader)
    {
        _serviceProvider = serviceProvider;
        _videoDownloader = videoDownloader;
    }

    public (Uri FileUri, string Title)? PickLocalVideo(Window owner)
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Видео файлы (*.mp4;*.mov;*.webm)|*.mp4;*.mov;*.webm|Все файлы (*.*)|*.*",
            Title = "Выберите видео для алерта"
        };

        if (dialog.ShowDialog(owner) == true)
        {
            var fileUri = new Uri(dialog.FileName);
            var title = Path.GetFileNameWithoutExtension(dialog.FileName) ?? "Custom Clip";
            return (fileUri, title);
        }

        return null;
    }

    public string? PromptVideoUrl(Window owner)
    {
        var dialog = _serviceProvider.GetRequiredService<AddUrlVideoWindow>();
        dialog.Owner = owner;
        return dialog.ShowDialog() == true ? dialog.VideoUrl : null;
    }

    public bool EnsureDownloaderAvailable(Window owner)
    {
        if (_videoDownloader.IsDownloaderAvailable())
        {
            return true;
        }

        var exePath = Assembly.GetExecutingAssembly().Location;
        var exeDir = Path.GetDirectoryName(exePath);
        MessageBox.Show(owner,
            $"yt-dlp.exe не найден.\n\nПожалуйста, скачайте yt-dlp.exe с https://github.com/yt-dlp/yt-dlp/releases и поместите его в папку:\n{exeDir}",
            "yt-dlp не найден",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        return false;
    }

    public void ShowFriendsWindow(Window owner)
    {
        foreach (Window window in Application.Current.Windows)
        {
            if (window is FriendsWindow friendsWindow)
            {
                friendsWindow.Activate();
                if (friendsWindow.WindowState == WindowState.Minimized)
                {
                    friendsWindow.WindowState = WindowState.Normal;
                }
                return;
            }
        }

        var newWindow = _serviceProvider.GetRequiredService<FriendsWindow>();
        newWindow.Owner = owner;
        newWindow.Show();
    }
}
