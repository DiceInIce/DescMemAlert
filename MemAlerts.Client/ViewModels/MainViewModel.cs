using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using MemAlerts.Client.Alerts;
using global::MemAlerts.Shared.Models;
using MemAlerts.Client.Networking;
using MemAlerts.Client.Services;

namespace MemAlerts.Client.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly IMemAlertService _service;
    private readonly PeerMessenger _peerMessenger;
    private readonly AlertOverlayManager _overlayManager;
    private readonly LocalVideoService _localVideoService;
    
    private readonly ObservableCollection<AlertVideo> _catalogInternal = new();
    private readonly ObservableCollection<HistoryItemViewModel> _requestsInternal = new();
    private readonly ObservableCollection<FriendInfo> _friendsInternal = new();
    private List<AlertVideo> _allVideos = new();
    private string _searchText = string.Empty;
    private AlertVideo? _selectedVideo;
    private FriendInfo? _selectedFriend;
    private bool _isBusy;
    private string _customMessage = string.Empty;
    private decimal _tipAmount = 1;
    private string _viewerName = "MemeFan";
    private string _statusMessage = "Подключаемся к мемам...";
    private Uri? _previewSource;
    private string _serverAddress = "127.0.0.1";
    private int _serverPort = 5050;
    private bool _isConnected;
    private string _connectionStatus = "Нет подключения";
    private string? _selectedFriendUserId;
    private bool _isGlobalLibrary;

    public string? UserLogin => _peerMessenger.UserLogin;
    
    public string? SelectedFriendUserId
    {
        get => _selectedFriendUserId;
        set => SetProperty(ref _selectedFriendUserId, value);
    }

    public FriendInfo? SelectedFriend
    {
        get => _selectedFriend;
        set
        {
            if (SetProperty(ref _selectedFriend, value))
            {
                SelectedFriendUserId = value?.UserId;
                SubmitRequestCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public MainViewModel(
        IMemAlertService service, 
        AlertOverlayManager overlayManager, 
        PeerMessenger peerMessenger,
        LocalVideoService localVideoService)
    {
        _service = service;
        _overlayManager = overlayManager;
        _peerMessenger = peerMessenger;
        _localVideoService = localVideoService;
        
        _peerMessenger.RequestReceived += OnPeerRequestReceived;
        _peerMessenger.MessageReceived += OnPeerMessageReceived;
        _peerMessenger.ConnectionChanged += OnConnectionChanged;

        Catalog = new ReadOnlyObservableCollection<AlertVideo>(_catalogInternal);
        ActiveRequests = new ReadOnlyObservableCollection<HistoryItemViewModel>(_requestsInternal);
        Friends = new ReadOnlyObservableCollection<FriendInfo>(_friendsInternal);

        RefreshCatalogCommand = new AsyncRelayCommand(LoadCatalogAsync, () => !IsBusy);
        SubmitRequestCommand = new AsyncRelayCommand(SubmitRequestAsync, CanSubmit);
        RefreshRequestsCommand = new AsyncRelayCommand(LoadRequestsAsync, () => !IsBusy);
        EstablishConnectionCommand = new AsyncRelayCommand(EstablishConnectionAsync, () => !IsConnected);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => IsConnected);
        OpenFriendsWindowCommand = new AsyncRelayCommand(OpenFriendsWindowAsync, () => _peerMessenger.IsAuthenticated);
        DeleteVideoCommand = new AsyncRelayCommand<AlertVideo>(DeleteVideoAsync, CanDeleteVideo);
        OpenHistoryLinkCommand = new AsyncRelayCommand<HistoryItemViewModel>(OpenHistoryLinkAsync);
    }

    public ReadOnlyObservableCollection<AlertVideo> Catalog { get; }
    public ReadOnlyObservableCollection<HistoryItemViewModel> ActiveRequests { get; }
    public ReadOnlyObservableCollection<FriendInfo> Friends { get; }

    public AsyncRelayCommand RefreshCatalogCommand { get; }
    public AsyncRelayCommand SubmitRequestCommand { get; }
    public AsyncRelayCommand RefreshRequestsCommand { get; }
    public AsyncRelayCommand EstablishConnectionCommand { get; }
    public AsyncRelayCommand DisconnectCommand { get; }
    public AsyncRelayCommand OpenFriendsWindowCommand { get; }
    public AsyncRelayCommand<AlertVideo> DeleteVideoCommand { get; }
    public AsyncRelayCommand<HistoryItemViewModel> OpenHistoryLinkCommand { get; }

    public bool IsGlobalLibrary
    {
        get => _isGlobalLibrary;
        set
        {
            if (SetProperty(ref _isGlobalLibrary, value))
            {
                _ = LoadCatalogAsync();
            }
        }
    }

    public string SearchText
    {
        get => _searchText;
        set
        {
            if (SetProperty(ref _searchText, value))
            {
                ApplyFilters();
            }
        }
    }

    public AlertVideo? SelectedVideo
    {
        get => _selectedVideo;
        set
        {
            if (SetProperty(ref _selectedVideo, value))
            {
                PreviewSource = value?.Source;
                SubmitRequestCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsServerAddressEditable => !IsConnected;

    public string ServerAddress
    {
        get => _serverAddress;
        set => SetProperty(ref _serverAddress, value);
    }

    public int ServerPort
    {
        get => _serverPort;
        set => SetProperty(ref _serverPort, value);
    }

    public string ConnectionStatus
    {
        get => _connectionStatus;
        private set => SetProperty(ref _connectionStatus, value);
    }

    public bool IsConnected
    {
        get => _isConnected;
        private set
        {
            if (SetProperty(ref _isConnected, value))
            {
                SubmitRequestCommand.RaiseCanExecuteChanged();
                EstablishConnectionCommand.RaiseCanExecuteChanged();
                DisconnectCommand.RaiseCanExecuteChanged();
                RaisePropertyChanged(nameof(IsServerAddressEditable));
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RefreshCatalogCommand.RaiseCanExecuteChanged();
                RefreshRequestsCommand.RaiseCanExecuteChanged();
                SubmitRequestCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string CustomMessage
    {
        get => _customMessage;
        set => SetProperty(ref _customMessage, value);
    }

    public decimal TipAmount
    {
        get => _tipAmount;
        set
        {
            if (SetProperty(ref _tipAmount, value))
            {
                SubmitRequestCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ViewerName
    {
        get => _viewerName;
        set => SetProperty(ref _viewerName, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public Uri? PreviewSource
    {
        get => _previewSource;
        private set => SetProperty(ref _previewSource, value);
    }

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
        try
        {
            IsBusy = true;
            StatusMessage = "Загрузка видео... (это может занять время)";
            
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var userId = UserLogin ?? "default";
            var videosDir = Path.Combine(appData, "MemAlerts", "Users", userId, "Videos");
            
            var downloader = new VideoDownloaderService();
            var filePath = await Task.Run(() => downloader.DownloadVideoAsync(url, videosDir));
            
            var title = "Downloaded Video";
            try 
            {
                title = Path.GetFileNameWithoutExtension(filePath);
            } 
            catch {}

            var video = await _localVideoService.AddDownloadedVideoAsync(filePath, url, title);
            AddVideoToCatalog(video);
            StatusMessage = "Видео успешно скачано и добавлено";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка загрузки: {ex.Message}";
            MessageBox.Show($"Не удалось скачать видео: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            IsBusy = false;
        }
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

    public async Task InitializeAsync()
    {
        try
        {
            if (_peerMessenger.IsConnected && _peerMessenger.IsAuthenticated)
            {
                IsConnected = true;
                ConnectionStatus = $"Подключено как {_peerMessenger.UserLogin ?? _peerMessenger.UserEmail}";
                RaisePropertyChanged(nameof(UserLogin));
                
                var userId = _peerMessenger.UserLogin ?? "default";
                _localVideoService.Initialize(userId);
                
                await _peerMessenger.SendMessageAsync(new GetFriendsRequest());
            }
            else
            {
                 _localVideoService.Initialize("offline-user");
            }

            await LoadCatalogAsync();
            await LoadRequestsAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка инициализации: {ex.Message}");
            StatusMessage = "Ошибка загрузки данных";
        }
    }

    private async Task LoadCatalogAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = IsGlobalLibrary ? "Загружаем глобальный каталог..." : "Загружаем ваши видео...";

        try
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
                videos = await _localVideoService.GetVideosAsync();
                videos = videos.OrderByDescending(v => v.Id).ToList();
            }

            _allVideos = videos;
            ApplyFilters();

            if (!_catalogInternal.Any())
            {
                StatusMessage = IsGlobalLibrary ? "Глобальный каталог пуст" : "У вас пока нет видео";
            }
            else
            {
                StatusMessage = $"Загружено {_catalogInternal.Count} видео";
            }

            SelectedVideo ??= _catalogInternal.FirstOrDefault();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка каталога: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadRequestsAsync()
    {
        if (IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Загружаем историю...";

        try
        {
            var requests = await _service.GetActiveRequestsAsync();
            _requestsInternal.Clear();
            foreach (var request in requests)
            {
                bool isIncoming = request.ViewerName != UserLogin;
                _requestsInternal.Add(new HistoryItemViewModel(request, isIncoming));
            }

            StatusMessage = "История обновлена";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка истории: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

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
            
            var request = await _service.SubmitRequestAsync(
                SelectedVideo,
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

        _catalogInternal.Clear();
        foreach (var video in filtered)
        {
            _catalogInternal.Add(video);
        }

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

    private async Task EstablishConnectionAsync()
    {
        if (IsConnected)
        {
            return;
        }

        ConnectionStatus = "Подключаемся к серверу...";

        try
        {
            if (!_peerMessenger.IsConnected)
            {
                await _peerMessenger.ConnectAsync(ServerAddress, ServerPort);
            }

            if (_peerMessenger.IsAuthenticated)
            {
                await OnAuthenticationSuccess();
            }
            else
            {
                ConnectionStatus = "Требуется авторизация";
            }
        }
        catch (Exception ex)
        {
            ConnectionStatus = $"Ошибка подключения: {ex.Message}";
        }
    }

    private async Task OnAuthenticationSuccess()
    {
        ConnectionStatus = $"Подключено как {_peerMessenger.UserLogin ?? _peerMessenger.UserEmail}";
        IsConnected = true;
        
        if (!string.IsNullOrWhiteSpace(_peerMessenger.UserLogin))
        {
            ViewerName = _peerMessenger.UserLogin;
        }
        
        var userId = _peerMessenger.UserLogin ?? "default";
        _localVideoService.Initialize(userId);
        await LoadCatalogAsync();
    }

    private Task DisconnectAsync()
    {
        _peerMessenger.Disconnect();
        ConnectionStatus = "Соединение разорвано";
        return Task.CompletedTask;
    }

    private void OnPeerRequestReceived(object? sender, AlertRequest e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _requestsInternal.Insert(0, new HistoryItemViewModel(e, isIncoming: true));
            StatusMessage = $"Новая заявка от {e.ViewerName}";
            _overlayManager.ShowAlert(e);
        });
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

    private void OnPeerMessageReceived(object? sender, MessageBase message)
    {
        switch (message)
        {
            case GetFriendsResponse response:
                Application.Current.Dispatcher.Invoke(() =>
                {
                    _friendsInternal.Clear();
                    foreach (var friend in response.Friends)
                    {
                        _friendsInternal.Add(friend);
                    }
                    
                    if (SelectedFriendUserId != null)
                    {
                        SelectedFriend = _friendsInternal.FirstOrDefault(f => f.UserId == SelectedFriendUserId);
                    }
                });
                break;
        }
    }

    private void OnConnectionChanged(object? sender, bool connected)
    {
        Application.Current.Dispatcher.Invoke(async () =>
        {
            IsConnected = connected && _peerMessenger.IsAuthenticated;
            if (connected && _peerMessenger.IsAuthenticated)
            {
                await OnConnectionEstablished();
            }
            else if (connected)
            {
                ConnectionStatus = "Подключено, требуется авторизация";
            }
            else
            {
                ConnectionStatus = "Нет соединения с сервером";
            }
        });
    }

    private async Task OnConnectionEstablished()
    {
        ConnectionStatus = $"Подключено как {_peerMessenger.UserLogin ?? _peerMessenger.UserEmail}";
        var userId = _peerMessenger.UserLogin ?? "default";
        _localVideoService.Initialize(userId);
        await LoadCatalogAsync();

        try 
        {
            await _peerMessenger.SendMessageAsync(new GetFriendsRequest());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка загрузки друзей: {ex.Message}");
        }
    }

    private Task OpenFriendsWindowAsync()
    {
        if (Application.Current?.MainWindow is MainWindow mainWindow)
        {
            mainWindow.OpenFriendsWindow();
        }
        return Task.CompletedTask;
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

    public void Dispose()
    {
        _peerMessenger.RequestReceived -= OnPeerRequestReceived;
        _peerMessenger.ConnectionChanged -= OnConnectionChanged;
        _peerMessenger.Dispose();
    }
}
