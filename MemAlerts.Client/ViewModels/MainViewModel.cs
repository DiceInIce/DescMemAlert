using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
    private readonly ObservableCollection<AlertVideo> _catalogInternal = new();
    private readonly ObservableCollection<AlertRequest> _requestsInternal = new();
    private List<AlertVideo> _allVideos = new();
    private string _searchText = string.Empty;
    private AlertVideo? _selectedVideo;
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

    public string? UserEmail => _peerMessenger.UserEmail;

    public MainViewModel(IMemAlertService service, AlertOverlayManager overlayManager, PeerMessenger peerMessenger)
    {
        _service = service;
        _overlayManager = overlayManager;
        _peerMessenger = peerMessenger;
        _peerMessenger.RequestReceived += OnPeerRequestReceived;
        _peerMessenger.ConnectionChanged += OnConnectionChanged;

        Catalog = new ReadOnlyObservableCollection<AlertVideo>(_catalogInternal);
        ActiveRequests = new ReadOnlyObservableCollection<AlertRequest>(_requestsInternal);

        RefreshCatalogCommand = new AsyncRelayCommand(LoadCatalogAsync, () => !IsBusy);
        SubmitRequestCommand = new AsyncRelayCommand(SubmitRequestAsync, CanSubmit);
        RefreshRequestsCommand = new AsyncRelayCommand(LoadRequestsAsync, () => !IsBusy);
        EstablishConnectionCommand = new AsyncRelayCommand(EstablishConnectionAsync, () => !IsConnected);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => IsConnected);
    }

    public ReadOnlyObservableCollection<AlertVideo> Catalog { get; }
    public ReadOnlyObservableCollection<AlertRequest> ActiveRequests { get; }

    public AsyncRelayCommand RefreshCatalogCommand { get; }
    public AsyncRelayCommand SubmitRequestCommand { get; }
    public AsyncRelayCommand RefreshRequestsCommand { get; }
    public AsyncRelayCommand EstablishConnectionCommand { get; }
    public AsyncRelayCommand DisconnectCommand { get; }

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
        var thumbnail = await ThumbnailGenerator.GenerateThumbnailAsync(fileUri.LocalPath);

        var customVideo = new AlertVideo
        {
            Id = $"custom-{Guid.NewGuid():N}",
            Title = title,
            Description = "Пользовательский клип",
            Category = "Custom",
            Duration = TimeSpan.FromSeconds(6),
            Source = fileUri,
            Thumbnail = thumbnail,
            IsCustom = true
        };

        InsertOrUpdateCatalog(customVideo);
        SelectedVideo = customVideo;
    }

    public async Task InitializeAsync()
    {
        try
        {
            await LoadCatalogAsync();
            await LoadRequestsAsync();
            
            if (_peerMessenger.IsConnected && _peerMessenger.IsAuthenticated)
            {
                IsConnected = true;
                ConnectionStatus = $"Подключено как {_peerMessenger.UserEmail}";
                RaisePropertyChanged(nameof(UserEmail));
            }
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
        StatusMessage = "Обновляем каталог...";

        try
        {
            var catalog = await _service.GetCatalogAsync();
            _allVideos = catalog
                .OrderByDescending(v => v.IsCommunityFavorite)
                .ThenBy(v => v.Title)
                .ToList();
            ApplyFilters();

            if (!_catalogInternal.Any())
            {
                StatusMessage = "Каталог пуст 👀";
            }
            else
            {
                StatusMessage = $"В каталоге {_catalogInternal.Count} клипов";
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
        StatusMessage = "Загружаем очередь...";

        try
        {
            var requests = await _service.GetActiveRequestsAsync();
            _requestsInternal.Clear();
            foreach (var request in requests)
            {
                _requestsInternal.Add(request);
            }

            StatusMessage = "Очередь обновлена";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка очереди: {ex.Message}";
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

        IsBusy = true;
        StatusMessage = "Отправляем запрос...";

        try
        {
            var request = await _service.SubmitRequestAsync(
                SelectedVideo,
                ViewerName,
                CustomMessage,
                TipAmount);

            _requestsInternal.Insert(0, request);
            StatusMessage = "Запрос доставлен ✉️";
            CustomMessage = string.Empty;

            if (IsConnected)
            {
                await _peerMessenger.SendRequestAsync(request);
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
            : _allVideos
                .Where(v => v.Title.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                            || v.Description.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
                            || v.Category.Contains(SearchText, StringComparison.OrdinalIgnoreCase))
                .ToList();

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
                ConnectionStatus = $"Подключено как {_peerMessenger.UserEmail}";
                IsConnected = true;
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
            InsertOrUpdateCatalog(e.Video);
            _requestsInternal.Insert(0, e);
            StatusMessage = $"Новая заявка от {e.ViewerName}";
            _overlayManager.ShowAlert(e);
        });
    }

    private void OnConnectionChanged(object? sender, bool connected)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            IsConnected = connected && _peerMessenger.IsAuthenticated;
            if (connected && _peerMessenger.IsAuthenticated)
            {
                ConnectionStatus = $"Подключено как {_peerMessenger.UserEmail}";
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

    public void Dispose()
    {
        _peerMessenger.RequestReceived -= OnPeerRequestReceived;
        _peerMessenger.ConnectionChanged -= OnConnectionChanged;
        _peerMessenger.Dispose();
    }
}
