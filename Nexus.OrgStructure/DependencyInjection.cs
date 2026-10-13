using Microsoft.Extensions.DependencyInjection;
using Nexus.OrgStructure.Application;
using Nexus.OrgStructure.Permissions;
using NexusCore.Application.Identity.Permissions;

namespace Nexus.OrgStructure;

public static class DependencyInjection
{
    /// <summary>
    /// Companies, positions and the manager/subordinate hierarchy. Optional: a host that does not
    /// call this (and AddOrgStructureInfrastructure / MapOrgStructureEndpoints) has none of it -
    /// no tables, no endpoints, no permissions. Needs Nexus.Organization (its units are the
    /// deputies and departments) to be registered too.
    /// </summary>
    public static IServiceCollection AddOrgStructure(this IServiceCollection services)
    {
        services.AddScoped<IOrgStructureService, OrgStructureService>();
        services.AddSingleton<IPermissionCatalog, OrgStructurePermissionCatalog>();

        services.AddAuthorization(options =>
        {
            foreach (var permission in OrgStructurePermissions.All)
            {
                options.AddPolicy(permission.Name, policy =>
                    policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission.Name)));
            }

            options.AddPolicy(OrgStructurePermissions.ReadPolicy, policy =>
                policy.RequireAuthenticatedUser().RequireAssertion(context =>
                    OrgStructurePermissions.All.Any(permission => context.User.HasClaim("permission", permission.Name))));
        });

        return services;
    }
}
