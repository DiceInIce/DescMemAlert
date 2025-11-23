using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Microsoft.Win32;
using MemAlerts.Client.ViewModels;
using MaterialDesignThemes.Wpf;

namespace MemAlerts.Client;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    private bool _isPreviewPaused;
    private bool _isPreviewMuted;
    private double _previousVolume = 70;

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

    private void PreviewPlayer_OnMediaOpened(object sender, RoutedEventArgs e)
    {
        _isPreviewPaused = false;
        PreviewPlayer.Position = TimeSpan.Zero;
        PreviewPlayer.Play();
        PlayPauseIcon.Kind = PackIconKind.Pause;
        PreviewPoster.Visibility = Visibility.Collapsed;
    }

    private void PreviewPlayPause_Click(object sender, RoutedEventArgs e)
    {
        if (PreviewPlayer.Source is null)
        {
            return;
        }

        if (_isPreviewPaused)
        {
            PreviewPlayer.Play();
            PlayPauseIcon.Kind = PackIconKind.Pause;
            PreviewPoster.Visibility = Visibility.Collapsed;
        }
        else
        {
            PreviewPlayer.Pause();
            PlayPauseIcon.Kind = PackIconKind.Play;
        }

        _isPreviewPaused = !_isPreviewPaused;
    }

    private void PreviewVolumeSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (PreviewPlayer is null || !IsLoaded)
        {
            return;
        }

        var newValue = e.NewValue;
        PreviewPlayer.Volume = newValue / 100.0;

        if (newValue > 0)
        {
            _previousVolume = newValue;
            _isPreviewMuted = false;
        }
        else
        {
            _isPreviewMuted = true;
        }

        UpdateMuteVisual();
    }

    private void PreviewMuteButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isPreviewMuted)
        {
            var targetVolume = _previousVolume > 0 ? _previousVolume : 50;
            PreviewVolumeSlider.Value = targetVolume;
        }
        else
        {
            _isPreviewMuted = true;
            _previousVolume = PreviewVolumeSlider.Value;
            PreviewVolumeSlider.Value = 0;
        }
    }

    private void UpdateMuteVisual()
    {
        if (MuteIcon is null)
        {
            return;
        }

        MuteIcon.Kind = _isPreviewMuted ? PackIconKind.VolumeOff : PackIconKind.VolumeHigh;
    }

    private void ListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        // Показываем постер при смене видео
        if (PreviewPoster != null)
        {
            PreviewPoster.Visibility = Visibility.Visible;
            
            // Сбрасываем состояние плеера
            _isPreviewPaused = false;
            if (PreviewPlayer != null)
            {
                PreviewPlayer.Stop();
                PreviewPlayer.Close();
            }
            
            if (PlayPauseIcon != null)
            {
                PlayPauseIcon.Kind = PackIconKind.Play;
            }
        }
    }

    private void ColorZone_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
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
        else
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
        Close();
    }
}
