using NexusCore.Application.Common;
using NexusCore.Application.Identity.Dtos;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.Application.Identity.Permissions;
using NexusCore.Application.Identity.Services;
using NexusCore.Application.Messaging;
using NexusCore.Application.Platform.Dtos;
using NexusCore.Application.Platform.Interfaces;
using NexusCore.SharedKernel.Interfaces;

namespace NexusCore.Application.Endpoints;

public static class IdentityEndpoints
{
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder app)
    {
        var auth = app.MapGroup("/api/identity/auth").WithTags("Authentication");

        auth.MapPost("/login", async (LoginRequest request, IIdentityService identityService, CancellationToken cancellationToken) =>
                (await identityService.LoginAsync(request, cancellationToken)).ToApiResult())
            .AllowAnonymous()
            .WithName("Login");

        auth.MapPost("/refresh", async (RefreshTokenRequest request, IIdentityService identityService, CancellationToken cancellationToken) =>
                (await identityService.RefreshTokenAsync(request, cancellationToken)).ToApiResult())
            .AllowAnonymous()
            .WithName("RefreshToken");

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

        auth.MapPost("/register", async (RegisterRequest request, IAccountService accounts, CancellationToken cancellationToken) =>
                (await accounts.RegisterAsync(request, cancellationToken)).ToApiResult())
            .AllowAnonymous()
            .WithName("Register")
            .WithSummary("Create an account (only when Identity:SelfRegistration:Enabled)");

        auth.MapPost("/forgot-password", async (ForgotPasswordRequest request, IAccountService accounts, CancellationToken cancellationToken) =>
                (await accounts.ForgotPasswordAsync(request, cancellationToken)).ToApiResult())
            .AllowAnonymous()
            .WithName("ForgotPassword")
            .WithSummary("Email a password-reset link. The answer never reveals whether the account exists.");

        auth.MapPost("/reset-password", async (ResetPasswordRequest request, IAccountService accounts, CancellationToken cancellationToken) =>
                (await accounts.ResetPasswordAsync(request, cancellationToken)).ToApiResult())
            .AllowAnonymous()
            .WithName("ResetPassword");

        auth.MapPut("/me/profile", async (UpdateMyProfileRequest request, IAccountService accounts, CancellationToken cancellationToken) =>
                (await accounts.UpdateMyProfileAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization()
            .WithName("UpdateMyProfile");

        auth.MapPut("/me/preferences", async (UpdateMyPreferencesRequest request, IAccountService accounts, CancellationToken cancellationToken) =>
                (await accounts.UpdateMyPreferencesAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization()
            .WithName("UpdateMyPreferences");

        var users = app.MapGroup("/api/identity/users").WithTags("Users").RequireAuthorization();

        // Without an explicit tenantId the list is the caller's own tenant - never every tenant.
        // Contact details (phone, Telegram) are only for those who may edit users; everyone else
        // with users.view - e.g. members picking an assignee - sees names and emails only.
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
                    Items = page.Items.Select(user => user with { PhoneNumber = null, TelegramChatId = null }).ToList()
                });
            })
            .RequireAuthorization(IdentityPermissions.UsersView);

        users.MapPost("/", async (CreateUserRequest request, IIdentityService identityService, CancellationToken cancellationToken) =>
                (await identityService.CreateUserAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization(IdentityPermissions.UsersCreate);

        users.MapPut("/{userId:guid}", async (Guid userId, UpdateUserRequest request, IIdentityService identityService, CancellationToken cancellationToken) =>
                (await identityService.UpdateUserAsync(userId, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(IdentityPermissions.UsersUpdate);

        users.MapDelete("/{userId:guid}", async (Guid userId, IIdentityService identityService, CancellationToken cancellationToken) =>
                (await identityService.DeleteUserAsync(userId, cancellationToken)).ToApiResult())
            .RequireAuthorization(IdentityPermissions.UsersDelete);

        users.MapPut("/{userId:guid}/roles", async (Guid userId, AssignUserRolesRequest request, IIdentityService identityService, CancellationToken cancellationToken) =>
                (await identityService.AssignRolesAsync(userId, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(IdentityPermissions.UsersAssignRoles);

        var roles = app.MapGroup("/api/identity/roles").WithTags("Roles").RequireAuthorization();

        roles.MapGet("/", async (Guid? tenantId, IIdentityService identityService, CancellationToken cancellationToken) =>
                (await identityService.ListRolesAsync(tenantId, cancellationToken)).ToApiResult())
            .RequireAuthorization(IdentityPermissions.RolesView);

        roles.MapPost("/", async (CreateRoleRequest request, IIdentityService identityService, CancellationToken cancellationToken) =>
                (await identityService.CreateRoleAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization(IdentityPermissions.RolesCreate);

        roles.MapPut("/{roleId:guid}", async (Guid roleId, UpdateRoleRequest request, IIdentityService identityService, CancellationToken cancellationToken) =>
                (await identityService.UpdateRoleAsync(roleId, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(IdentityPermissions.RolesUpdate);

        roles.MapPut("/{roleId:guid}/permissions", async (Guid roleId, AssignRolePermissionsRequest request, IIdentityService identityService, CancellationToken cancellationToken) =>
                (await identityService.AssignPermissionsAsync(roleId, request, cancellationToken)).ToApiResult())
            .RequireAuthorization(IdentityPermissions.RolesAssignPermissions);

        app.MapGet("/api/identity/permissions", async (IIdentityService identityService, CancellationToken cancellationToken) =>
                (await identityService.ListPermissionsGroupedAsync(cancellationToken)).ToApiResult())
            .WithTags("Permissions")
            .RequireAuthorization(IdentityPermissions.PermissionsView);

        var tenants = app.MapGroup("/api/platform/tenants").WithTags("Tenants").RequireAuthorization();

        tenants.MapGet("/", async (IIdentityService identityService, CancellationToken cancellationToken) =>
                (await identityService.ListTenantsAsync(cancellationToken)).ToApiResult())
            .RequireAuthorization(IdentityPermissions.TenantsView);

        tenants.MapPost("/", async (CreateTenantRequest request, IIdentityService identityService, CancellationToken cancellationToken) =>
                (await identityService.CreateTenantAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization(IdentityPermissions.TenantsCreate);

        var platform = app.MapGroup("/api/platform").WithTags("Platform").RequireAuthorization();

        platform.MapGet("/audit-logs", async (Guid? tenantId, int pageNumber, int pageSize, IPlatformService platformService, CancellationToken cancellationToken) =>
                (await platformService.ListAuditLogsAsync(tenantId, pageNumber, pageSize, cancellationToken)).ToApiResult())
            .RequireAuthorization(IdentityPermissions.AuditLogsView);

        platform.MapGet("/settings", async (Guid? tenantId, IPlatformService platformService, CancellationToken cancellationToken) =>
                (await platformService.ListSettingsAsync(tenantId, cancellationToken)).ToApiResult())
            .RequireAuthorization(IdentityPermissions.SettingsView);

        platform.MapPut("/settings", async (UpsertSettingRequest request, IPlatformService platformService, CancellationToken cancellationToken) =>
                (await platformService.UpsertSettingAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization(IdentityPermissions.SettingsUpdate);

        // SMS and Telegram gateways of the caller's tenant. Secrets are returned only to
        // holders of settings.view and are stored encrypted.
        var channels = platform.MapGroup("/notification-channels").WithTags("Platform - Notification channels");

        channels.MapGet("/", async (INotificationChannelService service, CancellationToken cancellationToken) =>
                (await service.GetAsync(cancellationToken)).ToApiResult())
            .RequireAuthorization(IdentityPermissions.SettingsView);

        channels.MapPut("/", async (NotificationChannelSettingsDto request, INotificationChannelService service, CancellationToken cancellationToken) =>
                (await service.SaveAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization(IdentityPermissions.SettingsUpdate);

        channels.MapPost("/test-sms", async (TestSmsRequest request, INotificationChannelService service, CancellationToken cancellationToken) =>
                (await service.TestSmsAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization(IdentityPermissions.SettingsUpdate);

        channels.MapPost("/test-telegram", async (TestTelegramRequest request, INotificationChannelService service, CancellationToken cancellationToken) =>
                (await service.TestTelegramAsync(request, cancellationToken)).ToApiResult())
            .RequireAuthorization(IdentityPermissions.SettingsUpdate);

        return app;
    }
}
