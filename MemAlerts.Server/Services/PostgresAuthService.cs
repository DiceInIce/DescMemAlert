using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Dapper;
using MemAlerts.Server.Models;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace MemAlerts.Server.Services;

/// <summary>
/// PostgreSQL implementation of the authentication service using Dapper.
/// Keeps tokens in-memory similar to the previous file-based implementation.
/// </summary>
public sealed class PostgresAuthService : IAuthService
{
    private readonly string _connectionString;
    private readonly ILogger<PostgresAuthService> _logger;
    private readonly ConcurrentDictionary<string, string> _tokens = new(); // token -> userId

    public PostgresAuthService(string connectionString, ILogger<PostgresAuthService> logger)
    {
        _connectionString = string.IsNullOrWhiteSpace(connectionString)
            ? throw new ArgumentException("PostgreSQL connection string is required", nameof(connectionString))
            : connectionString;
        _logger = logger;
    }

    public async Task<AuthResult> RegisterAsync(string login, string email, string password)
    {
        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            return new AuthResult { Success = false, ErrorMessage = "Логин, email и пароль обязательны" };
        }

        if (password.Length < 6)
        {
            return new AuthResult { Success = false, ErrorMessage = "Пароль должен содержать минимум 6 символов" };
        }

        var normalizedLogin = login.Trim();
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var passwordHash = HashPassword(password);
        var userId = Guid.NewGuid().ToString("N");

        await using var conn = CreateConnection();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var exists = await conn.ExecuteScalarAsync<int?>(
            @"select 1 from users where lower(login) = lower(@login) or lower(email) = lower(@email) limit 1",
            new { login = normalizedLogin, email = normalizedEmail }, tx);

        if (exists.HasValue)
        {
            await tx.RollbackAsync();
            return new AuthResult { Success = false, ErrorMessage = "Пользователь с таким логином или email уже существует" };
        }

        const string insertSql = @"
insert into users (id, login, email, password_hash, created_at)
values (@Id, @Login, @Email, @PasswordHash, now())";

        await conn.ExecuteAsync(insertSql, new
        {
            Id = userId,
            Login = normalizedLogin,
            Email = normalizedEmail,
            PasswordHash = passwordHash
        }, tx);

        await tx.CommitAsync();

        var token = GenerateToken(userId);
        _tokens[token] = userId;

        _logger.LogInformation("Зарегистрирован новый пользователь: {Login} ({Email})", normalizedLogin, normalizedEmail);

        return new AuthResult
        {
            Success = true,
            Token = token,
            UserId = userId,
            UserEmail = normalizedEmail,
            UserLogin = normalizedLogin
        };
    }

    public async Task<AuthResult> LoginAsync(string emailOrLogin, string password)
    {
        if (string.IsNullOrWhiteSpace(emailOrLogin) || string.IsNullOrWhiteSpace(password))
        {
            return new AuthResult { Success = false, ErrorMessage = "Логин/email и пароль обязательны" };
        }

        var normalizedInput = emailOrLogin.Trim();
        var passwordHash = HashPassword(password);

        await using var conn = CreateConnection();

        var user = await conn.QuerySingleOrDefaultAsync<User>(
            @"select id, login, email, password_hash as PasswordHash, created_at as CreatedAt
              from users
              where lower(login) = lower(@input) or lower(email) = lower(@input)
              limit 1",
            new { input = normalizedInput });

        if (user is null || user.PasswordHash != passwordHash)
        {
            return new AuthResult { Success = false, ErrorMessage = "Неверный логин/email или пароль" };
        }

        var token = GenerateToken(user.Id);
        _tokens[token] = user.Id;

        _logger.LogDebug("Пользователь {Login} вошел в систему", user.Login);

        return new AuthResult
        {
            Success = true,
            Token = token,
            UserId = user.Id,
            UserEmail = user.Email,
            UserLogin = user.Login
        };
    }

    public bool ValidateToken(string token) => _tokens.ContainsKey(token);

    public string? GetUserIdFromToken(string token) =>
        _tokens.TryGetValue(token, out var userId) ? userId : null;

    public User? GetUserById(string userId)
    {
        using var conn = CreateConnection();
        return conn.QuerySingleOrDefault<User>(
            @"select id, login, email, password_hash as PasswordHash, created_at as CreatedAt
              from users
              where id = @userId",
            new { userId });
    }

    public List<User> GetAllUsers()
    {
        using var conn = CreateConnection();
        var users = conn.Query<User>(
            @"select id, login, email, password_hash as PasswordHash, created_at as CreatedAt from users");
        return users.ToList();
    }

    private NpgsqlConnection CreateConnection() => new(_connectionString);

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

