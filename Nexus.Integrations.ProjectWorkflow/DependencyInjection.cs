using Microsoft.Extensions.DependencyInjection;
using Nexus.Integrations.ProjectWorkflow.Application;
using Nexus.Integrations.ProjectWorkflow.Permissions;
using NexusCore.Application.Identity.Permissions;

namespace Nexus.Integrations.ProjectWorkflow;

public static class DependencyInjection
{
    /// <summary>Requires AddProjectManagementCore() and AddWorkflowApplication() - install this
    /// on top of both only when Project-specific workflow overrides are actually needed; plain
    /// Workflow already serves every capability's General definitions without it.</summary>
    public static IServiceCollection AddProjectWorkflowIntegration(this IServiceCollection services)
    {
        services.AddScoped<IProjectWorkflowConfigurationService, ProjectWorkflowConfigurationService>();
        services.AddSingleton<IPermissionCatalog, ProjectWorkflowPermissionCatalog>();

        services.AddAuthorization(options =>
        {
            foreach (var permission in ProjectWorkflowPermissions.All)
            {
                options.AddPolicy(permission.Name, policy =>
                    policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission.Name)));
            }
        });

        return services;
    }
}
