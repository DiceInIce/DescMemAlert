using System.Collections.Generic;
using System.Threading.Tasks;
using MemAlerts.Server.Models;
using global::MemAlerts.Shared.Models;

namespace MemAlerts.Server.Services;

public interface IFriendService
{
    Task<List<UserSearchResult>> SearchUsersAsync(string query, string currentUserId);
    Task<FriendOperationResult> SendFriendRequestAsync(string requesterId, string targetUserId);
    Task<List<FriendInfo>> GetFriendsAsync(string userId);
    Task<List<FriendInfo>> GetPendingRequestsAsync(string userId);
    Task<FriendOperationResult> AcceptFriendRequestAsync(string friendshipId, string userId);
    Task<FriendOperationResult> RejectFriendRequestAsync(string friendshipId, string userId);
    Task<FriendOperationResult> RemoveFriendAsync(string friendshipId, string userId);
    Task<FriendInfo?> GetFriendshipInfoAsync(string userId1, string userId2);
    bool AreFriends(string userId1, string userId2);
}

public sealed class UserSearchResult
{
    public required string UserId { get; init; }
    public required string Login { get; init; }
    public required string Email { get; init; }
    public bool IsAlreadyFriend { get; init; }
    public bool HasPendingRequest { get; init; }
}

public sealed class FriendOperationResult
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public FriendInfo? FriendInfo { get; init; }
}

