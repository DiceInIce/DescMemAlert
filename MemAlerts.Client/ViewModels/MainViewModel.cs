using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using MemAlerts.Client.Alerts;
using MemAlerts.Client.Extensions;
using global::MemAlerts.Shared.Models;
using MemAlerts.Client.Networking;
using MemAlerts.Client.Services;
using System.Reflection;

namespace MemAlerts.Client.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IDisposable
{
    private readonly IMemAlertService _service;
    private readonly PeerMessenger _peerMessenger;
    private readonly AlertOverlayManager _overlayManager;
    private readonly LocalVideoService _localVideoService;
    private readonly VideoDownloaderService _videoDownloader;
    private readonly ConcurrentDictionary<string, Uri> _downloadedVideoCache = new();
    
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
        LocalVideoService localVideoService,
        VideoDownloaderService videoDownloader)
    {
        _service = service;
        _overlayManager = overlayManager;
        _peerMessenger = peerMessenger;
        _localVideoService = localVideoService;
        _videoDownloader = videoDownloader;
        
        _peerMessenger.RequestReceived += OnPeerRequestReceived;
        _peerMessenger.MessageReceived += OnPeerMessageReceived;
        _peerMessenger.ConnectionChanged += OnConnectionChanged;

        Catalog = new ReadOnlyObservableCollection<AlertVideo>(_catalogInternal);
        ActiveRequests = new ReadOnlyObservableCollection<HistoryItemViewModel>(_requestsInternal);
        Friends = new ReadOnlyObservableCollection<FriendInfo>(_friendsInternal);

        RefreshCatalogCommand = new AsyncRelayCommand(LoadCatalogAsync, () => !IsBusy, () => IsBusy, v => IsBusy = v);
        SubmitRequestCommand = new AsyncRelayCommand(SubmitRequestAsync, CanSubmit, () => IsBusy, v => IsBusy = v);
        RefreshRequestsCommand = new AsyncRelayCommand(LoadRequestsAsync, () => !IsBusy, () => IsBusy, v => IsBusy = v);
        EstablishConnectionCommand = new AsyncRelayCommand(EstablishConnectionAsync, () => !IsConnected);
        DisconnectCommand = new AsyncRelayCommand(DisconnectAsync, () => IsConnected);
        OpenFriendsWindowCommand = new AsyncRelayCommand(OpenFriendsWindowAsync, () => _peerMessenger.IsAuthenticated);
        DeleteVideoCommand = new AsyncRelayCommand<AlertVideo>(DeleteVideoAsync, CanDeleteVideo, () => IsBusy, v => IsBusy = v);
        OpenHistoryLinkCommand = new AsyncRelayCommand<HistoryItemViewModel>(OpenHistoryLinkAsync);
        ClearCacheCommand = new AsyncRelayCommand(ClearCacheAsync, () => !IsBusy, () => IsBusy, v => IsBusy = v);

        ApplicationVersion = $"Версия {Assembly.GetEntryAssembly()?.GetName().Version?.ToString(3) ?? "1.0.0"}";
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
    public AsyncRelayCommand ClearCacheCommand { get; }

    public string ApplicationVersion { get; }

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
                ClearCacheCommand.RaiseCanExecuteChanged();
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

    private Task RunBusyOperationAsync(
        string busyMessage,
        Func<Task> action,
        bool allowWhileBusy = false,
        Func<Exception, string>? errorMessageFactory = null)
    {
        if (IsBusy && !allowWhileBusy)
        {
            return Task.CompletedTask;
        }

        return ExecuteAsync();

        async Task ExecuteAsync()
        {
            var shouldToggleBusy = !allowWhileBusy || !IsBusy;

            if (shouldToggleBusy)
            {
                IsBusy = true;
                StatusMessage = busyMessage;
            }

            try
            {
                await action();
            }
            catch (Exception ex)
            {
                StatusMessage = errorMessageFactory?.Invoke(ex) ?? $"Ошибка: {ex.Message}";
            }
            finally
            {
                if (shouldToggleBusy)
                {
                    IsBusy = false;
                }
            }
        }
    }

    public void Dispose()
    {
        _peerMessenger.RequestReceived -= OnPeerRequestReceived;
        _peerMessenger.ConnectionChanged -= OnConnectionChanged;
        _peerMessenger.Dispose();
    }
}
