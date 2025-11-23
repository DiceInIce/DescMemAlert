using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using MemAlerts.Server.Models;

namespace MemAlerts.Server.Services;

public sealed class FileAuthService : IAuthService
{
    private readonly string _usersFilePath;
    private readonly Dictionary<string, User> _users = new();
    private readonly Dictionary<string, string> _tokens = new(); // token -> userId
    private readonly object _lock = new();

    public FileAuthService(string? dataDirectory = null)
    {
        dataDirectory ??= Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
        Directory.CreateDirectory(dataDirectory);
        _usersFilePath = Path.Combine(dataDirectory, "users.json");
        LoadUsers();
    }

    private void LoadUsers()
    {
        if (!File.Exists(_usersFilePath)) return;

        try
        {
            var json = File.ReadAllText(_usersFilePath);
            if (string.IsNullOrWhiteSpace(json)) return;

            var users = JsonSerializer.Deserialize<List<User>>(json);
            if (users is null) return;

            lock (_lock)
            {
                foreach (var user in users)
                {
                    _users[user.Id] = user;
                }
            }
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Загружено {_users.Count} пользователей");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка загрузки: {ex.Message}");
        }
    }

    private void SaveUsers()
    {
        try
        {
            List<User> users;
            lock (_lock)
            {
                users = _users.Values.ToList();
            }

            var json = JsonSerializer.Serialize(users, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_usersFilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Ошибка сохранения пользователей: {ex.Message}");
        }
    }

    public Task<AuthResult> RegisterAsync(string email, string password)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return Task.FromResult(new AuthResult
            {
                Success = false,
                ErrorMessage = "Email и пароль обязательны"
            });
        }

        if (password.Length < 6)
        {
            return Task.FromResult(new AuthResult
            {
                Success = false,
                ErrorMessage = "Пароль должен содержать минимум 6 символов"
            });
        }

        lock (_lock)
        {
            var normalizedEmail = email.ToLowerInvariant().Trim();
            if (_users.Values.Any(u => u.Email.Equals(normalizedEmail, StringComparison.OrdinalIgnoreCase)))
            {
                return Task.FromResult(new AuthResult
                {
                    Success = false,
                    ErrorMessage = "Пользователь с таким email уже существует"
                });
            }

            var userId = Guid.NewGuid().ToString("N");
            var passwordHash = HashPassword(password);
            var user = new User
            {
                Id = userId,
                Email = normalizedEmail,
                PasswordHash = passwordHash
            };

            _users[userId] = user;
            SaveUsers();

            var token = GenerateToken(userId);
            _tokens[token] = userId;

            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Зарегистрирован новый пользователь: {normalizedEmail}");

            return Task.FromResult(new AuthResult
            {
                Success = true,
                Token = token,
                UserId = userId,
                UserEmail = normalizedEmail
            });
        }
    }

    public Task<AuthResult> LoginAsync(string email, string password)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return Task.FromResult(new AuthResult
            {
                Success = false,
                ErrorMessage = "Email и пароль обязательны"
            });
        }

        lock (_lock)
        {
            var normalizedEmail = email.ToLowerInvariant().Trim();
            var user = _users.Values.FirstOrDefault(u => u.Email.Equals(normalizedEmail, StringComparison.OrdinalIgnoreCase));

            if (user is null)
            {
                return Task.FromResult(new AuthResult
                {
                    Success = false,
                    ErrorMessage = "Неверный email или пароль"
                });
            }

            var passwordHash = HashPassword(password);
            if (user.PasswordHash != passwordHash)
            {
                return Task.FromResult(new AuthResult
                {
                    Success = false,
                    ErrorMessage = "Неверный email или пароль"
                });
            }

            var token = GenerateToken(user.Id);
            _tokens[token] = user.Id;

            return Task.FromResult(new AuthResult
            {
                Success = true,
                Token = token,
                UserId = user.Id,
                UserEmail = user.Email
            });
        }
    }

    public bool ValidateToken(string token)
    {
        lock (_lock)
        {
            return _tokens.ContainsKey(token);
        }
    }

    public string? GetUserIdFromToken(string token)
    {
        lock (_lock)
        {
            return _tokens.TryGetValue(token, out var userId) ? userId : null;
        }
    }

    private static string HashPassword(string password)
    {
        using var sha256 = SHA256.Create();
        var bytes = Encoding.UTF8.GetBytes(password);
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }

    private static string GenerateToken(string userId)
    {
        var bytes = Encoding.UTF8.GetBytes($"{userId}:{Guid.NewGuid()}:{DateTime.UtcNow.Ticks}");
        using var sha256 = SHA256.Create();
        var hash = sha256.ComputeHash(bytes);
        return Convert.ToBase64String(hash);
    }
}

