using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace MemAlerts.Client.Services;

/// <summary>
/// Управляет боковым меню и его анимацией.
/// </summary>
public sealed class SideMenuController
{
    private readonly FrameworkElement? _overlay;
    private readonly FrameworkElement? _backdrop;
    private readonly FrameworkElement? _panel;
    private readonly TranslateTransform? _transform;
    private readonly TimeSpan _animationDuration;
    private readonly double _defaultWidth;

    private bool _isOpen;

    public SideMenuController(
        FrameworkElement? overlay,
        FrameworkElement? backdrop,
        FrameworkElement? panel,
        TranslateTransform? transform,
        TimeSpan animationDuration,
        double defaultWidth)
    {
        _overlay = overlay;
        _backdrop = backdrop;
        _panel = panel;
        _transform = transform;
        _animationDuration = animationDuration;
        _defaultWidth = defaultWidth;
    }

    public void Open()
    {
        if (_isOpen)
        {
            return;
        }

        _isOpen = true;

        if (_overlay != null)
        {
            _overlay.Visibility = Visibility.Visible;
        }

        if (_backdrop != null)
        {
            _backdrop.Visibility = Visibility.Visible;
            _backdrop.IsHitTestVisible = true;
        }

        Animate(opening: true);
    }

    public void Close()
    {
        if (!_isOpen)
        {
            return;
        }

        _isOpen = false;

        if (_backdrop != null)
        {
            _backdrop.Visibility = Visibility.Collapsed;
            _backdrop.IsHitTestVisible = false;
        }

        Animate(opening: false);
    }

    private void Animate(bool opening)
    {
        if (_transform == null || _panel == null)
        {
            return;
        }

        var panelWidth = _panel.ActualWidth;
        if (panelWidth <= 0)
        {
            panelWidth = _defaultWidth;
        }

        var from = opening ? -panelWidth : 0;
        var to = opening ? 0 : -panelWidth;

        if (opening)
        {
            _transform.X = from;
        }

        var animation = new DoubleAnimation
        {
            To = to,
            Duration = new Duration(_animationDuration),
            EasingFunction = new CubicEase
            {
                EasingMode = opening ? EasingMode.EaseOut : EasingMode.EaseIn
            }
        };

        if (!opening)
        {
            animation.Completed += (_, _) =>
            {
                if (_overlay != null)
                {
                    _overlay.Visibility = Visibility.Collapsed;
                }
                _transform.X = -panelWidth;
            };
        }

        _transform.BeginAnimation(TranslateTransform.XProperty, animation, HandoffBehavior.SnapshotAndReplace);
    }
}
