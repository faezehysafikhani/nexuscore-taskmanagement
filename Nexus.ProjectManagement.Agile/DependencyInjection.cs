using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Nexus.ProjectManagement.Agile.Application;
using Nexus.ProjectManagement.Agile.Application.Dtos;
using Nexus.ProjectManagement.Agile.Application.EventHandlers;
using Nexus.ProjectManagement.Agile.Application.Validators;
using Nexus.ProjectManagement.Agile.Permissions;
using NexusCore.Application.Approvals;
using NexusCore.Application.Identity.Permissions;
using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Agile;

public static class DependencyInjection
{
    /// <summary>Requires AddProjectManagementCore(). Optional: AddWorkflowApplication() and
    /// any IAgileTaskGenerator (AI). Agile Planning has no reference to Waterfall Planning and
    /// is never required to run it - the two are independent siblings under Core.</summary>
    public static IServiceCollection AddAgilePlanning(this IServiceCollection services)
    {
        services.AddScoped<IAgileTaskService, AgileTaskService>();
        services.AddScoped<IValidator<CreateAgileTaskRequest>, CreateAgileTaskRequestValidator>();
        services.AddScoped<IValidator<UpdateAgileTaskRequest>, UpdateAgileTaskRequestValidator>();
        services.AddSingleton<IPermissionCatalog, AgilePermissionCatalog>();

        services.AddScoped<IDomainEventHandler<ApprovalGranted>, AgileTaskApprovalGrantedHandler>();
        services.AddScoped<IDomainEventHandler<ApprovalRejected>, AgileTaskApprovalRejectedHandler>();

        services.AddAuthorization(options =>
        {
            foreach (var permission in AgilePermissions.All)
            {
                options.AddPolicy(permission.Name, policy =>
                    policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission.Name)));
            }
        });

        return services;
    }
}
