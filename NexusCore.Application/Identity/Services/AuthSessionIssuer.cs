using NexusCore.Application.Identity.Dtos;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.Application.Identity.Sessions;
using NexusCore.Application.Platform.Interfaces;
using NexusCore.Application.Security;
using NexusCore.Domain.Identity;
using NexusCore.SharedKernel.Interfaces;

namespace NexusCore.Application.Identity.Services;

/// <summary>
/// Issues a session (access token with the user's permissions and roles, plus a rotating
/// refresh token) to a user whose identity has already been proven - by a password at sign-in,
/// or by creating the account at registration. One place, so both give the same tokens.
/// </summary>
public sealed class AuthSessionIssuer(
    IIdentityRepository repository,
    IPasswordHasher passwordHasher,
    IJwtTokenService jwtTokenService,
    IUnitOfWork unitOfWork,
    IPlatformService platformService,
    ICurrentUserContext currentUser,
    ISessionService sessionService)
{
    /// <summary>
    /// Starts a session for the user and hands out its tokens. At the concurrent-session limit
    /// no token is issued: the answer carries a ticket and the user's sessions instead, and the
    /// sign-in is completed through <c>/auth/login/complete</c> (<paramref name="skipLimit"/>
    /// is true there, after the chosen sessions were closed).
    /// </summary>
    public async Task<AuthResponse> IssueAsync(User user, CancellationToken cancellationToken, bool skipLimit = false)
    {
        var permissions = await repository.GetUserPermissionNamesAsync(user.Id, cancellationToken);
        var start = await sessionService.BeginAsync(user, permissions, skipLimit, cancellationToken);
        if (start.Session is not { } session)
        {
            return new AuthResponse(string.Empty, string.Empty, DateTimeOffset.UtcNow, IdentityMappings.ToUserDto(user), null, start.Pending);
        }

        var accessToken = jwtTokenService.CreateAccessToken(user, permissions, session.Id);
        var refreshToken = jwtTokenService.CreateRefreshToken();
        // A new sign-in starts a new family of refresh tokens (its id is the session's) that ends with the session.
        var refreshTokenEntity = user.AddRefreshToken(
            passwordHasher.HashToken(refreshToken),
            session.AbsoluteExpiresAtUtc,
            currentUser.IpAddress,
            session.Id);
        await repository.AddRefreshTokenAsync(refreshTokenEntity, cancellationToken);
        user.MarkLoggedIn(DateTimeOffset.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        // Sign-in is an anonymous request: the tenant and user are named explicitly so the entry
        // shows up in that user's sign-in history.
        await platformService.AuditForAsync(user.TenantId, user.Id, "identity.login", nameof(User), user.Id.ToString(), user.Username ?? user.PhoneNumber, cancellationToken, sessionId: session.Id);

        var status = new SessionStatusDto(session.Id, session.IdleTimeoutSeconds, session.IdleTimeoutSeconds,
            Math.Max(0, (int)(session.AbsoluteExpiresAtUtc - DateTimeOffset.UtcNow).TotalSeconds), DateTimeOffset.UtcNow);
        return new AuthResponse(accessToken.Token, refreshToken, accessToken.ExpiresAtUtc, IdentityMappings.ToUserDto(user), refreshTokenEntity.ExpiresAtUtc, null, status);
    }
}
