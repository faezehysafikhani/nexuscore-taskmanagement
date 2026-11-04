using System.Collections.Concurrent;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using NexusCore.Application.Identity.Sessions;

namespace NexusCore.Infrastructure.Security;

/// <summary>
/// A SignalR connection is authenticated once, when it opens, and then lives on: without this, a
/// session that was closed (sign-out, "close that device", an administrator, the idle limit)
/// would keep receiving chat messages and notifications through its open connection. This filter
/// asks, on every call a client makes to a hub, whether the session of the connection is still
/// alive, and ends the connection when it is not. It reads the session from SQL on each call,
/// so it is right whichever server the connection is on.
///
/// Calls on a hub never count as activity (they only move "last seen"): an open chat window must
/// not keep a forgotten workstation signed in.
/// </summary>
public sealed class SessionHubFilter(InMemorySessionConnectionRegistry registry) : IHubFilter
{
    public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
    {
        await EnsureAliveAsync(invocationContext.Context, invocationContext.ServiceProvider);
        return await next(invocationContext);
    }

    public async Task OnConnectedAsync(HubLifetimeContext context, Func<HubLifetimeContext, Task> next)
    {
        await EnsureAliveAsync(context.Context, context.ServiceProvider);
        registry.Add(context.Context);
        await next(context);
    }

    public Task OnDisconnectedAsync(HubLifetimeContext context, Exception? exception, Func<HubLifetimeContext, Exception?, Task> next)
    {
        registry.Remove(context.Context);
        return next(context, exception);
    }

    private static async Task EnsureAliveAsync(HubCallerContext connection, IServiceProvider services)
    {
        var principal = connection.User;
        if (principal?.Identity?.IsAuthenticated != true)
        {
            // Anonymous connections are the hub's own authorization to refuse.
            return;
        }

        var sessions = services.GetRequiredService<ISessionRepository>();
        var failure = await ActiveUserTokenValidation.SessionFailureAsync(
            connection.GetHttpContext() ?? new Microsoft.AspNetCore.Http.DefaultHttpContext(), principal, sessions, connection.ConnectionAborted);
        if (failure is not null)
        {
            connection.Abort();
            throw new HubException(failure);
        }
    }
}

/// <summary>
/// Open hub connections by session, so closing a session closes its connections at once instead
/// of at the next call. Held in memory: the system runs on one server, and the filter above
/// re-checks on every call whatever this registry knows.
/// </summary>
public sealed class InMemorySessionConnectionRegistry : ISessionConnectionRegistry
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, HubCallerContext>> _bySession = new();

    internal void Add(HubCallerContext connection)
    {
        if (SessionOf(connection) is { } sessionId)
        {
            _bySession.GetOrAdd(sessionId, _ => new()).TryAdd(connection.ConnectionId, connection);
        }
    }

    internal void Remove(HubCallerContext connection)
    {
        if (SessionOf(connection) is { } sessionId && _bySession.TryGetValue(sessionId, out var connections))
        {
            connections.TryRemove(connection.ConnectionId, out _);
            if (connections.IsEmpty)
            {
                _bySession.TryRemove(sessionId, out _);
            }
        }
    }

    public void Close(IEnumerable<Guid> sessionIds)
    {
        foreach (var sessionId in sessionIds)
        {
            if (_bySession.TryRemove(sessionId, out var connections))
            {
                foreach (var connection in connections.Values)
                {
                    connection.Abort();
                }
            }
        }
    }

    private static Guid? SessionOf(HubCallerContext connection) =>
        Guid.TryParse(connection.User?.FindFirst("sid")?.Value ?? connection.User?.FindFirst(ClaimTypes.Sid)?.Value, out var id) ? id : null;
}
