using System.Collections.Concurrent;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Application.Identity.Services;

/// <summary>
/// Who is connected right now, from the real-time (SignalR) connections themselves: every hub
/// connection of a signed-in user is registered when it opens and removed when it closes. A user
/// is online while at least one connection is open - a second tab or device keeps them online
/// when the first closes - and offline once the last one has closed. There is no timer: a
/// connection that drops without closing is removed when SignalR notices (its keep-alive
/// timeout), which is the only delay.
/// </summary>
public interface IUserPresenceTracker
{
    void Connected(Guid userId, string connectionId);

    void Disconnected(Guid userId, string connectionId);

    bool IsOnline(Guid userId);
}

/// <summary>
/// The connections of this server process. A deployment running several instances behind a
/// load balancer registers a shared implementation (e.g. over Redis) instead.
/// </summary>
public sealed class InMemoryUserPresenceTracker : IUserPresenceTracker
{
    private readonly ConcurrentDictionary<Guid, HashSet<string>> _connections = new();

    public void Connected(Guid userId, string connectionId)
    {
        var set = _connections.GetOrAdd(userId, _ => []);
        lock (set)
        {
            set.Add(connectionId);
        }

        // A concurrent Disconnected may have removed the (then empty) set in between.
        _connections.TryAdd(userId, set);
    }

    public void Disconnected(Guid userId, string connectionId)
    {
        if (!_connections.TryGetValue(userId, out var set))
        {
            return;
        }

        lock (set)
        {
            set.Remove(connectionId);
            if (set.Count == 0)
            {
                _connections.TryRemove(new KeyValuePair<Guid, HashSet<string>>(userId, set));
            }
        }
    }

    public bool IsOnline(Guid userId)
    {
        if (!_connections.TryGetValue(userId, out var set))
        {
            return false;
        }

        lock (set)
        {
            return set.Count > 0;
        }
    }
}

public sealed record UserPresenceDto(Guid UserId, bool IsOnline);

public interface IUserPresenceService
{
    /// <summary>
    /// Online or offline for each of these users that the caller may know about: active users of
    /// the caller's own organization. Others are left out; a disabled account is never online.
    /// </summary>
    Task<Result<IReadOnlyList<UserPresenceDto>>> GetAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken);
}

public sealed class UserPresenceService(
    IUserPresenceTracker tracker,
    IUserDirectory directory,
    ICurrentUserContext currentUser) : IUserPresenceService
{
    public const int MaxUsersPerRequest = 200;

    public async Task<Result<IReadOnlyList<UserPresenceDto>>> GetAsync(IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is null || currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<IReadOnlyList<UserPresenceDto>>(Error.Unauthorized());
        }

        var ids = userIds.Where(id => id != Guid.Empty).Distinct().Take(MaxUsersPerRequest).ToList();
        if (ids.Count == 0)
        {
            return Result.Success<IReadOnlyList<UserPresenceDto>>([]);
        }

        var users = await directory.GetUsersAsync(ids, cancellationToken);
        return Result.Success<IReadOnlyList<UserPresenceDto>>(users
            .Where(user => user.TenantId == tenantId)
            .Select(user => new UserPresenceDto(user.Id, user.IsActive && tracker.IsOnline(user.Id)))
            .ToList());
    }
}
