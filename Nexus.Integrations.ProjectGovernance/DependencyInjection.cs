using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexus.Integrations.ProjectGovernance.Application;
using Nexus.Integrations.ProjectGovernance.Permissions;
using Nexus.ProjectManagement.Core.Application;
using Nexus.ProjectManagement.Waterfall.Application;
using NexusCore.Application.Identity.Permissions;

namespace Nexus.Integrations.ProjectGovernance;

public static class DependencyInjection
{
    /// <summary>
    /// Makes the organisation's governance switches editable at run time: which ways of seeing other people's work are on
    /// (read by Portfolio and by the visibility providers, when those are installed), whether a reviewer who rejects a
    /// progress report may confirm a figure (read by Waterfall), and each project's visible sections and contract/invoice access.
    /// Without it nothing changes: every switch stays at its original behaviour. Adds no tables.
    /// </summary>
    public static IServiceCollection AddProjectGovernance(this IServiceCollection services)
    {
        services.AddScoped<GovernanceSettingsService>();
        services.AddScoped<IGovernanceSettingsService>(provider => provider.GetRequiredService<GovernanceSettingsService>());
        services.Replace(ServiceDescriptor.Scoped<IVisibilityRules>(provider => provider.GetRequiredService<GovernanceSettingsService>()));
        services.Replace(ServiceDescriptor.Scoped<IProgressRejectionPolicy>(provider => provider.GetRequiredService<GovernanceSettingsService>()));
        services.AddSingleton<IPermissionCatalog, ProjectGovernancePermissionCatalog>();

        services.AddAuthorization(options =>
        {
            foreach (var permission in ProjectGovernancePermissions.All)
            {
                options.AddPolicy(permission.Name, policy =>
                    policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission.Name)));
            }
        });

        return services;
    }
}
