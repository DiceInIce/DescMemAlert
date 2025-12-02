using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MemAlerts.Client.ViewModels;
using MemAlerts.Client.Services;
using MaterialDesignThemes.Wpf;
using global::MemAlerts.Shared.Models;
using System.Threading.Tasks;

namespace MemAlerts.Client;

public partial class MainWindow : Window
{
    private static readonly TimeSpan MenuAnimationDuration = TimeSpan.FromMilliseconds(220);
    private const double DefaultMenuWidth = 320;

    private readonly PreviewController _previewController;
    private readonly SideMenuController _sideMenuController;
    private readonly SessionController _sessionController;
    private readonly DialogController _dialogController;

    public MainWindow(
        MainViewModel viewModel,
        WebVideoPlayerService webVideoPlayerService,
        SessionController sessionController,
        DialogController dialogController)
    {
        _sessionController = sessionController;
        _dialogController = dialogController;
        InitializeComponent();
        DataContext = viewModel;

        _previewController = new PreviewController(
            PreviewPlayer,
            PreviewWebView,
            PreviewPoster,
            PreviewVolumeSlider,
            PlayPauseIcon,
            MuteIcon,
            webVideoPlayerService);
        _previewController.InitializeDefaults();

        _sideMenuController = new SideMenuController(
            SideMenuOverlay,
            SideMenuBackdrop,
            SideMenuPanel,
            SideMenuTransform,
            MenuAnimationDuration,
            DefaultMenuWidth);
    }

    private void BrowseVideo_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var selection = _dialogController.PickLocalVideo(this);
        if (selection is { } result)
        {
            viewModel.LoadCustomVideo(result.FileUri, result.Title);
        }
    }

    private void AddUrlVideo_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        var url = _dialogController.PromptVideoUrl(this);
        if (string.IsNullOrWhiteSpace(url))
        {
            return;
        }

        var isTikTok = url.Contains("tiktok.com", StringComparison.OrdinalIgnoreCase);
        var isYouTubeShorts = VideoUrlHelper.IsYouTubeShorts(url);

        if (isTikTok || isYouTubeShorts)
        {
            if (!_dialogController.EnsureDownloaderAvailable(this))
            {
                return;
            }

            viewModel.DownloadAndAddVideo(url);
        }
        else
        {
            viewModel.LoadUrlVideo(url);
        }
    }

    private void PreviewPlayer_OnMediaEnded(object sender, RoutedEventArgs e)
    {
        _previewController?.HandleMediaEnded();
    }

    private void PreviewPlayer_OnMediaOpened(object sender, RoutedEventArgs e)
    {
        _previewController?.HandleMediaOpened();
    }

    private async void ListView_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (e.AddedItems.Count > 0 && e.AddedItems[0] is AlertVideo video)
        {
            await _previewController.ShowVideoAsync(video);
        }
        else
        {
            _previewController.Clear();
        }
    }

    private async void PreviewPlayPause_Click(object sender, RoutedEventArgs e)
    {
        await _previewController.TogglePlayPauseAsync();
    }

    private async void PreviewMuteButton_Click(object sender, RoutedEventArgs e)
    {
        await _previewController.ToggleMuteAsync();
    }

    private async void PreviewVolumeSlider_OnValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_previewController != null)
        {
            await _previewController.SetVolumeAsync(e.NewValue);
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

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _previewController.Dispose();
    }

    public void OpenFriendsWindow()
    {
        _dialogController.ShowFriendsWindow(this);
    }

    private void MenuButton_Click(object sender, RoutedEventArgs e) => OpenSideMenu();

    private void CloseMenuButton_Click(object sender, RoutedEventArgs e) => CloseSideMenu();

    private void MenuOverlayBackground_MouseDown(object sender, MouseButtonEventArgs e) => CloseSideMenu();

    private void OpenSideMenu() => _sideMenuController?.Open();

    private void CloseSideMenu() => _sideMenuController?.Close();

    private async void LogoutButton_Click(object sender, RoutedEventArgs e)
    {
        await LogoutAsync();
    }

    private async Task LogoutAsync()
    {
        if (DataContext is not MainViewModel viewModel)
        {
            return;
        }

        CloseSideMenu();

        var result = await _sessionController.ReloginAsync(this, viewModel);

        if (result.IsBusy)
        {
            return;
        }

        if (result.Success)
        {
            return;
        }

        if (!string.IsNullOrWhiteSpace(result.ErrorMessage))
        {
            MessageBox.Show(result.ErrorMessage, "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }

        Application.Current.Shutdown();
    }
}
