using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using global::MemAlerts.Shared.Models;

namespace MemAlerts.Client.Alerts;

public partial class AlertOverlayWindow : Window
{
    private static readonly Random Randomizer = new();

    public AlertOverlayWindow(AlertRequest request)
    {
        InitializeComponent();
        DataContext = request;
        Loaded += (_, _) => InitializeWindow(request);
        Closing += (_, _) => CleanupMediaElement();
        MouseLeftButtonDown += (_, _) => DragMove();
    }

    private void InitializeWindow(AlertRequest request)
    {
        if (request.Video.Source is not null)
        {
            OverlayPlayer.Source = request.Video.Source;
        }

        OverlayPlayer.Position = TimeSpan.Zero;
        OverlayPlayer.Play();

        BeginFadeIn();
    }

    private void BeginFadeIn()
    {
        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(200));
        fade.Completed += (_, _) => Topmost = false;
        BeginAnimation(OpacityProperty, fade);
    }

    private void OverlayPlayer_OnMediaOpened(object sender, RoutedEventArgs e)
    {
        // Размещаем окно после того, как оно получило свои размеры
        Dispatcher.BeginInvoke(new Action(() =>
        {
            UpdateLayout();
            PositionWindowRandomly();
        }), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    private void PositionWindowRandomly()
    {
        var workingArea = SystemParameters.WorkArea;
        var maxLeft = Math.Max(0, workingArea.Width - ActualWidth);
        var maxTop = Math.Max(0, workingArea.Height - ActualHeight);
        
        Left = workingArea.Left + Randomizer.NextDouble() * maxLeft;
        Top = workingArea.Top + Randomizer.NextDouble() * maxTop;
    }

    private async void OverlayPlayer_OnMediaEnded(object sender, RoutedEventArgs e)
    {
        CleanupMediaElement();
        await Dispatcher.InvokeAsync(Close);
    }

    private void CleanupMediaElement()
    {
        // Останавливаем анимацию
        BeginAnimation(OpacityProperty, null);
        
        if (OverlayPlayer != null)
        {
            try
            {
                OverlayPlayer.Stop();
                OverlayPlayer.Close();
                OverlayPlayer.Source = null;
                OverlayPlayer.Width = double.NaN;
                OverlayPlayer.Height = double.NaN;
            }
            catch
            {
                // Игнорируем ошибки при очистке
            }
        }
    }
}

