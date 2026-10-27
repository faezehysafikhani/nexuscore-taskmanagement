using NexusCore.Application.Common;
using NexusCore.Application.Files;
using Microsoft.Extensions.Options;
using NexusCore.Application.Identity.Dtos;
using NexusCore.Application.Identity.Options;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.Application.Identity.Permissions;
using NexusCore.Application.Identity.Security;
using NexusCore.Application.Identity.Services;
using NexusCore.Application.Ldap;
using NexusCore.Application.Messaging;
using NexusCore.Application.Platform.Dtos;
using NexusCore.Application.Platform.Interfaces;
using NexusCore.Application.Platform.Services;
using NexusCore.Application.Security.RateLimiting;
using NexusCore.SharedKernel.Interfaces;

namespace NexusCore.Application.Endpoints;

public static class IdentityEndpoints
{
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/identity/auth").WithTags("Authentication");

        auth.MapPost("/login", async (LoginRequest request, HttpContext http, IIdentityService identityService, IOptions<SignInSessionOptions> session, CancellationToken cancellationToken) =>
            {
                var result = await identityService.LoginAsync(request, cancellationToken);
                return result.IsSuccess ? WithSessionCookie(http, session.Value, result).ToApiResult() : result.ToApiResult();
            })
            .AllowAnonymous()
            .RequireRateLimiting(NexusRateLimitPolicies.Auth)
            .WithName("Login")
            .WithSummary("Sign in with a username or mobile number. After a failed attempt the next one needs a CAPTCHA (/auth/captcha).");

        auth.MapPost("/captcha", async (ILoginProtection protection, CancellationToken cancellationToken) =>
                (await protection.IssueCaptchaAsync(cancellationToken)).ToApiResult())
            .AllowAnonymous()
            .RequireRateLimiting(NexusRateLimitPolicies.Auth)
            .WithName("IssueLoginCaptcha")
            .WithSummary("A single-use CAPTCHA image for the next sign-in attempt from this client");

        // The refresh token comes from the request body (clients that keep it themselves) or from
        // the HttpOnly cookie (browsers; they send X-Nexus-Session: cookie and X-Requested-With).
        auth.MapPost("/refresh", async (RefreshTokenRequest? request, HttpContext http, IIdentityService identityService, IOptions<SignInSessionOptions> session, CancellationToken cancellationToken) =>
            {
                var token = SessionCookies.ReadRefreshToken(http, session.Value, request?.RefreshToken);
                var result = await identityService.RefreshTokenAsync(new RefreshTokenRequest(token), cancellationToken);
                if (result.IsFailure && result.Error.Code == "unauthorized")
                {
                    // A refresh token that no longer works must not stay in the browser.
                    SessionCookies.Clear(http, session.Value);
                }

                // A session that lives in the cookie stays there.
                var viaCookie = token is not null && !string.Equals(token, request?.RefreshToken, StringComparison.Ordinal);
                return result.IsSuccess ? WithSessionCookie(http, session.Value, result, viaCookie).ToApiResult() : result.ToApiResult();
            })
            .AllowAnonymous()
            .RequireRateLimiting(NexusRateLimitPolicies.Auth)
            .WithName("RefreshToken");

        // Anonymous like /refresh: the refresh token itself is the proof, and signing out must
        // work after the access token has expired.
        auth.MapPost("/logout", async (RefreshTokenRequest? request, HttpContext http, IIdentityService identityService, IOptions<SignInSessionOptions> session, CancellationToken cancellationToken) =>
            {
                var token = SessionCookies.ReadRefreshToken(http, session.Value, request?.RefreshToken);
                var result = await identityService.LogoutAsync(new RefreshTokenRequest(token), cancellationToken);
                SessionCookies.Clear(http, session.Value);
                return result.ToApiResult();
            })
            .AllowAnonymous()
            .RequireRateLimiting(NexusRateLimitPolicies.Auth)
            .WithName("Logout")
            .WithSummary("Sign out: the refresh token stops working at once");

        auth.MapGet("/me", async (ICurrentUserContext currentUser, IIdentityService identityService, CancellationToken cancellationToken) =>
            {
                if (currentUser.UserId is null)
                {
                    return Results.Unauthorized();
                }

                return (await identityService.GetCurrentUserAsync(currentUser.UserId.Value, cancellationToken)).ToApiResult();
            })
            .RequireAuthorization()
            .WithName("GetCurrentUser");

        // --- Self-service account -------------------------------------------------------------

        // There is no self-registration: accounts are created by administrators
        // (POST /api/identity/users, users.create).

        auth.MapPost("/forgot-password", async (ForgotPasswordRequest request, IAccountService accounts, CancellationToken cancellationToken) =>
                (await accounts.ForgotPasswordAsync(request, cancellationToken)).ToApiResult())
            .AllowAnonymous()
            .RequireRateLimiting(NexusRateLimitPolicies.PasswordRecovery)
            .WithName("ForgotPassword")
            .WithSummary("Step 1: send a one-time code by SMS to the account's mobile number. The answer never reveals whether the account exists.");

        auth.MapPost("/forgot-password/verify", async (VerifyResetCodeRequest request, IAccountService accounts, CancellationToken cancellationToken) =>
                (await accounts.VerifyResetCodeAsync(request, cancellationToken)).ToApiResult())
            .AllowAnonymous()
            .RequireRateLimiting(NexusRateLimitPolicies.PasswordResetVerification)
            .WithName("VerifyPasswordResetCode")
            .WithSummary("Step 2: check the SMS code; returns a short-lived token for setting the new password");

        auth.MapPost("/reset-password", async (ResetPasswordRequest request, IAccountService accounts, CancellationToken cancellationToken) =>
                (await accounts.ResetPasswordAsync(request, cancellationToken)).ToApiResult())
            .AllowAnonymous()
            .RequireRateLimiting(NexusRateLimitPolicies.PasswordResetVerification)
            .WithName("ResetPassword")
            .WithSummary("Step 3: set the new password with the token from step 2; every session of the user ends");

        auth.MapPut("/me/profile", async (UpdateMyProfileRequest request, IAccountService accounts, CancellationToken cancellationToken) =>
                (await accounts.UpdateMyProfileAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization()
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .WithName("UpdateMyProfile");

        auth.MapPut("/me/preferences", async (UpdateMyPreferencesRequest request, IAccountService accounts, CancellationToken cancellationToken) =>
                (await accounts.UpdateMyPreferencesAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization()
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .WithName("UpdateMyPreferences");

        auth.MapPut("/me/password", async (ChangeMyPasswordRequest request, HttpContext http, IAccountService accounts, IOptions<SignInSessionOptions> session, CancellationToken cancellationToken) =>
                (await accounts.ChangeMyPasswordAsync(request, cancellationToken, SessionCookies.ReadCookie(http, session.Value))).ToApiResult())
            .RequireAuthorization()
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .WithName("ChangeMyPassword")
            .WithSummary("Change your own password (the current one is required). Your other sessions end.");

        // Changing the mobile number (the number a forgotten password is recovered through) takes
        // the current password and a code sent by SMS to the NEW number.
        auth.MapPost("/me/phone/request", async (RequestPhoneChangeRequest request, IAccountService accounts, CancellationToken cancellationToken) =>
                (await accounts.RequestPhoneChangeAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization()
            .RequireRateLimiting(NexusRateLimitPolicies.Sms)
            .WithName("RequestPhoneChange")
            .WithSummary("Step 1: send a verification code by SMS to the new mobile number");

        auth.MapPost("/me/phone/confirm", async (ConfirmPhoneChangeRequest request, HttpContext http, IAccountService accounts, IOptions<SignInSessionOptions> session, CancellationToken cancellationToken) =>
                (await accounts.ConfirmPhoneChangeAsync(request, cancellationToken, SessionCookies.ReadCookie(http, session.Value))).ToApiResult())
            .RequireAuthorization()
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .WithName("ConfirmPhoneChange")
            .WithSummary("Step 2: the code from the SMS and the current password; the number changes and your other sessions end");

        var users = app.MapGroup("/api/identity/users")
            .WithTags("Users")
            .RequireAuthorization()
            .RequireRateLimiting(NexusRateLimitPolicies.AuthenticatedApi);

        // Without an explicit tenantId the list is the caller's own tenant - never every tenant.
        // Mobile numbers are only for those who may edit users; everyone else with users.view -
        // e.g. members picking an assignee - sees names, usernames and emails only.
        users.MapGet("/", async (Guid? tenantId, int? pageNumber, int? pageSize, string? search, HttpContext http, ICurrentUserContext currentUser, IIdentityService identityService, CancellationToken cancellationToken) =>
            {
                var result = await identityService.ListUsersAsync(tenantId ?? currentUser.TenantId, pageNumber, pageSize, search, cancellationToken);
                if (result.IsFailure || http.User.HasClaim("permission", IdentityPermissions.UsersUpdate))
                {
                    return result.ToApiResult();
                }

                var page = result.Value!;
                return Results.Ok(page with
                {
                    Items = page.Items.Select(user => user with { PhoneNumber = null }).ToList()
                });
            })
            .RequireAuthorization(IdentityPermissions.UsersView);

        users.MapPost("/", async (CreateUserRequest request, IIdentityService identityService, CancellationToken cancellationToken) =>
                (await identityService.CreateUserAsync(request, cancellationToken)).ToApiResult())
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(IdentityPermissions.UsersCreate);

        users.MapPut("/{userId:guid}", async (Guid userId, UpdateUserRequest request, IIdentityService identityService, CancellationToken cancellationToken) =>
                (await identityService.UpdateUserAsync(userId, request, cancellationToken)).ToApiResult())
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(IdentityPermissions.UsersUpdate);

        users.MapDelete("/{userId:guid}", async (Guid userId, IIdentityService identityService, CancellationToken cancellationToken) =>
                (await identityService.DeleteUserAsync(userId, cancellationToken)).ToApiResult())
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(IdentityPermissions.UsersDelete);

        users.MapPatch("/{userId:guid}/status", async (Guid userId, SetUserStatusRequest request, IIdentityService identityService, CancellationToken cancellationToken) =>
                (await identityService.SetUserStatusAsync(userId, request, cancellationToken)).ToApiResult())
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(IdentityPermissions.UsersChangeStatus)
            .WithSummary("Enable or disable a user (not the built-in system administrator, not yourself)");

        // Access of one user: every permission and where it comes from (direct, role, group).
        users.MapGet("/{userId:guid}/permissions", async (Guid userId, IIdentityService identityService, CancellationToken cancellationToken) =>
                (await identityService.GetUserAccessAsync(userId, cancellationToken)).ToApiResult())
            .RequireAuthorization(IdentityPermissions.UsersView);

        users.MapPut("/{userId:guid}/permissions", async (Guid userId, AssignUserPermissionsRequest request, IIdentityService identityService, CancellationToken cancellationToken) =>
                (await identityService.SetUserDirectPermissionsAsync(userId, request, cancellationToken)).ToApiResult())
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(IdentityPermissions.UsersAssignPermissions)
            .WithSummary("Replace the permissions granted directly to a user (only permissions the caller holds)");

        users.MapPut("/{userId:guid}/roles", async (Guid userId, AssignUserRolesRequest request, IIdentityService identityService, CancellationToken cancellationToken) =>
                (await identityService.AssignRolesAsync(userId, request, cancellationToken)).ToApiResult())
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(IdentityPermissions.UsersAssignRoles);

        // Online/offline of users of the caller's own organization (e.g. a chat partner), from their
        // live connections. Any signed-in user; others' organizations and disabled accounts are
        // never reported online.
        app.MapGet("/api/identity/presence", async (Guid[]? userIds, IUserPresenceService presence, CancellationToken cancellationToken) =>
                (await presence.GetAsync(userIds ?? [], cancellationToken)).ToApiResult())
            .RequireAuthorization()
            .WithTags("Users")
            .WithSummary("Online or offline for users of your organization (?userIds=...&userIds=...)");

        var roles = app.MapGroup("/api/identity/roles")
            .WithTags("Roles")
            .RequireAuthorization()
            .RequireRateLimiting(NexusRateLimitPolicies.AuthenticatedApi);

        roles.MapGet("/", async (Guid? tenantId, IIdentityService identityService, CancellationToken cancellationToken) =>
                (await identityService.ListRolesAsync(tenantId, cancellationToken)).ToApiResult())
            .RequireAuthorization(IdentityPermissions.RolesView);

        roles.MapPost("/", async (CreateRoleRequest request, IIdentityService identityService, CancellationToken cancellationToken) =>
                (await identityService.CreateRoleAsync(request, cancellationToken)).ToApiResult())
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(IdentityPermissions.RolesCreate);

        roles.MapPut("/{roleId:guid}", async (Guid roleId, UpdateRoleRequest request, IIdentityService identityService, CancellationToken cancellationToken) =>
                (await identityService.UpdateRoleAsync(roleId, request, cancellationToken)).ToApiResult())
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(IdentityPermissions.RolesUpdate);

        roles.MapPut("/{roleId:guid}/permissions", async (Guid roleId, AssignRolePermissionsRequest request, IIdentityService identityService, CancellationToken cancellationToken) =>
                (await identityService.AssignPermissionsAsync(roleId, request, cancellationToken)).ToApiResult())
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(IdentityPermissions.RolesAssignPermissions);

        app.MapGet("/api/identity/permissions", async (IIdentityService identityService, CancellationToken cancellationToken) =>
                (await identityService.ListPermissionsGroupedAsync(cancellationToken)).ToApiResult())
            .WithTags("Permissions")
            .RequireAuthorization(IdentityPermissions.PermissionsView);

        var tenants = app.MapGroup("/api/platform/tenants")
            .WithTags("Tenants")
            .RequireAuthorization()
            .RequireRateLimiting(NexusRateLimitPolicies.AuthenticatedApi);

        tenants.MapGet("/", async (IIdentityService identityService, CancellationToken cancellationToken) =>
                (await identityService.ListTenantsAsync(cancellationToken)).ToApiResult())
            .RequireAuthorization(IdentityPermissions.TenantsView);

        tenants.MapPost("/", async (CreateTenantRequest request, IIdentityService identityService, CancellationToken cancellationToken) =>
                (await identityService.CreateTenantAsync(request, cancellationToken)).ToApiResult())
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(IdentityPermissions.TenantsCreate);

        var platform = app.MapGroup("/api/platform")
            .WithTags("Platform")
            .RequireAuthorization()
            .RequireRateLimiting(NexusRateLimitPolicies.AuthenticatedApi);

        // The audit log, also the source of sign-in history (action=identity.login). Without an
        // explicit tenantId it is the caller's own tenant.
        platform.MapGet("/audit-logs", async (Guid? tenantId, int? pageNumber, int? pageSize, string? search, string? action, string? sort, ICurrentUserContext currentUser, IPlatformService platformService, CancellationToken cancellationToken) =>
                (await platformService.ListAuditLogsAsync(new AuditLogQuery(tenantId ?? currentUser.TenantId, pageNumber ?? 1, pageSize ?? 20, search, action, sort != "asc"), cancellationToken)).ToApiResult())
            .RequireAuthorization(IdentityPermissions.AuditLogsView);

        platform.MapGet("/settings", async (Guid? tenantId, IPlatformService platformService, CancellationToken cancellationToken) =>
                (await platformService.ListSettingsAsync(tenantId, cancellationToken)).ToApiResult())
            .RequireAuthorization(IdentityPermissions.SettingsView);

        platform.MapPut("/settings", async (UpsertSettingRequest request, IPlatformService platformService, CancellationToken cancellationToken) =>
                (await platformService.UpsertSettingAsync(request, cancellationToken)).ToApiResult())
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(IdentityPermissions.SettingsUpdate);

        // The system's name, logo and browser-tab icon: read by everyone, the sign-in page
        // included (anonymous); changed with settings.update.
        platform.MapGet("/branding", async (IBrandingService branding, CancellationToken cancellationToken) =>
                Results.Ok(await branding.GetAsync(cancellationToken)))
            .AllowAnonymous()
            .WithName("GetBranding");

        platform.MapGet("/branding/{kind:regex(^(logo|favicon)$)}", async (string kind, HttpContext http, IBrandingService branding, CancellationToken cancellationToken) =>
            {
                if (await branding.OpenImageAsync(kind, cancellationToken) is not { } image)
                {
                    return Results.NotFound();
                }

                // The address carries the version (?v=), so a changed image is a new address.
                http.Response.Headers.CacheControl = "public, max-age=86400";
                http.Response.Headers["Content-Security-Policy"] = "default-src 'none'";
                return Results.Stream(image.Content, image.ContentType);
            })
            .AllowAnonymous()
            .WithName("GetBrandingImage");

        platform.MapPut("/branding", async (HttpRequest http, IBrandingService branding, CancellationToken cancellationToken) =>
            {
                if (!http.HasFormContentType)
                {
                    return Results.BadRequest();
                }

                var form = await http.ReadFormAsync(cancellationToken);
                BrandingImage? Image(string name) =>
                    form.Files.GetFile(name) is { Length: > 0 } file ? new BrandingImage(file.FileName, file.OpenReadStream(), file.Length) : null;
                bool Flag(string name) => string.Equals(form[name].FirstOrDefault(), "true", StringComparison.OrdinalIgnoreCase);

                var request = new UpdateBrandingRequest(
                    form.ContainsKey("systemName") ? form["systemName"].FirstOrDefault() ?? string.Empty : null,
                    Image("logo"),
                    Image("favicon"),
                    Flag("removeLogo"),
                    Flag("removeFavicon"));
                return (await branding.UpdateAsync(request, cancellationToken)).ToApiResult();
            })
            .DisableAntiforgery()
            .WithUploadSizeLimit()
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(IdentityPermissions.SettingsUpdate)
            .WithName("UpdateBranding")
            .WithSummary("Name, logo and tab icon of the system (multipart: systemName, logo, favicon, removeLogo, removeFavicon)");

        // The current upload rules (max file size + accepted types): every signed-in user who can
        // upload a file needs to read this, not just admins, so it carries no extra permission -
        // it is changed through the generic settings endpoint above (Uploads.MaxFileSizeKb /
        // System), which settings.update already gates.
        platform.MapGet("/upload-policy", async (ICurrentUserContext currentUser, IUploadPolicyReader policyReader, CancellationToken cancellationToken) =>
        {
            var maxKb = await policyReader.GetMaxFileSizeKbAsync(currentUser.TenantId, cancellationToken);
            return Results.Ok(new UploadPolicyDto(maxKb, AllowedUploadTypes.Extensions));
        });

        // The SMS panel (پنل پیامکی) of the caller's tenant: provider settings, texts and a test
        // message. The API key is write-only and stored encrypted.
        var channels = platform.MapGroup("/notification-channels").WithTags("Platform - SMS panel");

        channels.MapGet("/", async (INotificationChannelService service, CancellationToken cancellationToken) =>
                (await service.GetAsync(cancellationToken)).ToApiResult())
            .RequireAuthorization(IdentityPermissions.SmsSettingsView);

        channels.MapGet("/providers", (INotificationChannelService service) => Results.Ok(service.ListProviders()))
            .RequireAuthorization(IdentityPermissions.SmsSettingsView);

        channels.MapPut("/", async (NotificationChannelSettingsDto request, INotificationChannelService service, CancellationToken cancellationToken) =>
                (await service.SaveAsync(request, cancellationToken)).ToApiResult())
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(IdentityPermissions.SmsSettingsUpdate);

        channels.MapPost("/test-sms", async (TestSmsRequest request, INotificationChannelService service, CancellationToken cancellationToken) =>
                (await service.TestSmsAsync(request, cancellationToken)).ToApiResult())
            .RequireRateLimiting(NexusRateLimitPolicies.Sms)
            .RequireAuthorization(IdentityPermissions.SmsSettingsTest);

        channels.MapGet("/templates", async (ISmsTemplateService service, CancellationToken cancellationToken) =>
                (await service.ListAsync(cancellationToken)).ToApiResult())
            .RequireAuthorization(IdentityPermissions.SmsSettingsView);

        channels.MapPut("/templates", async (SaveSmsTemplatesRequest request, ISmsTemplateService service, CancellationToken cancellationToken) =>
                (await service.SaveAsync(request, cancellationToken)).ToApiResult())
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(IdentityPermissions.SmsSettingsUpdate);

        // The hours notification SMS wait for (e.g. 22:00-07:00).
        channels.MapGet("/quiet-hours", async (ISmsQuietHours service, CancellationToken cancellationToken) =>
                (await service.GetAsync(cancellationToken)).ToApiResult())
            .RequireAuthorization(IdentityPermissions.SmsSettingsView);

        channels.MapPut("/quiet-hours", async (SmsQuietHoursDto request, ISmsQuietHours service, CancellationToken cancellationToken) =>
                (await service.SaveAsync(request, cancellationToken)).ToApiResult())
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(IdentityPermissions.SmsSettingsUpdate);

        // LDAP / Active Directory of the caller's tenant. The bind password is write-only and
        // stored encrypted; the test runs the given (unsaved) settings or the saved ones.
        var ldap = platform.MapGroup("/ldap").WithTags("Platform - LDAP");

        ldap.MapGet("/", async (ILdapSettingsService service, CancellationToken cancellationToken) =>
                (await service.GetAsync(cancellationToken)).ToApiResult())
            .RequireAuthorization(IdentityPermissions.LdapSettingsView);

        ldap.MapPut("/", async (LdapSettingsDto request, ILdapSettingsService service, CancellationToken cancellationToken) =>
                (await service.SaveAsync(request, cancellationToken)).ToApiResult())
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(IdentityPermissions.LdapSettingsUpdate);

        ldap.MapPost("/test", async (LdapSettingsDto? request, ILdapSettingsService service, CancellationToken cancellationToken) =>
                (await service.TestAsync(request, cancellationToken)).ToApiResult())
            .RequireRateLimiting(NexusRateLimitPolicies.Write)
            .RequireAuthorization(IdentityPermissions.LdapSettingsTest);

        return app;
    }

    /// <summary>
    /// For a client that wants the cookie, the refresh token moves from the response body into
    /// an HttpOnly cookie that lasts until the sign-in ends.
    /// </summary>
    private static NexusCore.SharedKernel.Results.Result<AuthResponse> WithSessionCookie(
        HttpContext http, SignInSessionOptions options, NexusCore.SharedKernel.Results.Result<AuthResponse> result, bool forceCookie = false)
    {
        if (!(forceCookie || SessionCookies.WantsCookie(http, options)) || result.Value is not { } response)
        {
            return result;
        }

        var expires = response.RefreshTokenExpiresAtUtc ?? DateTimeOffset.UtcNow.AddDays(Math.Max(1, options.RefreshTokenDays));
        return NexusCore.SharedKernel.Results.Result.Success(SessionCookies.Attach(http, options, response, expires));
    }
}
