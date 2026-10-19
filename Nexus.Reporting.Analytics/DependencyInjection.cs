using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Nexus.Reporting.Analytics;

public static class DependencyInjection
{
    /// <summary>Optional add-on to AddProjectReporting(): earned value, unit and project-manager performance,
    /// and the unit x status / strategy-alignment matrices. Needs the Project Management Core, Actions and
    /// Progress modules; Contracts, Organization, Strategy and the alignment integration are used only when
    /// they are registered. Map the endpoints with MapReportingAnalyticsEndpoints().</summary>
    public static IServiceCollection AddReportingAnalytics(this IServiceCollection services, Action<AnalyticsOptions>? configure = null)
    {
        var options = new AnalyticsOptions();
        configure?.Invoke(options);
        services.AddSingleton(options);
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IAnalyticsService, AnalyticsService>();
        // Who may read one project's figures without ViewAll: asks the portfolio (registered or not) for what the caller may see.
        services.AddScoped<IProjectViewAccess, PortfolioProjectViewAccess>();
        return services;
    }
}
