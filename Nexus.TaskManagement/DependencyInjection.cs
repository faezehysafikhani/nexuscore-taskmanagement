using Microsoft.Extensions.DependencyInjection;
using Nexus.TaskManagement.Permissions;
using NexusCore.Application.Identity.Permissions;

namespace Nexus.TaskManagement;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the TaskManagement application tier. Pair it with
    /// AddTaskManagementInfrastructure and MapTaskManagementEndpoints; dropping all three
    /// removes the module with nothing left behind.
    /// </summary>
    public static IServiceCollection AddTaskManagement(this IServiceCollection services)
    {
        services.AddSingleton<IPermissionCatalog, TaskManagementPermissionCatalog>();

        services.AddAuthorization(options =>
        {
            foreach (var permission in TaskManagementPermissions.All)
            {
                options.AddPolicy(permission.Name, policy =>
                    policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission.Name)));
            }
        });

        return services;
    }
}
