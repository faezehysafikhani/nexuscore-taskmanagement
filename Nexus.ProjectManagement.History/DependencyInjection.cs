using Microsoft.Extensions.DependencyInjection;
using Nexus.ProjectManagement.History.Application;
using Nexus.ProjectManagement.History.Permissions;
using NexusCore.Application.Common;
using NexusCore.Application.Identity.Permissions;

namespace Nexus.ProjectManagement.History;

public static class DependencyInjection
{
    /// <summary>Independent of every other module: it listens to what they save through NexusCore's
    /// IEntityChangeObserver, so it records history for whichever Project Management modules are installed.
    /// Needs AddProjectHistoryInfrastructure(), and the modules' DbContexts must use NexusCore's AuditingInterceptor
    /// (they all do).</summary>
    public static IServiceCollection AddProjectHistory(this IServiceCollection services)
    {
        services.AddScoped<IProjectHistoryService, ProjectHistoryService>();
        services.AddScoped<IEntityChangeObserver, ProjectChangeRecorder>();
        services.AddSingleton<IPermissionCatalog, ProjectHistoryPermissionCatalog>();

        services.AddAuthorization(options =>
        {
            foreach (var permission in ProjectHistoryPermissions.All)
            {
                options.AddPolicy(permission.Name, policy =>
                    policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission.Name)));
            }
        });

        return services;
    }
}
