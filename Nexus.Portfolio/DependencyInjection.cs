using Microsoft.Extensions.DependencyInjection;
using Nexus.Portfolio.Application;
using Nexus.Portfolio.Permissions;
using NexusCore.Application.Identity.Permissions;

namespace Nexus.Portfolio;

public static class DependencyInjection
{
    /// <summary>Requires AddProjectManagementCore() and AddActionManagement() - Portfolio
    /// combines both and owns neither. No matching Infrastructure registration exists because
    /// this module has no database of its own.</summary>
    public static IServiceCollection AddPortfolio(this IServiceCollection services)
    {
        services.AddScoped<IPortfolioService, PortfolioService>();
        services.AddSingleton<IPermissionCatalog, PortfolioPermissionCatalog>();

        services.AddAuthorization(options =>
        {
            foreach (var permission in PortfolioPermissions.All)
            {
                options.AddPolicy(permission.Name, policy =>
                    policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission.Name)));
            }
        });

        return services;
    }
}
