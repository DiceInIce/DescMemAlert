using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using global::MemAlerts.Shared.Models;
using MemAlerts.Client.Services;
using Microsoft.Web.WebView2.Core;

namespace MemAlerts.Client.Alerts;

public partial class AlertOverlayWindow : Window
{
    private static readonly Random Randomizer = new();
    private bool _isWebVideo;

    public AlertOverlayWindow(AlertRequest request)
    {
        InitializeComponent();
        DataContext = request;
        Loaded += (_, _) => InitializeWindow(request);
        Closing += (_, _) => CleanupMediaElement();
        MouseLeftButtonDown += (_, _) => DragMove();
    }

    private async void InitializeWindow(AlertRequest request)
    {
        if (request.Video.Source is null) return;

        _isWebVideo = VideoUrlHelper.IsWebVideo(request.Video.Source);

        if (_isWebVideo)
        {
            OverlayPlayer.Visibility = Visibility.Collapsed;
            OverlayWebView.Visibility = Visibility.Visible;
            
            var isVertical = VideoAspectHelper.IsLikelyVerticalVideo(request.Video.Source, request.Video.Category);
            if (isVertical)
            {
                var (width, height) = VideoAspectHelper.CalculateVerticalVideoSize(640, 720, 9.0 / 16.0);
                VideoContainer.Width = width;
                VideoContainer.Height = height;
            }
            
            try 
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                var userDataFolder = Path.Combine(appData, "MemAlerts", "WebView2");
                var options = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder, options);
                
                await OverlayWebView.EnsureCoreWebView2Async(env);
                OverlayWebView.CoreWebView2.Settings.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
                
                var embedUri = VideoUrlHelper.GetEmbedUri(request.Video.Source, autoplay: true);
                if (OverlayWebView.Source != embedUri)
                {
                    OverlayWebView.Source = embedUri;
                }
                OverlayWebView.NavigationCompleted += OverlayWebView_NavigationCompleted;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка инициализации веб-плеера: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        else
        {
            OverlayWebView.Visibility = Visibility.Collapsed;
            OverlayPlayer.Visibility = Visibility.Visible;
            OverlayPlayer.Source = request.Video.Source;
            OverlayPlayer.Position = TimeSpan.Zero;
            OverlayPlayer.Play();
            OverlayPlayer.Volume = 1.0;
        }

        BeginFadeIn();
    }

    private void OverlayWebView_NavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
         Dispatcher.BeginInvoke(new Action(() =>
         {
             UpdateLayout();
             PositionWindowRandomly();
         }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void BeginFadeIn()
    {
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
        fade.Completed += (_, _) => Topmost = false;
        BeginAnimation(OpacityProperty, fade);
    }

    private void OverlayPlayer_OnMediaOpened(object sender, RoutedEventArgs e)
    {
        if (_isWebVideo) return;

        if (OverlayPlayer.NaturalVideoWidth > 0 && OverlayPlayer.NaturalVideoHeight > 0)
        {
            AdjustVideoContainerSize();
        }

        Dispatcher.BeginInvoke(new Action(() =>
        {
            UpdateLayout();
            PositionWindowRandomly();
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void AdjustVideoContainerSize()
    {
        var aspectRatio = VideoAspectHelper.GetAspectRatio(
            OverlayPlayer.NaturalVideoWidth, 
            OverlayPlayer.NaturalVideoHeight);
        
        var isVertical = VideoAspectHelper.IsVertical(aspectRatio);
        
        if (isVertical)
        {
            var (width, height) = VideoAspectHelper.CalculateVerticalVideoSize(640, 720, aspectRatio);
            VideoContainer.Width = width;
            VideoContainer.Height = height;
            OverlayPlayer.Stretch = System.Windows.Media.Stretch.UniformToFill;
        }
        else
        {
            var (width, height) = VideoAspectHelper.CalculateHorizontalVideoSize(640, 360, aspectRatio);
            VideoContainer.Width = width;
            VideoContainer.Height = height;
            OverlayPlayer.Stretch = System.Windows.Media.Stretch.Uniform;
        }
    }

    private void PositionWindowRandomly()
    {
        var workingArea = SystemParameters.WorkArea;
        var width = ActualWidth > 100 ? ActualWidth : 640; 
        var height = ActualHeight > 100 ? ActualHeight : 360;

        var maxLeft = Math.Max(0, workingArea.Width - width);
        var maxTop = Math.Max(0, workingArea.Height - height);
        
        Left = workingArea.Left + Randomizer.NextDouble() * maxLeft;
        Top = workingArea.Top + Randomizer.NextDouble() * maxTop;
    }

    private async void OverlayPlayer_OnMediaEnded(object sender, RoutedEventArgs e)
    {
        if (OverlayPlayer.NaturalDuration.HasTimeSpan && 
            OverlayPlayer.Position >= OverlayPlayer.NaturalDuration.TimeSpan.Subtract(TimeSpan.FromMilliseconds(100)))
        {
            CleanupMediaElement();
            await Dispatcher.InvokeAsync(Close);
        }
    }

    private void OverlayPlayer_OnMediaFailed(object sender, ExceptionRoutedEventArgs e)
    {
        System.Diagnostics.Debug.WriteLine($"Media failed: {e.ErrorException?.Message}");
    }

    private void CleanupMediaElement()
    {
        BeginAnimation(OpacityProperty, null);
        
        if (OverlayPlayer != null)
        {
            try
            {
                OverlayPlayer.Stop();
                OverlayPlayer.Close();
                OverlayPlayer.Source = null;
            }
            catch { }
        }

        if (OverlayWebView != null)
        {
            try
            {
                OverlayWebView.Dispose();
            }
            catch { }
        }
    }

    private async void MuteButton_Click(object sender, RoutedEventArgs e)
    {
        if (OverlayVolumeSlider.Value > 0)
        {
            OverlayVolumeSlider.Tag = OverlayVolumeSlider.Value;
            OverlayVolumeSlider.Value = 0;
        }
        else
        {
            if (OverlayVolumeSlider.Tag is double savedVol)
            {
                OverlayVolumeSlider.Value = savedVol > 0 ? savedVol : 100;
            }
            else
            {
                OverlayVolumeSlider.Value = 100;
            }
        }
    }

    private async void VolumeSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        var vol = e.NewValue;

        if (MuteIcon != null)
        {
            if (vol == 0) MuteIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.VolumeMute;
            else if (vol < 50) MuteIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.VolumeMedium;
            else MuteIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.VolumeHigh;
        }

        if (_isWebVideo && OverlayWebView != null && OverlayWebView.CoreWebView2 != null)
        {
            try
            {
                await OverlayWebView.CoreWebView2.ExecuteScriptAsync($"setVolume({(int)vol});");
            }
            catch { }
        }
        else if (OverlayPlayer != null)
        {
            OverlayPlayer.Volume = vol / 100.0;
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e)
    {
        CleanupMediaElement();
        Close();
    }
}
