using Microsoft.Extensions.DependencyInjection;
using Nexus.Calendar.Application.Meetings;

namespace Nexus.Integrations.CalendarOrgStructure;

public static class DependencyInjection
{
    public static IServiceCollection AddCalendarOrgStructureIntegration(this IServiceCollection services)
    {
        services.AddScoped<IMeetingPeopleScope, HierarchyMeetingPeopleScope>();
        return services;
    }
}
