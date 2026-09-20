using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
        services.AddSingleton<IPermissionCatalog, UserGroupPermissionCatalog>();
        return services;
    }
}
