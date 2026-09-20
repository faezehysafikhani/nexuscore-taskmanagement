using Microsoft.Extensions.DependencyInjection;
using Nexus.Organization.Application;
using Nexus.Organization.Permissions;
using NexusCore.Application.Identity.Permissions;

namespace Nexus.Organization;

public static class DependencyInjection
{
    public static IServiceCollection AddOrganizationApplication(this IServiceCollection services)
    {
        services.AddScoped<IOrganizationService, OrganizationService>();
        services.AddSingleton<IPermissionCatalog, OrganizationPermissionCatalog>();

        services.AddAuthorization(options =>
        {
            foreach (var permission in OrganizationPermissions.All)
            {
                options.AddPolicy(permission.Name, policy =>
                    policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission.Name)));
            }
        });

        return services;
    }
}
