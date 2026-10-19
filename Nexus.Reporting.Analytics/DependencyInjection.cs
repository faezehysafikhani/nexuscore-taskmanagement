using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Nexus.Reporting.Analytics;

public static class DependencyInjection
{
    /// <summary>Optional add-on to AddProjectReporting(): earned value, unit and project-manager performance,
    /// the unit x status / strategy-alignment matrices, and the management boards (health, items needing attention,
    /// risks, KPIs). Needs the Project Management Core, Actions and Progress modules; Contracts, Organization,
    /// Strategy, the alignment integration, Risk management and KPIs are used only when they are registered. Map the endpoints with MapReportingAnalyticsEndpoints().</summary>
    public static IServiceCollection AddReportingAnalytics(this IServiceCollection services, Action<AnalyticsOptions>? configure = null)
    {
        var options = new AnalyticsOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IAnalyticsService, AnalyticsService>();
        services.AddScoped<IManagementBoardService, ManagementBoardService>();
        return services;
    }
}
