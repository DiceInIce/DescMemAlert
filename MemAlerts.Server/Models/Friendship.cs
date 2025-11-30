using System;
using global::MemAlerts.Shared.Models;

namespace MemAlerts.Server.Models;

public sealed class Friendship
{
    public required string Id { get; init; }
    public required string UserId1 { get; init; }
    public required string UserId2 { get; init; }
    public required string UserLogin1 { get; init; }
    public required string UserLogin2 { get; init; }
    public FriendshipStatus Status { get; set; }
    public string? RequesterId { get; init; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? AcceptedAt { get; init; }
}

