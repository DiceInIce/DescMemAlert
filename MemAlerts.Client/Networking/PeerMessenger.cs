using System;
using System.Buffers;
using System.Net.Sockets;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using global::MemAlerts.Shared.Models;

namespace MemAlerts.Client.Networking;

public sealed class PeerMessenger : IDisposable
{
    private static readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private TcpClient? _client;
    private NetworkStream? _stream;
    private CancellationTokenSource? _cts;
    private string? _authToken;

    public event EventHandler<AlertRequest>? RequestReceived;
    public event EventHandler<bool>? ConnectionChanged;
    public event EventHandler<AuthResponse>? AuthResponseReceived;
    public event EventHandler<MessageBase>? MessageReceived;

    public bool IsConnected { get; private set; }
    public bool IsAuthenticated { get; private set; }
    public string? UserLogin { get; private set; }
    public string? UserEmail { get; private set; }

    public PeerMessenger()
    {
    }

    public async Task ConnectAsync(string serverAddress, int serverPort, CancellationToken cancellationToken = default)
    {
        Disconnect();

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        var client = new TcpClient();
        await client.ConnectAsync(serverAddress, serverPort, cancellationToken);
        AttachClient(client);
    }

    public async Task SendMessageAsync(MessageBase message, CancellationToken cancellationToken = default)
    {
        if (_stream is null)
        {
            throw new InvalidOperationException("Нет активного соединения с сервером");
        }

        var payload = JsonSerializer.SerializeToUtf8Bytes(message, _jsonOptions);
        var lengthPrefix = BitConverter.GetBytes(payload.Length);

        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _stream.WriteAsync(lengthPrefix.AsMemory(0, 4), cancellationToken).ConfigureAwait(false);
            await _stream.WriteAsync(payload.AsMemory(), cancellationToken).ConfigureAwait(false);
            await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
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
        }
        else
        {
            IsAuthenticated = false;
            _authToken = null;
            UserLogin = null;
            UserEmail = null;
        }
    }

    public void Disconnect()
    {
        try
        {
            _cts?.Cancel();
        }
        catch
        {
            // ignored
        }

        _stream?.Dispose();
        _client?.Dispose();
        _cts?.Dispose();

        _stream = null;
        _client = null;
        _cts = null;
        _authToken = null;
        IsAuthenticated = false;
        UserLogin = null;
        UserEmail = null;

        UpdateConnectionState(false);
    }

    private void AttachClient(TcpClient client)
    {
        _client?.Dispose();
        _stream?.Dispose();

        _client = client;
        _stream = client.GetStream();

        UpdateConnectionState(true);

        _ = Task.Run(() => ReceiveLoopAsync(_cts?.Token ?? CancellationToken.None), _cts?.Token ?? CancellationToken.None);
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        if (_stream is null)
        {
            return;
        }

        var lengthBuffer = ArrayPool<byte>.Shared.Rent(4);
        try
        {
            while (!cancellationToken.IsCancellationRequested && _stream.CanRead)
            {
                if (!await ReadExactAsync(_stream, lengthBuffer, 4, cancellationToken).ConfigureAwait(false))
                {
                    break;
                }

                var length = BitConverter.ToInt32(lengthBuffer, 0);
                if (length <= 0)
                {
                    continue;
                }

                var payload = ArrayPool<byte>.Shared.Rent(length);
                try
                {
                    if (!await ReadExactAsync(_stream, payload, length, cancellationToken).ConfigureAwait(false))
                    {
                        break;
                    }

                    var jsonBytes = payload.AsSpan(0, length).ToArray();
                    var message = JsonSerializer.Deserialize<MessageBase>(jsonBytes, _jsonOptions);
                    
                    if (message is not null)
                    {
                        // Отправляем общее событие для всех сообщений
                        MessageReceived?.Invoke(this, message);
                        
                        switch (message)
                        {
                            case AuthResponse authResponse:
                                HandleAuthResponse(authResponse);
                                AuthResponseReceived?.Invoke(this, authResponse);
                                break;
                            case AlertRequestMessage alertMessage:
                                RequestReceived?.Invoke(this, alertMessage.Request);
                                break;
                        }
                    }
                }
                catch (Exception)
                {
                    // Deserialization error - ignore message or log
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(payload);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // ignore
        }
        catch
        {
            // remote closed unexpectedly
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(lengthBuffer);
            Disconnect();
        }
    }

    private static async Task<bool> ReadExactAsync(NetworkStream stream, byte[] buffer, int length, CancellationToken token)
    {
        var totalRead = 0;
        while (totalRead < length)
        {
            var read = await stream.ReadAsync(buffer, totalRead, length - totalRead, token).ConfigureAwait(false);
            if (read == 0)
            {
                return false;
            }

            totalRead += read;
        }

        return true;
    }

    private void UpdateConnectionState(bool isConnected)
    {
        IsConnected = isConnected;
        ConnectionChanged?.Invoke(this, isConnected);
    }

    public void Dispose()
    {
        Disconnect();
        _sendLock.Dispose();
    }
}
