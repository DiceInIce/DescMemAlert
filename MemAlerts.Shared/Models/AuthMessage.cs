using System.Text.Json.Serialization;

namespace MemAlerts.Shared.Models;

[JsonDerivedType(typeof(LoginRequest), typeDiscriminator: "login")]
[JsonDerivedType(typeof(RegisterRequest), typeDiscriminator: "register")]
[JsonDerivedType(typeof(AuthResponse), typeDiscriminator: "auth_response")]
[JsonDerivedType(typeof(AlertRequestMessage), typeDiscriminator: "alert_request")]
public abstract class MessageBase
{
}

public sealed class LoginRequest : MessageBase
{
    public required string Email { get; init; }
    public required string Password { get; init; }
}

public sealed class RegisterRequest : MessageBase
{
    public required string Email { get; init; }
    public required string Password { get; init; }
}

public sealed class AuthResponse : MessageBase
{
    public bool Success { get; init; }
    public string? Token { get; init; }
    public string? ErrorMessage { get; init; }
    public string? UserEmail { get; init; }
}

public sealed class AlertRequestMessage : MessageBase
{
    public required AlertRequest Request { get; init; }
}

