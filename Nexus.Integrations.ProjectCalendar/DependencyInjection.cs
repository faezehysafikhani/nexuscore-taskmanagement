using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexus.Integrations.ProjectCalendar.Application;
using Nexus.ProjectManagement.Waterfall.Application.Scheduling;

namespace Nexus.Integrations.ProjectCalendar;

public static class DependencyInjection
{
    /// <summary>Requires AddWaterfallPlanning() and the Calendar module (AddCalendarApplication()
    /// and its infrastructure). Install it to make Waterfall's schedule honour each project's work
    /// calendar - weekends and holidays; without it every day counts as a working day. Safe to
    /// register before or after Waterfall.</summary>
    public static IServiceCollection AddProjectCalendarIntegration(this IServiceCollection services)
    {
        services.Replace(ServiceDescriptor.Scoped<IWorkingDayCalendarProvider, WorkCalendarProvider>());
        return services;
    }
}
