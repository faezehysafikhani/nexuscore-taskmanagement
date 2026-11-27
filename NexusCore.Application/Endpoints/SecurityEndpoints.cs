using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Options;
using NexusCore.Application.Identity.Dtos;
using NexusCore.Application.Identity.Options;
using NexusCore.Application.Identity.Permissions;
using NexusCore.Application.Identity.Security;
using NexusCore.Application.Common;
using NexusCore.Application.Editions;
using NexusCore.Application.Identity.Sessions;
using NexusCore.Application.Security.RateLimiting;

namespace NexusCore.Application.Endpoints;

/// <summary>The second sign-in step (SMS code), service accounts with API keys, and account kinds (phase 9b).</summary>
public static class SecurityEndpoints
{
    public static IEndpointRouteBuilder MapIdentitySecurityEndpoints(this IEndpointRouteBuilder app)
    {
        // Each part below belongs to a commercial feature: a host without it does not map it.
        var features = app.ServiceProvider.GetService(typeof(IEditionFeatures)) as IEditionFeatures;
        // Which commercial features this host runs, so a front end can show only those.
        app.MapGet("/api/platform/edition", (IEditionFeatures gate) => Results.Ok(new
            {
                features = CommercialFeature.All.Where(gate.IsEnabled).ToArray(),
            }))
            .RequireAuthorization()
            .WithTags("Platform")
            .WithName("GetEditionFeatures")
            .WithSummary("The commercial features this host runs (Edition:Commercial, Edition:Features:<Name>)");

        var auth = app.MapGroup("/api/identity/auth").WithTags("Authentication");

        if (features.Allows(CommercialFeature.Mfa))
        {
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
        }

        if (features.Allows(CommercialFeature.ServiceAccounts))
        {
            auth.MapPost("/api-key/token", async (ApiKeyTokenRequest request, IServiceAccountService accounts, CancellationToken ct) => (await accounts.ExchangeAsync(request, ct)).ToApiResult())
                .AllowAnonymous().RequireRateLimiting(NexusRateLimitPolicies.Auth)
                .WithSummary("Exchange an API key of a service account for an access token");
        }

        if (features.Allows(CommercialFeature.Sso))
        {
            // --- single sign-on (CAT-226): OpenID Connect and SAML 2.0 ---
            auth.MapGet("/sso/providers", (NexusCore.Application.Identity.Sso.ISsoService sso) => Results.Ok(sso.Providers()))
                .AllowAnonymous()
                .WithSummary("The single sign-on buttons the sign-in page should show");

            auth.MapGet("/sso/{key}/start", async (string key, string? returnUrl, HttpContext http, NexusCore.Application.Identity.Sso.ISsoService sso, CancellationToken ct) =>
                {
                    var started = await sso.StartAsync(key, returnUrl, ct);
                    if (started.IsFailure)
                    {
                        return Results.Redirect(sso.FailureUrl(returnUrl, started.Error.Message));
                    }

                    if (started.Value!.Binder is { } binder)
                    {
                        // Lax: the browser comes back from the identity provider with a top-level GET, which Lax cookies accompany.
                        http.Response.Cookies.Append(SsoCookie, binder, new CookieOptions
                        {
                            HttpOnly = true,
                            Secure = http.Request.IsHttps,
                            SameSite = SameSiteMode.Lax,
                            Path = "/api/identity/auth/sso",
                            MaxAge = TimeSpan.FromMinutes(15),
                            IsEssential = true,
                        });
                    }

                    return Results.Redirect(started.Value.RedirectUrl);
                })
                .AllowAnonymous()
                .RequireRateLimiting(NexusRateLimitPolicies.Auth)
                .WithName("SsoStart");

            auth.MapGet("/sso/{key}/callback", async (string key, string? code, string? state, string? error, HttpContext http, NexusCore.Application.Identity.Sso.ISsoService sso, IOptions<SignInSessionOptions> session, CancellationToken ct) =>
                {
                    http.Request.Cookies.TryGetValue(SsoCookie, out var binder);
                    http.Response.Cookies.Delete(SsoCookie, new CookieOptions { Path = "/api/identity/auth/sso" });
                    var done = await sso.CompleteOidcAsync(key, code, state, error, binder, ct);
                    return SsoRedirect(http, session.Value, sso, done);
                })
                .AllowAnonymous()
                .RequireRateLimiting(NexusRateLimitPolicies.Auth)
                .WithName("SsoCallback");

            auth.MapPost("/sso/{key}/acs", async (string key, HttpContext http, NexusCore.Application.Identity.Sso.ISsoService sso, IOptions<SignInSessionOptions> session, CancellationToken ct) =>
                {
                    // Read by hand: a [FromForm] parameter would demand anti-forgery middleware, and the identity provider cannot send a token.
                    var form = http.Request.HasFormContentType ? await http.Request.ReadFormAsync(ct) : null;
                    var done = await sso.CompleteSamlAsync(key, form?["SAMLResponse"].ToString(), form?["RelayState"].ToString(), ct);
                    return SsoRedirect(http, session.Value, sso, done);
                })
                .AllowAnonymous()
                .DisableAntiforgery()
                .RequireRateLimiting(NexusRateLimitPolicies.Auth)
                .WithName("SsoSamlAcs");

            auth.MapGet("/sso/{key}/metadata", (string key, NexusCore.Application.Identity.Sso.ISsoService sso) =>
                    sso.SamlMetadata(key) is { IsSuccess: true } metadata ? Results.Content(metadata.Value!, "application/samlmetadata+xml") : Results.NotFound())
                .AllowAnonymous()
                .WithSummary("The service provider metadata to give to the SAML identity provider");
        }

        var users = app.MapGroup("/api/identity/users").WithTags("Users").RequireAuthorization();
        if (features.Allows(CommercialFeature.Mfa))
        {
            users.MapPost("/{userId:guid}/mfa/reset", async (Guid userId, IMfaService mfa, CancellationToken ct) => (await mfa.ResetAsync(userId, ct)).ToApiResult())
                .WithSummary("Switch the SMS code off for someone who lost their phone").RequireAuthorization(IdentityPermissions.UsersUpdate);
        }

        if (features.Allows(CommercialFeature.GuestAccounts))
        {
            users.MapPut("/{userId:guid}/kind", async (Guid userId, SetUserKindRequest request, IServiceAccountService accounts, CancellationToken ct) => (await accounts.SetKindAsync(userId, request, ct)).ToApiResult())
                .WithSummary("Mark a user as a guest (an outside person; not counted against the user limit) or a normal user").RequireAuthorization(IdentityPermissions.UsersUpdate);

            users.MapPut("/{userId:guid}/access-expiry", async (Guid userId, SetAccessExpiryRequest request, IServiceAccountService accounts, CancellationToken ct) => (await accounts.SetAccessExpiryAsync(userId, request, ct)).ToApiResult())
                .WithSummary("End a guest's invitation (or any account's access) at a given moment; null removes the end").RequireAuthorization(IdentityPermissions.UsersUpdate);
        }

        if (features.Allows(CommercialFeature.ServiceAccounts))
        {
            var service = app.MapGroup("/api/identity/service-accounts").WithTags("Service accounts").RequireAuthorization();
            service.MapGet("/", async (IServiceAccountService accounts, CancellationToken ct) => (await accounts.ListAsync(ct)).ToApiResult())
                .RequireAuthorization(IdentityPermissions.UsersView);
            service.MapPost("/", async (CreateServiceAccountRequest request, IServiceAccountService accounts, CancellationToken ct) => (await accounts.CreateAsync(request, ct)).ToApiResult())
                .WithSummary("Make a service account; its first API key is in the answer, once").RequireAuthorization(IdentityPermissions.UsersCreate);
            service.MapPost("/{id:guid}/api-keys", async (Guid id, CreateApiKeyRequest request, IServiceAccountService accounts, CancellationToken ct) => (await accounts.AddKeyAsync(id, request, ct)).ToApiResult())
                .RequireAuthorization(IdentityPermissions.UsersUpdate);
            service.MapDelete("/{id:guid}/api-keys/{keyId:guid}", async (Guid id, Guid keyId, IServiceAccountService accounts, CancellationToken ct) => (await accounts.RevokeKeyAsync(id, keyId, ct)).ToApiResult())
                .RequireAuthorization(IdentityPermissions.UsersUpdate);
        }

        return app;
    }

    private const string SsoCookie = "nexus_sso";

    /// <summary>Signed in: the refresh token goes in its cookie and the browser back to the UI, which then asks /refresh for its access token. Failed: back to the UI with the reason.</summary>
    private static IResult SsoRedirect(HttpContext http, SignInSessionOptions options, NexusCore.Application.Identity.Sso.ISsoService sso, NexusCore.SharedKernel.Results.Result<NexusCore.Application.Identity.Sso.SsoCompleted> done)
    {
        if (done.IsFailure)
        {
            return Results.Redirect(sso.FailureUrl(null, done.Error.Message));
        }

        SessionCookies.Attach(http, options, done.Value!.Session);
        var target = done.Value.ReturnUrl;
        return Results.Redirect(target + (target.Contains('?') ? "&" : "?") + "sso=ok");
    }
}
