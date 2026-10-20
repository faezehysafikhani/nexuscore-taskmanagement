using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Nexus.Reporting.Export.Application;

namespace Nexus.Reporting.Export;

public static class DependencyInjection
{
    /// <summary>Optional add-on to AddReportingAnalytics(): the analytics reports as Excel files and as print-ready pages
    /// (the browser turns those into PDF). Owns no data and no tables. Map the endpoint with MapReportingExportEndpoints().</summary>
    public static IServiceCollection AddReportingExport(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.AddScoped<IReportExportService, ReportExportService>();
        return services;
    }
}
