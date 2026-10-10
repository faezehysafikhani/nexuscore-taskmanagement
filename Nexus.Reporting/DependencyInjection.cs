using Microsoft.Extensions.DependencyInjection;
using Nexus.Reporting.Application;
using Nexus.Reporting.Permissions;
using NexusCore.Application.Identity.Permissions;

namespace Nexus.Reporting;

public static class DependencyInjection
{
    /// <summary>Requires AddProjectManagementCore() and AddActionManagement(). AddProgressManagement()
    /// is optional: DashboardService takes IProgressService as an optional constructor parameter
    /// (defaults to null), which the built-in container resolves to null when Progress Management
    /// isn't registered - project dashboards then simply omit progress fields instead of failing.
    /// No matching Infrastructure registration exists because this module has no database of its own.</summary>
    public static IServiceCollection AddProjectReporting(this IServiceCollection services)
    {
        services.AddScoped<IDashboardService, DashboardService>();
        services.AddSingleton<IPermissionCatalog, ReportingPermissionCatalog>();

        services.AddAuthorization(options =>
        {
            foreach (var permission in ReportingPermissions.All)
            {
                options.AddPolicy(permission.Name, policy =>
                    policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission.Name)));
            }
        });

        return services;
    }
}
