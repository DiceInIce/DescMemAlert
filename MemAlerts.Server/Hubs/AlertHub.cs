using Microsoft.AspNetCore.SignalR;
using MemAlerts.Server.Services;
using MemAlerts.Shared.Models;
using Microsoft.Extensions.Logging;

namespace MemAlerts.Server.Hubs;

public interface IAlertClient
{
    Task ReceiveAlert(AlertRequest request);
    Task ReceiveFriendRequestNotification(IncomingFriendRequestNotification notification);
}

public class AlertHub : Hub<IAlertClient>
{
    private readonly IAuthService _authService;
    private readonly IFriendService _friendService;
    private readonly ILogger<AlertHub> _logger;

    public AlertHub(IAuthService authService, IFriendService friendService, ILogger<AlertHub> logger)
    {
        _authService = authService;
        _friendService = friendService;
        _logger = logger;
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        _logger.LogInformation("Client disconnected: {ConnectionId}", Context.ConnectionId);
        return base.OnDisconnectedAsync(exception);
    }

    public async Task<AuthResponse> Login(LoginRequest request)
    {
        var result = await _authService.LoginAsync(request.Email, request.Password);
        if (result.Success && result.UserId != null)
        {
            Context.Items["UserId"] = result.UserId;
            Context.Items["UserLogin"] = result.UserLogin;
            Context.Items["UserEmail"] = result.UserEmail;
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{result.UserId}");
            _logger.LogInformation("Client {ConnectionId} logged in as {UserLogin}", Context.ConnectionId, result.UserLogin);
        }

        return new AuthResponse
        {
            Success = result.Success,
            Token = result.Token,
            ErrorMessage = result.ErrorMessage,
            UserEmail = result.UserEmail,
            UserLogin = result.UserLogin,
            UserId = result.UserId
        };
    }

    public async Task<AuthResponse> LoginWithToken(string token)
    {
        if (_authService.ValidateToken(token))
        {
             var userId = _authService.GetUserIdFromToken(token);
             if (userId != null)
             {
                 var user = _authService.GetUserById(userId);
                 if (user != null)
                 {
                     Context.Items["UserId"] = user.Id;
                     Context.Items["UserLogin"] = user.Login;
                     Context.Items["UserEmail"] = user.Email;
                     await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{user.Id}");
                     _logger.LogInformation("Client {ConnectionId} re-authenticated with token as {UserLogin}", Context.ConnectionId, user.Login);
                     
                     return new AuthResponse 
                     { 
                        Success = true, 
                        Token = token, 
                        UserId = user.Id, 
                        UserLogin = user.Login, 
                        UserEmail = user.Email 
                     };
                 }
             }
        }
        return new AuthResponse { Success = false, ErrorMessage = "Invalid token" };
    }

    public async Task<AuthResponse> Register(RegisterRequest request)
    {
        var result = await _authService.RegisterAsync(request.Login, request.Email, request.Password);
        if (result.Success && result.UserId != null)
        {
            Context.Items["UserId"] = result.UserId;
            Context.Items["UserLogin"] = result.UserLogin;
            Context.Items["UserEmail"] = result.UserEmail;
            await Groups.AddToGroupAsync(Context.ConnectionId, $"user_{result.UserId}");
            _logger.LogInformation("Client {ConnectionId} registered as {UserLogin}", Context.ConnectionId, result.UserLogin);
        }

        return new AuthResponse
        {
            Success = result.Success,
            Token = result.Token,
            ErrorMessage = result.ErrorMessage,
            UserEmail = result.UserEmail,
            UserLogin = result.UserLogin,
            UserId = result.UserId
        };
    }

    public async Task SendAlert(AlertRequest request)
    {
        var senderUserId = GetCurrentUserId();
        if (senderUserId == null) return;

        _logger.LogInformation("Alert received from {SenderId}: {ViewerName} - {VideoTitle}", senderUserId, request.ViewerName, request.Video.Title);

        if (!string.IsNullOrEmpty(request.RecipientUserId))
        {
            if (_friendService.AreFriends(senderUserId, request.RecipientUserId))
            {
                await Clients.Group($"user_{request.RecipientUserId}").ReceiveAlert(request);
            }
            else
            {
                _logger.LogWarning("Attempt to send alert to non-friend. Sender: {SenderId}, Recipient: {RecipientId}", senderUserId, request.RecipientUserId);
            }
        }
        else
        {
            await Clients.Others.ReceiveAlert(request);
        }
    }

    public async Task<SearchUsersResponse> SearchUsers(SearchUsersRequest request)
    {
        var userId = GetCurrentUserId();
        if (userId == null) 
            return new SearchUsersResponse { Success = false, ErrorMessage = "Unauthorized", Users = new() };

        var results = await _friendService.SearchUsersAsync(request.Query, userId);
        var users = results.Select(r => new UserInfo
        {
            UserId = r.UserId,
            Login = r.Login,
            Email = r.Email
        }).ToList();

        return new SearchUsersResponse { Success = true, Users = users };
    }

    public async Task<FriendRequestResponse> SendFriendRequest(SendFriendRequestMessage request)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
            return new FriendRequestResponse { Success = false, ErrorMessage = "Unauthorized" };

        var result = await _friendService.SendFriendRequestAsync(userId, request.FriendUserId);

        if (result.Success && result.FriendInfo != null)
        {
             var senderInfo = new FriendInfo
             {
                 FriendshipId = result.FriendInfo.FriendshipId,
                 UserId = userId,
                 Login = GetCurrentUserLogin() ?? "",
                 Email = GetCurrentUserEmail() ?? "",
                 Status = result.FriendInfo.Status,
                 IsIncomingRequest = true
             };

            await Clients.Group($"user_{request.FriendUserId}").ReceiveFriendRequestNotification(new IncomingFriendRequestNotification { FriendRequest = senderInfo });
        }

        return new FriendRequestResponse { Success = result.Success, ErrorMessage = result.ErrorMessage };
    }

    public async Task<GetFriendsResponse> GetFriends()
    {
        var userId = GetCurrentUserId();
        if (userId == null)
            return new GetFriendsResponse { Success = false, ErrorMessage = "Unauthorized", Friends = new(), PendingRequests = new() };

        var friends = await _friendService.GetFriendsAsync(userId);
        var pending = await _friendService.GetPendingRequestsAsync(userId);

        return new GetFriendsResponse { Success = true, Friends = friends, PendingRequests = pending };
    }

    public async Task<FriendRequestResponse> AcceptFriendRequest(AcceptFriendRequestMessage request)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
            return new FriendRequestResponse { Success = false, ErrorMessage = "Unauthorized" };

        var result = await _friendService.AcceptFriendRequestAsync(request.FriendshipId, userId);
        return new FriendRequestResponse { Success = result.Success, ErrorMessage = result.ErrorMessage };
    }

    public async Task<FriendRequestResponse> RejectFriendRequest(RejectFriendRequestMessage request)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
            return new FriendRequestResponse { Success = false, ErrorMessage = "Unauthorized" };

        var result = await _friendService.RejectFriendRequestAsync(request.FriendshipId, userId);
        return new FriendRequestResponse { Success = result.Success, ErrorMessage = result.ErrorMessage };
    }

    public async Task<FriendRequestResponse> RemoveFriend(RemoveFriendRequestMessage request)
    {
        var userId = GetCurrentUserId();
        if (userId == null)
            return new FriendRequestResponse { Success = false, ErrorMessage = "Unauthorized" };

        var result = await _friendService.RemoveFriendAsync(request.FriendshipId, userId);
        return new FriendRequestResponse { Success = result.Success, ErrorMessage = result.ErrorMessage };
    }

    private string? GetCurrentUserId() => Context.Items["UserId"] as string;
    private string? GetCurrentUserLogin() => Context.Items["UserLogin"] as string;
    private string? GetCurrentUserEmail() => Context.Items["UserEmail"] as string;
}
