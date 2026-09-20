using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Nexus.ProjectManagement.Kpi.Application;
using Nexus.ProjectManagement.Kpi.Application.Dtos;
using Nexus.ProjectManagement.Kpi.Application.Validators;
using Nexus.ProjectManagement.Kpi.Permissions;
using NexusCore.Application.Identity.Permissions;

namespace Nexus.ProjectManagement.Kpi;

public static class DependencyInjection
{
    /// <summary>Requires AddProjectManagementCore() and AddProjectDeliverables() - a KPI
    /// always relates to a Deliverable.</summary>
    public static IServiceCollection AddProjectKpi(this IServiceCollection services)
    {
        services.AddScoped<IKpiService, KpiService>();
        services.AddScoped<IValidator<CreateKpiDefinitionRequest>, CreateKpiDefinitionRequestValidator>();
        services.AddScoped<IValidator<UpdateKpiDefinitionRequest>, UpdateKpiDefinitionRequestValidator>();
        services.AddSingleton<IPermissionCatalog, KpiPermissionCatalog>();

        services.AddAuthorization(options =>
        {
            foreach (var permission in KpiPermissions.All)
            {
                options.AddPolicy(permission.Name, policy =>
                    policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission.Name)));
            }
        });

        return services;
    }
}
