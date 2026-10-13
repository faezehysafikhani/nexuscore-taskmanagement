using Microsoft.Extensions.DependencyInjection;

namespace Nexus.Integrations.TaskReporting;

public static class DependencyInjection
{
    /// <summary>
    /// Task reports that follow the organization structure. Needs Task Management
    /// (AddTaskManagement + AddTaskManagementInfrastructure) and the organization structure
    /// (AddOrganizationApplication/Infrastructure + AddOrgStructure/Infrastructure). Map the
    /// endpoints with MapTaskReportEndpoints. Adds no tables and no permissions of its own.
    /// </summary>
    public static IServiceCollection AddTaskReporting(this IServiceCollection services)
    {
        services.AddScoped<ITaskReportService, TaskReportService>();
        return services;
    }
}
