using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using Microsoft.Extensions.DependencyInjection;
using MemAlerts.Client.ViewModels;
using MemAlerts.Client.Views;
using MemAlerts.Client.Services;
using MaterialDesignThemes.Wpf;
using global::MemAlerts.Shared.Models;
using Microsoft.Web.WebView2.Core;

namespace MemAlerts.Client;

public partial class MainWindow : Window
{
    private bool _isPreviewPaused;
    private bool _isPreviewMuted;
    private double _previousVolume = 70;
    private bool _isWebVideo;
    private bool _isWebViewInitialized;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        PreviewVolumeSlider.Value = _previousVolume;
        PreviewPlayer.Volume = _previousVolume / 100.0;
        UpdateMuteVisual();
    }

    private void BrowseVideo_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var dialog = new OpenFileDialog
        {
            Filter = "Видео файлы (*.mp4;*.mov;*.webm)|*.mp4;*.mov;*.webm|Все файлы (*.*)|*.*",
            Title = "Выберите видео для алерта"
        };

        if (dialog.ShowDialog() == true)
        {
            var fileUri = new Uri(dialog.FileName);
            var title = Path.GetFileNameWithoutExtension(dialog.FileName) ?? "Custom Clip";
            viewModel.LoadCustomVideo(fileUri, title);
        }
    }

    private void AddUrlVideo_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var dialog = new AddUrlVideoWindow
        {
            Owner = this
        };

        if (dialog.ShowDialog() == true && !string.IsNullOrWhiteSpace(dialog.VideoUrl))
        {
            var url = dialog.VideoUrl;
            var isTikTok = url.Contains("tiktok.com", StringComparison.OrdinalIgnoreCase);
            var isYouTubeShorts = VideoUrlHelper.IsYouTubeShorts(url);
            
            if (isTikTok || isYouTubeShorts)
            {
                var downloader = new VideoDownloaderService();
                if (!downloader.IsDownloaderAvailable())
                {
                    var exePath = System.Reflection.Assembly.GetExecutingAssembly().Location;
                    var exeDir = Path.GetDirectoryName(exePath);
                    MessageBox.Show($"yt-dlp.exe не найден.\n\nПожалуйста, скачайте yt-dlp.exe с https://github.com/yt-dlp/yt-dlp/releases и поместите его в папку:\n{exeDir}", 
                        "yt-dlp не найден", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                viewModel.DownloadAndAddVideo(url);
            }
            else
            {
                viewModel.LoadUrlVideo(url);
            }
        }
    }

    private void PreviewPlayer_OnMediaEnded(object sender, RoutedEventArgs e)
    {
        if (sender is MediaElement media)
        {
            media.Position = TimeSpan.Zero;
            media.Play();
            PlayPauseIcon.Kind = PackIconKind.Pause;
            PreviewPoster.Visibility = Visibility.Collapsed;
        }
    }

    private async void PreviewPlayer_OnMediaOpened(object sender, RoutedEventArgs e)
    {
        _isPreviewPaused = true;
        PreviewPlayer.Position = TimeSpan.Zero;
        PreviewPlayer.Pause();
        PlayPauseIcon.Kind = PackIconKind.Play;
        PreviewPoster.Visibility = Visibility.Visible;
        PreviewPlayer.Stretch = System.Windows.Media.Stretch.Uniform;
        PreviewPlayer.Volume = _isPreviewMuted ? 0 : PreviewVolumeSlider.Value / 100.0;
    }

    private async void ListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _isPreviewPaused = false;
        PlayPauseIcon.Kind = PackIconKind.Pause;
        PreviewPoster.Visibility = Visibility.Visible;

        if (e.AddedItems.Count > 0 && e.AddedItems[0] is AlertVideo video)
        {
            _isWebVideo = VideoUrlHelper.IsWebVideo(video.Source);

            if (_isWebVideo)
            {
                await LoadWebVideoPreview(video);
            }
            else
            {
                LoadLocalVideoPreview(video);
            }
        }
        else
        {
            ClearPreview();
        }
    }

    private async Task LoadWebVideoPreview(AlertVideo video)
    {
        PreviewPlayer.Stop();
        PreviewPlayer.Visibility = Visibility.Collapsed;
        PreviewWebView.Visibility = Visibility.Visible;
        PreviewPoster.Visibility = Visibility.Collapsed;

        try
        {
            if (!_isWebViewInitialized)
            {
                await InitializeWebView();
            }
            
            var embedUri = VideoUrlHelper.GetEmbedUri(video.Source, autoplay: false);
            if (PreviewWebView.Source != embedUri)
            {
                PreviewWebView.Source = embedUri;
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Ошибка инициализации превью: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void LoadLocalVideoPreview(AlertVideo video)
    {
        PreviewWebView.Visibility = Visibility.Collapsed;
        PreviewPlayer.Visibility = Visibility.Visible;
        PreviewPlayer.Source = video.Source;
        PreviewPlayer.Pause();
        PreviewPlayer.Position = TimeSpan.Zero;
        _isPreviewPaused = true;
        PlayPauseIcon.Kind = PackIconKind.Play;
        PreviewPoster.Visibility = Visibility.Visible;
    }

    private void ClearPreview()
    {
        _isWebVideo = false;
        
        if (PreviewPlayer != null)
        {
            PreviewPlayer.Stop();
            PreviewPlayer.Source = null;
            PreviewPlayer.Visibility = Visibility.Visible;
        }

        if (PreviewWebView != null)
        {
            PreviewWebView.Visibility = Visibility.Collapsed;
            if (PreviewWebView.CoreWebView2 != null)
            {
                PreviewWebView.Source = new Uri("about:blank");
            }
        }

        if (PreviewPoster != null)
        {
            PreviewPoster.Visibility = Visibility.Visible; 
        }
    }

    private async Task InitializeWebView()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var userDataFolder = Path.Combine(appData, "MemAlerts", "WebView2");
        var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
        await PreviewWebView.EnsureCoreWebView2Async(env);
        PreviewWebView.CoreWebView2.Settings.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
        _isWebViewInitialized = true;
    }

    private async void PreviewPlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (_isWebVideo)
        {
            await ToggleWebVideoPlayback();
        }
        else
        {
            ToggleLocalVideoPlayback();
        }
    }

    private async Task ToggleWebVideoPlayback()
    {
        if (PreviewWebView?.CoreWebView2 == null) return;

        if (_isPreviewPaused)
        {
            try { await PreviewWebView.CoreWebView2.ExecuteScriptAsync("playVideo();"); } catch { }
            SetPlayingState();
        }
        else
        {
            try { await PreviewWebView.CoreWebView2.ExecuteScriptAsync("pauseVideo();"); } catch { }
            SetPausedState();
        }
    }

    private void ToggleLocalVideoPlayback()
    {
        if (PreviewPlayer.Source == null) return;

        if (_isPreviewPaused)
        {
            PreviewPlayer.Play();
            SetPlayingState();
        }
        else
        {
            PreviewPlayer.Pause();
            SetPausedState();
        }
    }

    private void SetPlayingState()
    {
        PlayPauseIcon.Kind = PackIconKind.Pause;
        PreviewPoster.Visibility = Visibility.Collapsed;
        _isPreviewPaused = false;
    }

    private void SetPausedState()
    {
        PlayPauseIcon.Kind = PackIconKind.Play;
        _isPreviewPaused = true;
    }

    private async void PreviewMuteButton_Click(object sender, RoutedEventArgs e)
    {
        _isPreviewMuted = !_isPreviewMuted;
        
        if (_isPreviewMuted)
        {
            _previousVolume = PreviewVolumeSlider.Value;
            PreviewVolumeSlider.Value = 0;
        }
        else
        {
            PreviewVolumeSlider.Value = _previousVolume > 0 ? _previousVolume : 50;
        }
        
        UpdateMuteVisual();
        await UpdateWebViewVolume();
    }

    private async void PreviewVolumeSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (PreviewPlayer != null)
        {
            PreviewPlayer.Volume = e.NewValue / 100.0;
            
            if (e.NewValue > 0 && _isPreviewMuted)
            {
                _isPreviewMuted = false;
                UpdateMuteVisual();
            }
            else if (e.NewValue == 0 && !_isPreviewMuted)
            {
                _isPreviewMuted = true;
                UpdateMuteVisual();
            }
        }

        await UpdateWebViewVolume();
    }

    private async Task UpdateWebViewVolume()
    {
        if (_isWebVideo && PreviewWebView?.CoreWebView2 != null)
        {
            var vol = _isPreviewMuted ? 0 : (int)PreviewVolumeSlider.Value;
            try
            {
                await PreviewWebView.CoreWebView2.ExecuteScriptAsync($"setVolume({vol});");
            }
            catch { }
        }
    }

    private void UpdateMuteVisual()
    {
        if (MuteIcon != null)
        {
            if (_isPreviewMuted || PreviewVolumeSlider.Value == 0)
            {
                MuteIcon.Kind = PackIconKind.VolumeMute;
            }
            else if (PreviewVolumeSlider.Value < 50)
            {
                MuteIcon.Kind = PackIconKind.VolumeMedium;
            }
            else
            {
                MuteIcon.Kind = PackIconKind.VolumeHigh;
            }
        }
    }

    private void ColorZone_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void MinimizeButton_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void MaximizeButton_Click(object sender, RoutedEventArgs e)
    {
        if (WindowState == WindowState.Maximized)
        {
            WindowState = WindowState.Normal;
        }
        else
        {
            WindowState = WindowState.Maximized;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        Application.Current.Shutdown();
    }

    public void OpenFriendsWindow()
    {
        foreach (Window window in Application.Current.Windows)
        {
            if (window is FriendsWindow)
            {
                window.Activate();
                if (window.WindowState == WindowState.Minimized)
                {
                    window.WindowState = WindowState.Normal;
                }
                return;
            }
        }

        if (Application.Current is App app && app._host != null)
        {
            var friendsWindow = app._host.Services.GetRequiredService<FriendsWindow>();
            friendsWindow.Owner = this;
            friendsWindow.Show();
        }
    }
}
