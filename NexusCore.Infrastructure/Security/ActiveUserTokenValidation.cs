using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Http;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.Application.Identity.Sessions;

namespace NexusCore.Infrastructure.Security;

/// <summary>
/// A valid signature is not enough: every request with an access token also checks that its
/// user still exists and is enabled (User.IsActive). Disabling an account therefore ends access
/// at once instead of when the access token expires; refresh tokens are revoked separately.
///
/// The permission claims copied into the token at sign-in are replaced by the user's current
/// permissions (roles, direct grants, groups), so granting or revoking access - or removing a
/// role - takes effect on the next request rather than when the token is renewed.
///
/// Added to the JWT bearer scheme of every host that calls AddInfrastructure, after the host's
/// own configuration, and chained behind a host's own OnTokenValidated handler.
/// </summary>
internal sealed class ActiveUserTokenValidation : IPostConfigureOptions<JwtBearerOptions>
{
    public void PostConfigure(string? name, JwtBearerOptions options)
    {
        options.Events ??= new JwtBearerEvents();
        var hostHandler = options.Events.OnTokenValidated;
        options.Events.OnTokenValidated = async context =>
        {
            await hostHandler(context);
            if (context.Result is not null)
            {
                return;
            }

            var services = context.HttpContext.RequestServices;
            var repository = services.GetRequiredService<IIdentityRepository>();
            var failure = await SessionFailureAsync(context.HttpContext, context.Principal, services.GetRequiredService<ISessionRepository>(), context.HttpContext.RequestAborted);
            if (failure is not null)
            {
                // The client reads the reason to tell the user why they were signed out.
                context.HttpContext.Response.Headers[SessionReasons.HeaderName] = failure;
                context.Fail(failure);
                return;
            }

            await RefreshPermissionsAsync(context.Principal!, repository, context.HttpContext.RequestAborted);
        };
    }

    internal static async Task RefreshPermissionsAsync(ClaimsPrincipal principal, IIdentityRepository repository, CancellationToken cancellationToken)
    {
        if (principal.Identity is not ClaimsIdentity identity || !Guid.TryParse(SubjectOf(principal), out var userId))
        {
            return;
        }

        var current = await repository.GetUserPermissionNamesAsync(userId, cancellationToken);
        foreach (var stale in identity.FindAll(PermissionClaim).ToList())
        {
            identity.RemoveClaim(stale);
        }

        identity.AddClaims(current.Select(permission => new Claim(PermissionClaim, permission)));
    }

    /// <summary>
    /// One read for the user and the session together: the account must be enabled and the session
    /// (the token's <c>sid</c>) neither revoked nor past its idle or absolute limit. Null when the
    /// request may go on; otherwise the reason code. A token without a session is refused: it
    /// predates sessions, and signing in again gives one.
    ///
    /// A request that carries <c>X-Nexus-Activity</c> was caused by the user doing something and
    /// moves the idle clock (at most once per 30 seconds); every other request - polling,
    /// refresh, hub traffic - only moves the "last seen" time, which never extends a session.
    /// </summary>
    internal static async Task<string?> SessionFailureAsync(HttpContext http, ClaimsPrincipal? principal, ISessionRepository sessions, CancellationToken cancellationToken)
    {
        if (!Guid.TryParse(SubjectOf(principal), out var userId))
        {
            return SessionReasons.AccountDisabled;
        }

        var sid = principal?.FindFirst("sid")?.Value ?? principal?.FindFirst(ClaimTypes.Sid)?.Value;
        if (!Guid.TryParse(sid, out var sessionId))
        {
            return SessionReasons.Missing;
        }

        var now = DateTimeOffset.UtcNow;
        var state = await sessions.ValidateAsync(userId, sessionId, cancellationToken);
        if (state is null)
        {
            return SessionReasons.Missing;
        }

        var failure = state.FailureCode(now);
        if (failure is null)
        {
            var activity = http.Request.Headers.ContainsKey(ActivityHeader);
            await sessions.TouchAsync(sessionId, now, activity, http.Connection.RemoteIpAddress?.ToString(), SessionService.TouchInterval, cancellationToken);
        }

        return failure;
    }

    /// <summary>Sent by the UI on a request that follows a real interaction (click, key, scroll, touch, mouse move).</summary>
    public const string ActivityHeader = "X-Nexus-Activity";

    internal static async Task<bool> IsActiveAsync(ClaimsPrincipal? principal, IIdentityRepository repository, CancellationToken cancellationToken)
    {
        return Guid.TryParse(SubjectOf(principal), out var userId) && await repository.IsUserActiveAsync(userId, cancellationToken);
    }

    private const string PermissionClaim = "permission";

    private static string? SubjectOf(ClaimsPrincipal? principal) =>
        principal?.FindFirst(JwtRegisteredClaimNames.Sub)?.Value
        ?? principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
}
