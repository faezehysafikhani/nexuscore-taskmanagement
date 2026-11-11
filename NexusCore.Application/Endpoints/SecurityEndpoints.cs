using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using NexusCore.Application.Identity.Dtos;
using NexusCore.Application.Identity.Options;
using NexusCore.Application.Identity.Permissions;
using NexusCore.Application.Identity.Security;
using NexusCore.Application.Common;
using NexusCore.Application.Identity.Sessions;
using NexusCore.Application.Security.RateLimiting;

namespace NexusCore.Application.Endpoints;

/// <summary>The second sign-in step (SMS code), service accounts with API keys, and account kinds (phase 9b).</summary>
public static class SecurityEndpoints
{
    public static IEndpointRouteBuilder MapIdentitySecurityEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/identity/auth").WithTags("Authentication");

        auth.MapPost("/login/mfa", async (CompleteMfaRequest request, HttpContext http, IMfaService mfa, IOptions<SignInSessionOptions> session, CancellationToken ct) =>
            {
                var result = await mfa.CompleteSignInAsync(request, ct);
                return (result.IsSuccess && result.Value is { } response && SessionCookies.WantsCookie(http, session.Value)
                    ? NexusCore.SharedKernel.Results.Result.Success(SessionCookies.Attach(http, session.Value, response))
                    : result).ToApiResult();
            })
            .AllowAnonymous().RequireRateLimiting(NexusRateLimitPolicies.Auth)
            .WithSummary("Step 2 of a sign-in that asked for the SMS code: the challenge id from /auth/login and the code");

        auth.MapPost("/me/mfa/request-enable", async (IMfaService mfa, CancellationToken ct) => (await mfa.RequestEnableAsync(ct)).ToApiResult())
            .RequireAuthorization().RequireRateLimiting(NexusRateLimitPolicies.Auth)
            .WithSummary("Turn the SMS code on for yourself: a code is sent to your mobile number first");
        auth.MapPost("/me/mfa/confirm-enable", async (ConfirmMfaEnableRequest request, IMfaService mfa, CancellationToken ct) => (await mfa.ConfirmEnableAsync(request, ct)).ToApiResult())
            .RequireAuthorization().RequireRateLimiting(NexusRateLimitPolicies.Auth);
        auth.MapPost("/me/mfa/disable", async (DisableMfaRequest request, IMfaService mfa, CancellationToken ct) => (await mfa.DisableAsync(request, ct)).ToApiResult())
            .RequireAuthorization().RequireRateLimiting(NexusRateLimitPolicies.Auth);

        auth.MapPost("/api-key/token", async (ApiKeyTokenRequest request, IServiceAccountService accounts, CancellationToken ct) => (await accounts.ExchangeAsync(request, ct)).ToApiResult())
            .AllowAnonymous().RequireRateLimiting(NexusRateLimitPolicies.Auth)
            .WithSummary("Exchange an API key of a service account for an access token");

        var users = app.MapGroup("/api/identity/users").WithTags("Users").RequireAuthorization();
        users.MapPost("/{userId:guid}/mfa/reset", async (Guid userId, IMfaService mfa, CancellationToken ct) => (await mfa.ResetAsync(userId, ct)).ToApiResult())
            .WithSummary("Switch the SMS code off for someone who lost their phone").RequireAuthorization(IdentityPermissions.UsersUpdate);
        users.MapPut("/{userId:guid}/kind", async (Guid userId, SetUserKindRequest request, IServiceAccountService accounts, CancellationToken ct) => (await accounts.SetKindAsync(userId, request, ct)).ToApiResult())
            .WithSummary("Mark a user as a guest (an outside person; not counted against the user limit) or a normal user").RequireAuthorization(IdentityPermissions.UsersUpdate);

        var service = app.MapGroup("/api/identity/service-accounts").WithTags("Service accounts").RequireAuthorization();
        service.MapGet("/", async (IServiceAccountService accounts, CancellationToken ct) => (await accounts.ListAsync(ct)).ToApiResult())
            .RequireAuthorization(IdentityPermissions.UsersView);
        service.MapPost("/", async (CreateServiceAccountRequest request, IServiceAccountService accounts, CancellationToken ct) => (await accounts.CreateAsync(request, ct)).ToApiResult())
            .WithSummary("Make a service account; its first API key is in the answer, once").RequireAuthorization(IdentityPermissions.UsersCreate);
        service.MapPost("/{id:guid}/api-keys", async (Guid id, CreateApiKeyRequest request, IServiceAccountService accounts, CancellationToken ct) => (await accounts.AddKeyAsync(id, request, ct)).ToApiResult())
            .RequireAuthorization(IdentityPermissions.UsersUpdate);
        service.MapDelete("/{id:guid}/api-keys/{keyId:guid}", async (Guid id, Guid keyId, IServiceAccountService accounts, CancellationToken ct) => (await accounts.RevokeKeyAsync(id, keyId, ct)).ToApiResult())
            .RequireAuthorization(IdentityPermissions.UsersUpdate);
        return app;
    }
}
