using Microsoft.Extensions.DependencyInjection;
using Nexus.Calendar.Application.Meetings;

namespace Nexus.Integrations.CalendarTasks;

public static class DependencyInjection
{
    public static IServiceCollection AddCalendarTasksIntegration(this IServiceCollection services)
    {
        services.AddScoped<IMeetingTaskDirectory, MeetingTaskDirectory>();
        return services;
    }
}
