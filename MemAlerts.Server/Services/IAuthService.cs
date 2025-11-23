using System.Threading.Tasks;
using MemAlerts.Server.Models;

namespace MemAlerts.Server.Services;

public interface IAuthService
{
    Task<AuthResult> RegisterAsync(string email, string password);
    Task<AuthResult> LoginAsync(string email, string password);
    bool ValidateToken(string token);
    string? GetUserIdFromToken(string token);
}

public sealed class AuthResult
{
    public bool Success { get; init; }
    public string? Token { get; init; }
    public string? ErrorMessage { get; init; }
    public string? UserId { get; init; }
    public string? UserEmail { get; init; }
}

