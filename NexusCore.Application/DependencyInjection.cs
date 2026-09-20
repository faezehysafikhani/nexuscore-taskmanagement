using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using NexusCore.Application.Identity.Dtos;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.Application.Identity.Permissions;
using NexusCore.Application.Identity.Services;
using NexusCore.Application.Identity.Validators;
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
        services.AddScoped<IIdentityService, IdentityService>();
        services.AddScoped<IPlatformService, PlatformService>();
        services.AddSingleton<IPermissionCatalog, IdentityPermissionCatalog>();
        return services;
    }
}
