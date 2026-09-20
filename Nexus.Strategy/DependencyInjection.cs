using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Nexus.StrategyManagement.Application;
using Nexus.StrategyManagement.Application.Dtos;
using Nexus.StrategyManagement.Application.Validators;
using Nexus.StrategyManagement.Permissions;
using NexusCore.Application.Identity.Permissions;

namespace Nexus.StrategyManagement;

public static class DependencyInjection
{
    /// <summary>No required dependency beyond NexusCore itself - Strategy Management is
    /// standalone. See Nexus.Integrations.ProjectStrategyAlignment for the module that
    /// relates a Strategy to a Project without either depending on the other.</summary>
    public static IServiceCollection AddStrategyManagement(this IServiceCollection services)
    {
        services.AddScoped<IStrategyService, StrategyService>();
        services.AddScoped<IValidator<CreateStrategyRequest>, CreateStrategyRequestValidator>();
        services.AddScoped<IValidator<UpdateStrategyRequest>, UpdateStrategyRequestValidator>();
        services.AddSingleton<IPermissionCatalog, StrategyPermissionCatalog>();

        services.AddAuthorization(options =>
        {
            foreach (var permission in StrategyPermissions.All)
            {
                options.AddPolicy(permission.Name, policy =>
                    policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission.Name)));
            }
        });

        return services;
    }
}
