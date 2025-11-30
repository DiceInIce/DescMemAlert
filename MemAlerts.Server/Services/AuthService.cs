using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using MemAlerts.Server.Models;

namespace MemAlerts.Server.Services;

public sealed class AuthService : IAuthService
{
    private readonly Dictionary<string, User> _users = new();
    private readonly Dictionary<string, string> _tokens = new(); // token -> userId
    private readonly object _lock = new();

    public Task<AuthResult> RegisterAsync(string login, string email, string password)
    {
        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return Task.FromResult(new AuthResult
            {
                Success = false,
                ErrorMessage = "Логин, email и пароль обязательны"
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
            var normalizedLogin = login.Trim();
            var normalizedEmail = email.ToLowerInvariant().Trim();
            
            if (_users.Values.Any(u => u.Login.Equals(normalizedLogin, StringComparison.OrdinalIgnoreCase)))
            {
                return Task.FromResult(new AuthResult
                {
                    Success = false,
                    ErrorMessage = "Пользователь с таким логином уже существует"
                });
            }
            
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
                Login = normalizedLogin,
                Email = normalizedEmail,
                PasswordHash = passwordHash
            };

            _users[userId] = user;
            var token = GenerateToken(userId);
            _tokens[token] = userId;

            return Task.FromResult(new AuthResult
            {
                Success = true,
                Token = token,
                UserId = userId,
                UserEmail = normalizedEmail,
                UserLogin = normalizedLogin
            });
        }
    }

    public Task<AuthResult> LoginAsync(string emailOrLogin, string password)
    {
        if (string.IsNullOrWhiteSpace(emailOrLogin) || string.IsNullOrWhiteSpace(password))
        {
            return Task.FromResult(new AuthResult
            {
                Success = false,
                ErrorMessage = "Логин/email и пароль обязательны"
            });
        }

        lock (_lock)
        {
            var normalizedInput = emailOrLogin.Trim();
            // Ищем по логину или по email
            var user = _users.Values.FirstOrDefault(u => 
                u.Login.Equals(normalizedInput, StringComparison.OrdinalIgnoreCase) ||
                u.Email.Equals(normalizedInput.ToLowerInvariant(), StringComparison.OrdinalIgnoreCase));

            if (user is null)
            {
                return Task.FromResult(new AuthResult
                {
                    Success = false,
                    ErrorMessage = "Неверный логин/email или пароль"
                });
            }

            var passwordHash = HashPassword(password);
            if (user.PasswordHash != passwordHash)
            {
                return Task.FromResult(new AuthResult
                {
                    Success = false,
                    ErrorMessage = "Неверный логин/email или пароль"
                });
            }

            var token = GenerateToken(user.Id);
            _tokens[token] = user.Id;

            return Task.FromResult(new AuthResult
            {
                Success = true,
                Token = token,
                UserId = user.Id,
                UserEmail = user.Email,
                UserLogin = user.Login
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

    public User? GetUserById(string userId)
    {
        lock (_lock)
        {
            return _users.TryGetValue(userId, out var user) ? user : null;
        }
    }

    public List<User> GetAllUsers()
    {
        lock (_lock)
        {
            return _users.Values.ToList();
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

