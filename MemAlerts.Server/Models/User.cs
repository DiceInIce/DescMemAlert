using System;

namespace MemAlerts.Server.Models;

public sealed class User
{
    public required string Id { get; init; }
    public required string Email { get; init; }
    public required string PasswordHash { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
}

