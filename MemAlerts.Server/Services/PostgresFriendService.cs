using Dapper;
using MemAlerts.Server.Models;
using MemAlerts.Shared.Models;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace MemAlerts.Server.Services;

/// <summary>
/// PostgreSQL implementation of the friend service using Dapper.
/// </summary>
public sealed class PostgresFriendService : IFriendService
{
    private readonly string _connectionString;
    private readonly ILogger<PostgresFriendService> _logger;

    public PostgresFriendService(string connectionString, ILogger<PostgresFriendService> logger)
    {
        _connectionString = string.IsNullOrWhiteSpace(connectionString)
            ? throw new ArgumentException("PostgreSQL connection string is required", nameof(connectionString))
            : connectionString;
        _logger = logger;
    }

    public async Task<List<UserSearchResult>> SearchUsersAsync(string query, string currentUserId)
    {
        if (string.IsNullOrWhiteSpace(query))
            return new List<UserSearchResult>();

        var pattern = $"%{query.Trim()}%";

        await using var conn = CreateConnection();
        await conn.OpenAsync();

        var friendshipRows = await conn.QueryAsync<Friendship>(
            @"select id,
                     user_id1     as UserId1,
                     user_id2     as UserId2,
                     user_login1  as UserLogin1,
                     user_login2  as UserLogin2,
                     status,
                     requester_id as RequesterId,
                     created_at   as CreatedAt,
                     accepted_at  as AcceptedAt
              from friendships
              where user_id1 = @currentUserId or user_id2 = @currentUserId",
            new { currentUserId });

        var friends = new HashSet<string>();
        var pending = new HashSet<string>();
        foreach (var f in friendshipRows)
        {
            var otherId = f.UserId1 == currentUserId ? f.UserId2 : f.UserId1;
            if (f.Status == FriendshipStatus.Accepted)
                friends.Add(otherId);
            else if (f.Status == FriendshipStatus.Pending)
                pending.Add(otherId);
        }

        var users = await conn.QueryAsync<User>(
            @"select id, login, email, password_hash as PasswordHash, created_at as CreatedAt
              from users
              where id <> @currentUserId
                and (login ilike @pattern or email ilike @pattern)",
            new { currentUserId, pattern });

        return users.Select(u => new UserSearchResult
        {
            UserId = u.Id,
            Login = u.Login,
            Email = u.Email,
            IsAlreadyFriend = friends.Contains(u.Id),
            HasPendingRequest = pending.Contains(u.Id)
        }).ToList();
    }

    public async Task<FriendOperationResult> SendFriendRequestAsync(string requesterId, string targetUserId)
    {
        if (requesterId == targetUserId)
        {
            return new FriendOperationResult { Success = false, ErrorMessage = "Нельзя добавить себя в друзья" };
        }

        await using var conn = CreateConnection();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var existing = await conn.QuerySingleOrDefaultAsync<Friendship>(
            @"select id,
                     user_id1     as UserId1,
                     user_id2     as UserId2,
                     user_login1  as UserLogin1,
                     user_login2  as UserLogin2,
                     status,
                     requester_id as RequesterId,
                     created_at   as CreatedAt,
                     accepted_at  as AcceptedAt
              from friendships
              where (user_id1 = @a and user_id2 = @b) or (user_id1 = @b and user_id2 = @a)
              limit 1",
            new { a = requesterId, b = targetUserId }, tx);

        if (existing is not null)
        {
            if (existing.Status == FriendshipStatus.Accepted)
                return new FriendOperationResult { Success = false, ErrorMessage = "Вы уже друзья" };

            if (existing.Status == FriendshipStatus.Pending)
                return new FriendOperationResult { Success = false, ErrorMessage = "Запрос на дружбу уже отправлен" };

            if (existing.Status == FriendshipStatus.Rejected)
            {
                await conn.ExecuteAsync("delete from friendships where id = @id", new { id = existing.Id }, tx);
            }
        }

        var requester = await conn.QuerySingleOrDefaultAsync<User>(
            @"select id, login, email, password_hash as PasswordHash, created_at as CreatedAt
              from users where id = @id",
            new { id = requesterId }, tx);

        var target = await conn.QuerySingleOrDefaultAsync<User>(
            @"select id, login, email, password_hash as PasswordHash, created_at as CreatedAt
              from users where id = @id",
            new { id = targetUserId }, tx);

        if (requester is null || target is null)
        {
            await tx.RollbackAsync();
            return new FriendOperationResult { Success = false, ErrorMessage = "Пользователь не найден" };
        }

        var friendshipId = Guid.NewGuid().ToString("N");
        const string insertSql = @"
insert into friendships (id, user_id1, user_id2, user_login1, user_login2, status, requester_id, created_at)
values (@Id, @UserId1, @UserId2, @UserLogin1, @UserLogin2, @Status, @RequesterId, now())";

        await conn.ExecuteAsync(insertSql, new
        {
            Id = friendshipId,
            UserId1 = requesterId,
            UserId2 = targetUserId,
            UserLogin1 = requester.Login,
            UserLogin2 = target.Login,
            Status = FriendshipStatus.Pending,
            RequesterId = requesterId
        }, tx);

        await tx.CommitAsync();

        _logger.LogInformation("Отправлен запрос в друзья от {Requester} к {Target}", requester.Login, target.Login);

        return new FriendOperationResult
        {
            Success = true,
            FriendInfo = new FriendInfo
            {
                FriendshipId = friendshipId,
                UserId = targetUserId,
                Login = target.Login,
                Email = target.Email,
                Status = FriendshipStatus.Pending,
                IsIncomingRequest = false
            }
        };
    }

    public async Task<List<FriendInfo>> GetFriendsAsync(string userId)
    {
        await using var conn = CreateConnection();
        var friendships = await conn.QueryAsync<Friendship>(
            @"select id,
                     user_id1     as UserId1,
                     user_id2     as UserId2,
                     user_login1  as UserLogin1,
                     user_login2  as UserLogin2,
                     status,
                     requester_id as RequesterId,
                     created_at   as CreatedAt,
                     accepted_at  as AcceptedAt
              from friendships
              where status = @status and (user_id1 = @userId or user_id2 = @userId)",
            new { status = FriendshipStatus.Accepted, userId });

        var friendIds = friendships.Select(f => f.UserId1 == userId ? f.UserId2 : f.UserId1).Distinct().ToArray();
        var friendLookup = await LoadUsersByIdsAsync(conn, friendIds);

        return friendships.Select(f => CreateFriendInfo(f, userId, friendLookup, accepted: true)).ToList();
    }

    public async Task<List<FriendInfo>> GetPendingRequestsAsync(string userId)
    {
        await using var conn = CreateConnection();
        var friendships = await conn.QueryAsync<Friendship>(
            @"select id,
                     user_id1     as UserId1,
                     user_id2     as UserId2,
                     user_login1  as UserLogin1,
                     user_login2  as UserLogin2,
                     status,
                     requester_id as RequesterId,
                     created_at   as CreatedAt,
                     accepted_at  as AcceptedAt
              from friendships
              where status = @status and (user_id1 = @userId or user_id2 = @userId)",
            new { status = FriendshipStatus.Pending, userId });

        var friendIds = friendships.Select(f => f.UserId1 == userId ? f.UserId2 : f.UserId1).Distinct().ToArray();
        var friendLookup = await LoadUsersByIdsAsync(conn, friendIds);

        return friendships.Select(f => CreateFriendInfo(f, userId, friendLookup, accepted: false)).ToList();
    }

    public async Task<FriendOperationResult> AcceptFriendRequestAsync(string friendshipId, string userId)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var friendship = await conn.QuerySingleOrDefaultAsync<Friendship>(
            @"select id,
                     user_id1     as UserId1,
                     user_id2     as UserId2,
                     user_login1  as UserLogin1,
                     user_login2  as UserLogin2,
                     status,
                     requester_id as RequesterId,
                     created_at   as CreatedAt,
                     accepted_at  as AcceptedAt
              from friendships
              where id = @friendshipId",
            new { friendshipId }, tx);

        if (friendship is null)
        {
            await tx.RollbackAsync();
            return new FriendOperationResult { Success = false, ErrorMessage = "Запрос на дружбу не найден" };
        }

        if (friendship.Status != FriendshipStatus.Pending)
        {
            await tx.RollbackAsync();
            return new FriendOperationResult { Success = false, ErrorMessage = "Запрос уже обработан" };
        }

        if (friendship.RequesterId == userId)
        {
            await tx.RollbackAsync();
            return new FriendOperationResult { Success = false, ErrorMessage = "Нельзя принять собственный запрос" };
        }

        if (friendship.UserId1 != userId && friendship.UserId2 != userId)
        {
            await tx.RollbackAsync();
            return new FriendOperationResult { Success = false, ErrorMessage = "У вас нет прав на принятие этого запроса" };
        }

        await conn.ExecuteAsync(
            @"update friendships set status = @accepted, accepted_at = now() where id = @id",
            new { accepted = FriendshipStatus.Accepted, id = friendshipId }, tx);

        friendship.Status = FriendshipStatus.Accepted;
        friendship.AcceptedAt = DateTime.UtcNow;

        var friendId = friendship.UserId1 == userId ? friendship.UserId2 : friendship.UserId1;
        var friendLookup = await LoadUsersByIdsAsync(conn, new[] { friendId }, tx);
        var info = CreateFriendInfo(friendship, userId, friendLookup, accepted: true);

        await tx.CommitAsync();

        _logger.LogInformation("Принят запрос дружбы {FriendshipId} пользователем {UserId}", friendshipId, userId);

        return new FriendOperationResult { Success = true, FriendInfo = info };
    }

    public async Task<FriendOperationResult> RejectFriendRequestAsync(string friendshipId, string userId)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var friendship = await conn.QuerySingleOrDefaultAsync<Friendship>(
            @"select id,
                     user_id1     as UserId1,
                     user_id2     as UserId2,
                     user_login1  as UserLogin1,
                     user_login2  as UserLogin2,
                     status,
                     requester_id as RequesterId,
                     created_at   as CreatedAt,
                     accepted_at  as AcceptedAt
              from friendships
              where id = @friendshipId",
            new { friendshipId }, tx);

        if (friendship is null)
        {
            await tx.RollbackAsync();
            return new FriendOperationResult { Success = false, ErrorMessage = "Запрос на дружбу не найден" };
        }

        if (friendship.UserId1 != userId && friendship.UserId2 != userId)
        {
            await tx.RollbackAsync();
            return new FriendOperationResult { Success = false, ErrorMessage = "У вас нет прав на отклонение этого запроса" };
        }

        await conn.ExecuteAsync(
            @"update friendships set status = @rejected where id = @id",
            new { rejected = FriendshipStatus.Rejected, id = friendshipId }, tx);

        await tx.CommitAsync();

        _logger.LogInformation("Отклонен запрос дружбы {FriendshipId} пользователем {UserId}", friendshipId, userId);

        return new FriendOperationResult { Success = true };
    }

    public async Task<FriendOperationResult> RemoveFriendAsync(string friendshipId, string userId)
    {
        await using var conn = CreateConnection();
        await conn.OpenAsync();
        await using var tx = await conn.BeginTransactionAsync();

        var friendship = await conn.QuerySingleOrDefaultAsync<Friendship>(
            @"select id, user_id1, user_id2, user_login1, user_login2, status, requester_id, created_at, accepted_at
              from friendships
              where id = @friendshipId",
            new { friendshipId }, tx);

        if (friendship is null)
        {
            await tx.RollbackAsync();
            return new FriendOperationResult { Success = false, ErrorMessage = "Дружба не найдена" };
        }

        if (friendship.UserId1 != userId && friendship.UserId2 != userId)
        {
            await tx.RollbackAsync();
            return new FriendOperationResult { Success = false, ErrorMessage = "У вас нет прав на удаление этого друга" };
        }

        await conn.ExecuteAsync("delete from friendships where id = @id", new { id = friendshipId }, tx);
        await tx.CommitAsync();

        _logger.LogInformation("Удалена дружба {FriendshipId} пользователем {UserId}", friendshipId, userId);

        return new FriendOperationResult { Success = true };
    }

    public async Task<FriendInfo?> GetFriendshipInfoAsync(string userId1, string userId2)
    {
        await using var conn = CreateConnection();

        var friendship = await conn.QuerySingleOrDefaultAsync<Friendship>(
            @"select id,
                     user_id1     as UserId1,
                     user_id2     as UserId2,
                     user_login1  as UserLogin1,
                     user_login2  as UserLogin2,
                     status,
                     requester_id as RequesterId,
                     created_at   as CreatedAt,
                     accepted_at  as AcceptedAt
              from friendships
              where (user_id1 = @a and user_id2 = @b) or (user_id1 = @b and user_id2 = @a)
              limit 1",
            new { a = userId1, b = userId2 });

        if (friendship is null)
            return null;

        var friendId = friendship.UserId1 == userId1 ? friendship.UserId2 : friendship.UserId1;
        var friendLookup = await LoadUsersByIdsAsync(conn, new[] { friendId });

        return CreateFriendInfo(friendship, userId1, friendLookup, accepted: friendship.Status == FriendshipStatus.Accepted);
    }

    public bool AreFriends(string userId1, string userId2)
    {
        using var conn = CreateConnection();
        var exists = conn.ExecuteScalar<int?>(
            @"select 1 from friendships
              where status = @status
                and ((user_id1 = @a and user_id2 = @b) or (user_id1 = @b and user_id2 = @a))
              limit 1",
            new { status = FriendshipStatus.Accepted, a = userId1, b = userId2 });
        return exists.HasValue;
    }

    private static FriendInfo CreateFriendInfo(
        Friendship friendship,
        string currentUserId,
        IReadOnlyDictionary<string, User> users,
        bool accepted)
    {
        var friendUserId = friendship.UserId1 == currentUserId ? friendship.UserId2 : friendship.UserId1;
        users.TryGetValue(friendUserId, out var friendUser);

        return new FriendInfo
        {
            FriendshipId = friendship.Id,
            UserId = friendUserId,
            Login = friendship.UserId1 == currentUserId ? friendship.UserLogin2 : friendship.UserLogin1,
            Email = friendUser?.Email ?? string.Empty,
            Status = accepted ? FriendshipStatus.Accepted : friendship.Status,
            IsIncomingRequest = friendship.RequesterId != currentUserId
        };
    }

    private async Task<Dictionary<string, User>> LoadUsersByIdsAsync(
        NpgsqlConnection conn,
        IEnumerable<string> ids,
        NpgsqlTransaction? tx = null)
    {
        var idArray = ids.Distinct().ToArray();
        if (idArray.Length == 0)
        {
            return new Dictionary<string, User>();
        }

        var rows = await conn.QueryAsync<User>(
            @"select id, login, email, password_hash as PasswordHash, created_at as CreatedAt
              from users
              where id = any(@ids)",
            new { ids = idArray }, tx);

        return rows.ToDictionary(u => u.Id, u => u);
    }

    private NpgsqlConnection CreateConnection() => new(_connectionString);
}

