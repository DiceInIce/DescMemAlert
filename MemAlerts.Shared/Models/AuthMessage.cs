using System.Text.Json.Serialization;

namespace MemAlerts.Shared.Models;

[JsonDerivedType(typeof(LoginRequest), typeDiscriminator: "login")]
[JsonDerivedType(typeof(RegisterRequest), typeDiscriminator: "register")]
[JsonDerivedType(typeof(AuthResponse), typeDiscriminator: "auth_response")]
[JsonDerivedType(typeof(AlertRequestMessage), typeDiscriminator: "alert_request")]
[JsonDerivedType(typeof(SearchUsersRequest), typeDiscriminator: "search_users")]
[JsonDerivedType(typeof(SearchUsersResponse), typeDiscriminator: "search_users_response")]
[JsonDerivedType(typeof(SendFriendRequestMessage), typeDiscriminator: "send_friend_request")]
[JsonDerivedType(typeof(FriendRequestResponse), typeDiscriminator: "friend_request_response")]
[JsonDerivedType(typeof(GetFriendsRequest), typeDiscriminator: "get_friends")]
[JsonDerivedType(typeof(GetFriendsResponse), typeDiscriminator: "get_friends_response")]
[JsonDerivedType(typeof(AcceptFriendRequestMessage), typeDiscriminator: "accept_friend_request")]
[JsonDerivedType(typeof(RejectFriendRequestMessage), typeDiscriminator: "reject_friend_request")]
[JsonDerivedType(typeof(RemoveFriendRequestMessage), typeDiscriminator: "remove_friend_request")]
[JsonDerivedType(typeof(IncomingFriendRequestNotification), typeDiscriminator: "incoming_friend_request")]
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
    public required string Login { get; init; }
    public required string Email { get; init; }
    public required string Password { get; init; }
}

public sealed class AuthResponse : MessageBase
{
    public bool Success { get; init; }
    public string? Token { get; init; }
    public string? ErrorMessage { get; init; }
    public string? UserEmail { get; init; }
    public string? UserLogin { get; init; }
}

public sealed class AlertRequestMessage : MessageBase
{
    public required AlertRequest Request { get; init; }
}

