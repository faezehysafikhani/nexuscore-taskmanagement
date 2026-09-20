using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Nexus.ProjectManagement.Waterfall.Application;
using Nexus.ProjectManagement.Waterfall.Application.Dtos;
using Nexus.ProjectManagement.Waterfall.Application.EventHandlers;
using Nexus.ProjectManagement.Waterfall.Application.Validators;
using Nexus.ProjectManagement.Waterfall.Permissions;
using NexusCore.Application.Approvals;
using NexusCore.Application.Identity.Permissions;
using NexusCore.SharedKernel.Domain;

namespace Nexus.ProjectManagement.Waterfall;

public static class DependencyInjection
{
    /// <summary>Requires AddProjectManagementCore() to already be registered. Optional:
    /// AddWorkflowApplication() (works standalone without it) and any IWbsGenerator (AI).</summary>
    public static IServiceCollection AddWaterfallPlanning(this IServiceCollection services)
    {
        services.AddScoped<IActivityService, ActivityService>();
        services.AddScoped<IValidator<CreateActivityRequest>, CreateActivityRequestValidator>();
        services.AddScoped<IValidator<UpdateActivityRequest>, UpdateActivityRequestValidator>();
        services.AddScoped<IValidator<UpdateActivityProgressRequest>, UpdateActivityProgressRequestValidator>();
        services.AddSingleton<IPermissionCatalog, WaterfallPermissionCatalog>();

        services.AddScoped<IDomainEventHandler<ApprovalGranted>, ActivityApprovalGrantedHandler>();
        services.AddScoped<IDomainEventHandler<ApprovalRejected>, ActivityApprovalRejectedHandler>();

        services.AddAuthorization(options =>
        {
            foreach (var permission in WaterfallPermissions.All)
            {
                options.AddPolicy(permission.Name, policy =>
                    policy.RequireAuthenticatedUser().AddRequirements(new PermissionRequirement(permission.Name)));
            }
        });

        return services;
    }
}
