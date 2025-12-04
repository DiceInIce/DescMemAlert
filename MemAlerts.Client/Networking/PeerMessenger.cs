using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR.Client;
using global::MemAlerts.Shared.Models;

namespace MemAlerts.Client.Networking;

public sealed class PeerMessenger : IDisposable
{
    private const long MaxMessageSizeBytes = 1024L * 1024 * 200; // 200 MB payload support
    private HubConnection? _hubConnection;
    private string? _authToken;

    public event EventHandler<AlertRequest>? RequestReceived;
    public event EventHandler<bool>? ConnectionChanged;
    public event EventHandler<AuthResponse>? AuthResponseReceived;
    public event EventHandler<MessageBase>? MessageReceived;

    public bool IsConnected => _hubConnection?.State == HubConnectionState.Connected;
    public bool IsAuthenticated { get; private set; }
    public string? UserLogin { get; private set; }
    public string? UserEmail { get; private set; }
    public string? UserId { get; private set; }

    public PeerMessenger()
    {
    }

    public async Task ConnectAsync(string serverAddress, int serverPort, CancellationToken cancellationToken = default)
    {
        Disconnect();

        var url = $"http://{serverAddress}:{serverPort}/alerthub";
        _hubConnection = new HubConnectionBuilder()
            .WithUrl(url, options =>
            {
                options.Transports = HttpTransportType.WebSockets | HttpTransportType.LongPolling;
                options.TransportMaxBufferSize = MaxMessageSizeBytes;
                options.ApplicationMaxBufferSize = MaxMessageSizeBytes;
                options.CloseTimeout = TimeSpan.FromMinutes(2);
            })
            .WithAutomaticReconnect()
            .Build();

        _hubConnection.ServerTimeout = TimeSpan.FromMinutes(2);
        _hubConnection.KeepAliveInterval = TimeSpan.FromSeconds(15);

        _hubConnection.Closed += (ex) =>
        {
            UpdateConnectionState(false);
            return Task.CompletedTask;
        };

        _hubConnection.Reconnecting += (ex) =>
        {
            UpdateConnectionState(false);
            return Task.CompletedTask;
        };

        _hubConnection.Reconnected += async (connectionId) =>
        {
            UpdateConnectionState(true);
            // Try to re-login
            if (!string.IsNullOrEmpty(_authToken))
            {
                try 
                {
                    var response = await _hubConnection.InvokeAsync<AuthResponse>("LoginWithToken", _authToken);
                    HandleAuthResponse(response);
                }
                catch
                {
                    // Failed to re-auth
                }
            }
        };

        // Register handlers
        _hubConnection.On<AlertRequest>("ReceiveAlert", (request) =>
        {
            RequestReceived?.Invoke(this, request);
            // Also invoke generic message received if needed, but AlertRequest is wrapped in AlertRequestMessage usually
            // The client expects AlertRequestMessage? 
            // Looking at old code: 
            // if (message is AlertRequestMessage alertMessage) RequestReceived?.Invoke(this, alertMessage.Request);
            
            MessageReceived?.Invoke(this, new AlertRequestMessage { Request = request });
        });

        _hubConnection.On<IncomingFriendRequestNotification>("ReceiveFriendRequestNotification", (notification) =>
        {
            MessageReceived?.Invoke(this, notification);
        });

        _hubConnection.On<FriendshipChangedNotification>("ReceiveFriendshipChangedNotification", (notification) =>
        {
            MessageReceived?.Invoke(this, notification);
        });

        try
        {
            await _hubConnection.StartAsync(cancellationToken);
            UpdateConnectionState(true);
        }
        catch
        {
            UpdateConnectionState(false);
            throw;
        }
    }

    public async Task SendMessageAsync(MessageBase message, CancellationToken cancellationToken = default)
    {
        if (_hubConnection is null)
        {
            throw new InvalidOperationException("Нет активного соединения с сервером");
        }

        // Map messages to Hub methods
        try
        {
            switch (message)
            {
                case LoginRequest req:
                    {
                        var res = await _hubConnection.InvokeAsync<AuthResponse>("Login", req, cancellationToken);
                        HandleAuthResponse(res);
                        AuthResponseReceived?.Invoke(this, res); // Specific handler
                        MessageReceived?.Invoke(this, res); // Generic handler
                    }
                    break;

                case RegisterRequest req:
                    {
                        var res = await _hubConnection.InvokeAsync<AuthResponse>("Register", req, cancellationToken);
                        HandleAuthResponse(res);
                        AuthResponseReceived?.Invoke(this, res);
                        MessageReceived?.Invoke(this, res);
                    }
                    break;

                case AlertRequestMessage req:
                    // AlertRequestMessage contains AlertRequest
                    // Original: SendRequestAsync calls SendMessageAsync(AlertRequestMessage)
                    await _hubConnection.InvokeAsync("SendAlert", req.Request, cancellationToken);
                    // No response expected for Alert, but maybe confirmation? Old code didn't verify.
                    break;

                case SearchUsersRequest req:
                    {
                        var res = await _hubConnection.InvokeAsync<SearchUsersResponse>("SearchUsers", req, cancellationToken);
                        MessageReceived?.Invoke(this, res);
                    }
                    break;

                case SendFriendRequestMessage req:
                    {
                        var res = await _hubConnection.InvokeAsync<FriendRequestResponse>("SendFriendRequest", req, cancellationToken);
                        MessageReceived?.Invoke(this, res);
                    }
                    break;
                
                case GetFriendsRequest req:
                    {
                        var res = await _hubConnection.InvokeAsync<GetFriendsResponse>("GetFriends", cancellationToken); // Method has no args
                        MessageReceived?.Invoke(this, res);
                    }
                    break;

                case AcceptFriendRequestMessage req:
                    {
                        var res = await _hubConnection.InvokeAsync<FriendRequestResponse>("AcceptFriendRequest", req, cancellationToken);
                        MessageReceived?.Invoke(this, res);
                    }
                    break;

                case RejectFriendRequestMessage req:
                    {
                        var res = await _hubConnection.InvokeAsync<FriendRequestResponse>("RejectFriendRequest", req, cancellationToken);
                        MessageReceived?.Invoke(this, res);
                    }
                    break;

                case RemoveFriendRequestMessage req:
                    {
                        var res = await _hubConnection.InvokeAsync<FriendRequestResponse>("RemoveFriend", req, cancellationToken);
                        MessageReceived?.Invoke(this, res);
                    }
                    break;
                
                default:
                    throw new NotSupportedException($"Message type {message.GetType().Name} not supported in SignalR migration yet.");
            }
        }
        catch (Exception ex)
        {
            // Handle connection errors or invocation errors
            Console.WriteLine($"Error sending message: {ex.Message}");
            throw;
        }
    }

    public async Task LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        var request = new LoginRequest
        {
            Email = email,
            Password = password
        };

        await SendMessageAsync(request, cancellationToken);
    }

    public async Task RegisterAsync(string login, string email, string password, CancellationToken cancellationToken = default)
    {
        var request = new RegisterRequest
        {
            Login = login,
            Email = email,
            Password = password
        };

        await SendMessageAsync(request, cancellationToken);
    }

    public async Task SendRequestAsync(AlertRequest request, CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated)
        {
            throw new InvalidOperationException("Требуется авторизация");
        }

        var message = new AlertRequestMessage
        {
            Request = request
        };

        await SendMessageAsync(message, cancellationToken);
    }

    private void HandleAuthResponse(AuthResponse response)
    {
        if (response.Success)
        {
            IsAuthenticated = true;
            _authToken = response.Token;
            UserLogin = response.UserLogin;
            UserEmail = response.UserEmail;
            UserId = response.UserId;
        }
        else
        {
            // Don't clear everything on failure unless it was a specific login attempt?
            // If re-auth fails, maybe we should logout.
        }
    }

    public void Disconnect()
    {
        if (_hubConnection != null)
        {
            _hubConnection.StopAsync().GetAwaiter().GetResult();
            _hubConnection.DisposeAsync().GetAwaiter().GetResult();
            _hubConnection = null;
        }

        _authToken = null;
        IsAuthenticated = false;
        UserLogin = null;
        UserEmail = null;
        UserId = null;

        UpdateConnectionState(false);
    }

    private void UpdateConnectionState(bool isConnected)
    {
        ConnectionChanged?.Invoke(this, isConnected);
    }

    public void Dispose()
    {
        Disconnect();
    }
}
