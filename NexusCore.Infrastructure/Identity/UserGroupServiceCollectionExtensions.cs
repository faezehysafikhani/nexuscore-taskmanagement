using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NexusCore.Application.Identity.Interfaces;
using NexusCore.Application.Identity.Permissions;
using NexusCore.Application.Identity.Services;
using NexusCore.Infrastructure.Persistence.Repositories;

namespace NexusCore.Infrastructure.Identity;

/// <summary>
/// Single entry point for the optional user-group feature.
///
/// TO DISABLE IN ANOTHER PROJECT: set Features:UserGroups:Enabled to false (or omit it).
/// The null provider is registered instead, no group services exist, and Program.cs skips
/// MapUserGroupEndpoints. Nothing else in the Core is aware of the feature.
/// </summary>
public static class UserGroupServiceCollectionExtensions
{
    public static bool IsUserGroupFeatureEnabled(this IConfiguration configuration) =>
        bool.TryParse(configuration[$"{UserGroupOptions.SectionName}:Enabled"], out var enabled) && enabled;

    public static IServiceCollection AddUserGroupFeature(this IServiceCollection services, IConfiguration configuration)
    {
        var enabled = configuration.IsUserGroupFeatureEnabled();
        services.Configure<UserGroupOptions>(options => options.Enabled = enabled);

        if (!enabled)
        {
            // Feature off: contribute nothing to permission resolution.
            services.AddScoped<IUserGroupPermissionProvider, NullUserGroupPermissionProvider>();
            return services;
        }

        services.AddScoped<IUserGroupPermissionProvider, UserGroupPermissionProvider>();
        services.AddScoped<IUserGroupRepository, UserGroupRepository>();
        services.AddScoped<IUserGroupService, UserGroupService>();
        // Nobody is told about new members unless the host adds a channel (the task-notifications
        // integration does); TryAdd so that registration wins whichever is called first.
        services.TryAddScoped<IUserGroupMembershipNotifier, NullUserGroupMembershipNotifier>();
        // Anyone may be added to a work team unless a module limits it (Nexus.OrgStructure does).
        services.TryAddScoped<IWorkTeamMemberScope, UnlimitedWorkTeamMemberScope>();
        services.AddSingleton<IPermissionCatalog, UserGroupPermissionCatalog>();

        // The group endpoints require these names as policies. They are registered here, next to
        // the catalog, the same way every module registers its own - without this every group
        // request failed with "AuthorizationPolicy named 'groups.view' was not found" (500).
        services.AddAuthorization(options =>
        {
            foreach (var permission in UserGroupPermissions.All)
            {
                options.AddPolicy(permission.Name, policy =>
                    policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission.Name)));
            }
        });
        return services;
    }
}
