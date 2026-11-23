using Microsoft.Extensions.DependencyInjection;
using Nexus.Calendar.Application;
using Nexus.Calendar.Permissions;
using NexusCore.Application.Identity.Permissions;
using NexusCore.Application.Common;

namespace Nexus.Calendar;

public static class DependencyInjection
{
    public static IServiceCollection AddCalendarApplication(this IServiceCollection services, Action<CalendarOptions>? configure = null)
    {
        var options = new CalendarOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.AddScoped<IWorkCalendarService, WorkCalendarService>();
        // New tasks and meetings follow the default work calendar (CAT-85); without one, the fixed Iranian rule.
        services.AddScoped<ISchedulingDayPolicy, WorkCalendarSchedulingDayPolicy>();
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
