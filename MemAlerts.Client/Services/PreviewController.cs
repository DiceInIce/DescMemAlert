using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;
using global::MemAlerts.Shared.Models;
using Microsoft.Web.WebView2.Wpf;

namespace MemAlerts.Client.Services;

/// <summary>
/// Инкапсулирует всю логику управления предпросмотром видео, разгружая MainWindow.
/// </summary>
public sealed class PreviewController : IDisposable
{
    private readonly MediaElement _player;
    private readonly WebView2 _webView;
    private readonly FrameworkElement _poster;
    private readonly Slider _volumeSlider;
    private readonly PackIcon _playPauseIcon;
    private readonly PackIcon _muteIcon;
    private readonly WebVideoPlayerService _webVideoPlayerService;

    private bool _isPreviewPaused = true;
    private bool _isPreviewMuted;
    private bool _isWebVideo;
    private bool _isWebViewInitialized;
    private double _previousVolume = 70;

    public PreviewController(
        MediaElement player,
        WebView2 webView,
        FrameworkElement poster,
        Slider volumeSlider,
        PackIcon playPauseIcon,
        PackIcon muteIcon,
        WebVideoPlayerService webVideoPlayerService)
    {
        _player = player;
        _webView = webView;
        _poster = poster;
        _volumeSlider = volumeSlider;
        _playPauseIcon = playPauseIcon;
        _muteIcon = muteIcon;
        _webVideoPlayerService = webVideoPlayerService;
    }

    public void InitializeDefaults()
    {
        _volumeSlider.Value = _previousVolume;
        _player.Volume = _previousVolume / 100.0;
        UpdateMuteVisual();
    }

    public async Task ShowVideoAsync(AlertVideo video)
    {
        if (video?.Source == null)
        {
            Clear();
            return;
        }

        _isPreviewPaused = false;
        _isWebVideo = VideoUrlHelper.IsWebVideo(video.Source);
        _playPauseIcon.Kind = PackIconKind.Pause;
        _poster.Visibility = Visibility.Visible;

        if (_isWebVideo)
        {
            await ShowWebVideoAsync(video);
        }
        else
        {
            ShowLocalVideo(video);
        }
    }

    public void Clear()
    {
        _isWebVideo = false;
        _isPreviewPaused = true;
        _playPauseIcon.Kind = PackIconKind.Play;

        if (_player != null)
        {
            _player.Stop();
            _player.Source = null;
            _player.Visibility = Visibility.Visible;
        }

        if (_webView != null)
        {
            _webView.Visibility = Visibility.Collapsed;
            if (_webView.CoreWebView2 != null)
            {
                _webView.Source = new Uri("about:blank");
            }
        }

        _poster.Visibility = Visibility.Visible;
    }

    public async Task TogglePlayPauseAsync()
    {
        if (_isWebVideo)
        {
            await ToggleWebVideoPlaybackAsync();
        }
        else
        {
            ToggleLocalVideoPlayback();
        }
    }

    public async Task ToggleMuteAsync()
    {
        _isPreviewMuted = !_isPreviewMuted;

        if (_isPreviewMuted)
        {
            _previousVolume = _volumeSlider.Value;
            _volumeSlider.Value = 0;
        }
        else
        {
            _volumeSlider.Value = _previousVolume > 0 ? _previousVolume : 50;
        }

        UpdateMuteVisual();
        await UpdateWebViewVolumeAsync();
    }

    public async Task SetVolumeAsync(double newValue)
    {
        if (_player != null)
        {
            _player.Volume = newValue / 100.0;

            if (newValue > 0 && _isPreviewMuted)
            {
                _isPreviewMuted = false;
                UpdateMuteVisual();
            }
            else if (newValue == 0 && !_isPreviewMuted)
            {
                _isPreviewMuted = true;
                UpdateMuteVisual();
            }
        }

        await UpdateWebViewVolumeAsync();
    }

    public void HandleMediaOpened()
    {
        if (_isWebVideo)
        {
            return;
        }

        _isPreviewPaused = true;
        _player.Position = TimeSpan.Zero;
        _player.Pause();
        _player.Stretch = Stretch.Uniform;
        _player.Volume = _isPreviewMuted ? 0 : _volumeSlider.Value / 100.0;
        SetPausedState();
        _poster.Visibility = Visibility.Visible;
    }

    public void HandleMediaEnded()
    {
        if (_player.Source == null)
        {
            return;
        }

        _player.Position = TimeSpan.Zero;
        _player.Play();
        SetPlayingState();
    }

    private async Task ShowWebVideoAsync(AlertVideo video)
    {
        _player.Stop();
        _player.Visibility = Visibility.Collapsed;
        _webView.Visibility = Visibility.Visible;
        _poster.Visibility = Visibility.Collapsed;

        try
        {
            if (!_isWebViewInitialized)
            {
                await _webVideoPlayerService.EnsureWebViewInitializedAsync(_webView);
                _isWebViewInitialized = true;
            }

            await _webVideoPlayerService.LoadVideoAsync(_webView, video.Source, autoplay: false);
        }
        catch (Exception ex)
        {
            MessageBox.Show(
                $"Ошибка инициализации превью: {ex.Message}",
                "Ошибка",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
    }

    private void ShowLocalVideo(AlertVideo video)
    {
        _webView.Visibility = Visibility.Collapsed;
        _player.Visibility = Visibility.Visible;
        _player.Source = video.Source;
        _player.Pause();
        _player.Position = TimeSpan.Zero;
        SetPausedState();
        _poster.Visibility = Visibility.Visible;
    }

    private async Task ToggleWebVideoPlaybackAsync()
    {
        if (_webView?.CoreWebView2 == null)
        {
            return;
        }

        try
        {
            if (_isPreviewPaused)
            {
                await _webVideoPlayerService.PlayAsync(_webView);
                SetPlayingState();
            }
            else
            {
                await _webVideoPlayerService.PauseAsync(_webView);
                SetPausedState();
            }
        }
        catch
        {
            // Игнорируем ошибки выполнения скрипта
        }
    }

    private void ToggleLocalVideoPlayback()
    {
        if (_player.Source == null)
        {
            return;
        }

        if (_isPreviewPaused)
        {
            _player.Play();
            SetPlayingState();
        }
        else
        {
            _player.Pause();
            SetPausedState();
        }
    }

    private async Task UpdateWebViewVolumeAsync()
    {
        if (!_isWebVideo || _webView?.CoreWebView2 == null)
        {
            return;
        }

        var vol = _isPreviewMuted ? 0 : (int)_volumeSlider.Value;
        await _webVideoPlayerService.SetVolumeAsync(_webView, vol);
    }

    private void SetPlayingState()
    {
        _playPauseIcon.Kind = PackIconKind.Pause;
        _poster.Visibility = Visibility.Collapsed;
        _isPreviewPaused = false;
    }

    private void SetPausedState()
    {
        _playPauseIcon.Kind = PackIconKind.Play;
        _isPreviewPaused = true;
    }

    private void UpdateMuteVisual()
    {
        if (_muteIcon == null)
        {
            return;
        }

        if (_isPreviewMuted || _volumeSlider.Value == 0)
        {
            _muteIcon.Kind = PackIconKind.VolumeMute;
        }
        else if (_volumeSlider.Value < 50)
        {
            _muteIcon.Kind = PackIconKind.VolumeMedium;
        }
        else
        {
            _muteIcon.Kind = PackIconKind.VolumeHigh;
        }
    }

    public void Dispose()
    {
        Clear();

        if (_player != null)
        {
            try
            {
                _player.Close();
            }
            catch { }
        }

        if (_webView != null)
        {
            try
            {
                _webView.Dispose();
            }
            catch { }
        }
    }
}
