using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using MemAlerts.Client.Networking;
using global::MemAlerts.Shared.Models;

namespace MemAlerts.Client.ViewModels;

public sealed class FriendViewModel : ObservableObject, IDisposable
{
    private readonly PeerMessenger _messenger;
    private readonly ObservableCollection<FriendInfo> _friendsInternal = new();
    private readonly ObservableCollection<FriendInfo> _pendingRequestsInternal = new();
    private readonly ObservableCollection<UserInfo> _searchResultsInternal = new();
    private string _searchQuery = string.Empty;
    private bool _isBusy;
    private string _statusMessage = string.Empty;

    public event EventHandler<FriendInfo>? FriendRequestReceived;

    public FriendViewModel(PeerMessenger messenger)
    {
        _messenger = messenger;
        _messenger.MessageReceived += OnMessageReceived;

        Friends = new ReadOnlyObservableCollection<FriendInfo>(_friendsInternal);
        PendingRequests = new ReadOnlyObservableCollection<FriendInfo>(_pendingRequestsInternal);
        SearchResults = new ReadOnlyObservableCollection<UserInfo>(_searchResultsInternal);

        RefreshFriendsCommand = new AsyncRelayCommand(LoadFriendsAsync, () => !IsBusy);
        SearchUsersCommand = new AsyncRelayCommand(SearchUsersAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(SearchQuery));
        SendFriendRequestCommand = new AsyncRelayCommand<UserInfo>(SendFriendRequestAsync, _ => !IsBusy);
        AcceptFriendRequestCommand = new AsyncRelayCommand<FriendInfo>(AcceptFriendRequestAsync, _ => !IsBusy);
        RejectFriendRequestCommand = new AsyncRelayCommand<FriendInfo>(RejectFriendRequestAsync, _ => !IsBusy);
        RemoveFriendCommand = new AsyncRelayCommand<FriendInfo>(RemoveFriendAsync, _ => !IsBusy);
    }

    public ReadOnlyObservableCollection<FriendInfo> Friends { get; }
    public ReadOnlyObservableCollection<FriendInfo> PendingRequests { get; }
    public ReadOnlyObservableCollection<UserInfo> SearchResults { get; }

    public int FriendsCount => Friends.Count;
    public int PendingRequestsCount => PendingRequests.Count;

    public AsyncRelayCommand RefreshFriendsCommand { get; }
    public AsyncRelayCommand SearchUsersCommand { get; }
    public AsyncRelayCommand<UserInfo> SendFriendRequestCommand { get; }
    public AsyncRelayCommand<FriendInfo> AcceptFriendRequestCommand { get; }
    public AsyncRelayCommand<FriendInfo> RejectFriendRequestCommand { get; }
    public AsyncRelayCommand<FriendInfo> RemoveFriendCommand { get; }

    public string SearchQuery
    {
        get => _searchQuery;
        set
        {
            if (SetProperty(ref _searchQuery, value))
            {
                SearchUsersCommand.RaiseCanExecuteChanged();
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
                RefreshFriendsCommand.RaiseCanExecuteChanged();
                SearchUsersCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    public async Task InitializeAsync()
    {
        await LoadFriendsAsync();
    }

    private async Task LoadFriendsAsync()
    {
        if (!_messenger.IsAuthenticated || IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Загрузка друзей...";

        try
        {
            await _messenger.SendMessageAsync(new GetFriendsRequest());
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SearchUsersAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery) || IsBusy || !_messenger.IsAuthenticated)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = "Поиск...";

        try
        {
            await _messenger.SendMessageAsync(new SearchUsersRequest { Query = SearchQuery });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка поиска: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task SendFriendRequestAsync(UserInfo userInfo)
    {
        if (!_messenger.IsAuthenticated || IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = $"Отправка запроса {userInfo.Login}...";

        try
        {
            await _messenger.SendMessageAsync(new SendFriendRequestMessage { FriendUserId = userInfo.UserId });
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task AcceptFriendRequestAsync(FriendInfo friendInfo)
    {
        if (!_messenger.IsAuthenticated || IsBusy)
        {
            return;
        }

        IsBusy = true;
        StatusMessage = $"Принятие запроса от {friendInfo.Login}...";

        try
        {
            await _messenger.SendMessageAsync(new AcceptFriendRequestMessage { FriendshipId = friendInfo.FriendshipId });
            await LoadFriendsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RejectFriendRequestAsync(FriendInfo friendInfo)
    {
        if (!_messenger.IsAuthenticated || IsBusy)
        {
            return;
        }

        IsBusy = true;

        try
        {
            await _messenger.SendMessageAsync(new RejectFriendRequestMessage { FriendshipId = friendInfo.FriendshipId });
            await LoadFriendsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RemoveFriendAsync(FriendInfo friendInfo)
    {
        if (!_messenger.IsAuthenticated || IsBusy)
        {
            return;
        }

        var result = System.Windows.MessageBox.Show($"Вы уверены, что хотите удалить {friendInfo.Login} из друзей?", "Подтверждение", System.Windows.MessageBoxButton.YesNo, System.Windows.MessageBoxImage.Question);
        if (result != System.Windows.MessageBoxResult.Yes)
        {
            return;
        }

        IsBusy = true;

        try
        {
            await _messenger.SendMessageAsync(new RemoveFriendRequestMessage { FriendshipId = friendInfo.FriendshipId });
            await LoadFriendsAsync();
        }
        catch (Exception ex)
        {
            StatusMessage = $"Ошибка: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnMessageReceived(object? sender, MessageBase message)
    {
        switch (message)
        {
            case GetFriendsResponse response:
                UpdateFriends(response);
                break;
            case SearchUsersResponse response:
                UpdateSearchResults(response);
                break;
            case FriendRequestResponse response:
                HandleFriendRequestResponse(response);
                break;
            case IncomingFriendRequestNotification notification:
                HandleIncomingFriendRequest(notification);
                break;
        }
    }

    private void UpdateFriends(GetFriendsResponse response)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            _friendsInternal.Clear();
            foreach (var friend in response.Friends)
            {
                _friendsInternal.Add(friend);
            }

            _pendingRequestsInternal.Clear();
            foreach (var request in response.PendingRequests)
            {
                _pendingRequestsInternal.Add(request);
            }

            StatusMessage = $"Друзей: {response.Friends.Count}, Запросов: {response.PendingRequests.Count}";
            RaisePropertyChanged(nameof(FriendsCount));
            RaisePropertyChanged(nameof(PendingRequestsCount));
        });
    }

    private void UpdateSearchResults(SearchUsersResponse response)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            _searchResultsInternal.Clear();
            if (response.Success)
            {
                foreach (var user in response.Users)
                {
                    _searchResultsInternal.Add(user);
                }
                StatusMessage = $"Найдено: {response.Users.Count}";
            }
            else
            {
                StatusMessage = response.ErrorMessage ?? "Ошибка поиска";
            }
        });
    }

    private void HandleFriendRequestResponse(FriendRequestResponse response)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (response.Success)
            {
                StatusMessage = "Успешно!";
                _ = LoadFriendsAsync();
            }
            else
            {
                StatusMessage = response.ErrorMessage ?? "Ошибка";
            }
        });
    }

    private void HandleIncomingFriendRequest(IncomingFriendRequestNotification notification)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            var request = notification.FriendRequest;
            _pendingRequestsInternal.Add(request);
            FriendRequestReceived?.Invoke(this, request);
            StatusMessage = $"Новый запрос от {request.Login}";
        });
    }

    public void Dispose()
    {
        if (_messenger != null)
        {
            _messenger.MessageReceived -= OnMessageReceived;
        }
    }
}

