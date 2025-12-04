using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using MemAlerts.Client.Extensions;
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

        RefreshFriendsCommand = new AsyncRelayCommand(() => LoadFriendsAsync(), () => !IsBusy, () => IsBusy);
        SearchUsersCommand = new AsyncRelayCommand(SearchUsersAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(SearchQuery), () => IsBusy);
        SendFriendRequestCommand = new AsyncRelayCommand<UserInfo>(SendFriendRequestAsync, _ => !IsBusy, () => IsBusy);
        AcceptFriendRequestCommand = new AsyncRelayCommand<FriendInfo>(AcceptFriendRequestAsync, _ => !IsBusy, () => IsBusy);
        RejectFriendRequestCommand = new AsyncRelayCommand<FriendInfo>(RejectFriendRequestAsync, _ => !IsBusy, () => IsBusy);
        RemoveFriendCommand = new AsyncRelayCommand<FriendInfo>(RemoveFriendAsync, _ => !IsBusy, () => IsBusy);
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

    private Task LoadFriendsAsync(bool ignoreBusy = false) =>
        RunSafeAsync(
            "Загрузка друзей...",
            () => _messenger.SendMessageAsync(new GetFriendsRequest()),
            allowWhileBusy: ignoreBusy);

    private Task SearchUsersAsync()
    {
        if (string.IsNullOrWhiteSpace(SearchQuery))
        {
            return Task.CompletedTask;
        }

        return RunSafeAsync(
            "Поиск...",
            () => _messenger.SendMessageAsync(new SearchUsersRequest { Query = SearchQuery }),
            errorMessageFactory: ex => $"Ошибка поиска: {ex.Message}");
    }

    private Task SendFriendRequestAsync(UserInfo userInfo)
    {
        ArgumentNullException.ThrowIfNull(userInfo);

        return RunSafeAsync(
            $"Отправка запроса {userInfo.Login}...",
            () => _messenger.SendMessageAsync(new SendFriendRequestMessage { FriendUserId = userInfo.UserId }));
    }

    private Task AcceptFriendRequestAsync(FriendInfo friendInfo)
    {
        ArgumentNullException.ThrowIfNull(friendInfo);

        return RunSafeAsync(
            $"Принятие запроса от {friendInfo.Login}...",
            async () =>
            {
                await _messenger.SendMessageAsync(new AcceptFriendRequestMessage { FriendshipId = friendInfo.FriendshipId });
                await LoadFriendsAsync(ignoreBusy: true);
                StatusMessage = $"Теперь вы друзья с {friendInfo.Login}";
            });
    }

    private Task RejectFriendRequestAsync(FriendInfo friendInfo)
    {
        ArgumentNullException.ThrowIfNull(friendInfo);

        return RunSafeAsync(
            $"Отклоняем запрос {friendInfo.Login}...",
            async () =>
            {
                await _messenger.SendMessageAsync(new RejectFriendRequestMessage { FriendshipId = friendInfo.FriendshipId });
                await LoadFriendsAsync(ignoreBusy: true);
                StatusMessage = $"Запрос {friendInfo.Login} отклонён";
            });
    }

    private Task RemoveFriendAsync(FriendInfo friendInfo)
    {
        ArgumentNullException.ThrowIfNull(friendInfo);

        var confirmation = System.Windows.MessageBox.Show(
            $"Вы уверены, что хотите удалить {friendInfo.Login} из друзей?",
            "Подтверждение",
            System.Windows.MessageBoxButton.YesNo,
            System.Windows.MessageBoxImage.Question);

        if (confirmation != System.Windows.MessageBoxResult.Yes)
        {
            return Task.CompletedTask;
        }

        return RunSafeAsync(
            $"Удаляем {friendInfo.Login}...",
            async () =>
            {
                await _messenger.SendMessageAsync(new RemoveFriendRequestMessage { FriendshipId = friendInfo.FriendshipId });
                await LoadFriendsAsync(ignoreBusy: true);
                StatusMessage = $"{friendInfo.Login} удалён из друзей";
            });
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
            case FriendshipChangedNotification notification:
                HandleFriendshipChanged(notification);
                break;
        }
    }

    private void UpdateFriends(GetFriendsResponse response)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            _friendsInternal.ReplaceWith(response.Friends);
            _pendingRequestsInternal.ReplaceWith(response.PendingRequests);

            StatusMessage = $"Друзей: {response.Friends.Count}, Запросов: {response.PendingRequests.Count}";
            RaisePropertyChanged(nameof(FriendsCount));
            RaisePropertyChanged(nameof(PendingRequestsCount));
        });
    }

    private void UpdateSearchResults(SearchUsersResponse response)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            if (response.Success)
            {
                _searchResultsInternal.ReplaceWith(response.Users);
                StatusMessage = $"Найдено: {response.Users.Count}";
            }
            else
            {
                _searchResultsInternal.Clear();
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
                _ = LoadFriendsAsync(ignoreBusy: true);
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
            _ = LoadFriendsAsync(ignoreBusy: true);
            FriendRequestReceived?.Invoke(this, request);
            StatusMessage = $"Новый запрос от {request.Login}";
        });
    }

    private void HandleFriendshipChanged(FriendshipChangedNotification notification)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            _ = LoadFriendsAsync(ignoreBusy: true);
            StatusMessage = notification.Status switch
            {
                FriendshipStatus.Accepted => "Новый друг добавлен",
                FriendshipStatus.Rejected => "Запрос отклонён",
                _ => "Список друзей обновлён"
            };
        });
    }

    private Task RunSafeAsync(
        string? busyMessage,
        Func<Task> action,
        bool allowWhileBusy = false,
        Func<Exception, string>? errorMessageFactory = null)
    {
        if (!_messenger.IsAuthenticated)
        {
            return Task.CompletedTask;
        }

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
                if (!string.IsNullOrWhiteSpace(busyMessage))
                {
                    StatusMessage = busyMessage;
                }
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
        if (_messenger != null)
        {
            _messenger.MessageReceived -= OnMessageReceived;
        }
    }
}

