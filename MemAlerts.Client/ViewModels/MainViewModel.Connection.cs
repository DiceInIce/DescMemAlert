using System;
using System.Threading.Tasks;
using System.Windows;
using global::MemAlerts.Shared.Models;
using MemAlerts.Client.Extensions;

namespace MemAlerts.Client.ViewModels;

public sealed partial class MainViewModel
{
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

    private void OnPeerMessageReceived(object? sender, MessageBase message)
    {
        switch (message)
        {
            case GetFriendsResponse response:
                Application.Current.Dispatcher.Invoke(() =>
                {
                    _friendsInternal.ReplaceWith(response.Friends);

                    if (SelectedFriendUserId != null)
                    {
                        SelectedFriend = _friendsInternal.FirstOrDefault(f => f.UserId == SelectedFriendUserId);
                    }
                });
                break;
            case FriendRequestResponse friendResponse when friendResponse.Success:
                _ = RequestFriendsSnapshotAsync();
                break;
            case IncomingFriendRequestNotification:
                _ = RequestFriendsSnapshotAsync();
                break;
        }
    }

    private async Task RequestFriendsSnapshotAsync()
    {
        if (!_peerMessenger.IsAuthenticated)
        {
            return;
        }

        try
        {
            await _peerMessenger.SendMessageAsync(new GetFriendsRequest());
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Ошибка обновления друзей: {ex.Message}");
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
}
