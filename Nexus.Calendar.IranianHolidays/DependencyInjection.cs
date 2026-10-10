using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexus.Calendar.Application;

namespace Nexus.Calendar.IranianHolidays;

public static class DependencyInjection
{
    /// <summary>Makes calendars that follow official holidays use Iran's. Optional; call it next to
    /// AddCalendarApplication(). Without it (or with another provider registered first) nothing changes.</summary>
    public static IServiceCollection AddIranianOfficialHolidays(this IServiceCollection services)
    {
        services.TryAddSingleton<IOfficialHolidayProvider, IranianOfficialHolidayProvider>();
        return services;
    }
}
