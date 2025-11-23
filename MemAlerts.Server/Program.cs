using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using global::MemAlerts.Shared.Models;
using MemAlerts.Server.Services;

namespace MemAlerts.Server;

class Program
{
    private static readonly List<ClientConnection> _clients = new();
    private static readonly object _clientsLock = new();
    private static TcpListener? _listener;
    private static CancellationTokenSource? _cts;
    private static readonly IAuthService _authService = new FileAuthService();

    static async Task Main(string[] args)
    {
        var port = 5050;
        
        // Попытка загрузить конфигурацию
        try 
        {
            if (File.Exists("config.json"))
            {
                var json = await File.ReadAllTextAsync("config.json");
                var config = JsonSerializer.Deserialize<AppConfig>(json);
                if (config != null)
                {
                    port = config.ServerPort;
                    Console.WriteLine($"Загружена конфигурация: порт {port}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка чтения config.json: {ex.Message}. Используется порт по умолчанию.");
        }

        if (args.Length > 0 && int.TryParse(args[0], out var customPort))
        {
            port = customPort;
        }

        Console.WriteLine($"Запуск сервера MemAlerts на порту {port}...");
        Console.WriteLine("Нажмите Ctrl+C для остановки");

        _cts = new CancellationTokenSource();
        _listener = new TcpListener(IPAddress.Any, port);
        _listener.Start();

        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            _cts.Cancel();
        };

        try
        {
            await AcceptClientsAsync(_cts.Token);
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("\nОстановка сервера...");
        }
        finally
        {
            Cleanup();
        }
    }

    private static async Task AcceptClientsAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                var client = await _listener!.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                var clientId = Guid.NewGuid().ToString("N")[..8];
                var connection = new ClientConnection(client, clientId);

                connection.MessageReceived += (sender, msg) => OnMessageReceived(sender as ClientConnection, msg);
                connection.RequestReceived += (sender, request) => OnRequestReceived(sender as ClientConnection, request);
                connection.Disconnected += (sender, _) => OnClientDisconnected(sender as ClientConnection);

                lock (_clientsLock)
                {
                    _clients.Add(connection);
                }

                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Клиент {clientId} подключился (ожидает авторизации). Всего клиентов: {_clients.Count}");
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Ошибка при принятии клиента: {ex.Message}");
            }
        }
    }

    private static async void OnMessageReceived(ClientConnection? sender, MessageBase message)
    {
        if (sender is null)
        {
            return;
        }

        switch (message)
        {
            case LoginRequest loginRequest:
                await HandleLoginAsync(sender, loginRequest);
                break;
            case RegisterRequest registerRequest:
                await HandleRegisterAsync(sender, registerRequest);
                break;
        }
    }

    private static async Task HandleLoginAsync(ClientConnection connection, LoginRequest request)
    {
        var result = await _authService.LoginAsync(request.Email, request.Password);
        var response = new AuthResponse
        {
            Success = result.Success,
            Token = result.Token,
            ErrorMessage = result.ErrorMessage,
            UserEmail = result.UserEmail
        };

        await connection.SendMessageAsync(response);

        if (result.Success && result.UserId is not null)
        {
            connection.SetAuthenticated(result.UserId, result.UserEmail ?? string.Empty);
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Клиент {connection.Id} авторизован как {result.UserEmail}");
        }
        else
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Ошибка авторизации клиента {connection.Id}: {result.ErrorMessage}");
        }
    }

    private static async Task HandleRegisterAsync(ClientConnection connection, RegisterRequest request)
    {
        var result = await _authService.RegisterAsync(request.Email, request.Password);
        var response = new AuthResponse
        {
            Success = result.Success,
            Token = result.Token,
            ErrorMessage = result.ErrorMessage,
            UserEmail = result.UserEmail
        };

        await connection.SendMessageAsync(response);

        if (result.Success && result.UserId is not null)
        {
            connection.SetAuthenticated(result.UserId, result.UserEmail ?? string.Empty);
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Клиент {connection.Id} зарегистрирован как {result.UserEmail}");
        }
        else
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Ошибка регистрации клиента {connection.Id}: {result.ErrorMessage}");
        }
    }

    private static void OnRequestReceived(ClientConnection? sender, AlertRequest request)
    {
        if (sender is null)
        {
            return;
        }

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Получен алерт от {sender.Id}: {request.ViewerName} - {request.Video.Title}");

        // Отправляем алерт всем остальным авторизованным клиентам
        List<ClientConnection> clientsToNotify;
        lock (_clientsLock)
        {
            clientsToNotify = _clients.Where(c => c != sender && c.IsConnected && c.IsAuthenticated).ToList();
        }

        var tasks = clientsToNotify.Select(async client =>
        {
            try
            {
                await client.SendRequestAsync(request, _cts?.Token ?? CancellationToken.None);
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Алерт отправлен клиенту {client.Id}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Ошибка отправки клиенту {client.Id}: {ex.Message}");
            }
        });

        _ = Task.Run(async () => await Task.WhenAll(tasks));
    }

    private static void OnClientDisconnected(ClientConnection? client)
    {
        if (client is null)
        {
            return;
        }

        lock (_clientsLock)
        {
            _clients.Remove(client);
        }

        Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Клиент {client.Id} отключился. Всего клиентов: {_clients.Count}");
        client.Dispose();
    }

    private static void Cleanup()
    {
        _listener?.Stop();

        lock (_clientsLock)
        {
            foreach (var client in _clients)
            {
                client.Dispose();
            }

            _clients.Clear();
        }

        _cts?.Dispose();
        Console.WriteLine("Сервер остановлен");
    }
}
