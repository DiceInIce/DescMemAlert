using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using MemAlerts.Server.Models;
using global::MemAlerts.Shared.Models;

namespace MemAlerts.Server.Services;

public sealed class FileFriendService : IFriendService
{
    private readonly string _friendshipsFilePath;
    private readonly IAuthService _authService;
    private readonly Dictionary<string, Friendship> _friendships = new();
    private readonly object _lock = new();

    public FileFriendService(IAuthService authService, string? dataDirectory = null)
    {
        _authService = authService;
        dataDirectory ??= Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
        Directory.CreateDirectory(dataDirectory);
        _friendshipsFilePath = Path.Combine(dataDirectory, "friendships.json");
        LoadFriendships();
    }

    private void LoadFriendships()
    {
        if (!File.Exists(_friendshipsFilePath)) return;

        try
        {
            var json = File.ReadAllText(_friendshipsFilePath);
            if (string.IsNullOrWhiteSpace(json)) return;

            var friendships = JsonSerializer.Deserialize<List<Friendship>>(json);
            if (friendships is null) return;

            lock (_lock)
            {
                foreach (var friendship in friendships)
                {
                    _friendships[friendship.Id] = friendship;
                }
            }
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Загружено {_friendships.Count} дружеских связей");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Ошибка загрузки друзей: {ex.Message}");
        }
    }

    private void SaveFriendships()
    {
        try
        {
            List<Friendship> friendships;
            lock (_lock)
            {
                friendships = _friendships.Values.ToList();
            }

            var json = JsonSerializer.Serialize(friendships, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_friendshipsFilePath, json);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.Now:HH:mm:ss}] Ошибка сохранения друзей: {ex.Message}");
        }
    }

    public Task<List<UserSearchResult>> SearchUsersAsync(string query, string currentUserId)
    {
        if (string.IsNullOrWhiteSpace(query))
        {
            return Task.FromResult(new List<UserSearchResult>());
        }

        var normalizedQuery = query.Trim().ToLowerInvariant();
        var allUsers = _authService.GetAllUsers();
        
        HashSet<string> friendships;
        HashSet<string> pendingUsers;
        lock (_lock)
        {
            // Добавляем только принятых друзей
            friendships = new HashSet<string>();
            foreach (var friendship in _friendships.Values)
            {
                if (friendship.Status == FriendshipStatus.Accepted)
                {
                    if (friendship.UserId1 == currentUserId)
                        friendships.Add(friendship.UserId2);
                    else if (friendship.UserId2 == currentUserId)
                        friendships.Add(friendship.UserId1);
                }
            }

            // Собираем пользователей с ожидающими запросами
            pendingUsers = new HashSet<string>();
            foreach (var friendship in _friendships.Values)
            {
                if (friendship.Status == FriendshipStatus.Pending)
                {
                    if (friendship.UserId1 == currentUserId)
                        pendingUsers.Add(friendship.UserId2);
                    else if (friendship.UserId2 == currentUserId)
                        pendingUsers.Add(friendship.UserId1);
                }
            }
        }

        var results = allUsers
            .Where(u => u.Id != currentUserId &&
                       (u.Login.ToLowerInvariant().Contains(normalizedQuery) ||
                        u.Email.ToLowerInvariant().Contains(normalizedQuery)))
            .Select(u => new UserSearchResult
            {
                UserId = u.Id,
                Login = u.Login,
                Email = u.Email,
                IsAlreadyFriend = friendships.Contains(u.Id),
                HasPendingRequest = pendingUsers.Contains(u.Id)
            })
            .ToList();

        return Task.FromResult(results);
    }

    public Task<FriendOperationResult> SendFriendRequestAsync(string requesterId, string targetUserId)
    {
        lock (_lock)
        {
            if (requesterId == targetUserId)
            {
                return Task.FromResult(new FriendOperationResult
                {
                    Success = false,
                    ErrorMessage = "Нельзя добавить себя в друзья"
                });
            }

            // Проверяем, есть ли уже дружба
            var existing = _friendships.Values.FirstOrDefault(f =>
                (f.UserId1 == requesterId && f.UserId2 == targetUserId) ||
                (f.UserId1 == targetUserId && f.UserId2 == requesterId));

            if (existing != null)
            {
                if (existing.Status == FriendshipStatus.Accepted)
                {
                    return Task.FromResult(new FriendOperationResult
                    {
                        Success = false,
                        ErrorMessage = "Вы уже друзья"
                    });
                }
                if (existing.Status == FriendshipStatus.Pending)
                {
                    return Task.FromResult(new FriendOperationResult
                    {
                        Success = false,
                        ErrorMessage = "Запрос на дружбу уже отправлен"
                    });
                }
                // Если запрос был отклонен, удаляем старую запись для повторной отправки
                if (existing.Status == FriendshipStatus.Rejected)
                {
                    _friendships.Remove(existing.Id);
                }
            }

            var requesterUser = _authService.GetUserById(requesterId);
            var targetUser = _authService.GetUserById(targetUserId);

            if (requesterUser == null || targetUser == null)
            {
                return Task.FromResult(new FriendOperationResult
                {
                    Success = false,
                    ErrorMessage = "Пользователь не найден"
                });
            }

            var friendshipId = Guid.NewGuid().ToString("N");
            var friendship = new Friendship
            {
                Id = friendshipId,
                UserId1 = requesterId,
                UserId2 = targetUserId,
                UserLogin1 = requesterUser.Login,
                UserLogin2 = targetUser.Login,
                Status = FriendshipStatus.Pending,
                RequesterId = requesterId
            };

            _friendships[friendshipId] = friendship;
            SaveFriendships();

            return Task.FromResult(new FriendOperationResult
            {
                Success = true,
                FriendInfo = new FriendInfo
                {
                    FriendshipId = friendshipId,
                    UserId = targetUserId,
                    Login = targetUser.Login,
                    Email = targetUser.Email,
                    Status = FriendshipStatus.Pending,
                    IsIncomingRequest = false
                }
            });
        }
    }

    public Task<List<FriendInfo>> GetFriendsAsync(string userId)
    {
        lock (_lock)
        {
            var friends = _friendships.Values
                .Where(f => f.Status == FriendshipStatus.Accepted &&
                           (f.UserId1 == userId || f.UserId2 == userId))
                .Select(f => CreateFriendInfo(f, userId))
                .ToList();

            return Task.FromResult(friends);
        }
    }

    public bool AreFriends(string userId1, string userId2)
    {
        lock (_lock)
        {
            return _friendships.Values.Any(f =>
                f.Status == FriendshipStatus.Accepted &&
                ((f.UserId1 == userId1 && f.UserId2 == userId2) ||
                 (f.UserId1 == userId2 && f.UserId2 == userId1)));
        }
    }

    public Task<List<FriendInfo>> GetPendingRequestsAsync(string userId)
    {
        lock (_lock)
        {
            var requests = _friendships.Values
                .Where(f => f.Status == FriendshipStatus.Pending &&
                           (f.UserId1 == userId || f.UserId2 == userId))
                .Select(f => CreateFriendInfo(f, userId))
                .ToList();

            return Task.FromResult(requests);
        }
    }

    public Task<FriendOperationResult> AcceptFriendRequestAsync(string friendshipId, string userId)
    {
        lock (_lock)
        {
            if (!_friendships.TryGetValue(friendshipId, out var friendship))
            {
                return Task.FromResult(new FriendOperationResult
                {
                    Success = false,
                    ErrorMessage = "Запрос на дружбу не найден"
                });
            }

            if (friendship.Status != FriendshipStatus.Pending)
            {
                return Task.FromResult(new FriendOperationResult
                {
                    Success = false,
                    ErrorMessage = "Запрос уже обработан"
                });
            }

            if (friendship.RequesterId == userId)
            {
                return Task.FromResult(new FriendOperationResult
                {
                    Success = false,
                    ErrorMessage = "Нельзя принять собственный запрос"
                });
            }

            if (friendship.UserId1 != userId && friendship.UserId2 != userId)
            {
                return Task.FromResult(new FriendOperationResult
                {
                    Success = false,
                    ErrorMessage = "У вас нет прав на принятие этого запроса"
                });
            }

            friendship.Status = FriendshipStatus.Accepted;
            SaveFriendships();

            var baseInfo = CreateFriendInfo(friendship, userId);
            var acceptedInfo = new FriendInfo
            {
                FriendshipId = baseInfo.FriendshipId,
                UserId = baseInfo.UserId,
                Login = baseInfo.Login,
                Email = baseInfo.Email,
                Status = FriendshipStatus.Accepted,
                IsIncomingRequest = false
            };

            return Task.FromResult(new FriendOperationResult
            {
                Success = true,
                FriendInfo = acceptedInfo
            });
        }
    }

    public Task<FriendOperationResult> RejectFriendRequestAsync(string friendshipId, string userId)
    {
        lock (_lock)
        {
            if (!_friendships.TryGetValue(friendshipId, out var friendship))
            {
                return Task.FromResult(new FriendOperationResult
                {
                    Success = false,
                    ErrorMessage = "Запрос на дружбу не найден"
                });
            }

            if (friendship.UserId1 != userId && friendship.UserId2 != userId)
            {
                return Task.FromResult(new FriendOperationResult
                {
                    Success = false,
                    ErrorMessage = "У вас нет прав на отклонение этого запроса"
                });
            }

            friendship.Status = FriendshipStatus.Rejected;
            SaveFriendships();

            return Task.FromResult(new FriendOperationResult { Success = true });
        }
    }

    public Task<FriendOperationResult> RemoveFriendAsync(string friendshipId, string userId)
    {
        lock (_lock)
        {
            if (!_friendships.TryGetValue(friendshipId, out var friendship))
            {
                return Task.FromResult(new FriendOperationResult
                {
                    Success = false,
                    ErrorMessage = "Дружба не найдена"
                });
            }

            if (friendship.UserId1 != userId && friendship.UserId2 != userId)
            {
                return Task.FromResult(new FriendOperationResult
                {
                    Success = false,
                    ErrorMessage = "У вас нет прав на удаление этого друга"
                });
            }

            _friendships.Remove(friendshipId);
            SaveFriendships();

            return Task.FromResult(new FriendOperationResult { Success = true });
        }
    }

    public Task<FriendInfo?> GetFriendshipInfoAsync(string userId1, string userId2)
    {
        lock (_lock)
        {
            var friendship = _friendships.Values.FirstOrDefault(f =>
                (f.UserId1 == userId1 && f.UserId2 == userId2) ||
                (f.UserId1 == userId2 && f.UserId2 == userId1));

            if (friendship == null)
                return Task.FromResult<FriendInfo?>(null);

            return Task.FromResult<FriendInfo?>(CreateFriendInfo(friendship, userId1));
        }
    }

    private FriendInfo CreateFriendInfo(Friendship friendship, string currentUserId)
    {
        var friendUserId = friendship.UserId1 == currentUserId ? friendship.UserId2 : friendship.UserId1;
        var friendUser = _authService.GetUserById(friendUserId);
        
        return new FriendInfo
        {
            FriendshipId = friendship.Id,
            UserId = friendUserId,
            Login = friendship.UserId1 == currentUserId ? friendship.UserLogin2 : friendship.UserLogin1,
            Email = friendUser?.Email ?? "",
            Status = friendship.Status,
            IsIncomingRequest = friendship.RequesterId != currentUserId
        };
    }
}

