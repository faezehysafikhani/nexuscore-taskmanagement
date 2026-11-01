using Microsoft.EntityFrameworkCore;
using NexusCore.Application.Identity.Sessions;
using NexusCore.Domain.Identity;

namespace NexusCore.Infrastructure.Persistence.Repositories;

public sealed class SessionRepository(NexusCoreDbContext dbContext) : ISessionRepository
{
    public async Task AddSessionAsync(UserSession session, CancellationToken cancellationToken) =>
        await dbContext.UserSessions.AddAsync(session, cancellationToken);

    public Task<UserSession?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
        dbContext.UserSessions.IgnoreQueryFilters().FirstOrDefaultAsync(session => session.Id == sessionId, cancellationToken);

    public Task<SessionValidation?> ValidateAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken) =>
        // One primary-key lookup joined to the user: the check that runs on every request.
        dbContext.UserSessions.IgnoreQueryFilters().AsNoTracking()
            .Where(session => session.Id == sessionId && session.UserId == userId)
            .Select(session => new SessionValidation(
                dbContext.Users.IgnoreQueryFilters().Any(user => user.Id == userId && user.IsActive),
                session.RevokedAtUtc,
                session.RevokedReason,
                session.AbsoluteExpiresAtUtc,
                session.IdleTimeoutSeconds,
                session.LastActivityAtUtc))
            .FirstOrDefaultAsync(cancellationToken);

    public async Task TouchAsync(Guid sessionId, DateTimeOffset nowUtc, bool activity, string? ipAddress, TimeSpan minInterval, CancellationToken cancellationToken)
    {
        var threshold = nowUtc - minInterval;
        if (dbContext.Database.IsRelational())
        {
            var query = dbContext.UserSessions.IgnoreQueryFilters()
                .Where(session => session.Id == sessionId && session.RevokedAtUtc == null);
            if (activity)
            {
                await query.Where(session => session.LastActivityAtUtc < threshold)
                    .ExecuteUpdateAsync(set => set
                        .SetProperty(session => session.LastActivityAtUtc, nowUtc)
                        .SetProperty(session => session.LastSeenAtUtc, nowUtc)
                        .SetProperty(session => session.LastIp, session => ipAddress ?? session.LastIp), cancellationToken);
            }
            else
            {
                await query.Where(session => session.LastSeenAtUtc < threshold)
                    .ExecuteUpdateAsync(set => set.SetProperty(session => session.LastSeenAtUtc, nowUtc), cancellationToken);
            }

            return;
        }

        var tracked = await dbContext.UserSessions.IgnoreQueryFilters().FirstOrDefaultAsync(session => session.Id == sessionId && session.RevokedAtUtc == null, cancellationToken);
        if (tracked is null)
        {
            return;
        }

        if (activity && tracked.LastActivityAtUtc < threshold)
        {
            tracked.RecordActivity(nowUtc, ipAddress);
            await dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task<IReadOnlyList<UserSession>> ListActiveSessionsAsync(Guid userId, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var rows = await dbContext.UserSessions.IgnoreQueryFilters().AsNoTracking()
            .Where(session => session.UserId == userId && session.RevokedAtUtc == null && session.AbsoluteExpiresAtUtc > nowUtc)
            .OrderBy(session => session.CreatedAtUtc)
            .ToListAsync(cancellationToken);
        // The idle limit is per row (a column), so it is applied here rather than in SQL.
        return rows.Where(session => session.IsActiveAt(nowUtc)).ToList();
    }

    public async Task<IReadOnlyList<UserSession>> ListEndedSessionsAsync(Guid userId, DateTimeOffset nowUtc, DateTimeOffset sinceUtc, int take, CancellationToken cancellationToken)
    {
        var rows = await dbContext.UserSessions.IgnoreQueryFilters().AsNoTracking()
            .Where(session => session.UserId == userId && session.CreatedAtUtc >= sinceUtc.AddDays(-1))
            .OrderByDescending(session => session.CreatedAtUtc)
            .Take(Math.Max(1, take) * 4)
            .ToListAsync(cancellationToken);
        return rows
            .Where(session => !session.IsActiveAt(nowUtc))
            .Where(session => (session.RevokedAtUtc ?? (session.AbsoluteExpiresAtUtc < session.IdleExpiresAtUtc ? session.AbsoluteExpiresAtUtc : session.IdleExpiresAtUtc)) >= sinceUtc)
            .Take(take)
            .ToList();
    }

    public async Task<bool> HasSeenDeviceAsync(Guid userId, string? deviceLabel, string? addressRange, CancellationToken cancellationToken)
    {
        var sessions = await dbContext.UserSessions.IgnoreQueryFilters().AsNoTracking()
            .Where(session => session.UserId == userId && session.DeviceLabel == deviceLabel)
            .OrderByDescending(session => session.CreatedAtUtc)
            .Select(session => session.CreatedIp)
            .Take(50)
            .ToListAsync(cancellationToken);
        return sessions.Any(ip => DeviceLabel.AddressRange(ip) == addressRange);
    }

    public async Task<bool> RevokeSessionAsync(Guid sessionId, SessionEndReason reason, DateTimeOffset nowUtc, Guid? byUserId, CancellationToken cancellationToken)
    {
        var session = await dbContext.UserSessions.IgnoreQueryFilters().FirstOrDefaultAsync(row => row.Id == sessionId, cancellationToken);
        if (session is null)
        {
            return false;
        }

        // A session whose idle or absolute limit already passed ended by itself: that stays its
        // reason (and its end time), whoever "closes" it afterwards. Only a live session ends for
        // the reason given.
        if (session.EndReasonAt(nowUtc) is { } natural && session.RevokedAtUtc is null)
        {
            var endedAt = session.AbsoluteExpiresAtUtc < session.IdleExpiresAtUtc ? session.AbsoluteExpiresAtUtc : session.IdleExpiresAtUtc;
            session.Revoke(natural, endedAt, null);
            await RevokeTokensOfAsync([sessionId], nowUtc, cancellationToken);
            return false;
        }

        var wasAlive = session.IsActiveAt(nowUtc);
        session.Revoke(reason, nowUtc, byUserId);
        await RevokeTokensOfAsync([sessionId], nowUtc, cancellationToken);
        return wasAlive;
    }

    public async Task<IReadOnlyList<Guid>> RevokeUserSessionsAsync(Guid userId, Guid? exceptSessionId, SessionEndReason reason, DateTimeOffset nowUtc, Guid? byUserId, CancellationToken cancellationToken)
    {
        var sessions = await dbContext.UserSessions.IgnoreQueryFilters()
            .Where(session => session.UserId == userId && session.RevokedAtUtc == null && session.Id != exceptSessionId)
            .ToListAsync(cancellationToken);
        return await EndAsync(sessions, reason, nowUtc, byUserId, cancellationToken);
    }

    public async Task<IReadOnlyList<Guid>> RevokeTenantSessionsAsync(Guid tenantId, Guid? exceptSessionId, SessionEndReason reason, DateTimeOffset nowUtc, Guid? byUserId, CancellationToken cancellationToken)
    {
        var sessions = await dbContext.UserSessions.IgnoreQueryFilters()
            .Where(session => session.TenantId == tenantId && session.RevokedAtUtc == null && session.Id != exceptSessionId)
            .ToListAsync(cancellationToken);
        return await EndAsync(sessions, reason, nowUtc, byUserId, cancellationToken);
    }

    private async Task<IReadOnlyList<Guid>> EndAsync(List<UserSession> sessions, SessionEndReason reason, DateTimeOffset nowUtc, Guid? byUserId, CancellationToken cancellationToken)
    {
        var ended = new List<Guid>();
        foreach (var session in sessions)
        {
            // Sessions that already ended by themselves keep their own reason (see RevokeSessionAsync).
            if (session.EndReasonAt(nowUtc) is { } natural)
            {
                session.Revoke(natural, session.AbsoluteExpiresAtUtc < session.IdleExpiresAtUtc ? session.AbsoluteExpiresAtUtc : session.IdleExpiresAtUtc, null);
            }
            else
            {
                session.Revoke(reason, nowUtc, byUserId);
            }

            ended.Add(session.Id);
        }

        await RevokeTokensOfAsync(ended, nowUtc, cancellationToken);
        return ended;
    }

    /// <summary>The refresh tokens of the sessions (family id = session id) stop working with them.</summary>
    private async Task RevokeTokensOfAsync(IReadOnlyCollection<Guid> sessionIds, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        if (sessionIds.Count == 0)
        {
            return;
        }

        var tokens = await dbContext.RefreshTokens
            .Where(token => token.RevokedAtUtc == null && token.FamilyId != null && sessionIds.Contains(token.FamilyId.Value))
            .ToListAsync(cancellationToken);
        foreach (var token in tokens)
        {
            token.Revoke(null);
        }
    }

    public async Task<IReadOnlyList<UserSession>> ListExpiredUnmarkedAsync(DateTimeOffset nowUtc, int take, CancellationToken cancellationToken)
    {
        var candidates = await dbContext.UserSessions.IgnoreQueryFilters()
            .Where(session => session.RevokedAtUtc == null && (session.AbsoluteExpiresAtUtc <= nowUtc || session.LastActivityAtUtc < nowUtc.AddMinutes(-5)))
            .OrderBy(session => session.LastActivityAtUtc)
            .Take(Math.Max(1, take) * 4)
            .ToListAsync(cancellationToken);
        return candidates.Where(session => !session.IsActiveAt(nowUtc)).Take(take).ToList();
    }

    public async Task<int> PurgeAsync(DateTimeOffset sessionsEndedBeforeUtc, DateTimeOffset tokensOfEndedSessionsBeforeUtc, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        var removed = 0;
        var endedTokens = tokensOfEndedSessionsBeforeUtc;

        var oldTokens = await dbContext.RefreshTokens
            .Where(token => (token.RevokedAtUtc != null && token.RevokedAtUtc < endedTokens) || token.ExpiresAtUtc < endedTokens)
            .Take(2000)
            .ToListAsync(cancellationToken);
        dbContext.RefreshTokens.RemoveRange(oldTokens);
        removed += oldTokens.Count;

        var oldSessions = await dbContext.UserSessions.IgnoreQueryFilters()
            .Where(session => session.RevokedAtUtc != null && session.RevokedAtUtc < sessionsEndedBeforeUtc)
            .Take(1000)
            .ToListAsync(cancellationToken);
        dbContext.UserSessions.RemoveRange(oldSessions);
        removed += oldSessions.Count;

        var oldTickets = await dbContext.PendingSignIns
            .Where(ticket => ticket.UsedAtUtc != null || ticket.ExpiresAtUtc < nowUtc.AddDays(-1))
            .Take(1000)
            .ToListAsync(cancellationToken);
        dbContext.PendingSignIns.RemoveRange(oldTickets);
        removed += oldTickets.Count;

        await dbContext.SaveChangesAsync(cancellationToken);
        return removed;
    }

    public async Task AddPendingSignInAsync(PendingSignIn pending, CancellationToken cancellationToken) =>
        await dbContext.PendingSignIns.AddAsync(pending, cancellationToken);

    public Task<PendingSignIn?> FindPendingSignInAsync(string ticketHash, CancellationToken cancellationToken) =>
        dbContext.PendingSignIns.FirstOrDefaultAsync(ticket => ticket.TicketHash == ticketHash, cancellationToken);

    public async Task<bool> TryUsePendingSignInAsync(Guid pendingId, DateTimeOffset nowUtc, CancellationToken cancellationToken)
    {
        if (dbContext.Database.IsRelational())
        {
            var rows = await dbContext.PendingSignIns
                .Where(ticket => ticket.Id == pendingId && ticket.UsedAtUtc == null && ticket.ExpiresAtUtc > nowUtc)
                .ExecuteUpdateAsync(set => set.SetProperty(ticket => ticket.UsedAtUtc, nowUtc), cancellationToken);
            return rows == 1;
        }

        var tracked = await dbContext.PendingSignIns.FirstOrDefaultAsync(ticket => ticket.Id == pendingId && ticket.UsedAtUtc == null && ticket.ExpiresAtUtc > nowUtc, cancellationToken);
        if (tracked is null)
        {
            return false;
        }

        tracked.MarkUsed(nowUtc);
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
