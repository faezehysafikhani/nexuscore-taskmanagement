using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Nexus.Reporting.Analytics;

public static class DependencyInjection
{
    /// <summary>Optional add-on to AddProjectReporting(): earned value, unit and project-manager performance,
    /// and the unit x status / strategy-alignment matrices. Needs the Project Management Core, Actions and
    /// Progress modules; Contracts, Organization, Strategy and the alignment integration are used only when
    /// they are registered. Map the endpoints with MapReportingAnalyticsEndpoints().</summary>
    public static IServiceCollection AddReportingAnalytics(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IAnalyticsService, AnalyticsService>();
        return services;
    }
}
