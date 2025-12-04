namespace MemAlerts.Shared.Models;

public sealed class SearchUsersRequest : MessageBase
{
    public required string Query { get; init; }
}

public sealed class UserInfo
{
    public required string UserId { get; init; }
    public required string Login { get; init; }
    public required string Email { get; init; }
}

public sealed class SearchUsersResponse : MessageBase
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public required List<UserInfo> Users { get; init; }
}

public sealed class SendFriendRequestMessage : MessageBase
{
    public required string FriendUserId { get; init; }
}

public sealed class FriendRequestResponse : MessageBase
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
}

public sealed class GetFriendsRequest : MessageBase
{
}

public sealed class FriendInfo
{
    public required string FriendshipId { get; init; }
    public required string UserId { get; init; }
    public required string Login { get; init; }
    public required string Email { get; init; }
    public FriendshipStatus Status { get; init; }
    public bool IsIncomingRequest { get; init; }
}

public sealed class GetFriendsResponse : MessageBase
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public required List<FriendInfo> Friends { get; init; }
    public required List<FriendInfo> PendingRequests { get; init; }
}

public sealed class AcceptFriendRequestMessage : MessageBase
{
    public required string FriendshipId { get; init; }
}

public sealed class RejectFriendRequestMessage : MessageBase
{
    public required string FriendshipId { get; init; }
}

public sealed class IncomingFriendRequestNotification : MessageBase
{
    public required FriendInfo FriendRequest { get; init; }
}

public sealed class FriendshipChangedNotification : MessageBase
{
    public required string FriendshipId { get; init; }
    public FriendshipStatus Status { get; init; }
}

public sealed class RemoveFriendRequestMessage : MessageBase
{
    public required string FriendshipId { get; init; }
}

public enum FriendshipStatus
{
    Pending,
    Accepted,
    Rejected
}


