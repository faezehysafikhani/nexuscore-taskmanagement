using NexusCore.Application.Identity.Dtos;
using NexusCore.Application.Identity.Interfaces;
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
    IPlatformService platformService)
{
    public async Task<AuthResponse> IssueAsync(User user, CancellationToken cancellationToken)
    {
        var permissions = await repository.GetUserPermissionNamesAsync(user.Id, cancellationToken);
        var accessToken = jwtTokenService.CreateAccessToken(user, permissions);
        var refreshToken = jwtTokenService.CreateRefreshToken();
        var refreshTokenEntity = user.AddRefreshToken(passwordHasher.HashToken(refreshToken), DateTimeOffset.UtcNow.AddDays(14), null);
        await repository.AddRefreshTokenAsync(refreshTokenEntity, cancellationToken);
        user.MarkLoggedIn(DateTimeOffset.UtcNow);

        await unitOfWork.SaveChangesAsync(cancellationToken);
        // Sign-in is an anonymous request: the tenant and user are named explicitly so the entry
        // shows up in that user's sign-in history.
        await platformService.AuditForAsync(user.TenantId, user.Id, "identity.login", nameof(User), user.Id.ToString(), user.Username ?? user.PhoneNumber, cancellationToken);

        return new AuthResponse(accessToken.Token, refreshToken, accessToken.ExpiresAtUtc, IdentityMappings.ToUserDto(user));
    }
}
