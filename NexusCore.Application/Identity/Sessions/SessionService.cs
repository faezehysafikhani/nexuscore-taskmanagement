using System.Security.Cryptography;
using NexusCore.Application.Common;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.Application.Identity.Permissions;
using NexusCore.Application.Platform.Interfaces;
using NexusCore.Application.Security;
using NexusCore.Domain.Identity;
using NexusCore.SharedKernel.Interfaces;
using NexusCore.SharedKernel.Results;

namespace NexusCore.Application.Identity.Sessions;

public sealed class SessionService(
    ISessionRepository sessions,
    IIdentityRepository identity,
    ISessionPolicyProvider policyProvider,
    IPasswordHasher passwordHasher,
    IUnitOfWork unitOfWork,
    IPlatformService platformService,
    ICurrentUserContext currentUser,
    ISessionConnectionRegistry connections,
    ISessionSecurityNotifier notifier) : ISessionService
{
    /// <summary>The idle clock and the "last seen" time move at most this often: one write per user per interval.</summary>
    public static readonly TimeSpan TouchInterval = TimeSpan.FromSeconds(30);

    /// <summary>How long a closed session still shows in "recent sessions", and in the table.</summary>
    public static readonly TimeSpan RecentWindow = TimeSpan.FromDays(7);
    public static readonly TimeSpan ClosedSessionRetention = TimeSpan.FromDays(90);
    public static readonly TimeSpan OldTokenRetention = TimeSpan.FromDays(7);
    public static readonly TimeSpan TicketLifetime = TimeSpan.FromMinutes(2);

    public async Task<SessionStart> BeginAsync(User user, IReadOnlyCollection<string> permissions, bool skipLimit, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var ip = currentUser.IpAddress;
        var userAgent = currentUser.UserAgent;
        var policy = await policyProvider.ResolveAsync(user, permissions, ip, cancellationToken);
        var limit = Math.Max(1, Math.Min(policy.MaxConcurrent, user.MaxConcurrentSessions ?? int.MaxValue));

        var active = await sessions.ListActiveSessionsAsync(user.Id, now, cancellationToken);
        if (!skipLimit && active.Count >= limit)
        {
            var ticket = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).Replace('+', '-').Replace('/', '_').TrimEnd('=');
            var pending = new PendingSignIn(Guid.NewGuid(), user.Id, passwordHasher.HashToken(ticket), now, TicketLifetime, ip);
            await sessions.AddPendingSignInAsync(pending, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await platformService.AuditForAsync(user.TenantId, user.Id, "identity.session_limit_reached", nameof(User), user.Id.ToString(), $"limit {limit}", cancellationToken);
            await notifier.NotifyAsync(user.TenantId, user.Id, SessionSecurityEvent.LimitReached, DeviceLabel.From(userAgent), null, cancellationToken);
            return new SessionStart(null, new PendingSignInDto(
                ticket, pending.ExpiresAtUtc, limit, active.Count - limit + 1,
                active.Select(session => ToDto(session, null, now, null)).ToList()));
        }

        var label = DeviceLabel.From(userAgent);
        var seenBefore = user.LastLoginAtUtc is null
            || await sessions.HasSeenDeviceAsync(user.Id, label, DeviceLabel.AddressRange(ip), cancellationToken);
        var session = new UserSession(
            Guid.NewGuid(), user.Id, user.TenantId, now, now.AddSeconds(policy.AbsoluteSeconds),
            policy.IdleSeconds, policy.Profile, ip, userAgent, label);
        await sessions.AddSessionAsync(session, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        if (!seenBefore)
        {
            await notifier.NotifyAsync(user.TenantId, user.Id, SessionSecurityEvent.NewDevice, label, null, cancellationToken);
        }

        if (policy.OutsideNetwork)
        {
            await notifier.NotifyAsync(user.TenantId, user.Id, SessionSecurityEvent.OutsideNetworkSignIn, label, null, cancellationToken);
        }

        if (user.SmsOnEveryNewSignIn)
        {
            await notifier.NotifyAsync(user.TenantId, user.Id, SessionSecurityEvent.EverySignIn, label, null, cancellationToken);
        }

        return new SessionStart(session, null);
    }

    public async Task<Result<User>> CompletePendingAsync(CompleteLoginRequest request, CancellationToken cancellationToken)
    {
        var invalid = Result.Failure<User>(Error.Unauthorized("The sign-in has expired. Sign in again."));
        if (string.IsNullOrWhiteSpace(request.Ticket))
        {
            return invalid;
        }

        var now = DateTimeOffset.UtcNow;
        var pending = await sessions.FindPendingSignInAsync(passwordHasher.HashToken(request.Ticket.Trim()), cancellationToken);
        if (pending is null || !pending.IsUsableAt(now))
        {
            return invalid;
        }

        var user = await identity.GetUserByIdAsync(pending.UserId, cancellationToken);
        if (user is null || !user.IsActive)
        {
            return invalid;
        }

        var permissions = await identity.GetUserPermissionNamesAsync(user.Id, cancellationToken);
        var policy = await policyProvider.ResolveAsync(user, permissions, currentUser.IpAddress, cancellationToken);
        var limit = Math.Max(1, Math.Min(policy.MaxConcurrent, user.MaxConcurrentSessions ?? int.MaxValue));
        var active = await sessions.ListActiveSessionsAsync(user.Id, now, cancellationToken);
        var mustClose = Math.Max(0, active.Count - limit + 1);

        // Only the user's own live sessions can be chosen; the oldest ones fill what is missing.
        var chosen = (request.CloseSessionIds ?? [])
            .Distinct()
            .Where(id => active.Any(session => session.Id == id))
            .ToList();
        if (request.CloseOldest)
        {
            foreach (var session in active.OrderBy(session => session.CreatedAtUtc))
            {
                if (chosen.Count >= mustClose)
                {
                    break;
                }

                if (!chosen.Contains(session.Id))
                {
                    chosen.Add(session.Id);
                }
            }
        }

        if (chosen.Count < mustClose)
        {
            return Result.Failure<User>(Error.Validation($"Choose at least {mustClose} session(s) to close."));
        }

        if (!await sessions.TryUsePendingSignInAsync(pending.Id, now, cancellationToken))
        {
            return invalid;
        }

        foreach (var id in chosen)
        {
            await sessions.RevokeSessionAsync(id, SessionEndReason.ConcurrentLimit, now, user.Id, cancellationToken);
        }

        await unitOfWork.SaveChangesAsync(cancellationToken);
        connections.Close(chosen);
        await platformService.AuditForAsync(user.TenantId, user.Id, "identity.session_closed_for_limit", nameof(User), user.Id.ToString(), $"{chosen.Count} session(s)", cancellationToken);
        await notifier.NotifyAsync(user.TenantId, user.Id, SessionSecurityEvent.ClosedFromElsewhere, DeviceLabel.From(currentUser.UserAgent), null, cancellationToken);
        return Result.Success(user);
    }

    public async Task<Result<SessionListResponse>> ListMineAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result.Failure<SessionListResponse>(Error.Unauthorized());
        }

        var user = await identity.GetUserByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<SessionListResponse>(Error.NotFound("User was not found."));
        }

        var permissions = await identity.GetUserPermissionNamesAsync(userId, cancellationToken);
        var policy = await policyProvider.ResolveAsync(user, permissions, currentUser.IpAddress, cancellationToken);
        return Result.Success(await BuildListAsync(user, currentUser.SessionId, policy, cancellationToken));
    }

    public async Task<Result<SessionStatusDto>> GetCurrentStatusAsync(CancellationToken cancellationToken)
    {
        var session = await CurrentSessionAsync(cancellationToken);
        return session.IsFailure ? Result.Failure<SessionStatusDto>(session.Error) : Result.Success(ToStatus(session.Value!, DateTimeOffset.UtcNow));
    }

    public async Task<Result<SessionStatusDto>> RecordActivityAsync(CancellationToken cancellationToken)
    {
        var current = await CurrentSessionAsync(cancellationToken);
        if (current.IsFailure)
        {
            return Result.Failure<SessionStatusDto>(current.Error);
        }

        var now = DateTimeOffset.UtcNow;
        // "Keep me signed in" is explicit: the idle clock restarts now, without the 30-second throttle.
        await sessions.TouchAsync(current.Value!.Id, now, true, currentUser.IpAddress, TimeSpan.Zero, cancellationToken);
        var fresh = await sessions.GetSessionAsync(current.Value.Id, cancellationToken);
        return Result.Success(ToStatus(fresh ?? current.Value, DateTimeOffset.UtcNow));
    }

    public async Task<Result> RevokeMineAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result.Failure(Error.Unauthorized());
        }

        var session = await sessions.GetSessionAsync(sessionId, cancellationToken);
        // Someone else's session does not exist for this caller (no IDOR hint).
        if (session is null || session.UserId != userId)
        {
            return Result.Failure(Error.NotFound("Session was not found."));
        }

        var isCurrent = currentUser.SessionId == sessionId;
        var now = DateTimeOffset.UtcNow;
        var wasAlive = await sessions.RevokeSessionAsync(sessionId, isCurrent ? SessionEndReason.Logout : SessionEndReason.Revoked, now, userId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        connections.Close([sessionId]);
        if (wasAlive)
        {
            await platformService.AuditForAsync(session.TenantId, userId, "identity.session_revoked", nameof(UserSession), sessionId.ToString(), session.DeviceLabel, cancellationToken, sessionId: currentUser.SessionId);
            if (!isCurrent)
            {
                await notifier.NotifyAsync(session.TenantId, userId, SessionSecurityEvent.ClosedFromElsewhere, session.DeviceLabel, null, cancellationToken);
            }
        }

        return Result.Success();
    }

    public async Task<Result<int>> RevokeMyOthersAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result.Failure<int>(Error.Unauthorized());
        }

        var ended = await sessions.RevokeUserSessionsAsync(userId, currentUser.SessionId, SessionEndReason.Revoked, DateTimeOffset.UtcNow, userId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        connections.Close(ended);
        if (ended.Count > 0 && currentUser.TenantId is { } tenantId)
        {
            await platformService.AuditAsync("identity.sessions_revoke_others", nameof(User), userId.ToString(), $"{ended.Count} session(s)", cancellationToken);
            await notifier.NotifyAsync(tenantId, userId, SessionSecurityEvent.ClosedFromElsewhere, null, null, cancellationToken);
        }

        return Result.Success(ended.Count);
    }

    public async Task<Result<int>> LogoutAllAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result.Failure<int>(Error.Unauthorized());
        }

        var ended = await sessions.RevokeUserSessionsAsync(userId, null, SessionEndReason.Logout, DateTimeOffset.UtcNow, userId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        connections.Close(ended);
        await platformService.AuditAsync("identity.logout_all", nameof(User), userId.ToString(), $"{ended.Count} session(s)", cancellationToken);
        return Result.Success(ended.Count);
    }

    public async Task<Result> UpdateMyPreferencesAsync(UpdateSessionPreferencesRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId)
        {
            return Result.Failure(Error.Unauthorized());
        }

        var user = await identity.GetUserByIdAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure(Error.NotFound("User was not found."));
        }

        var permissions = await identity.GetUserPermissionNamesAsync(userId, cancellationToken);
        var policy = await policyProvider.ResolveAsync(user, permissions, currentUser.IpAddress, cancellationToken);
        var allowed = Math.Min(policy.MaxConcurrent, SessionPolicySettings.MaxConcurrentCeiling);
        if (request.MaxConcurrentSessions is { } chosen && (chosen < 1 || chosen > allowed))
        {
            return Result.Failure(Error.Validation($"The number of concurrent sessions must be between 1 and {allowed}."));
        }

        // Lowering it does not close open sessions: it applies at the next sign-in.
        user.SetSessionPreferences(request.MaxConcurrentSessions, request.SmsOnEveryNewSignIn);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        await platformService.AuditAsync("identity.session_preferences", nameof(User), userId.ToString(), null, cancellationToken);
        return Result.Success();
    }

    // ---------------------------------------------------------------- administrators

    public async Task<Result<SessionListResponse>> ListForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await TargetUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<SessionListResponse>(Error.NotFound("User was not found."));
        }

        return Result.Success(await BuildListAsync(user, null, null, cancellationToken));
    }

    public async Task<Result> RevokeUserSessionAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        var user = await TargetUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure(Error.NotFound("User was not found."));
        }

        var outranked = await GuardAgainstHigherPrivilegeAsync(user, cancellationToken);
        if (outranked.IsFailure)
        {
            return outranked;
        }

        var session = await sessions.GetSessionAsync(sessionId, cancellationToken);
        if (session is null || session.UserId != userId)
        {
            return Result.Failure(Error.NotFound("Session was not found."));
        }

        var wasAlive = await sessions.RevokeSessionAsync(sessionId, SessionEndReason.Administrator, DateTimeOffset.UtcNow, currentUser.UserId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        connections.Close([sessionId]);
        if (wasAlive)
        {
            await platformService.AuditAsync("identity.session_revoked_by_admin", nameof(User), userId.ToString(), session.DeviceLabel, cancellationToken);
            await notifier.NotifyAsync(user.TenantId, userId, SessionSecurityEvent.ClosedFromElsewhere, session.DeviceLabel, await AdministratorNameAsync(cancellationToken), cancellationToken);
        }

        return Result.Success();
    }

    public async Task<Result<int>> RevokeUserSessionsAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await TargetUserAsync(userId, cancellationToken);
        if (user is null)
        {
            return Result.Failure<int>(Error.NotFound("User was not found."));
        }

        var outranked = await GuardAgainstHigherPrivilegeAsync(user, cancellationToken);
        if (outranked.IsFailure)
        {
            return Result.Failure<int>(outranked.Error);
        }

        var ended = await sessions.RevokeUserSessionsAsync(userId, null, SessionEndReason.Administrator, DateTimeOffset.UtcNow, currentUser.UserId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        connections.Close(ended);
        await platformService.AuditAsync("identity.sessions_revoked_by_admin", nameof(User), userId.ToString(), $"{ended.Count} session(s)", cancellationToken);
        if (ended.Count > 0)
        {
            await notifier.NotifyAsync(user.TenantId, userId, SessionSecurityEvent.ClosedFromElsewhere, null, await AdministratorNameAsync(cancellationToken), cancellationToken);
        }

        return Result.Success(ended.Count);
    }

    public async Task<Result<int>> RevokeAllInOrganizationAsync(RevokeAllSessionsRequest request, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } callerId || currentUser.TenantId is not { } tenantId)
        {
            return Result.Failure<int>(Error.Unauthorized());
        }

        var reason = request.Reason?.Trim() ?? string.Empty;
        if (reason.Length < 3 || reason.Length > 300)
        {
            return Result.Failure<int>(Error.Validation("Write the reason (3 to 300 characters)."));
        }

        var caller = await identity.GetUserByIdAsync(callerId, cancellationToken);
        if (caller is null || string.IsNullOrEmpty(request.Password) || !passwordHasher.Verify(request.Password, caller.PasswordHash))
        {
            return Result.Failure<int>(Error.Validation("The password is not correct."));
        }

        var ended = await sessions.RevokeTenantSessionsAsync(tenantId, currentUser.SessionId, SessionEndReason.Administrator, DateTimeOffset.UtcNow, callerId, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        connections.Close(ended);
        await platformService.AuditAsync("identity.sessions_revoke_all", nameof(Tenant), tenantId.ToString(), $"{ended.Count} session(s): {reason}", cancellationToken);
        return Result.Success(ended.Count);
    }

    public async Task<Result<SessionPolicySettingsDto>> GetPolicySettingsAsync(Guid? tenantId, CancellationToken cancellationToken)
    {
        var tenant = currentUser.ResolveTenant(tenantId);
        if (tenant is null)
        {
            return Result.Failure<SessionPolicySettingsDto>(Error.Validation("Choose an organization."));
        }

        return Result.Success(ToDto(await policyProvider.GetSettingsAsync(tenant.Value, cancellationToken)));
    }

    public async Task<Result<SessionPolicySettingsDto>> UpdatePolicySettingsAsync(Guid? tenantId, SessionPolicySettingsDto settings, CancellationToken cancellationToken)
    {
        var tenant = currentUser.ResolveTenant(tenantId);
        if (tenant is null)
        {
            return Result.Failure<SessionPolicySettingsDto>(Error.Validation("Choose an organization."));
        }

        var model = new SessionPolicySettings
        {
            IdleMinutes = settings.IdleMinutes, AbsoluteHours = settings.AbsoluteHours, MaxConcurrent = settings.MaxConcurrent,
            SensitiveIdleMinutes = settings.SensitiveIdleMinutes, SensitiveAbsoluteHours = settings.SensitiveAbsoluteHours, SensitiveMaxConcurrent = settings.SensitiveMaxConcurrent,
            SystemIdleMinutes = settings.SystemIdleMinutes, SystemAbsoluteHours = settings.SystemAbsoluteHours, SystemMaxConcurrent = settings.SystemMaxConcurrent,
            InternalNetworks = settings.InternalNetworks?.ToList() ?? [],
            ExternalIdleMinutes = settings.ExternalIdleMinutes, ExternalAbsoluteHours = settings.ExternalAbsoluteHours
        };
        var saved = await policyProvider.SaveSettingsAsync(tenant.Value, model, cancellationToken);
        if (!saved.Ok)
        {
            return Result.Failure<SessionPolicySettingsDto>(Error.Validation(saved.Error ?? "The settings are not valid."));
        }

        await platformService.AuditAsync("identity.session_policy_update", nameof(Tenant), tenant.Value.ToString(), null, cancellationToken);
        return Result.Success(ToDto(await policyProvider.GetSettingsAsync(tenant.Value, cancellationToken)));
    }

    // ---------------------------------------------------------------- system

    public async Task EndUserSessionsAsync(Guid userId, Guid? exceptSessionId, SessionEndReason reason, CancellationToken cancellationToken)
    {
        var ended = await sessions.RevokeUserSessionsAsync(userId, exceptSessionId, reason, DateTimeOffset.UtcNow, currentUser.UserId, cancellationToken);
        connections.Close(ended);
    }

    public async Task EndSessionAsync(Guid sessionId, SessionEndReason reason, CancellationToken cancellationToken)
    {
        await sessions.RevokeSessionAsync(sessionId, reason, DateTimeOffset.UtcNow, null, cancellationToken);
        connections.Close([sessionId]);
    }

    public async Task<int> ExpireDueSessionsAsync(CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var due = await sessions.ListExpiredUnmarkedAsync(now, 200, cancellationToken);
        var count = 0;
        foreach (var session in due)
        {
            var reason = session.EndReasonAt(now) ?? SessionEndReason.IdleTimeout;
            if (await sessions.RevokeSessionAsync(session.Id, reason, now, null, cancellationToken))
            {
                count++;
            }

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await platformService.AuditForAsync(session.TenantId, session.UserId, "identity.session_expired", nameof(UserSession), session.Id.ToString(), SessionReasons.CodeFor(reason), cancellationToken, sessionId: session.Id);
        }

        connections.Close(due.Select(session => session.Id).ToList());
        await sessions.PurgeAsync(now - ClosedSessionRetention, now - OldTokenRetention, now, cancellationToken);
        return count;
    }

    // ---------------------------------------------------------------- helpers

    private async Task<Result<UserSession>> CurrentSessionAsync(CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } userId || currentUser.SessionId is not { } sessionId)
        {
            return Result.Failure<UserSession>(Error.Unauthorized());
        }

        var session = await sessions.GetSessionAsync(sessionId, cancellationToken);
        return session is null || session.UserId != userId || !session.IsActiveAt(DateTimeOffset.UtcNow)
            ? Result.Failure<UserSession>(Error.Unauthorized())
            : Result.Success(session);
    }

    private async Task<User?> TargetUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await identity.GetUserByIdAsync(userId, cancellationToken);
        // Another organization's user does not exist for this caller.
        return user is null || !currentUser.CanAccessTenant(user.TenantId) ? null : user;
    }

    private async Task<Result> GuardAgainstHigherPrivilegeAsync(User target, CancellationToken cancellationToken)
    {
        if (currentUser.UserId is not { } callerId || callerId == target.Id)
        {
            return Result.Success();
        }

        var callerPermissions = (await identity.GetUserPermissionNamesAsync(callerId, cancellationToken)).ToHashSet(StringComparer.Ordinal);
        var higher = (await identity.GetUserPermissionNamesAsync(target.Id, cancellationToken)).Where(name => !callerPermissions.Contains(name)).ToList();
        return higher.Count == 0
            ? Result.Success()
            : Result.Failure(Error.Forbidden("This user holds permissions you do not have, so you cannot close their sessions."));
    }

    private async Task<string?> AdministratorNameAsync(CancellationToken cancellationToken) =>
        currentUser.UserId is { } id ? (await identity.GetUserByIdAsync(id, cancellationToken))?.DisplayName : null;

    private async Task<SessionListResponse> BuildListAsync(User user, Guid? currentSid, SessionPolicy? policy, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var active = await sessions.ListActiveSessionsAsync(user.Id, now, cancellationToken);
        var ended = await sessions.ListEndedSessionsAsync(user.Id, now, now - RecentWindow, 20, cancellationToken);
        var names = new Dictionary<Guid, string?>();
        foreach (var id in ended.Where(session => session.RevokedByUserId is { } by && by != user.Id).Select(session => session.RevokedByUserId!.Value).Distinct())
        {
            names[id] = (await identity.GetUserByIdAsync(id, cancellationToken))?.DisplayName;
        }

        string? NameOf(UserSession session) => session.RevokedByUserId is { } by && names.TryGetValue(by, out var name) ? name : null;
        return new SessionListResponse(
            active.Select(session => ToDto(session, currentSid, now, null)).ToList(),
            ended.Select(session => ToDto(session, currentSid, now, NameOf(session))).ToList(),
            policy is null ? null : new SessionPolicyInfo(
                policy.Profile, policy.IdleSeconds, policy.AbsoluteSeconds, policy.MaxConcurrent,
                Math.Min(policy.MaxConcurrent, user.MaxConcurrentSessions ?? int.MaxValue)),
            user.SmsOnEveryNewSignIn);
    }

    private static SessionStatusDto ToStatus(UserSession session, DateTimeOffset now) => new(
        session.Id,
        session.IdleTimeoutSeconds,
        Math.Max(0, (int)(session.IdleExpiresAtUtc - now).TotalSeconds),
        Math.Max(0, (int)(session.AbsoluteExpiresAtUtc - now).TotalSeconds),
        now);

    private static SessionDto ToDto(UserSession session, Guid? currentSid, DateTimeOffset now, string? endedByName)
    {
        var active = session.IsActiveAt(now);
        var endedAt = active ? (DateTimeOffset?)null
            : session.RevokedAtUtc ?? (session.AbsoluteExpiresAtUtc < session.IdleExpiresAtUtc ? session.AbsoluteExpiresAtUtc : session.IdleExpiresAtUtc);
        var reason = session.EndReasonAt(now);
        return new SessionDto(
            session.Id, session.UserId, session.DeviceLabel, session.UserAgent, session.CreatedIp, session.LastIp,
            session.CreatedAtUtc, session.LastActivityAtUtc, session.LastSeenAtUtc, session.IdleExpiresAtUtc, session.AbsoluteExpiresAtUtc,
            session.PolicyProfile, currentSid == session.Id, active, endedAt, reason is null ? null : SessionReasons.CodeFor(reason.Value), endedByName);
    }

    private static SessionPolicySettingsDto ToDto(SessionPolicySettings s) => new(
        s.IdleMinutes, s.AbsoluteHours, s.MaxConcurrent,
        s.SensitiveIdleMinutes, s.SensitiveAbsoluteHours, s.SensitiveMaxConcurrent,
        s.SystemIdleMinutes, s.SystemAbsoluteHours, s.SystemMaxConcurrent,
        s.InternalNetworks, s.ExternalIdleMinutes, s.ExternalAbsoluteHours,
        new SessionPolicyRange(
            SessionPolicySettings.MinIdleMinutes, SessionPolicySettings.MaxIdleMinutes,
            SessionPolicySettings.MinAbsoluteHours, SessionPolicySettings.MaxAbsoluteHours,
            SessionPolicySettings.MinConcurrent, SessionPolicySettings.MaxConcurrentCeiling));
}
