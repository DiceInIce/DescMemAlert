using System;
using System.Buffers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using global::MemAlerts.Shared.Models;

namespace MemAlerts.Server;

public sealed class ClientConnection : IDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly JsonSerializerOptions _jsonOptions;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly CancellationTokenSource _cts = new();

    public string Id { get; }
    public bool IsConnected { get; private set; } = true;
    public bool IsAuthenticated { get; private set; }
    public string? UserId { get; private set; }
    public string? UserEmail { get; private set; }

    public event EventHandler<AlertRequest>? RequestReceived;
    public event EventHandler<MessageBase>? MessageReceived;
    public event EventHandler? Disconnected;

    public ClientConnection(TcpClient client, string id)
    {
        _client = client;
        _stream = client.GetStream();
        Id = id;

        _jsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            WriteIndented = false,
            PropertyNameCaseInsensitive = true
        };
        _jsonOptions.Converters.Add(new JsonStringEnumConverter());

        _ = Task.Run(() => ReceiveLoopAsync(_cts.Token));
    }

    public async Task SendMessageAsync(MessageBase message, CancellationToken cancellationToken = default)
    {
        if (!IsConnected || !_stream.CanWrite)
        {
            return;
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
        catch
        {
            Disconnect();
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public async Task SendRequestAsync(AlertRequest request, CancellationToken cancellationToken = default)
    {
        if (!IsAuthenticated)
        {
            return;
        }

        var message = new AlertRequestMessage
        {
            Request = request
        };

        await SendMessageAsync(message, cancellationToken);
    }

    public void SetAuthenticated(string userId, string userEmail)
    {
        IsAuthenticated = true;
        UserId = userId;
        UserEmail = userEmail;
    }

    private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
    {
        var lengthBuffer = ArrayPool<byte>.Shared.Rent(4);
        try
        {
            while (!cancellationToken.IsCancellationRequested && IsConnected && _stream.CanRead)
            {
                if (!await ReadExactAsync(_stream, lengthBuffer, 4, cancellationToken).ConfigureAwait(false))
                {
                    break;
                }

                var length = BitConverter.ToInt32(lengthBuffer, 0);
                if (length <= 0 || length > 10_000_000) // Максимум 10MB
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
                    
                    // Используем полиморфную десериализацию
                    var message = JsonSerializer.Deserialize<MessageBase>(jsonBytes, _jsonOptions);

                    if (message is not null)
                    {
                        MessageReceived?.Invoke(this, message);

                        if (message is AlertRequestMessage alertMessage)
                        {
                            RequestReceived?.Invoke(this, alertMessage.Request);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Ошибка десериализации от клиента {Id}: {ex.Message}");
                    // Не разрываем соединение при ошибке десериализации, но логируем
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(payload);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Ожидаемо при закрытии
        }
        catch
        {
            // Клиент отключился
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

    private void Disconnect()
    {
        if (!IsConnected)
        {
            return;
        }

        IsConnected = false;
        _cts.Cancel();
        Disconnected?.Invoke(this, EventArgs.Empty);
    }

    public void Dispose()
    {
        Disconnect();
        _sendLock.Dispose();
        _stream.Dispose();
        _client.Dispose();
        _cts.Dispose();
    }
}
