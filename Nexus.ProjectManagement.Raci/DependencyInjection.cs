using Microsoft.Extensions.DependencyInjection;
using Nexus.ProjectManagement.Raci.Application;
using Nexus.ProjectManagement.Raci.Permissions;
using NexusCore.Application.Identity.Permissions;

namespace Nexus.ProjectManagement.Raci;

public static class DependencyInjection
{
    /// <summary>Standalone and optional: needs nothing but NexusCore itself. The project and the items are bare Guids, so a host
    /// that does not call this simply has no RACI; a project that never records a role simply has an empty matrix.</summary>
    public static IServiceCollection AddProjectRaci(this IServiceCollection services)
    {
        services.AddScoped<IRaciService, RaciService>();
        services.AddSingleton<IPermissionCatalog, RaciPermissionCatalog>();

        services.AddAuthorization(options =>
        {
            foreach (var permission in RaciPermissions.All)
            {
                options.AddPolicy(permission.Name, policy =>
                    policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission.Name)));
            }
        });

        return services;
    }
}
