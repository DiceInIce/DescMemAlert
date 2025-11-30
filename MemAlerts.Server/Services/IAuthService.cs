using System.Threading.Tasks;
using MemAlerts.Server.Models;

namespace MemAlerts.Server.Services;

public interface IAuthService
{
    Task<AuthResult> RegisterAsync(string login, string email, string password);
    Task<AuthResult> LoginAsync(string emailOrLogin, string password);
    bool ValidateToken(string token);
    string? GetUserIdFromToken(string token);
    User? GetUserById(string userId);
    List<User> GetAllUsers();
}

public sealed class AuthResult
{
    public bool Success { get; init; }
    public string? Token { get; init; }
    public string? ErrorMessage { get; init; }
    public string? UserId { get; init; }
    public string? UserEmail { get; init; }
    public string? UserLogin { get; init; }
}

