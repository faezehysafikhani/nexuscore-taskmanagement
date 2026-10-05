using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NexusCore.Application.Identity.Dtos;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.Application.Identity.Permissions;
using NexusCore.Application.Identity.Services;
using NexusCore.Application.Identity.Validators;
using NexusCore.Application.Files;
using NexusCore.Application.Messaging;
using NexusCore.Application.Platform.Interfaces;
using NexusCore.Application.Platform.Services;

namespace NexusCore.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {

        services.AddScoped<IValidator<LoginRequest>, LoginRequestValidator>();
        services.AddScoped<IValidator<CreateUserRequest>, CreateUserRequestValidator>();
        services.AddScoped<IValidator<UpdateUserRequest>, UpdateUserRequestValidator>();
        services.AddScoped<IValidator<CreateRoleRequest>, CreateRoleRequestValidator>();
        services.AddScoped<IValidator<CreateTenantRequest>, CreateTenantRequestValidator>();
        services.AddScoped<IValidator<ChangeMyPasswordRequest>, ChangeMyPasswordRequestValidator>();
        services.AddScoped<IValidator<ResetPasswordRequest>, ResetPasswordRequestValidator>();
        services.AddScoped<IValidator<UpdateMyProfileRequest>, UpdateMyProfileRequestValidator>();
        services.AddScoped<AuthSessionIssuer>();
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<IAccountService, AccountService>();
        services.AddScoped<INotificationChannelSettingsReader, NotificationChannelSettingsReader>();
        services.AddScoped<INotificationChannelService, NotificationChannelService>();
        services.AddScoped<IPlatformService, PlatformService>();
        services.AddScoped<IUploadPolicyReader, UploadPolicyReader>();
        // Live presence: one tracker for the process (replaceable by a shared one), fed by the hubs.
        services.TryAddSingleton<IUserPresenceTracker, InMemoryUserPresenceTracker>();
        services.AddScoped<IUserPresenceService, UserPresenceService>();
        services.AddSingleton<IPermissionCatalog, IdentityPermissionCatalog>();
        return services;
    }
}
