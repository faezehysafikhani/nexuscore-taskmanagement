using Microsoft.Extensions.DependencyInjection;
using Nexus.Calendar.Application;
using Nexus.Calendar.Permissions;
using NexusCore.Application.Identity.Permissions;

namespace Nexus.Calendar;

public static class DependencyInjection
{
    public static IServiceCollection AddCalendarApplication(this IServiceCollection services)
    {
        services.AddScoped<IWorkCalendarService, WorkCalendarService>();
        services.AddSingleton<IPermissionCatalog, CalendarPermissionCatalog>();

        services.AddAuthorization(options =>
        {
            foreach (var permission in CalendarPermissions.All)
            {
                options.AddPolicy(permission.Name, policy =>
                    policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission.Name)));
            }
        });

        return services;
    }
}
