using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexus.ProjectManagement.Delegation.Application;
using Nexus.ProjectManagement.Delegation.Permissions;
using NexusCore.Application.Identity.Permissions;

namespace Nexus.ProjectManagement.Delegation;

public static class DependencyInjection
{
    /// <summary>Independent of every other module. Needs AddItemDelegationInfrastructure().</summary>
    public static IServiceCollection AddItemDelegation(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IItemDelegationService, ItemDelegationService>();
        services.AddSingleton<IPermissionCatalog, ItemDelegationPermissionCatalog>();

        services.AddAuthorization(options =>
        {
            foreach (var permission in ItemDelegationPermissions.All)
            {
                options.AddPolicy(permission.Name, policy =>
                    policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission.Name)));
            }
        });

        return services;
    }
}
