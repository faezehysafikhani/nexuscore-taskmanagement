using Microsoft.Extensions.DependencyInjection;
using Nexus.ProjectManagement.Meetings.Application;
using Nexus.ProjectManagement.Meetings.Permissions;
using NexusCore.Application.Identity.Permissions;

namespace Nexus.ProjectManagement.Meetings;

public static class DependencyInjection
{
    /// <summary>Standalone: needs nothing but NexusCore itself. The project a meeting belongs to is a bare Guid.</summary>
    public static IServiceCollection AddProjectMeetings(this IServiceCollection services)
    {
        services.AddScoped<IMeetingService, MeetingService>();
        services.AddSingleton<IPermissionCatalog, MeetingPermissionCatalog>();

        services.AddAuthorization(options =>
        {
            foreach (var permission in MeetingPermissions.All)
            {
                options.AddPolicy(permission.Name, policy =>
                    policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission.Name)));
            }
        });

        return services;
    }
}
