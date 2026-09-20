using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Nexus.ProjectManagement.Deliverables.Application;
using Nexus.ProjectManagement.Deliverables.Application.Dtos;
using Nexus.ProjectManagement.Deliverables.Application.Validators;
using Nexus.ProjectManagement.Deliverables.Permissions;
using NexusCore.Application.Identity.Permissions;

namespace Nexus.ProjectManagement.Deliverables;

public static class DependencyInjection
{
    /// <summary>Requires AddProjectManagementCore().</summary>
    public static IServiceCollection AddProjectDeliverables(this IServiceCollection services)
    {
        services.AddScoped<IDeliverableService, DeliverableService>();
        services.AddScoped<IValidator<CreateDeliverableRequest>, CreateDeliverableRequestValidator>();
        services.AddScoped<IValidator<UpdateDeliverableRequest>, UpdateDeliverableRequestValidator>();
        services.AddSingleton<IPermissionCatalog, DeliverablePermissionCatalog>();

        services.AddAuthorization(options =>
        {
            foreach (var permission in DeliverablePermissions.All)
            {
                options.AddPolicy(permission.Name, policy =>
                    policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission.Name)));
            }
        });

        return services;
    }
}
